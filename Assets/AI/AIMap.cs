using System.Collections.Generic;
using UnityEngine;

namespace HexTactica.AI {

    /// <summary>
    /// One player's read of the board, built once at the start of their turn and
    /// reused for every decision within it.
    ///
    /// Two things distinguish this from QuickStrat's AIManager. First, there is no
    /// static "active nation" pointer — the player is a constructor argument, so two
    /// AIMaps for opposing players can exist at the same time and the AI can score
    /// the board from the enemy's side to check whether a move exposes it. Second,
    /// nothing here mutates HexTile.G or HexTile.parent, so evaluating a hypothetical
    /// never disturbs what the human player is looking at.
    /// </summary>
    public class AIMap {

        public readonly Player player;
        public readonly AIProfile profile;
        public SupplyNetwork.Result supply;

        readonly Dictionary<Unit, MoveField> thisTurnFields = new Dictionary<Unit, MoveField>();
        readonly Dictionary<Unit, MoveField> horizonFields = new Dictionary<Unit, MoveField>();

        // Derived layers live here, owned by this map, not on HexTile.aiData. Tile state
        // is wiped by GameController.ResetTileData after every unit action, which zeroed
        // these for every unit after the first; and two players' maps would overwrite
        // each other's values on the shared tiles.
        readonly Dictionary<HexTile, float> conflict = new Dictionary<HexTile, float>();
        readonly Dictionary<HexTile, float> distress = new Dictionary<HexTile, float>();
        readonly Dictionary<Unit, float> supplyImportance = new Dictionary<Unit, float>();

        /// <summary>One enemy unit's reach for tonight: every tile it could fire on.</summary>
        class EnemyFire {
            public Unit unit;
            public HashSet<HexTile> tiles;
            public int exposedFriendlies;
            /// <summary>Sum of how attractive each currently exposed friendly is to this enemy.</summary>
            public float exposedAttraction;
            public readonly Dictionary<Unit, float> attractionOf = new Dictionary<Unit, float>();
        }

        /// <summary>
        /// How much an enemy wants to shoot a given unit on a given tile: the damage it would
        /// do there, times what that damage is worth, doubled if it kills. This is how any
        /// sensible opponent picks targets — greedy, this AI, or a person.
        /// </summary>
        static float Attraction(Unit enemy, Unit target, HexTile at) {
            int damage = Mathf.Min(target.Strength, Unit.calculateDamage(enemy, target, at, 0));
            if (damage <= 0) return 0f;
            float a = damage * AIScore.Worth(target);
            if (damage >= target.Strength) a *= 2f;
            return a;
        }
        readonly List<EnemyFire> enemyFire = new List<EnemyFire>();
        readonly Dictionary<HexTile, List<EnemyFire>> firersAt = new Dictionary<HexTile, List<EnemyFire>>();

        public readonly List<Unit> ownUnits = new List<Unit>();
        public readonly List<Unit> enemyUnits = new List<Unit>();
        public readonly List<SupplyPoint> ownHubs = new List<SupplyPoint>();
        public readonly List<SupplyPoint> enemyHubs = new List<SupplyPoint>();

        public AIMap(Player player, AIProfile profile) {
            this.player = player;
            this.profile = profile != null ? profile : AIProfile.Default;
            Rebuild();
        }

        /// <summary>Full refresh. Called once per AI turn, and after any capture or kill that reshapes supply.</summary>
        public void Rebuild() { Rebuild(true); }


        /// <summary>
        /// Refresh after one of this player's own actions. The game has already rebuilt the
        /// threat field (FinishActive), so only the AI's derived view needs recomputing:
        /// who is still alive, where supply reaches, who the enemy can now fire on.
        /// </summary>
        public void RefreshAfterAction() { Rebuild(false); }

