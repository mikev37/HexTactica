using System.Collections.Generic;
using UnityEngine;

namespace HexTactica.AI {

    /// <summary>
    /// Supply over water (AIProfile.seaSupplyChain), planned at the start of each turn.
    ///
    /// Land our supply buildings do not reach over land (an island, a far shore we landed on) where we have fighting units
    /// standing is a post: the water tile beside that land nearest them, where a ship with a supply unit aboard gives supply
    /// ashore (SupplyNetwork: a carried supply unit gives supply from a ship beside the shore). The ship is linked if
    /// supplied ground is within its move, or another ship with supply aboard is (a convoy) — so a post far from home takes
    /// a line of laden ships, one a move apart along the water back to our supplied shore.
    ///
    /// Every station of every post (the largest need first) takes a laden ship already at sea (nearest first), else an
    /// empty transport ship that is not carrying troops over (a sea invasion keeps its ships) and a supply unit to put in
    /// it: the ship goes to the naval yard nearest the supply unit and waits on it, the supply unit walks there and boards.
    /// What is short (ships, supply units) is asked of production (<see cref="Demand"/>).
    ///
    /// A post whose units stand further inland than the supply from the shore reaches, with no truck on that land, asks
    /// for a truck (<see cref="Demand"/>.trucks): the matryoshka — a supply unit in a truck in a ship — is not driven yet.
    /// </summary>
    public class AISeaSupply {

        public class Station { public Post post; public int index; public HexTile tile; public Unit ship; public Unit hub; public Building yard; }
        public class Post { public int land; public HexTile shore; public float power; public int units; public bool wantsTruck; public readonly List<Station> stations = new List<Station>(); }

        /// <summary>What a side's sea supply is short of, for production (AIProduction).</summary>
        public class Want { public int ships, hubs, trucks; public float power; public readonly List<Unit> ashore = new List<Unit>(); }
        static readonly Dictionary<Player, Want> demand = new Dictionary<Player, Want>();
        public static Want Demand(Player p) { lock (demand) { Want w; return p != null && demand.TryGetValue(p, out w) ? w : null; } }

        public readonly List<Post> posts = new List<Post>();
        public readonly Dictionary<Unit, Station> shipOf = new Dictionary<Unit, Station>();
        public readonly Dictionary<Unit, Station> hubOf = new Dictionary<Unit, Station>();
        /// <summary>Ships waiting on a naval yard for their supply unit: they may end on it (AIAir.BlocksBuildSlot).</summary>
        [System.ThreadStatic] public static HashSet<Unit> Loading;

        readonly AIMap map; readonly AIProfile profile; readonly Player player;
        readonly Dictionary<HexTile, int> landOf = new Dictionary<HexTile, int>();
        System.Func<Unit, HexTile> groundOf;
        readonly HashSet<int> suppliedLand = new HashSet<int>();
        readonly Dictionary<Post, List<Unit>> ashoreOf = new Dictionary<Post, List<Unit>>();
        /// <summary>Troop ships bound for land without supply, and the supply unit riding along in each (a seat kept for it).</summary>
        public readonly Dictionary<Unit, Unit> troopShipHub = new Dictionary<Unit, Unit>();
        readonly Dictionary<Unit, Unit> hubForTroopShip = new Dictionary<Unit, Unit>();
        /// <summary>Supply units sent to wait at a naval yard where troops gather to cross to land without supply.</summary>
        readonly Dictionary<Unit, Building> staged = new Dictionary<Unit, Building>();
        static readonly Dictionary<Player, Dictionary<Unit, Building>> stagedBefore = new Dictionary<Player, Dictionary<Unit, Building>>();
        /// <summary>Ships holding a station: no troops get into them (AIAssault.Board).</summary>
        [System.ThreadStatic] public static HashSet<Unit> Stationed;

        public static System.Action<string> Debug;

        AISeaSupply(Player p, AIMap map, AIProfile profile) { player = p; this.map = map; this.profile = profile; }

        static bool IsLand(HexTile t) { return t != null && t.terrain != null && t.terrain.elevation >= 0; }
        static bool IsSupplyShip(Unit t) { return t != null && LocomotionRules.IsNaval(t.locomotion) && Transporting.IsTransport(t) && t.Firepower <= 1 && t.Hub == null; }

