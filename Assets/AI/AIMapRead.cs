using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace HexTactica.AI {

    /// <summary>
    /// The board read's supply and reach layers.
    ///
    /// Supply, split by where it comes from:
    ///   static    — buildings and the HQ. Survives every supply unit dying or walking off.
    ///   mobile    — only there because a supply unit stands where it stands.
    ///   potential — not supplied now, but inside what our supply units could cover after one
    ///               move (SupplyNetwork.Extent). Worth profile.potentialSupplyValue (a quarter)
    ///               of real supply: better than nothing, a long way short of the real thing.
    /// Plus the hop distance from every tile to our supply and to the enemy's.
    ///
    /// Enemy reach, three turns deep: for every tile, how many enemy turns until some enemy
    /// combat unit could strike it (1 = their coming turn), with real movement, terrain and
    /// blocking. The coming turn is the world simulation's job; turns 2 and 3 are a heuristic
    /// for things that move slowly and plan ahead (supply units, objectives).
    ///
    /// And the ghost: before anyone moves, each supply unit plans its move and the field is
    /// re-solved as if it had made it, so the line can advance into supply that is coming.
    /// The supply unit then moves last, for real, and the ghost is dropped.
    /// </summary>
    public partial class AIMap {

        public SupplyNetwork.Result staticSupply;
        /// <summary>The enemy's live supply, all enemy players merged.</summary>
        public readonly SupplyNetwork.Result theirSupply = new SupplyNetwork.Result();
        readonly HashSet<HexTile> potentialSupply = new HashSet<HexTile>();
        readonly Dictionary<HexTile, int> distOurSupply = new Dictionary<HexTile, int>();
        readonly Dictionary<HexTile, int> distTheirSupply = new Dictionary<HexTile, int>();
        readonly Dictionary<HexTile, int> strikeTurns = new Dictionary<HexTile, int>();
        readonly Dictionary<SupplyPoint, HexTile> ghost = new Dictionary<SupplyPoint, HexTile>();

        /// <summary>Milliseconds the last three-turn enemy reach computation took.</summary>
        public double reachMillis;
        public int reachRuns;
        public double reachMillisTotal;

        public enum SupplySource { None, Potential, Mobile, Static }

        public SupplySource SourceAt(HexTile t) {
            if (t == null) return SupplySource.None;
            if (staticSupply != null && staticSupply.DepthAt(t) > 0) return SupplySource.Static;
            if (supply != null && supply.DepthAt(t) > 0) return SupplySource.Mobile;
            if (potentialSupply.Contains(t)) return SupplySource.Potential;
            return SupplySource.None;
        }

        /// <summary>How supplied a tile is, 0..1: supplied 1, potential profile.potentialSupplyValue, else 0.</summary>
        public float SupplyCover(HexTile t) {
            if (t == null) return 0f;
            if (supply != null && supply.DepthAt(t) > 0) return 1f;
            if (potentialSupply.Contains(t)) return profile.potentialSupplyValue;
            return 0f;
        }

        public int DistanceToOurSupply(HexTile t) { int d; return t != null && distOurSupply.TryGetValue(t, out d) ? d : 99; }
        public int DistanceToTheirSupply(HexTile t) { int d; return t != null && distTheirSupply.TryGetValue(t, out d) ? d : 99; }

        /// <summary>Enemy turns until something could strike this tile: 1 = their coming turn; 99 = not within three.</summary>
        public int EnemyStrikeTurns(HexTile t) { int d; return t != null && strikeTurns.TryGetValue(t, out d) ? d : 99; }

        public bool HasGhost { get { return ghost.Count > 0; } }
        public HexTile GhostOf(SupplyPoint hub) { HexTile t; return hub != null && ghost.TryGetValue(hub, out t) ? t : null; }

        /// <summary>Plan supply as if these hubs had already moved. Re-solves now and after every action until cleared.</summary>
        public void SetGhost(SupplyPoint hub, HexTile at) {
            if (hub == null) return;
            if (at == null || hub.myUnit == null || at == hub.myUnit.tile) ghost.Remove(hub); else ghost[hub] = at;
            SolveOurSupply();
        }

        public void ClearGhost(SupplyPoint hub) {
            if (hub != null && ghost.Remove(hub)) SolveOurSupply();
        }

        /// <summary>Our field: real, or with the ghosts applied. The real one is always what the board shows.</summary>
        void SolveOurSupply() {
            supply = SupplyNetwork.Solve(player, false);
            for (int i = 0; i < supply.connectedHubs.Count; i++) supply.connectedHubs[i].connected = true;
            for (int i = 0; i < supply.strandedHubs.Count; i++) supply.strandedHubs[i].connected = false;
            if (ghost.Count > 0) {
                List<SupplyPoint> gone = new List<SupplyPoint>();
                foreach (KeyValuePair<SupplyPoint, HexTile> kv in ghost)
                    if (kv.Key == null || kv.Key.myUnit == null || kv.Key.myUnit.Strength <= 0 || kv.Key.myUnit.tile == null) gone.Add(kv.Key);
                foreach (SupplyPoint g in gone) ghost.Remove(g);
                if (ghost.Count > 0) supply = SupplyNetwork.SolveMoved(player, ghost);
            }
            ComputeSupplyRead();
        }

        readonly Dictionary<SupplyPoint, float> hubStake = new Dictionary<SupplyPoint, float>();
        readonly HashSet<SupplyPoint> theirConnected = new HashSet<SupplyPoint>();
        public readonly List<Player> enemyPlayers = new List<Player>();

        /// <summary>
        /// Will this unit heal next turn if it ends on <paramref name="at"/>? The game heals a
        /// strength at the start of its owner's turn on any supplied tile — except a supply unit
        /// cut off from the network, which is spending its own health.
        /// </summary>
        public bool Heals(Unit x, HexTile at) {
            if (x == null || at == null || x.owner == null) return false;
            // Aircraft and ships: only on a building of theirs that services them.
            if (LocomotionRules.NeedsBase(x.locomotion)) return at.building != null && at.building.owner == x.owner && at.building.Services(x);
            SupplyPoint sp = x.GetComponent<SupplyPoint>();
            if (x.owner == player) {
                if (supply == null || supply.DepthAt(at) <= 0) return false;
                return sp == null || sp.producer || supply.connectedHubs.Contains(sp);
            }
            if (theirSupply.DepthAt(at) <= 0) return false;
            return sp == null || sp.producer || theirConnected.Contains(sp);
        }

        // ---- space: tiles each of our units could move to, and could strike, next turn.
        readonly Dictionary<Unit, HashSet<HexTile>[]> ourSpace = new Dictionary<Unit, HashSet<HexTile>[]>();

        /// <summary>Movement tiles [0] and strike tiles [1] of one of our units from where it stands. Cached per board read.</summary>
        /// <summary>
        /// An enemy's move and strike tiles for the space forecast, computed from the move tiles
        /// <paramref name="positions"/>: kept while the forecasts hand in the same set (the reply
        /// cache's, for an enemy far from the unit deciding), so each is worked out once per board.
        /// </summary>
        public HashSet<HexTile>[] EnemySpace(Unit e, HashSet<HexTile> positions) {
            KeyValuePair<HashSet<HexTile>, HashSet<HexTile>[]> kv;
            // No move tiles handed in (a unit that does not fire tonight): its reach on the board as
            // it stands, which is the same for every forecast on this board.
            if (enemySpace.TryGetValue(e, out kv) && ReferenceEquals(kv.Key, positions)) return kv.Value;
            HashSet<HexTile>[] s = SpaceFrom(e, e.tile, positions);
            enemySpace[e] = new KeyValuePair<HashSet<HexTile>, HashSet<HexTile>[]>(positions, s);
            return s;
        }
        readonly Dictionary<Unit, KeyValuePair<HashSet<HexTile>, HashSet<HexTile>[]>> enemySpace = new Dictionary<Unit, KeyValuePair<HashSet<HexTile>, HashSet<HexTile>[]>>();

        public HashSet<HexTile>[] SpaceOf(Unit x) {
            HashSet<HexTile>[] s;
            if (ourSpace.TryGetValue(x, out s)) return s;
            s = SpaceFrom(x, x.tile, null);
            ourSpace[x] = s;
            return s;
        }

        /// <summary>
        /// Tiles <paramref name="x"/> could end its next move on, and tiles it could strike, from
        /// <paramref name="origin"/>. <paramref name="positions"/> may supply the move tiles already.
        /// Artillery strikes only from where it stands (it cannot move and fire).
        /// </summary>
        public static HashSet<HexTile>[] SpaceFrom(Unit x, HexTile origin, HashSet<HexTile> positions) {
            HashSet<HexTile> move = positions;
            if (move == null) {
                move = new HashSet<HexTile>();
                move.Add(origin);
                if (x.moveRange > 0 && x.trasnporter == null) {
                    MoveField f = AIPath.ThisTurnFrom(x, origin, x.moveRange);
                    foreach (HexTile p in f.Tiles) if (p != origin && f.ReachableThisTurn(p) && AIPath.CanFinishTurnOn(x, p)) move.Add(p);
                }
            }
            HashSet<HexTile> strike = new HashSet<HexTile>();
            if (x.Firepower > 0 && x.AttackRange > 0) {
                if (x.moveAndAttack) { foreach (HexTile p in move) foreach (HexTile r in AIPath.TilesInRange(p, x.MinRange, x.AttackRange)) strike.Add(r); }
                else foreach (HexTile r in AIPath.TilesInRange(origin, x.MinRange, x.AttackRange)) strike.Add(r);
            }
            return new[] { move, strike };
        }

        /// <summary>
        /// What a (mobile) supply unit's death costs its side: one strength of every unit
        /// that is supplied only because of it — in supply now, out of it without the hub.
        /// Either side's hubs. Cached per board read.
        /// </summary>
        public float HubStake(Unit hubUnit) {
            SupplyPoint hub = hubUnit != null ? hubUnit.GetComponent<SupplyPoint>() : null;
            if (hub == null || hub.producer || hubUnit.owner == null) return 0f;
            float v;
            if (hubStake.TryGetValue(hub, out v)) return v;
            Player o = hubUnit.owner;
            SupplyNetwork.Result with = o == player ? supply : SupplyNetwork.Solve(o, false);
            SupplyNetwork.Result without = SupplyNetwork.SolveWithout(o, hub);
            v = 0f;
            for (int i = 0; i < o.units.Count; i++) {
                Unit x = o.units[i];
                if (x == null || x.tile == null || x == hubUnit || x.Strength <= 0) continue;
                if (with.DepthAt(x.tile) > 0 && without.DepthAt(x.tile) <= 0) v += AIScore.Worth(x);
            }
            hubStake[hub] = v;
            return v;
        }

        void ComputeSupplyRead() {
            hubStake.Clear();
            staticSupply = SupplyNetwork.SolveStatic(player);

            potentialSupply.Clear();
            List<Unit> mine = player != null ? player.units : null;
            if (mine != null) {
                for (int i = 0; i < mine.Count; i++) {
                    Unit u = mine[i];
                    if (u == null || u.tile == null || u.Strength <= 0) continue;
                    SupplyPoint sp = u.GetComponent<SupplyPoint>();
                    if (sp == null || sp.producer) continue;
                    SupplyNetwork.Result ext = SupplyNetwork.Extent(sp);
                    foreach (HexTile t in ext.depth.Keys) if (supply.DepthAt(t) <= 0) potentialSupply.Add(t);
                }
            }

            // Enemy supply: solve each enemy player (this publishes their real field, which is correct).
            theirSupply.depth.Clear();
            theirConnected.Clear();
            enemyPlayers.Clear();
            ourSpace.Clear();
            enemySpace.Clear();
            HashSet<Player> enemies = new HashSet<Player>();
            List<Unit> all = UnitController.uc != null ? UnitController.uc.units : null;
            if (all != null) for (int i = 0; i < all.Count; i++) if (all[i] != null && all[i].owner != null && all[i].owner != player) enemies.Add(all[i].owner);
            foreach (Player o in enemies) {
                SupplyNetwork.Result r = SupplyNetwork.Solve(o, false);
                enemyPlayers.Add(o);
                foreach (SupplyPoint h in r.connectedHubs) theirConnected.Add(h);
                foreach (KeyValuePair<HexTile, int> kv in r.depth)
                    if (kv.Value > theirSupply.DepthAt(kv.Key)) theirSupply.depth[kv.Key] = kv.Value;
            }

            HopDistance(supply, distOurSupply);
            HopDistance(theirSupply, distTheirSupply);
        }

        /// <summary>Multi-source breadth-first hop distance from every supplied tile, over land and water alike.</summary>
        static void HopDistance(SupplyNetwork.Result field, Dictionary<HexTile, int> dist) {
            dist.Clear();
            Queue<HexTile> q = new Queue<HexTile>();
            foreach (KeyValuePair<HexTile, int> kv in field.depth) if (kv.Value > 0) { dist[kv.Key] = 0; q.Enqueue(kv.Key); }
            while (q.Count > 0) {
                HexTile c = q.Dequeue();
                int d = dist[c];
                List<HexTile> n = c.neighbors;
                for (int i = 0; i < n.Count; i++) {
                    if (n[i] == null || n[i].terrain == null || dist.ContainsKey(n[i])) continue;
                    dist[n[i]] = d + 1;
                    q.Enqueue(n[i]);
                }
            }
        }

        void ComputeStrikeTurns() {
            strikeTurns.Clear();
            if (profile.enemyReachTurns <= 1) return;
            Stopwatch sw = Stopwatch.StartNew();
            for (int i = 0; i < enemyUnits.Count; i++) {
                Unit e = enemyUnits[i];
                if (e == null || e.tile == null || e.Firepower <= 0 || e.GetComponent<SupplyPoint>() != null) continue;
                if (e.trasnporter != null) continue;
                MoveField f = AIPath.MultiTurn(e, profile.enemyReachTurns - 1);
                foreach (HexTile p in f.Tiles) {
                    Eta eta = f.Get(p);
                    if (eta.IsUnreachable) continue;
                    // Reached on turn k (0 = their coming turn); fire the same turn if it can move and shoot.
                    int strike = eta.turns + (e.moveAndAttack || p == e.tile ? 1 : 2);
                    if (strike > profile.enemyReachTurns) continue;
                    if (p != e.tile && !AIPath.CanFinishTurnOn(e, p)) continue;
                    List<HexTile> ring = AIPath.TilesInRange(p, e.MinRange, e.AttackRange);
                    for (int k = 0; k < ring.Count; k++) {
                        int cur;
                        if (!strikeTurns.TryGetValue(ring[k], out cur) || strike < cur) strikeTurns[ring[k]] = strike;
                    }
                }
            }
            sw.Stop();
            reachMillis = sw.Elapsed.TotalMilliseconds;
            reachRuns++;
            reachMillisTotal += reachMillis;
        }

        /// <summary>One line per layer for the narrated log.</summary>
        public string SupplyReport() {
            int st = 0, mob = 0, pot = potentialSupply.Count;
            if (supply != null) foreach (KeyValuePair<HexTile, int> kv in supply.depth) {
                if (kv.Value <= 0) continue;
                if (staticSupply != null && staticSupply.DepthAt(kv.Key) > 0) st++; else mob++;
            }
            int s1 = 0, s2 = 0, s3 = 0;
            foreach (int v in strikeTurns.Values) { if (v == 1) s1++; else if (v == 2) s2++; else if (v == 3) s3++; }
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("supply tiles: static {0}, mobile {1}, potential {2}; enemy supplied {3}", st, mob, pot, theirSupply.depth.Count);
            sb.AppendFormat("; enemy strike in 1/2/3 turns: {0}/{1}/{2} tiles ({3:0.0} ms)", s1, s2, s3, reachMillis);
            if (ghost.Count > 0) foreach (KeyValuePair<SupplyPoint, HexTile> kv in ghost)
                sb.AppendFormat("; ghost {0} -> ({1},{2})", kv.Key.myUnit.name, kv.Value.x, kv.Value.y);
            return sb.ToString();
        }

        /// <summary>A unit's read of its tile, for the log.</summary>
        public string TileRead(HexTile t) {
            return string.Format("{0} d{1} to-ours {2} to-theirs {3} strike-in {4}", SourceAt(t), supply.DepthAt(t),
                DistanceToOurSupply(t), DistanceToTheirSupply(t), EnemyStrikeTurns(t) >= 99 ? "-" : EnemyStrikeTurns(t).ToString());
        }
    }
}