        void Rebuild(bool recalculateThreat) {
            thisTurnFields.Clear();
            horizonFields.Clear();
            approaches.Clear();
            costHome.Clear();
            enemyFire.Clear();
            firersAt.Clear();
            conflict.Clear();
            distress.Clear();
            supplyImportance.Clear();
            ownUnits.Clear();
            enemyUnits.Clear();
            ownHubs.Clear();
            enemyHubs.Clear();

            if (recalculateThreat) GameController.RecalculateThreat();
            supply = SupplyNetwork.Solve(player, false);

            // Publish connection state. Solve(applyDrain:false) deliberately does not
            // write it, but the planner reads hub.connected to decide how urgent
            // reconnection is — leaving it stale made every hub look disconnected.
            for (int i = 0; i < supply.connectedHubs.Count; i++) supply.connectedHubs[i].connected = true;
            for (int i = 0; i < supply.strandedHubs.Count; i++) supply.strandedHubs[i].connected = false;

            List<Unit> all = UnitController.uc != null ? UnitController.uc.units : new List<Unit>();
            for (int i = 0; i < all.Count; i++) {
                Unit u = all[i];
                if (u == null || u.tile == null || u.Strength <= 0) continue;
                if (u.owner == player) ownUnits.Add(u); else enemyUnits.Add(u);

                SupplyPoint sp = u.GetComponent<SupplyPoint>();
                if (sp != null) {
                    if (u.owner == player) ownHubs.Add(sp); else enemyHubs.Add(sp);
                }
            }

            SortStable(ownUnits);
            SortStable(enemyUnits);

            ComputeConflict();
            ComputeDistress();
            ScoreSupplyImportance();
            ComputeEnemyFire();
        }

        /// <summary>
        /// Model of tonight's enemy fire, one enemy unit at a time.
        ///
        /// The game's threat field (Heat) sums every enemy that could reach a tile, as if
        /// all of them would fire at it. As an estimate of loss that is a worst case, and
        /// pricing worst cases made the AI refuse every approach: on the shipped roster,
        /// every game against an equal army was a draw with almost no shots fired.
        ///
        /// Here each enemy unit fires once, at someone in its reach, so its fire is shared
        /// among the friendly units exposed to it. That is also exactly why advancing
        /// together is safer than advancing alone, and the AI now sees it. Damage comes
        /// from calculateDamage itself, with the defender's real armour, evasion and the
        /// cover of the tile in question.
        /// </summary>
        void ComputeEnemyFire() {
            enemyFire.Clear();
            firersAt.Clear();
            for (int i = 0; i < enemyUnits.Count; i++) {
                Unit e = enemyUnits[i];
                if (e == null || e.tile == null || !e.CanAttack(e.tile)) continue;

                List<HexTile> firingPositions = new List<HexTile>();
                firingPositions.Add(e.tile);
                if (e.moveAndAttack) {
                    MoveField f = AIPath.ThisTurn(e);
                    foreach (HexTile t in f.Tiles) {
                        if (t != e.tile && f.ReachableThisTurn(t) && AIPath.CanFinishTurnOn(e, t)) firingPositions.Add(t);
                    }
                }

                EnemyFire ef = new EnemyFire { unit = e, tiles = new HashSet<HexTile>() };
                for (int j = 0; j < firingPositions.Count; j++) {
                    List<HexTile> hits = AIPath.TilesInRange(firingPositions[j], e.MinRange, e.AttackRange);
                    for (int k = 0; k < hits.Count; k++) ef.tiles.Add(hits[k]);
                }
                for (int j = 0; j < ownUnits.Count; j++) {
                    Unit u = ownUnits[j];
                    if (u != null && u.tile != null && u.trasnporter == null && ef.tiles.Contains(u.tile)) {
                        ef.exposedFriendlies++;
                        float at = Attraction(e, u, u.tile);
                        ef.attractionOf[u] = at;
                        ef.exposedAttraction += at;
                    }
                }
                enemyFire.Add(ef);
                foreach (HexTile t in ef.tiles) {
                    List<EnemyFire> list;
                    if (!firersAt.TryGetValue(t, out list)) { list = new List<EnemyFire>(); firersAt[t] = list; }
                    list.Add(ef);
                }
            }
        }