        /// <summary>The plan for this turn. <paramref name="busy"/>: transports already given a job (supply rides, chain trucks).</summary>
        public static AISeaSupply Plan(Player p, AIMap map, AIProfile profile, HashSet<Unit> busy, HashSet<SupplyPoint> chainLinks, System.Func<Unit, HexTile> groundOf) {
            AISeaSupply s = new AISeaSupply(p, map, profile);
            s.groundOf = groundOf;
            Loading = new HashSet<Unit>(); Stationed = new HashSet<Unit>();
            s.Make(busy, chainLinks);
            return s;
        }

        void Make(HashSet<Unit> busy, HashSet<SupplyPoint> chainLinks) {
            // The land masses (bridges join them, water parts them).
            int n = 0;
            List<HexTile> all = new List<HexTile>();
            HexGenerator.forAllTiles(t => { if (t != null) all.Add(t); return 0; });
            all.Sort(CombatMath.CompareTiles);
            foreach (HexTile t in all) {
                if (!IsLand(t) || landOf.ContainsKey(t)) continue;
                Stack<HexTile> st = new Stack<HexTile>(); st.Push(t); landOf[t] = n;
                while (st.Count > 0) { HexTile c = st.Pop(); foreach (HexTile x in c.neighbors) if (IsLand(x) && !landOf.ContainsKey(x)) { landOf[x] = n; st.Push(x); } }
                n++;
            }
            // Home: land our supply buildings reach (the static network).
            SupplyNetwork.Result home = SupplyNetwork.SolveStatic(player);
            HashSet<int> homeLand = new HashSet<int>();
            foreach (KeyValuePair<HexTile, int> kv in home.depth) { int l; if (kv.Value > 0 && landOf.TryGetValue(kv.Key, out l)) homeLand.Add(l); }
            // Our fighting units ashore on other land: each land is a post.
            Dictionary<int, Post> byLand = new Dictionary<int, Post>();
            Dictionary<int, List<Unit>> needy = new Dictionary<int, List<Unit>>();
            foreach (Unit u in map.ownUnits) {
                if (u == null || u.tile == null || u.trasnporter != null || u.Hub != null || u.Firepower <= 0) continue;
                if (LocomotionRules.IsNaval(u.locomotion) || LocomotionRules.IsAirborne(u.locomotion)) continue;
                int l; if (!landOf.TryGetValue(u.tile, out l) || homeLand.Contains(l)) continue;
                List<Unit> list; if (!needy.TryGetValue(l, out list)) needy[l] = list = new List<Unit>();
                list.Add(u);
            }
            // Land where our supply reaches now, by any means (a second wave goes there: its ships stay troop ships).
            HexGenerator.forAllTiles(t => { int l; if (t != null && map.SupplyDepth(t) > 0 && landOf.TryGetValue(t, out l)) suppliedLand.Add(l); return 0; });
            // A ship to measure the water by: ours, else one we can build.
            Unit shipKind = null;
            foreach (Unit u in map.ownUnits) if (IsSupplyShip(u)) { shipKind = u; break; }
            if (shipKind == null) foreach (Building b in Building.all) if (b != null && b.owner == player) foreach (Unit t in b.Purchasable()) if (IsSupplyShip(t)) { shipKind = t; break; }
            if (needy.Count == 0 || shipKind == null) { Assign(busy, chainLinks); return; }
            int move = Mathf.Max(1, shipKind.moveRange);
            // Water beside home supplied ground: where a laden ship is linked by itself.
            HashSet<HexTile> anchor = new HashSet<HexTile>();
            foreach (KeyValuePair<HexTile, int> kv in home.depth) {
                if (kv.Value <= 0 || !IsLand(kv.Key)) continue;
                foreach (HexTile w in kv.Key.neighbors) if (w != null && !IsLand(w) && shipKind.CanEnter(w)) anchor.Add(w);
            }
            Unit sample = null;   // a supply unit: its range ashore
            foreach (Unit u in map.ownUnits) if (u != null && u.Hub != null && !u.Hub.producer) { sample = u; break; }
            int hubRange = sample != null ? sample.Hub.range : 4;
            List<int> lands = new List<int>(needy.Keys); lands.Sort();
            foreach (int l in lands) {
                List<Unit> us = needy[l];
                Post post = new Post { land = l, units = us.Count }; ashoreOf[post] = us;
                foreach (Unit u in us) post.power += AIScore.Worth(u) * u.Strength;
                // The shore: water beside that land, nearest the units in all.
                HexTile best = null; int bs = int.MaxValue;
                foreach (HexTile t in all) {
                    if (IsLand(t) || !shipKind.CanEnter(t)) continue;
                    bool beside = false; foreach (HexTile x in t.neighbors) { int lx; if (IsLand(x) && landOf.TryGetValue(x, out lx) && lx == l) { beside = true; break; } }
                    if (!beside) continue;
                    int sum = 0; foreach (Unit u in us) sum += t.Distance(u.tile);
                    if (sum < bs || (sum == bs && CombatMath.Precedes(t, best))) { bs = sum; best = t; }
                }
                if (best == null) continue;
                post.shore = best;
                // Units further inland than its supply reaches from there: a truck wanted, if none on that land.
                bool far = false; foreach (Unit u in us) if (u.tile.Distance(best) > hubRange) far = true;
                bool truck = false;
                foreach (Unit u in map.ownUnits) {
                    int lu; if (u == null || u.tile == null || !Transporting.IsTransport(u) || u.Firepower > 1 || LocomotionRules.IsNaval(u.locomotion) || LocomotionRules.IsAirborne(u.locomotion)) continue;
                    if (landOf.TryGetValue(u.tile, out lu) && lu == l) truck = true;
                }
                post.wantsTruck = far && !truck;
                // The convoy: back along the water from the shore to water beside home supply, a ship a move apart.
                List<HexTile> path = WaterPath(best, anchor, shipKind);
                if (path == null) continue;
                int stations = path.Count <= 1 ? 1 : Mathf.CeilToInt((path.Count - 1) / (float)move);
                stations = Mathf.Max(1, stations);
                for (int i = 0; i < stations; i++) post.stations.Add(new Station { post = post, index = i, tile = path[Mathf.Min(path.Count - 1, i * move)] });
                posts.Add(post);
            }
            posts.Sort((a, b) => { int c = b.power.CompareTo(a.power); return c != 0 ? c : CombatMath.CompareTiles(a.shore, b.shore); });
            Assign(busy, chainLinks);
        }