        /// <summary>
        /// Strength this unit should expect to lose tonight if it ends its turn on
        /// <paramref name="at"/>: each enemy that can fire there contributes its real damage
        /// against this unit on this tile, times the share of that enemy's attention this
        /// unit would draw among everything it could shoot.
        ///
        /// The share is weighted by attraction, not split evenly: an opponent finishes the
        /// one-strength unit rather than scratching the healthy one. An even split
        /// under-predicted losses exactly when units were already wounded.
        /// </summary>
        public float ExpectedLoss(Unit u, HexTile at) { return ExpectedLoss(u, at, null); }

        /// <param name="exclude">An enemy that will not be firing tonight — typically the
        /// target of a shot that kills it.</param>
        public float ExpectedLoss(Unit u, HexTile at, Unit exclude) {
            if (u == null || at == null) return 0f;
            List<EnemyFire> list;
            if (!firersAt.TryGetValue(at, out list)) return 0f;
            float loss = 0f;
            for (int i = 0; i < list.Count; i++) {
                EnemyFire ef = list[i];
                if (ef.unit == exclude) continue;
                int damage = Mathf.Min(u.Strength, Unit.calculateDamage(ef.unit, u, at, 0));
                if (damage <= 0) continue;
                float mine = Attraction(ef.unit, u, at);
                float others = ef.exposedAttraction;
                float counted;
                if (ef.attractionOf.TryGetValue(u, out counted)) others -= counted;   // replace, not add
                float share = mine / Mathf.Max(0.0001f, mine + Mathf.Max(0f, others));
                loss += damage * share;
            }
            return Mathf.Min(u.Strength, loss);
        }

        /// <summary>
        /// Supply depth of an enemy unit's tile in its owner's network, read from the board
        /// (each player's field is published on their turn). Used to tell damage that will
        /// heal from damage that will stick.
        /// </summary>
        public int TheirSupplyDepth(Unit enemy) {
            if (enemy == null || enemy.tile == null || enemy.owner == null) return 0;
            int d;
            return enemy.tile.supply != null && enemy.tile.supply.TryGetValue(enemy.owner, out d) ? d : 0;
        }

        readonly Dictionary<Unit, Dictionary<HexTile, int>> costHome = new Dictionary<Unit, Dictionary<HexTile, int>>();

        /// <summary>
        /// Turns this unit would need to get from <paramref name="from"/> back into supply,
        /// with its real movement (0 if already supplied; a large number if it cannot within
        /// the planning horizon).
        /// </summary>
        public int TurnsToSupply(Unit u, HexTile from) {
            if (u == null || from == null) return 99;
            if (SupplyDepth(from) > 0) return 0;
            if (u.moveRange <= 0) return 99;
            Dictionary<HexTile, int> cost;
            if (!costHome.TryGetValue(u, out cost)) {
                List<HexTile> supplied = new List<HexTile>(supply.depth.Keys);
                Dictionary<HexTile, HexTile> unused;
                cost = AIPath.CostToReach(u, supplied, u.moveRange * Mathf.Max(1, profile.planningHorizon), out unused);
                costHome[u] = cost;
            }
            int c;
            if (!cost.TryGetValue(from, out c)) return 99;
            return Mathf.CeilToInt(c / (float)Mathf.Max(1, u.moveRange));
        }

        class Approach {
            public Dictionary<HexTile, int> cost;
            public Dictionary<HexTile, HexTile> firingTile;
        }
        readonly Dictionary<Unit, Dictionary<Unit, Approach>> approaches = new Dictionary<Unit, Dictionary<Unit, Approach>>();