        /// <summary>The water from <paramref name="from"/> to the nearest of <paramref name="to"/>, as <paramref name="ship"/> sails (from first); null: none.</summary>
        static List<HexTile> WaterPath(HexTile from, HashSet<HexTile> to, Unit ship) {
            if (to.Count == 0) return null;
            Dictionary<HexTile, HexTile> parent = new Dictionary<HexTile, HexTile>();
            Queue<HexTile> q = new Queue<HexTile>(); q.Enqueue(from); parent[from] = null;
            HexTile end = null;
            while (q.Count > 0) {
                HexTile c = q.Dequeue();
                if (to.Contains(c)) { end = c; break; }
                List<HexTile> nb = new List<HexTile>(c.neighbors); nb.Sort(CombatMath.CompareTiles);
                foreach (HexTile x in nb) if (x != null && !parent.ContainsKey(x) && !IsLand(x) && ship.CanEnter(x)) { parent[x] = c; q.Enqueue(x); }
            }
            if (end == null) return null;
            List<HexTile> back = new List<HexTile>();
            for (HexTile c = end; c != null; c = parent[c]) back.Add(c);
            back.Reverse();   // from ... end
            return back;
        }

        void Assign(HashSet<Unit> busy, HashSet<SupplyPoint> chainLinks) {
            List<Station> open = new List<Station>();
            foreach (Post p in posts) foreach (Station s in p.stations) open.Add(s);
            // Laden ships at sea (supply aboard, no troops): the nearest open station each.
            List<Unit> laden = new List<Unit>(), empty = new List<Unit>();
            foreach (Unit u in map.ownUnits) {
                if (!IsSupplyShip(u) || u.tile == null || busy.Contains(u)) continue;
                if (AIAssault.Riders(u).Count > 0) continue;   // carrying troops over: a sea invasion keeps it
                if (AIAssault.CarriesHub(u)) laden.Add(u);
                else if (u.transported.Count == 0) empty.Add(u);
            }
            laden.Sort(AIMap.CompareUnits); empty.Sort(AIMap.CompareUnits);
            // Troops waiting to cross come first: the empty ships it takes to carry them over are theirs (a sea invasion);
            // only ships beyond those may be taken for supply (else production is asked for one).
            int waiting = 0;
            if (groundOf != null)
                foreach (Unit u in map.ownUnits) {
                    if (u == null || u.tile == null || u.trasnporter != null || u.Hub != null || u.Firepower <= 0 || LocomotionRules.IsNaval(u.locomotion) || LocomotionRules.IsAirborne(u.locomotion)) continue;
                    HexTile g = groundOf(u);
                    if (g != null && map.AcrossWater(u.tile, g)) waiting += Mathf.Max(1, u.weight);
                }
            int forTroops = 0;
            while (waiting > 0 && forTroops < empty.Count) { waiting -= Mathf.Max(1, empty[forTroops].transportSlots); forTroops++; }
            // (the troop ships: those nearest a yard keep to the troops; the rest are free for supply)
            empty.Sort((a, b) => { Building ya = NearYard(a), yb = NearYard(b); int da = ya != null ? a.tile.Distance(ya.tile) : 99, db = yb != null ? b.tile.Distance(yb.tile) : 99; int c = da.CompareTo(db); return c != 0 ? c : AIMap.CompareUnits(a, b); });
            empty.RemoveRange(0, forTroops);
            foreach (Unit sh in laden) {
                Station best = null; int bd = int.MaxValue;
                // The shores first (the troops landed there need it now), then the convoy behind them.
                foreach (Station s in open) { if (s.ship != null) continue; int d = sh.tile.Distance(s.tile) + (s.index > 0 ? 1000 : 0); if (d < bd || (d == bd && best != null && s.post.power > best.post.power)) { bd = d; best = s; } }
                if (best == null) break;
                best.ship = sh; shipOf[sh] = best; Stationed.Add(sh);
                foreach (Unit c in sh.transported) if (c != null && c.Hub != null && !c.Hub.producer) { best.hub = c; hubOf[c] = best; break; }
            }
            // Supply units free for it: on foot, not a link of a front's chain, on home land.
            List<Unit> free = new List<Unit>();
            foreach (Unit u in map.ownUnits) {
                if (u == null || u.tile == null || u.Hub == null || u.Hub.producer || u.trasnporter != null) continue;
                if (chainLinks != null && chainLinks.Contains(u.Hub)) continue;
                if (LocomotionRules.IsNaval(u.locomotion) || LocomotionRules.IsAirborne(u.locomotion)) continue;
                free.Add(u);
            }
            free.Sort(AIMap.CompareUnits);
            Want want = new Want();
            // A troop ship bound for land without supply carries a supply unit too (a seat kept for it): once the troops are
            // ashore it is their supply ship, off that shore (a laden ship, above), until another relieves it. One bound
            // where our supply already reaches (a second wave) stays a troop ship. Where troops gather at a yard to cross to
            // land without supply, a supply unit goes ahead to wait there.
            int waitingBare = 0; HashSet<Building> gatherYards = new HashSet<Building>();
            if (groundOf != null && profile.seaSupplyRideAlong)
                foreach (Unit u in map.ownUnits) {
                    if (u == null || u.tile == null || u.trasnporter != null || u.Hub != null || u.Firepower <= 0 || LocomotionRules.IsNaval(u.locomotion) || LocomotionRules.IsAirborne(u.locomotion)) continue;
                    HexTile g = groundOf(u); int lg;
                    if (g == null || !map.AcrossWater(u.tile, g) || !landOf.TryGetValue(g, out lg) || suppliedLand.Contains(lg)) continue;
                    waitingBare++;
                    Building y = AIAssault.NearestYard(u, map); if (y != null) gatherYards.Add(y);
                }
            List<Unit> troopShips = new List<Unit>();
            if (profile.seaSupplyRideAlong) foreach (Unit u in map.ownUnits) {
                if (!IsSupplyShip(u) || u.tile == null || AIAssault.CarriesHub(u) || Transporting.FreeWeight(u) < 1) continue;
                List<Unit> riders = AIAssault.Riders(u);
                HexTile landing = null;
                if (groundOf != null) foreach (Unit r in riders) { HexTile g = groundOf(r); if (g != null) { landing = g; break; } }
                if (landing != null) { int ll; if (!landOf.TryGetValue(landing, out ll) || suppliedLand.Contains(ll)) continue; }
                else if (riders.Count > 0 || waitingBare == 0) continue;   // (empty, troops waiting to cross to land without supply: it will carry them)
                troopShips.Add(u);
            }
            troopShips.Sort(AIMap.CompareUnits);
            foreach (Unit sh in troopShips) {
                Unit hub = null; int hd = int.MaxValue;
                foreach (Unit h in free) {
                    if (hubForTroopShip.ContainsKey(h) || !Transporting.CarriesKind(sh, h)) continue;
                    int d = map.Travel(h, h.tile, sh.tile);
                    if (d < hd) { hd = d; hub = h; }
                }
                if (hub == null) { want.hubs++; continue; }
                troopShipHub[sh] = hub; hubForTroopShip[hub] = sh;
            }
            foreach (Building y in gatherYards) {
                bool has = false;
                foreach (KeyValuePair<Unit, Unit> kv in hubForTroopShip) if (kv.Value.tile != null && kv.Value.tile.Distance(y.tile) <= 1) has = true;
                if (has) continue;
                Unit hub = null; int hd = int.MaxValue;
                // The one sent there before keeps going (not a new one each turn).
                Dictionary<Unit, Building> before; lock (stagedBefore) stagedBefore.TryGetValue(player, out before);
                if (before != null) foreach (KeyValuePair<Unit, Building> kv in before) if (kv.Value == y && free.Contains(kv.Key) && !hubForTroopShip.ContainsKey(kv.Key)) { hub = kv.Key; break; }
                if (hub == null) foreach (Unit h in free) {
                    if (hubForTroopShip.ContainsKey(h) || staged.ContainsKey(h)) continue;
                    Building hy = AIAssault.NearestYard(h, map); if (hy != y) continue;
                    int d = map.Travel(h, h.tile, y.tile);
                    if (d < hd) { hd = d; hub = h; }
                }
                if (hub == null) { want.hubs++; continue; }
                staged[hub] = y;
            }
            free.RemoveAll(h => hubForTroopShip.ContainsKey(h) || staged.ContainsKey(h));
            lock (stagedBefore) stagedBefore[player] = new Dictionary<Unit, Building>(staged);
            foreach (Station s in open) {
                if (s.ship != null) continue;
                // An empty ship and the supply unit nearest a yard it can reach.
                Unit sh = null; int sd = int.MaxValue;
                foreach (Unit e in empty) { if (shipOf.ContainsKey(e)) continue; int d = e.tile.Distance(s.tile); if (d < sd) { sd = d; sh = e; } }
                Unit hub = null; Building yard = null; int hd = int.MaxValue;
                foreach (Unit h in free) {
                    if (hubOf.ContainsKey(h) || (sh != null && !Transporting.CarriesKind(sh, h))) continue;
                    Building y = AIAssault.NearestYard(h, map);
                    if (y == null) continue;
                    int d = map.Travel(h, h.tile, y.tile);
                    if (d < hd) { hd = d; hub = h; yard = y; }
                }
                if (sh == null) want.ships++;
                if (hub == null) want.hubs++;
                if (sh == null || hub == null) continue;
                s.ship = sh; s.hub = hub; s.yard = yard; shipOf[sh] = s; hubOf[hub] = s;
            }
            foreach (Post p in posts) { if (p.wantsTruck) want.trucks++; want.power += p.power; if (p.stations.Count > 0 && p.stations[0].ship == null) foreach (Unit u in ashoreOf[p]) if (map.SupplyDepth(u.tile) <= 0) want.ashore.Add(u); }
            Publish(want);
            if (Debug != null) foreach (Post p in posts) {
                List<string> st = new List<string>();
                foreach (Station s in p.stations) st.Add("(" + s.tile.x + "," + s.tile.y + ")" + (s.ship != null ? " " + s.ship.name.Replace("(Clone)", "") + "(" + s.ship.tile.x + "," + s.ship.tile.y + ")" + (AIAssault.CarriesHub(s.ship) ? " laden" : " empty, " + (s.hub != null ? "supply from (" + s.hub.tile.x + "," + s.hub.tile.y + ") at yard (" + s.yard.tile.x + "," + s.yard.tile.y + ")" : "")) : " none"));
                Debug("sea supply: land " + p.land + ", " + p.units + " units, shore (" + p.shore.x + "," + p.shore.y + "), " + p.stations.Count + " ships: " + string.Join("; ", st.ToArray()) + (p.wantsTruck ? "; a truck wanted ashore" : ""));
            }
            if (Debug != null) foreach (KeyValuePair<Unit, Unit> kv in troopShipHub) Debug("sea supply: troop ship " + kv.Key.name.Replace("(Clone)", "") + "(" + kv.Key.tile.x + "," + kv.Key.tile.y + ") bound for land without supply: supply unit (" + kv.Value.tile.x + "," + kv.Value.tile.y + ") rides along");
            if (Debug != null) foreach (KeyValuePair<Unit, Building> kv in staged) Debug("sea supply: supply unit (" + kv.Key.tile.x + "," + kv.Key.tile.y + ") waits at yard (" + kv.Value.tile.x + "," + kv.Value.tile.y + ") for troops crossing to land without supply");
            if (Debug != null && (want.ships > 0 || want.hubs > 0 || want.trucks > 0)) Debug("sea supply short: " + want.ships + " ships, " + want.hubs + " supply units, " + want.trucks + " trucks (power ashore " + want.power.ToString("0") + ")");
        }