        /// <summary>
        /// If <paramref name="mover"/> stood on <paramref name="from"/> at the start of its turn,
        /// the tile it would fire on <paramref name="target"/> from, or null if it cannot reach
        /// any firing position that turn — using real movement, not hex distance.
        /// </summary>
        public HexTile FiringTileNextTurn(Unit mover, Unit target, HexTile from) {
            if (mover == null || target == null || target.tile == null || from == null) return null;
            Dictionary<Unit, Approach> byTarget;
            if (!approaches.TryGetValue(mover, out byTarget)) { byTarget = new Dictionary<Unit, Approach>(); approaches[mover] = byTarget; }
            Approach ap;
            if (!byTarget.TryGetValue(target, out ap)) {
                List<HexTile> ring = new List<HexTile>();
                List<HexTile> inRange = AIPath.TilesInRange(target.tile, mover.MinRange, mover.AttackRange);
                for (int i = 0; i < inRange.Count; i++) {
                    HexTile r = inRange[i];
                    if (!mover.CanEnter(r)) continue;
                    if (r.occupier != null && r.occupier != mover) continue;
                    ring.Add(r);
                }
                ap = new Approach();
                ap.cost = AIPath.CostToReach(mover, ring, Mathf.Max(0, mover.moveRange), out ap.firingTile);
                byTarget[target] = ap;
            }
            int c;
            if (!ap.cost.TryGetValue(from, out c)) return null;
            HexTile f;
            return ap.firingTile.TryGetValue(from, out f) ? f : null;
        }

        /// <summary>
        /// How deep a tile sits in the enemy's supply network, 0..1 (their published field,
        /// saturating at the comfort depth). Standing there blocks their supply through it.
        /// </summary>
        public float EnemySupplyShare(HexTile t) {
            if (t == null || t.supply == null) return 0f;
            int deepest = 0;
            foreach (KeyValuePair<Player, int> kv in t.supply) {
                if (kv.Key == player) continue;
                if (kv.Value > deepest) deepest = kv.Value;
            }
            return AIScore.Depth(deepest, profile);
        }

        /// <summary>Everything on the board, in gold: the stake of the game, and so the worth of winning it.</summary>
        public float BoardStake {
            get {
                float total = 0f;
                for (int i = 0; i < ownUnits.Count; i++) if (ownUnits[i] != null) total += AIScore.Worth(ownUnits[i]) * ownUnits[i].Strength;
                for (int i = 0; i < enemyUnits.Count; i++) if (enemyUnits[i] != null) total += AIScore.Worth(enemyUnits[i]) * enemyUnits[i].Strength;
                return total;
            }
        }

        /// <summary>
        /// Damage this player's other units, still to act this turn, could put on
        /// <paramref name="target"/> — each from any tile it can reach and fire from. Used to
        /// tell an opening hit in a coordinated assault from chip damage nobody will finish.
        /// </summary>
        public float FollowUpDamage(Unit target, Unit except) {
            if (target == null || target.tile == null) return 0f;
            float total = 0f;
            for (int i = 0; i < ownUnits.Count; i++) {
                Unit u = ownUnits[i];
                if (u == null || u == except || !u.active || u.tile == null || u.trasnporter != null) continue;
                if (!u.CanAttack(u.tile)) continue;

                bool canReach = false;
                int d0 = u.tile.Distance(target.tile);
                if (d0 <= u.AttackRange && d0 > u.MinRange) canReach = true;
                else if (u.moveAndAttack) {
                    MoveField f = ReachThisTurn(u);
                    foreach (HexTile t in f.Tiles) {
                        if (!f.ReachableThisTurn(t) || !AIPath.CanFinishTurnOn(u, t)) continue;
                        int d = t.Distance(target.tile);
                        if (d <= u.AttackRange && d > u.MinRange) { canReach = true; break; }
                    }
                }
                if (canReach) total += Mathf.Min(target.Strength, Unit.calculateDamage(u, target, target.tile, 0));
            }
            return total;
        }

        /// <summary>How many enemy units could fire on a tile tonight.</summary>
        public int FirersAt(HexTile at) {
            List<EnemyFire> list;
            return (at != null && firersAt.TryGetValue(at, out list)) ? list.Count : 0;
        }

        /// <summary>Deterministic ordering so batch runs replay exactly.</summary>
        public static void SortStable(List<Unit> list) {
            list.Sort(CompareUnits);
        }