        Building NearYard(Unit ship) {
            Building best = null; int bd = int.MaxValue;
            foreach (Building b in Building.all) {
                if (b == null || b.tile == null || b.owner != player || !b.Functioning || !AIAssault.ServesShips(b)) continue;
                int d = ship.tile.Distance(b.tile);
                if (d < bd) { bd = d; best = b; }
            }
            return best;
        }

        void Publish(Want w) { lock (demand) demand[player] = w; }

        /// <summary>What a unit of the plan does now; null: not the plan's.</summary>
        public AIAction? Decide(Unit u) {
            Station s;
            if (shipOf.TryGetValue(u, out s)) return ShipAction(u, s);
            Unit tsh; Building y;
            if (hubForTroopShip.TryGetValue(u, out tsh) && u.trasnporter == null) return BoardTroopShip(u, tsh);
            if (staged.TryGetValue(u, out y) && u.trasnporter == null) {
                if (u.tile.Distance(y.tile) <= 1) return AIAction.Wait(u, "sea supply: waits at the yard (" + y.tile.x + "," + y.tile.y + ") to cross with the troops");
                HexTile step = StepTo(u, y.tile);
                if (step == null || step == u.tile || step == y.tile) return AIAction.Wait(u, "sea supply: waits to cross with the troops");
                return AIAction.Move(u, step, 0f, "sea supply: to the yard (" + y.tile.x + "," + y.tile.y + ") where troops cross to land without supply");
            }
            if (hubOf.TryGetValue(u, out s)) {
                if (u.trasnporter != null) return AIAction.Wait(u, "sea supply: rides its ship to (" + s.tile.x + "," + s.tile.y + ")");
                return HubAction(u, s);
            }
            return null;
        }

        AIAction ShipAction(Unit ship, Station s) {
            if (AIAssault.CarriesHub(ship)) {
                // Laden: to its station, through tiles where its supply stays linked if it can.
                if (ship.tile == s.tile) return AIAction.Wait(ship, "sea supply: holds its station (" + s.tile.x + "," + s.tile.y + ")" + (s.index == 0 ? " off the shore" : " in the convoy"));
                MoveField f = map.ReachThisTurn(ship);
                Unit cargo = null; foreach (Unit c in ship.transported) if (c != null && c.Hub != null && !c.Hub.producer) { cargo = c; break; }
                HexTile best = null; bool bl = false; int bd = int.MaxValue;
                Dictionary<SupplyPoint, Unit> was = SupplyNetwork.PlannedAboard;
                try {
                    if (cargo != null) SupplyNetwork.PlannedAboard = new Dictionary<SupplyPoint, Unit> { { cargo.Hub, ship } };
                    foreach (HexTile t in f.Tiles) {
                        if (t != ship.tile && (!f.ReachableThisTurn(t) || !AIPath.CanFinishTurnOn(ship, t) || !AIAir.MayEndOn(ship, t, map))) continue;
                        bool linked = cargo != null && SupplyNetwork.SolvePlanned(player, new Dictionary<SupplyPoint, HexTile> { { cargo.Hub, t } }).connectedHubs.Contains(cargo.Hub);
                        int d = t.Distance(s.tile);
                        // Its station first (linked or not: the convoy behind it links it); else linked and nearer.
                        bool better;
                        if (best == null) better = true;
                        else if ((d == 0) != (bd == 0)) better = d == 0;
                        else if (linked != bl) better = linked;
                        else better = d < bd || (d == bd && CombatMath.Precedes(t, best));
                        if (better) { best = t; bl = linked; bd = d; }
                    }
                } finally { SupplyNetwork.PlannedAboard = was; SupplyNetwork.Solve(player, false); }
                if (best == null || best == ship.tile) return AIAction.Wait(ship, "sea supply: holds" + (bl ? " (linked)" : ""));
                return AIAction.Move(ship, best, 0f, "sea supply: takes its supply to (" + s.tile.x + "," + s.tile.y + ")" + (bl ? ", linked" : ", not linked yet"));
            }
            // Empty: onto the yard its supply unit walks to, and wait there for it.
            if (s.yard == null) return AIAction.Wait(ship, "sea supply: no yard");
            if (ship.tile == s.yard.tile) { Loading.Add(ship); return AIAction.Wait(ship, "sea supply: waits at the yard (" + s.yard.tile.x + "," + s.yard.tile.y + ") for its supply unit"); }
            Loading.Add(ship);
            HexTile step = AITransportPlanner.SafeStepToward(ship, s.yard.tile, map.ReachHorizon(ship), map, true);
            if (step == null || step == ship.tile) return AIAction.Wait(ship, "sea supply: cannot get to the yard");
            return AIAction.Move(ship, step, 0f, "sea supply: to the yard (" + s.yard.tile.x + "," + s.yard.tile.y + ") for a supply unit");
        }