        /// <summary>
        /// Total order on units: by tile, then carriers before their cargo (both report the
        /// transport's tile), then by name. Units without a tile sort last, and compare
        /// equal to each other rather than each claiming to be greater.
        /// </summary>
        public static int CompareUnits(Unit a, Unit b) {
            if (ReferenceEquals(a, b)) return 0;
            bool aNo = a == null || a.tile == null, bNo = b == null || b.tile == null;
            if (aNo || bNo) return aNo == bNo ? 0 : (aNo ? 1 : -1);
            int c = CombatMath.CompareTiles(a.tile, b.tile);
            if (c != 0) return c;
            bool aRiding = a.trasnporter != null, bRiding = b.trasnporter != null;
            if (aRiding != bRiding) return aRiding ? 1 : -1;
            return string.CompareOrdinal(a.name, b.name);
        }

        // ---------------------------------------------------------------- layers

        /// <summary>
        /// conflict = own projected firepower x enemy projected firepower.
        ///
        /// Near zero anywhere either side is unopposed, peaking exactly where the two
        /// projections overlap. A front-line detector out of a single multiplication —
        /// this was the best line in QuickStrat and it ports over almost free, because
        /// Heat is already a per-player influence field.
        /// </summary>
        void ComputeConflict() {
            HexGenerator.forAllTiles(t => {
                float mine = 0, theirs = 0;
                foreach (KeyValuePair<Player, Heat> kv in t.threat) {
                    if (kv.Key == player) mine += kv.Value.firepower;
                    else theirs += kv.Value.firepower;
                }
                float c = mine * theirs;
                if (c > 0) conflict[t] = c;
                return 0;
            });
        }

        /// <summary>
        /// Cut-off units broadcast distress, decaying with distance. Hubs read this to
        /// decide where they are needed; combat units read it to decide what to relieve.
        /// A hub that is itself stranded shouts twice as loud, because its clock is running.
        /// </summary>
        void ComputeDistress() {
            for (int i = 0; i < ownUnits.Count; i++) {
                Unit u = ownUnits[i];
                if (u.tile == null) continue;
                int depth = supply.DepthAt(u.tile);
                if (depth > 0 && !u.OutOfSupplies) continue;

                float volume = 1f;
                if (u.OutOfSupplies) volume += 1f;
                if (u.OutOfFuel) volume += 0.5f;
                SupplyPoint sp = u.GetComponent<SupplyPoint>();
                if (sp != null && !sp.connected) volume += 2f;

                const int radius = 6;
                List<HexTile> nearby = AIPath.TilesInRange(u.tile, -1, radius);
                for (int j = 0; j < nearby.Count; j++) {
                    HexTile t = nearby[j];
                    int d = u.tile.Distance(t);
                    float prev;
                    distress.TryGetValue(t, out prev);
                    distress[t] = prev + volume * (radius - d) / (float)radius;
                }
            }
        }

        /// <summary>
        /// How much a hub is worth as a target beyond its own price: a share of the value
        /// of every unit it is keeping alive. This is what makes the doc's "target the enemy
        /// providing the most supply" fall out of ordinary attack scoring.
        ///
        /// Kept here rather than written onto Unit, because it depends on this player's
        /// profile and on the moment it was computed — and Unit.value() also feeds army
        /// value reporting, which should not change because an AI looked at the board.
        /// </summary>
        void ScoreSupplyImportance() {
            List<SupplyPoint> all = new List<SupplyPoint>(ownHubs);
            all.AddRange(enemyHubs);
            for (int i = 0; i < all.Count; i++) {
                SupplyPoint hub = all[i];
                if (hub == null || hub.myUnit == null || hub.myUnit.tile == null) continue;
                Player p = hub.myUnit.owner;

                int dependents = 0;
                float sustained = 0;
                for (int j = 0; j < p.units.Count; j++) {
                    Unit u = p.units[j];
                    if (u == null || u.tile == null || u == hub.myUnit) continue;
                    if (u.tile.Distance(hub.myUnit.tile) <= hub.range) {
                        dependents++;
                        sustained += AIScore.Worth(u);
                    }
                }
                hub.dependents = dependents;
                supplyImportance[hub.myUnit] = sustained * 0.25f * profile.enemySupplyTargetWeight;
            }
        }

        // ---------------------------------------------------------------- queries

        public int SupplyDepth(HexTile t) { return supply.DepthAt(t); }

        public float Conflict(HexTile t) {
            float v;
            return (t != null && conflict.TryGetValue(t, out v)) ? v : 0f;
        }
        public float Distress(HexTile t) {
            float v;
            return (t != null && distress.TryGetValue(t, out v)) ? v : 0f;
        }
        /// <summary>Tiles with any conflict at all, for front detection.</summary>
        public IEnumerable<HexTile> ContestedTiles { get { return conflict.Keys; } }

        /// <summary>Extra target worth, in gold, for a unit that keeps others supplied.</summary>
        public float SupplyImportance(Unit u) {
            float v;
            return (u != null && supplyImportance.TryGetValue(u, out v)) ? v : 0f;
        }

        /// <summary>Incoming threat against a specific unit, using the correct damage channel.</summary>
        public float ThreatTo(Unit u, HexTile at) {
            if (u == null || at == null) return 0;
            return at.getThreatTo(u);
        }

        /// <summary>Cover this unit could actually use here, after destruction.</summary>
        public int UsableCover(Unit u, HexTile t) {
            if (u == null || t == null) return 0;
            if (LocomotionRules.IsAirborne(u.locomotion)) return 0;
            return Mathf.Min(u.Concealment, t.EffectiveCover);
        }

        public MoveField ReachThisTurn(Unit u) {
            MoveField f;
            if (thisTurnFields.TryGetValue(u, out f)) return f;
            f = AIPath.ThisTurn(u);
            thisTurnFields[u] = f;
            return f;
        }

        public MoveField ReachHorizon(Unit u) {
            MoveField f;
            if (horizonFields.TryGetValue(u, out f)) return f;
            f = AIPath.MultiTurn(u, profile.planningHorizon);
            horizonFields[u] = f;
            return f;
        }

        /// <summary>Drop cached fields for one unit after it moves.</summary>
        public void Invalidate(Unit u) {
            thisTurnFields.Remove(u);
            horizonFields.Remove(u);
        }

        /// <summary>
        /// Drop every cached movement field. Needed after any action, not just for the
        /// unit that acted: a unit moving onto a tile, or dying off one, changes what
        /// every other unit can reach and where it can stop.
        /// </summary>
        public void InvalidateAllFields() {
            thisTurnFields.Clear();
            horizonFields.Clear();
            approaches.Clear();
            costHome.Clear();
        }

        /// <summary>
        /// Refresh supply after the board has changed mid-turn (a hub moved, a unit died
        /// in a corridor). Cheap relative to a full Rebuild and keeps every supply-weighted
        /// decision for the rest of the turn honest.
        /// </summary>
        public void RefreshSupply() {
            supply = SupplyNetwork.Solve(player, false);
            for (int i = 0; i < supply.connectedHubs.Count; i++) supply.connectedHubs[i].connected = true;
            for (int i = 0; i < supply.strandedHubs.Count; i++) supply.strandedHubs[i].connected = false;
        }

        /// <summary>Nearest enemy building of any kind, or null.</summary>
        public Building NearestEnemyBuilding(HexTile from) {
            Building best = null;
            int bestDistance = int.MaxValue;
            for (int i = 0; i < Building.all.Count; i++) {
                Building b = Building.all[i];
                if (b == null || b.tile == null || b.owner == player) continue;
                int d = from.Distance(b.tile);
                if (d < bestDistance || (d == bestDistance && best != null && CombatMath.Precedes(b.tile, best.tile))) {
                    best = b;
                    bestDistance = d;
                }
            }
            return best;
        }

        public void Annotate(HexTile t, string tag) {
            if (t == null || !profile.annotateBoard) return;
            t.aiData.debugTag = tag;
            if (t.textField != null) t.textField.text = tag;
        }

        public void ClearAnnotations() {
            if (!profile.annotateBoard) return;
            HexGenerator.forAllTiles(t => {
                t.aiData.debugTag = null;
                if (t.textField != null) t.textField.text = "";
                return 0;
            });
        }
    }
}