        /// <summary>A step toward <paramref name="goal"/>, out of danger if it can be, else along its way.</summary>
        HexTile StepTo(Unit u, HexTile goal) {
            MoveField f = map.ReachHorizon(u);
            HexTile step = AITransportPlanner.SafeStepToward(u, goal, f, map, true);
            if (step == null || step == u.tile) step = f.StepToward(goal);
            return step;
        }

        /// <summary>A supply unit to ride along in a troop ship bound for land without supply: in, or toward it.</summary>
        AIAction BoardTroopShip(Unit hub, Unit ship) {
            if (Transporting.CanBoardFrom(ship, hub)) return AIAction.Board(hub, ship, hub.tile, "sea supply: rides along with the troops to land without supply");
            MoveField now = map.ReachThisTurn(hub);
            HexTile at = null;
            foreach (HexTile x in ship.tile.neighbors)
                if (x != null && now.ReachableThisTurn(x) && (x.occupier == null || x.occupier == hub) && Transporting.CanBoardFromTile(ship, hub, x) && (at == null || CombatMath.Precedes(x, at))) at = x;
            if (at != null) return AIAction.Board(hub, ship, at, "sea supply: rides along with the troops to land without supply");
            Building y = NearYard(ship);
            HexTile goal = y != null ? y.tile : ship.tile;
            if (hub.tile.Distance(goal) <= 1) return AIAction.Wait(hub, "sea supply: waits for the troop ship to ride along");
            HexTile step = StepTo(hub, goal);
            if (step == null || step == hub.tile || step == goal) return AIAction.Wait(hub, "sea supply: waits for the troop ship");
            return AIAction.Move(hub, step, 0f, "sea supply: to the troop ship's yard (" + goal.x + "," + goal.y + ") to ride along");
        }

        AIAction HubAction(Unit hub, Station s) {
            Unit ship = s.ship;
            if (ship != null && ship.tile != null && Transporting.CanBoardFrom(ship, hub)) return AIAction.Board(hub, ship, hub.tile, "sea supply: boards its ship for (" + s.tile.x + "," + s.tile.y + ")");
            if (ship != null && ship.tile != null && s.yard != null && ship.tile == s.yard.tile) {
                MoveField now = map.ReachThisTurn(hub);
                HexTile at = null;
                foreach (HexTile x in ship.tile.neighbors)
                    if (x != null && now.ReachableThisTurn(x) && (x.occupier == null || x.occupier == hub) && Transporting.CanBoardFromTile(ship, hub, x) && (at == null || CombatMath.Precedes(x, at))) at = x;
                if (at != null) return AIAction.Board(hub, ship, at, "sea supply: boards its ship at the yard");
            }
            if (s.yard == null) return AIAction.Wait(hub, "sea supply: no yard");
            if (hub.tile.Distance(s.yard.tile) <= 1) return AIAction.Wait(hub, "sea supply: waits by the yard for its ship");
            HexTile step = StepTo(hub, s.yard.tile);
            if (step == null || step == hub.tile || step == s.yard.tile) return AIAction.Wait(hub, "sea supply: waits for its ship");
            return AIAction.Move(hub, step, 0f, "sea supply: walks to the yard (" + s.yard.tile.x + "," + s.yard.tile.y + ") to board");
        }
    }
}
