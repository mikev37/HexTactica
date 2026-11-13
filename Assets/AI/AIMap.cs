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
    public partial class AIMap {

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
            /// <summary>Where it could stand to fire from tonight (its own tile included).</summary>
            public List<HexTile> positions;
            /// <summary>Material value of the best attack it can make on us next turn.</summary>
            public float bestBlow;
            public int exposedFriendlies;
            /// <summary>Sum of how attractive each currently exposed friendly is to this enemy.</summary>
            public float exposedAttraction;
            public readonly Dictionary<Unit, float> attractionOf = new Dictionary<Unit, float>();
            /// <summary>Same, for committed assault units still to act that can step into its reach.</summary>
            public float committedAttraction;
            public readonly Dictionary<Unit, float> committedOf = new Dictionary<Unit, float>();
        }

        /// <summary>
        /// Units committed to a coordinated assault this turn (see SetCommitted). Kept across
        /// mid-turn refreshes; only those still to act count.
        /// </summary>
        readonly HashSet<Unit> committed = new HashSet<Unit>();

        /// <summary>
        /// Declare which units are going in together this turn. Each enemy fires once, so when
        /// several units step into its reach in the same turn its fire is split among them.
        /// Judged one unit at a time, stepping into a defended position never pays — which is
        /// why the AI could not break an opponent that simply waited at home. With the group
        /// declared, every member prices its risk as part of the group.
        /// </summary>
        public void SetCommitted(IEnumerable<Unit> units) {
            committed.Clear();
            if (units != null) foreach (Unit u in units) if (u != null) committed.Add(u);
            ComputeEnemyFire();
            ComputeStrikeTurns();
        }

        public bool IsCommitted(Unit u) { return u != null && committed.Contains(u); }

        readonly Dictionary<Unit, HexTile> assaultObjective = new Dictionary<Unit, HexTile>();

        /// <summary>The objective each assaulting unit is going for, for <see cref="CohesionCost"/>.</summary>
        public void SetAssaultObjectives(Dictionary<Unit, HexTile> objectives) {
            assaultObjective.Clear();
            if (objectives != null) foreach (KeyValuePair<Unit, HexTile> kv in objectives) assaultObjective[kv.Key] = kv.Value;
        }

        /// <summary>
        /// Gold cost of ending the turn on <paramref name="at"/> ahead of the rest of the assault.
        ///
        /// Sharing the enemy's fire only works if the group arrives together. Slow units act
        /// first; a tank moving six hexes then stopped beside the enemy HQ while the grunts it
        /// was assaulting with were still three hexes back, and it was the only thing the
        /// defenders could shoot. The lead is the closest any other unit of the same assault
        /// is to the objective (after its move, or as close as its move could take it if it
        /// has yet to act); every hex beyond that, less a hex of slack, costs a share of the
        /// unit's whole worth.
        /// </summary>
        public float CohesionCost(Unit u, HexTile at) {
            HexTile objective;
            if (u == null || at == null || !assaultObjective.TryGetValue(u, out objective) || objective == null) return 0f;
            int lead = int.MaxValue;
            foreach (KeyValuePair<Unit, HexTile> kv in assaultObjective) {
                Unit v = kv.Key;
                if (v == u || kv.Value != objective || v == null || v.tile == null || v.Strength <= profile.withdrawStrength) continue;
                int d = v.tile.Distance(objective) - (v.active ? v.moveRange : 0);
                if (d < lead) lead = d;
            }
            if (lead == int.MaxValue) return 0f;
            int ahead = Mathf.Max(0, lead - profile.cohesionSlack) - at.Distance(objective);
            if (ahead <= 0) return 0f;
            return ahead * AIScore.Worth(u) * u.Strength * profile.cohesionWeight;
        }

        /// <summary>
        /// Where each friendly unit is heading (its last chosen destination). Shared with the
        /// caller, so it stays current through the turn. Supply hubs read it to move the
        /// network toward where the army is going, not only where it already is.
        /// </summary>
        public Dictionary<Unit, HexTile> intents;

        /// <summary>
        /// Distance from <paramref name="t"/> to the nearest tile where <paramref name="depthAt"/>
        /// is positive, capped at <paramref name="cap"/>.
        /// </summary>
        public static int GapToSupply(HexTile t, System.Func<HexTile, int> depthAt, int cap) {
            if (t == null) return cap;
            if (depthAt(t) > 0) return 0;
            for (int r = 1; r < cap; r++) {
                List<HexTile> ring = AIPath.TilesInRange(t, r - 1, r);
                for (int i = 0; i < ring.Count; i++) if (depthAt(ring[i]) > 0) return r;
            }
            return cap;
        }

        /// <summary>
        /// How much an enemy wants to shoot a given unit on a given tile: the damage it would
        /// do there, times what that damage is worth, doubled if it kills. This is how any
        /// sensible opponent picks targets — greedy, this AI, or a person.
        /// </summary>
        float Attraction(Unit enemy, Unit target, HexTile at) {
            int damage = Mathf.Min(target.Strength, Unit.calculateDamage(enemy, target, at, 0));
            if (damage <= 0) return 0f;
            float a = damage * AIScore.Worth(target);
            if (damage >= target.Strength) a *= 2f;
            // Opponents pick their best target rather than spreading fire in proportion; the
            // exponent sharpens the share toward that. 1 = proportional; large = argmax.
            float k = profile.focusExponent;
            return k == 1f ? a : Mathf.Pow(a / 1000f, k);
        }
        readonly List<EnemyFire> enemyFire = new List<EnemyFire>();
        readonly Dictionary<HexTile, List<EnemyFire>> firersAt = new Dictionary<HexTile, List<EnemyFire>>();

        public readonly List<Unit> ownUnits = new List<Unit>();
        public readonly List<Unit> enemyUnits = new List<Unit>();
        public readonly List<SupplyPoint> ownHubs = new List<SupplyPoint>();
        public readonly List<SupplyPoint> enemyHubs = new List<SupplyPoint>();

        /// <summary>This turn's reserved landings (AIAir.PlanLandings), set by AIPlayer when the turn is prepared.</summary>
        public Dictionary<Unit, AIAir.Landing> landings;
        /// <summary>Tiles the supply chain's links are moving to this turn, and which supply unit: nobody else ends there.</summary>
        public readonly Dictionary<HexTile, Unit> chainTiles = new Dictionary<HexTile, Unit>();

        public AIMap(Player player, AIProfile profile) : this(player, profile, null) { }

        /// <summary>
        /// With <paramref name="carry"/> (last turn's map for the same side), the caches it ended with that
        /// what happened since leaves standing are kept (AIMapCache).
        /// </summary>
        public AIMap(Player player, AIProfile profile, AIMap carry) : this(player, profile, carry, false) { }

        /// <summary>
        /// <paramref name="speculative"/>: read while the opponent is still playing (AIPlayer's speculation), only to
        /// warm caches for the turn's own map: the game's threat field is not recalculated and nothing on the real
        /// board's objects is written.
        /// </summary>
        public AIMap(Player player, AIProfile profile, AIMap carry, bool speculative) {
            this.player = player;
            this.profile = profile != null ? profile : AIProfile.Default;
            this.speculative = speculative;
            if (carry != null && carry.player == player && carry.profile == this.profile) Adopt(carry);
            Rebuild(!speculative);
        }
        /// <summary>Read while the opponent plays, to warm caches only (see the constructor).</summary>
        public readonly bool speculative;

        /// <summary>
        /// Speculation: work out now what the turn's own map will most likely ask for (every enemy's reach, every
        /// unit's move field and space), so the turn's map takes it over (what still stands is kept).
        /// </summary>
        public void Warm() {
            foreach (Unit e in enemyUnits) if (e != null && e.tile != null && e.Firepower > 0) BaseReach(e, false);
            foreach (Unit u in ownUnits) if (u != null && u.tile != null) { ReachThisTurn(u); SpaceOf(u); }
            foreach (Unit e in enemyUnits) if (e != null && e.tile != null) ReachThisTurn(e);
        }

        /// <summary>Full refresh. Called once per AI turn, and after any capture or kill that reshapes supply.</summary>
        public void Rebuild() { Rebuild(true); }


        /// <summary>
        /// Refresh after one of this player's own actions. The game has already rebuilt the
        /// threat field (FinishActive), so only the AI's derived view needs recomputing:
        /// who is still alive, where supply reaches, who the enemy can now fire on.
        /// </summary>
        public void RefreshAfterAction() { Rebuild(false); }

        /// <summary>
        /// Where each enemy could fire from tonight, for the reply forecasts (AIWorld.Evaluate) of
        /// every decision on this board: an enemy far from the unit deciding stands where it stands
        /// whatever that unit does. Cleared whenever the board is read again (after every action).
        /// (Each forecast used to start from scratch: on a 2,800-tile map with 40 enemies that was
        /// two dozen path searches per option, most of the AI's time.)
        /// </summary>
        public readonly AIWorld.Cache replyCache = new AIWorld.Cache();

        /// <summary>
        /// The units a forecast for <paramref name="u"/> plays out (profile.theaterForecast), else null
        /// (all of them). Enemies that could strike, with a move and their range plus
        /// profile.theaterMargin, anywhere <paramref name="u"/> could move or fire this turn; and our
        /// units those enemies could strike, with the same margin. Per unit and board read.
        /// </summary>
        public HashSet<Unit> TheaterOf(Unit u) {
            if (!profile.theaterForecast || u == null || u.tile == null) return null;
            lock (cacheLock) return TheaterOfLocked(u);
        }
        /// <summary>The map's lazily filled caches are shared by forecasts on worker threads (AIParallel): filled under this lock.</summary>
        internal readonly object cacheLock = new object();
        HashSet<Unit> TheaterOfLocked(Unit u) {
            HashSet<Unit> th;
            if (theaters.TryGetValue(u, out th)) return th;
            th = new HashSet<Unit>();
            th.Add(u);
            int margin = Mathf.Max(0, profile.theaterMargin);
            int reachU = Mathf.Max(0, u.moveRange) + Mathf.Max(0, u.AttackRange);
            int deepest = 0;
            foreach (Unit e in enemyUnits) {
                if (e == null || e.tile == null) continue;
                int eReach = (e.moveAndAttack ? Mathf.Max(0, e.moveRange) : 0) + Mathf.Max(0, e.AttackRange);
                if (e.tile.Distance(u.tile) <= reachU + eReach + margin) { th.Add(e); deepest = Mathf.Max(deepest, eReach); }
            }
            foreach (Unit x in ownUnits) {
                if (x == null || x.tile == null || x == u) continue;
                if (x.tile.Distance(u.tile) <= reachU + 2 * deepest + 2 * margin) th.Add(x);
            }
            theaters[u] = th;
            theaterRadius[u] = reachU + 2 * deepest + 2 * margin;
            return th;
        }
        readonly Dictionary<Unit, HashSet<Unit>> theaters = new Dictionary<Unit, HashSet<Unit>>();
        readonly Dictionary<Unit, int> theaterRadius = new Dictionary<Unit, int>();

        /// <summary>How far from <paramref name="u"/> its theatre reaches (int.MaxValue: no theatre).</summary>
        public int TheaterRadius(Unit u) {
            int r;
            if (TheaterOf(u) == null) return int.MaxValue;
            lock (cacheLock) return theaterRadius.TryGetValue(u, out r) ? r : int.MaxValue;
        }

        /// <summary>Where each enemy could move to tonight on the board as it stands (AIWorld.Positions), once per board read.</summary>
        public HashSet<HexTile> BaseReach(Unit e, bool ownSideBlocks) {
            Dictionary<Unit, HashSet<HexTile>> d = ownSideBlocks ? baseReachBlocked : baseReach;
            HashSet<HexTile> r;
            lock (cacheLock) if (!d.TryGetValue(e, out r)) { r = AIWorld.Positions(e, ownSideBlocks); d[e] = r; }
            return r;
        }
        readonly Dictionary<Unit, HashSet<HexTile>> baseReach = new Dictionary<Unit, HashSet<HexTile>>(), baseReachBlocked = new Dictionary<Unit, HashSet<HexTile>>();

        void Rebuild(bool recalculateThreat) {
            readCount++;
            // Only what the board's changes since the last read could have changed (AIMapCache).
            ReadDelta(); PruneUnitCaches();
            theaters.Clear();
            theaterRadius.Clear();
            space = null;
            denialBaseline = null;
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

            { long __p = AIPerf.Begin(); if (recalculateThreat) GameController.RecalculateThreat(); AIPerf.End("map: game threat field", __p); }
            // Our field (with any planned supply-unit ghosts applied), its static/mobile/potential
            // split, the enemy's field and the distance maps: AIMapRead.
            // Connection state is published there too — the planner reads hub.connected.
            { long __p = AIPerf.Begin(); SolveOurSupply(); AIPerf.End("map: supply", __p); }

            List<Unit> all = UnitController.uc != null ? UnitController.uc.units : new List<Unit>();
            for (int i = 0; i < all.Count; i++) {
                Unit u = all[i];
                if (u == null || u.tile == null || u.Strength <= 0) continue;
                if (u.owner == player) ownUnits.Add(u); else if (Player.Hostile(u.owner, player)) enemyUnits.Add(u);   // (an ally's: neither)

                SupplyPoint sp = u.Hub;
                if (sp != null) {
                    if (u.owner == player) ownHubs.Add(sp); else if (Player.Hostile(u.owner, player)) enemyHubs.Add(sp);
                }
            }

            SortStable(ownUnits);
            SortStable(enemyUnits);

            { long __p = AIPerf.Begin(); ComputeConflict(); AIPerf.End("map: conflict", __p); }
            { long __p = AIPerf.Begin(); ComputeDistress(); AIPerf.End("map: distress", __p); }
            { long __p = AIPerf.Begin(); ScoreSupplyImportance(); AIPerf.End("map: supply importance", __p); }
            { long __p = AIPerf.Begin(); ComputeHQDanger(); AIPerf.End("map: HQ danger", __p); }
            { long __p = AIPerf.Begin(); ComputeEnemyFire(); AIPerf.End("map: enemy fire", __p); }
            { long __p = AIPerf.Begin(); sigint = AISigint.Build(this); AIPerf.End("map: fog threat", __p); }
        }

        /// <summary>What the fog may hide (AISigint), or null: nothing to price.</summary>
        public AISigint sigint;
        /// <summary>How well we know the enemy's forces and how they compare; null when off.</summary>
        public AIKnowledge knowledge;
        /// <summary>fogUncertainty: the share of the enemy's forces we cannot place this turn (1 - known / estimate, AIKnowledge); -1 unset.</summary>
        public float fogUncertainty = -1f;
        /// <summary>The enemy's power we cannot place this turn (estimate - known, AIKnowledge); -1 unset.</summary>
        public float fogUnplacedPower = -1f;
        /// <summary>The enemy units we knew of when fogUnplacedPower was set: one seen later in the turn is taken out of the unplaced (not counted twice).</summary>
        public HashSet<Unit> fogKnownAtStart;
        /// <summary>All out (AIKnowledge): enemy known and much weaker.</summary>
        public bool AllOut { get { return knowledge != null && knowledge.allOut; } }

        /// <summary>Strength <paramref name="u"/> should expect to lose on <paramref name="at"/> to enemies the fog may hide (AISigint).</summary>
        /// <summary>
        /// What the fog may cost <paramref name="u"/> at <paramref name="at"/>, in strength. Unknown ground counts here only
        /// without fogUncertainty (the old way: fogAggression, in every use of the loss); with it, only in MoveFogLoss.
        /// </summary>
        public float FogLoss(Unit u, HexTile at) { return sigint != null ? sigint.Loss(u, at, UnknownEverywhere) : 0f; }
        /// <summary>
        /// The fog's cost for a unit choosing where to move (AIFog.FogRisk): lost units, and unknown ground by how much of the
        /// enemy we cannot place (fogUncertainty). The only place unknown ground is feared with fogUncertainty on: supply,
        /// trucks, regrouping, targeting and the coordinator price only the enemy we have seen.
        /// </summary>
        public float MoveFogLoss(Unit u, HexTile at) { return sigint != null ? sigint.Loss(u, at, true) : 0f; }
        bool UnknownEverywhere { get { return profile == null || !profile.fogUncertainty; } }

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

                EnemyFire ef = new EnemyFire { unit = e, tiles = new HashSet<HexTile>(), positions = firingPositions };
                for (int j = 0; j < firingPositions.Count; j++) {
                    List<HexTile> hits = AIPath.TilesInRangeShared(firingPositions[j], e.MinRange, e.AttackRange);
                    for (int k = 0; k < hits.Count; k++) ef.tiles.Add(hits[k]);
                }
                for (int j = 0; j < ownUnits.Count; j++) {
                    Unit u = ownUnits[j];
                    if (u != null && u.tile != null && u.trasnporter == null && ef.tiles.Contains(u.tile) && GameController.Targetable(u)) {
                        ef.exposedFriendlies++;
                        float at = Attraction(e, u, u.tile);
                        ef.attractionOf[u] = at;
                        ef.exposedAttraction += at;
                    }
                }
                // What this enemy could do with its one attack next turn: the most valuable blow it
                // can land on anything of ours, in material (kills counted with the kill bonus).
                for (int j = 0; j < ownUnits.Count; j++) {
                    Unit u = ownUnits[j];
                    if (u == null || u.tile == null || u.trasnporter != null || !ef.tiles.Contains(u.tile) || !GameController.Targetable(u)) continue;
                    int dmg = Mathf.Min(u.Strength, Unit.calculateDamage(e, u, u.tile, 0));
                    if (dmg <= 0) continue;
                    float v = dmg * AIScore.Worth(u) * (dmg >= u.Strength ? profile.killBonus : 1f);
                    if (v > ef.bestBlow) ef.bestBlow = v;
                }
                // Committed assault units that have yet to act and could step into this
                // enemy's fire this turn share its attention too.
                int reach = e.moveAndAttack ? e.moveRange + e.AttackRange : e.AttackRange;
                foreach (Unit c in committed) {
                    if (c == null || !c.active || c.tile == null || c.trasnporter != null || c.Strength <= 0) continue;
                    if (ef.attractionOf.ContainsKey(c)) continue;
                    if (c.tile.Distance(e.tile) > c.moveRange + reach) continue;
                    float at = Attraction(e, c, c.tile);
                    ef.committedOf[c] = at;
                    ef.committedAttraction += at;
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
        public float ExpectedLoss(Unit u, HexTile at, Unit exclude) { return ExpectedLoss(u, at, exclude, UnknownEverywhere); }

        /// <summary>
        /// Same, from the enemy we see and remember only — not from ground we have not seen (AISigint).
        /// For supply moving up behind its own army (never ahead of it): under the fog there is always
        /// unseen ground ahead, and priced as a full-strength enemy it held every supply link where it
        /// stood — the chain came apart behind an army at the enemy's undefended HQ.
        /// </summary>
        public float KnownLoss(Unit u, HexTile at) { return ExpectedLoss(u, at, null, false); }

        float ExpectedLoss(Unit u, HexTile at, Unit exclude, bool unknownGround) {
            if (u == null || at == null) return 0f;
            List<EnemyFire> list;
            float fog = sigint != null ? sigint.Loss(u, at, unknownGround) : 0f;
            if (!firersAt.TryGetValue(at, out list)) return Mathf.Min(u.Strength, fog);
            float loss = fog;
            for (int i = 0; i < list.Count; i++) {
                EnemyFire ef = list[i];
                if (ef.unit == exclude) continue;
                int damage = Mathf.Min(u.Strength, Unit.calculateDamage(ef.unit, u, at, 0));
                if (damage <= 0) continue;
                float mine, others;
                float share = ShareOf(ef, u, at, out mine, out others);   // own prior share replaced, not added
                // Each enemy puts its whole attack into the most valuable target it can reach: the same rule
                // the simulated reply (AIWorld) uses.
                float bestOther = 0f;
                foreach (KeyValuePair<Unit, float> kv in ef.attractionOf) if (kv.Key != u && kv.Value > bestOther) bestOther = kv.Value;
                foreach (KeyValuePair<Unit, float> kv in ef.committedOf) if (kv.Key != u && kv.Value > bestOther) bestOther = kv.Value;
                share = mine >= bestOther ? 1f : 0f;
                loss += damage * share;
            }
            return Mathf.Min(u.Strength, loss);
        }

        AIDenial.Baseline denialBaseline;

        /// <summary>Enemy movement and supply taken away by <paramref name="u"/> standing on <paramref name="at"/> (AIDenial).</summary>
        public AIDenial.Result Denial(Unit u, HexTile at) {
            if (denialBaseline == null) denialBaseline = new AIDenial.Baseline();
            return AIDenial.Measure(u, at, this, denialBaseline);
        }

        AISpace.Board space;

        /// <summary>
        /// Gold the more valuable units gain from <paramref name="u"/> (a screener) ending on
        /// <paramref name="at"/>, with the rest of the screen where the plan puts it (AISpace).
        /// The plan is built once per board state and rebuilt after every action.
        /// </summary>
        public float SpaceDenial(Unit u, HexTile at) {
            return 0f;
            if (space == null) space = AISpace.Prepare(this);
            return AISpace.Credit(space, u, at) * 0f;
        }

        /// <summary>The current screen plan, for diagnostics (null until a screener asks).</summary>
        public AISpace.Board SpacePlan { get { return space; } }

        /// <summary>Share of this enemy's single attack that <paramref name="u"/> would draw standing on <paramref name="at"/>.</summary>
        float ShareOf(EnemyFire ef, Unit u, HexTile at, out float mine, out float others) {
            mine = Attraction(ef.unit, u, at);
            others = ef.exposedAttraction + ef.committedAttraction;
            float counted;
            if (ef.attractionOf.TryGetValue(u, out counted)) others -= counted;
            if (ef.committedOf.TryGetValue(u, out counted)) others -= counted;
            others = Mathf.Max(0f, others);
            return mine / Mathf.Max(0.0001f, mine + others);
        }

        /// <summary>
        /// Gold this unit earns for the side by ending its turn on <paramref name="at"/>, beyond
        /// its own risk: the counterattack it invites and the fire it draws off its friends.
        ///
        /// Attacking first is the advantage. Whoever steps into the other's reach without
        /// striking hands over the first blow — which is why a defender that only reacts held
        /// out against everything, and why the AI, pricing only its own losses, would not step
        /// forward at all against an equal army. Two things make stepping forward right anyway,
        /// and neither was priced:
        ///
        ///   - Concentration. An enemy that comes forward to hit this unit ends its turn within
        ///     reach of our others, and they strike first next turn. Each enemy that could fire
        ///     here contributes the share of its attack this unit would draw, times what our
        ///     units still standing tomorrow could do to it where it would have to stand.
        ///     When we present more targets than they can attack, and each attack exposes the
        ///     attacker to several of ours, the forward step is profitable.
        ///   - Screening. Fire this unit draws is fire not spent on its friends. A grunt in
        ///     front of a tank spares the tank exactly the share it takes, priced at the tank's
        ///     worth. Cheap infantry ahead of expensive armour is the result, not a rule.
        ///
        /// Risk (AIScore.Risk) is still charged separately; this is only the other side of it.
        /// </summary>
        public float FirstStrikeCredit(Unit u, HexTile at) {
            if (u == null || at == null) return 0f;
            List<EnemyFire> list;
            if (!firersAt.TryGetValue(at, out list)) return 0f;
            float counter = 0f;
            for (int i = 0; i < list.Count; i++) {
                EnemyFire ef = list[i];
                Unit e = ef.unit;
                if (e == null || e.tile == null || e.Strength <= 0) continue;
                int hit = Mathf.Min(u.Strength, Unit.calculateDamage(e, u, at, 0));
                if (hit <= 0) continue;
                float mine, others;
                float share = ShareOf(ef, u, at, out mine, out others);

                // Concentration: who hits it back, first, if it comes for us. A counterattacker
                // that has to step up beside `at` to do it is itself then standing where the rest
                // of their army can reach, so its expected loss there is netted off: without that,
                // a lone tank parked in front of their line looked like bait for a counterpunch,
                // and the counterpunch walked the grunts into the same fire one turn later.
                if (profile.counterWeight > 0f) {
                    float damage = 0f, exposure = 0f;
                    for (int j = 0; j < ownUnits.Count; j++) {
                        Unit v = ownUnits[j];
                        if (v == null || v.tile == null || v.trasnporter != null || v.Firepower <= 0) continue;
                        if (v.Hub != null) continue;
                        HexTile from = v == u ? at : v.tile;
                        int d = e.moveAndAttack ? from.Distance(at) - e.AttackRange : from.Distance(e.tile);
                        int reach = v.moveAndAttack ? v.moveRange + v.AttackRange : v.AttackRange;
                        if (d > reach) continue;
                        float alive = 1f - ExpectedLoss(v, from) / Mathf.Max(1, v.Strength);
                        if (v == u) alive = Mathf.Min(alive, 1f - (float)hit / Mathf.Max(1, u.Strength));
                        if (alive <= 0f) continue;
                        float dealt = Unit.calculateDamage(v, e, e.tile, 0) * alive;
                        // Ranged units fire from where they stand; anyone who closes in stands near `at`.
                        float cost = (v.moveAndAttack && v != u && d > 0) ? ExpectedLoss(v, at, e) * AIScore.Worth(v) * profile.riskWeight : 0f;
                        if (dealt * AIScore.Worth(e) <= cost) continue;
                        damage += dealt;
                        exposure += cost;
                    }
                    float landed = Mathf.Min(e.Strength, damage);
                    float value = landed * AIScore.Worth(e);
                    if (landed >= e.Strength) value *= profile.killBonus;
                    counter += share * Mathf.Max(0f, value - exposure);
                }

            }
            return counter * profile.counterWeight * profile.Aggro(0.5f, 1.5f);
        }

        /// <summary>
        /// Supply depth of an enemy unit's tile in its owner's network, as this AI worked it out
        /// (on the board as it knows it). Used to tell damage that will heal from damage that will
        /// stick. (It used to read whatever field was last published on the tiles, which depended on
        /// who had last worked supply out, and under fog of war was the enemy's real one.)
        /// </summary>
        public int TheirSupplyDepth(Unit enemy) {
            if (enemy == null || enemy.tile == null || enemy.owner == null) return 0;
            SupplyNetwork.Result r;
            return theirByPlayer.TryGetValue(enemy.owner, out r) ? r.DepthAt(enemy.tile) : 0;
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
            lock (cacheLock) if (!costHome.TryGetValue(u, out cost)) {
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
            Approach ap;
            lock (cacheLock) {
            if (!approaches.TryGetValue(mover, out byTarget)) { byTarget = new Dictionary<Unit, Approach>(); approaches[mover] = byTarget; }
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
            }
            int c;
            if (!ap.cost.TryGetValue(from, out c)) return null;
            HexTile f;
            return ap.firingTile.TryGetValue(from, out f) ? f : null;
        }

        /// <summary>
        /// How deep a tile sits in the enemy's supply network, 0..1 (their fields as this AI worked
        /// them out, saturating at the comfort depth). Standing there blocks their supply through it.
        /// </summary>
        public float EnemySupplyShare(HexTile t) {
            if (t == null) return 0f;
            return AIScore.Depth(theirSupply.DepthAt(t), profile);
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

        /// <summary>
        /// Damage this player's healthy combat units could put on <paramref name="target"/> next
        /// turn, by hex distance from where they stand now (move plus range). The attacker
        /// counts too, from where it fires. Each unit's damage is weighted by the fraction of it
        /// expected to survive tonight: a unit standing in the enemy's whole army will not be
        /// there to finish anything.
        /// </summary>
        public float NextTurnDamage(Unit target, Unit attacker, HexTile attackerAt) {
            if (target == null || target.tile == null) return 0f;
            float total = 0f;
            for (int i = 0; i < ownUnits.Count; i++) {
                Unit u = ownUnits[i];
                if (u == null || u.tile == null || u.trasnporter != null || u.Firepower <= 0) continue;
                if (u.Strength <= profile.withdrawStrength) continue;
                HexTile at = u == attacker && attackerAt != null ? attackerAt : u.tile;
                int d = at.Distance(target.tile);
                int reach = u.moveAndAttack ? u.moveRange + u.AttackRange : u.AttackRange;
                if (d > reach) continue;
                // Only what survives tonight shoots tomorrow.
                float survives = 1f - ExpectedLoss(u, at) / Mathf.Max(1, u.Strength);
                if (survives <= 0f) continue;
                total += Mathf.Min(target.Strength, Unit.calculateDamage(u, target, target.tile, 0)) * survives;
            }
            return total;
        }

        readonly Dictionary<Unit, float> hqThreat = new Dictionary<Unit, float>();
        /// <summary>0..1 per own HQ: how close the enemy is to being able to destroy it next turn.</summary>
        public readonly Dictionary<Unit, float> hqDanger = new Dictionary<Unit, float>();
        readonly Dictionary<Unit, float> hqAttackPower = new Dictionary<Unit, float>();

        /// <summary>
        /// Gold for standing where this unit could strike, next turn, anything that comes to
        /// attack our HQ: within its reach of the HQ's surrounding tiles.
        ///
        /// Threat to the HQ was only priced in target values, which needs the attacker to be in
        /// reach already. Against a fast push that is too late: a stronger Legacy army drove at the
        /// HQ while our tank went raiding a gun in the far corner, and the HQ fell on turn four.
        /// The guard value is the HQ's danger times what it is worth, times this unit's share of
        /// the force needed to answer the attackers.
        /// </summary>
        public float GuardValue(Unit u, HexTile at) {
            if (u == null || at == null || u.Firepower <= 0) return 0f;
            float total = 0f;
            foreach (KeyValuePair<Unit, float> kv in hqDanger) {
                if (kv.Value <= 0f || kv.Key == null || kv.Key.tile == null) continue;
                int reach = u.moveAndAttack ? u.moveRange + u.AttackRange : u.AttackRange;
                if (at.Distance(kv.Key.tile) > reach + 1) continue;
                float attackers;
                if (!hqAttackPower.TryGetValue(kv.Key, out attackers) || attackers <= 0f) continue;
                float share = Mathf.Min(1f, AIObjectives.Power(u) / attackers);
                total += kv.Value * BoardStake * profile.hqDefenseWeight * share * 0f;
            }
            return total;
        }

        /// <summary>
        /// How much of the game each enemy unit puts at risk by threatening one of our HQs.
        ///
        /// Losing the HQ loses everything, and nothing in the evaluator said so: two enemy
        /// tanks drove at the HQ while our own tank, the only unit that could hurt them, went
        /// off on an advance, and the game ended in four turns. For every own HQ, each enemy
        /// that could strike it next turn (full weight) or the turn after (half) contributes
        /// its damage; danger is that total against the HQ's strength plus a turn of healing.
        /// An enemy's threat is its share of the HQ's strength, times the danger, times what
        /// the HQ is worth. AITargeting adds it to the value of hitting that enemy, so units
        /// both shoot the attackers and — through next-turn targeting — move to where they can.
        /// </summary>
        void ComputeHQDanger() {
            hqThreat.Clear();
            hqDanger.Clear();
            hqAttackPower.Clear();
            if (GameController.gc != null && GameController.gc.rules == RuleSet.Attrition) return;   // HQs out of play
            float stake = BoardStake;
            for (int h = 0; h < ownHubs.Count; h++) {
                SupplyPoint sp = ownHubs[h];
                if (sp == null || !sp.producer || sp.myUnit == null || sp.myUnit.tile == null) continue;
                Unit hq = sp.myUnit;
                Dictionary<Unit, float> contribution = new Dictionary<Unit, float>();
                float potential = 0f;
                for (int i = 0; i < enemyUnits.Count; i++) {
                    Unit e = enemyUnits[i];
                    if (e == null || e.tile == null || e.Firepower <= 0) continue;
                    int d = e.tile.Distance(hq.tile);
                    int reach = e.moveAndAttack ? e.moveRange + e.AttackRange : e.AttackRange;
                    float weight = d <= reach ? 1f : d <= reach + e.moveRange ? 0.5f : 0f;
                    if (weight <= 0f) continue;
                    float dmg = Mathf.Min(hq.Strength, Unit.calculateDamage(e, hq, hq.tile, 0)) * weight;
                    if (dmg <= 0f) continue;
                    contribution[e] = dmg;
                    potential += dmg;
                }
                float danger = Mathf.Clamp01(potential / (hq.Strength + 1f));
                hqDanger[hq] = danger;
                float attackers = 0f;
                foreach (KeyValuePair<Unit, float> kv in contribution) attackers += AIObjectives.Power(kv.Key);
                hqAttackPower[hq] = attackers;
                if (danger <= 0f) continue;
                foreach (KeyValuePair<Unit, float> kv in contribution) {
                    float share = kv.Value / Mathf.Max(1f, potential);
                    float v = share * danger * stake;
                    float prior;
                    hqThreat[kv.Key] = hqThreat.TryGetValue(kv.Key, out prior) ? prior + v : v;
                }
            }
        }

        /// <summary>Gold at stake in this enemy unit's threat to our HQs (see ComputeHQDanger).</summary>
        public float HQThreat(Unit enemy) {
            float v;
            return enemy != null && hqThreat.TryGetValue(enemy, out v) ? v : 0f;
        }

        /// <summary>
        /// Turns before this unit and an enemy combat unit could first exchange fire, either way,
        /// by hex distance and movement (0 = in reach now). 99 when there is no enemy.
        /// </summary>
        // ------------------------------------------------------------ travel distance

        /// <summary>
        /// Land steps depend only on the terrain (elevation, roads, bridges) and the buildings standing on it (their
        /// service tiles): kept for the whole game, one table per layout (AIMapCache.LayoutSignature), shared by both
        /// sides — a bridge falling, a road broken or a building destroyed makes a new layout and a new table.
        /// </summary>
        Dictionary<HexTile, Dictionary<HexTile, int>> landSteps { get {
            long sig = LayoutSignature();
            lock (landByLayout) {
                if (landLayout != sig || landShared == null) { landShared = new Dictionary<HexTile, Dictionary<HexTile, int>>(); landLayout = sig; }
                return landShared;
            }
        } }
        static readonly object landByLayout = new object();
        static Dictionary<HexTile, Dictionary<HexTile, int>> landShared;
        static long landLayout;

        /// <summary>
        /// Steps over land from every tile to <paramref name="to"/>, one a tile, around water (units
        /// ignored: this is the shape of the map, not tonight's traffic). Cached for the turn.
        /// </summary>
        public Dictionary<HexTile, int> LandSteps(HexTile to) {
            if (to == null) return null;
            lock (cacheLock) return LandStepsLocked(to);
        }
        Dictionary<HexTile, int> LandStepsLocked(HexTile to) {
            Dictionary<HexTile, int> d;
            Dictionary<HexTile, Dictionary<HexTile, int>> table = landSteps;
            lock (landByLayout) if (table.TryGetValue(to, out d)) return d;
            d = new Dictionary<HexTile, int>();
            Queue<HexTile> q = new Queue<HexTile>();
            d[to] = 0; q.Enqueue(to);
            while (q.Count > 0) {
                HexTile t = q.Dequeue();
                foreach (HexTile n in t.neighbors) {
                    if (n == null || n.terrain == null || d.ContainsKey(n)) continue;
                    if (n.terrain.elevation < 0) continue;
                    if (!LocomotionRules.BridgeStep(t, n, Locomotion.Wheels)) continue;
                    d[n] = d[t] + 1;
                    q.Enqueue(n);
                }
            }
            lock (landByLayout) table[to] = d;
            return d;
        }

        /// <summary>
        /// How far <paramref name="from"/> is from <paramref name="to"/> for strategy. With
        /// A unit that walks: steps over land (hex distance plus the
        /// map's width when there is no land route). Otherwise, and for aircraft and ships, hex distance.
        /// </summary>
        /// <summary>
        /// No way over land from <paramref name="from"/> to <paramref name="to"/> (water between them: LandSteps does
        /// not reach it). Across water only a ship (or an aircraft) gets a land unit there.
        /// </summary>
        /// <summary>
        /// "Forward" for supply toward <paramref name="to"/>. Over land (LandSteps) when the land it
        /// stands on holds ground our supply buildings reach (a road around water is followed, not cut across); else —
        /// an island, a far shore — over land and water alike, one a tile, so supply carried by ship (or aircraft that
        /// hold where they stop) goes the way it can only go. Cached for the turn.
        /// </summary>
        public Dictionary<HexTile, int> SupplySteps(HexTile to) {
            if (to == null) return null;
            if (!SupplyAcrossWater(to)) return LandSteps(to);
            lock (cacheLock) {
                Dictionary<HexTile, int> d;
                if (mixedSteps.TryGetValue(to, out d)) return d;
                d = new Dictionary<HexTile, int>();
                Queue<HexTile> q = new Queue<HexTile>();
                d[to] = 0; q.Enqueue(to);
                while (q.Count > 0) {
                    HexTile t = q.Dequeue();
                    foreach (HexTile n in t.neighbors) {
                        if (n == null || n.terrain == null || d.ContainsKey(n)) continue;
                        if (t.terrain.elevation >= 0 && n.terrain.elevation >= 0 && !LocomotionRules.BridgeStep(t, n, Locomotion.Wheels)) continue;
                        d[n] = d[t] + 1;
                        q.Enqueue(n);
                    }
                }
                mixedSteps[to] = d;
                return d;
            }
        }
        readonly Dictionary<HexTile, Dictionary<HexTile, int>> mixedSteps = new Dictionary<HexTile, Dictionary<HexTile, int>>();

        /// <summary>The land <paramref name="to"/> stands on holds no ground our supply buildings reach: supply gets there over water.</summary>
        public bool SupplyAcrossWater(HexTile to) {
            if (to == null) return false;
            lock (cacheLock) {
                bool r;
                if (acrossWater.TryGetValue(to, out r)) return r;
                if (staticForLogistics == null) staticForLogistics = SupplyNetwork.SolveStatic(player);
                r = true;
                foreach (KeyValuePair<HexTile, int> kv in LandStepsLocked(to)) if (staticForLogistics.DepthAt(kv.Key) > 0) { r = false; break; }
                acrossWater[to] = r;
                return r;
            }
        }
        readonly Dictionary<HexTile, bool> acrossWater = new Dictionary<HexTile, bool>();
        SupplyNetwork.Result staticForLogistics;

        /// <summary>The land mass a tile is on (bridges join them; water parts them); -1: water. Computed once a turn.</summary>
        public int LandOf(HexTile t) {
            if (t == null || t.terrain == null || t.terrain.elevation < 0) return -1;
            lock (cacheLock) {
                if (landOf == null) {
                    landOf = new Dictionary<HexTile, int>();
                    List<HexTile> all = new List<HexTile>();
                    HexGenerator.forAllTiles(x => { if (x != null) all.Add(x); return 0; });
                    all.Sort(CombatMath.CompareTiles);
                    int n = 0;
                    foreach (HexTile x in all) {
                        if (x.terrain == null || x.terrain.elevation < 0 || landOf.ContainsKey(x)) continue;
                        Stack<HexTile> st = new Stack<HexTile>(); st.Push(x); landOf[x] = n;
                        while (st.Count > 0) {
                            HexTile c = st.Pop();
                            foreach (HexTile y in c.neighbors) if (y != null && y.terrain != null && y.terrain.elevation >= 0 && !landOf.ContainsKey(y)) { landOf[y] = n; st.Push(y); }
                        }
                        n++;
                    }
                }
                int l; return landOf.TryGetValue(t, out l) ? l : -1;
            }
        }
        Dictionary<HexTile, int> landOf;

        public bool AcrossWater(HexTile from, HexTile to) {
            if (from == null || to == null || from == to) return false;
            Dictionary<HexTile, int> d = LandSteps(to);
            return d != null && !d.ContainsKey(from);
        }

        public int Travel(Unit u, HexTile from, HexTile to) {
            if (from == null || to == null) return 99;
            if ((u != null && (LocomotionRules.IsAirborne(u.locomotion) || LocomotionRules.IsNaval(u.locomotion))))
                return from.Distance(to);
            Dictionary<HexTile, int> d = LandSteps(to);
            int s;
            if (d.TryGetValue(from, out s)) return s;
            int sea = SeaTravel(from, to);
            return sea < int.MaxValue ? sea : from.Distance(to) + 64;
        }

        /// <summary>
        /// Across water, while we have a transport ship or a naval yard that builds one: the walk from
        /// <paramref name="from"/> to its shore, the crossing (a step a tile of water), the walk from where it lands to
        /// <paramref name="to"/>, and seaLiftBoardSteps for getting in and out. Troops board from any shore
        /// (Transporting.CanBoardOnto), so the trip starts at whichever is best, not at a yard. int.MaxValue: no way by
        /// sea either.
        /// </summary>
        public int SeaTravel(HexTile from, HexTile to) {
            if (from == null || to == null || !SeaLiftPossible()) return int.MaxValue;
            int c;
            if (!SeaSteps(to).TryGetValue(from, out c)) return int.MaxValue;
            return c + Mathf.Max(0, profile.seaLiftBoardSteps);
        }

        /// <summary>
        /// Where <paramref name="ship"/> and <paramref name="rider"/> (a land unit to take over the water) meet soonest:
        /// a water tile <paramref name="water"/> the ship can lie on, beside a tile <paramref name="shore"/> on the rider's
        /// land it walks to; the rider boards from the shore (Transporting: onto a ship on the water, from beside). Soonest:
        /// the later of the ship's turns and the rider's walk in turns, then the shorter walk, then the ship's nearer.
        /// False: no shore of the rider's land the ship reaches.
        /// </summary>
        public bool Meeting(Unit ship, Unit rider, out HexTile water, out HexTile shore) {
            water = null; shore = null;
            if (ship == null || rider == null || ship.tile == null || rider.tile == null) return false;
            Dictionary<HexTile, int> walk = LandSteps(rider.tile);
            MoveField sail = ReachHorizon(ship);
            int riderMove = Mathf.Max(1, rider.moveRange);
            int best = int.MaxValue, bestWalk = int.MaxValue, bestSail = int.MaxValue;
            foreach (HexTile w in sail.Tiles) {
                if (w.terrain == null || w.terrain.elevation >= 0) continue;   // off the shore, on the water
                if (w != ship.tile && !AIPath.CanFinishTurnOn(ship, w)) continue;
                int sailTurns = w == ship.tile ? 0 : sail.Get(w).turns;
                foreach (HexTile l in w.neighbors) {
                    int steps;
                    if (l == null || l.terrain == null || l.terrain.elevation < 0 || !walk.TryGetValue(l, out steps)) continue;
                    if (!rider.CanEnter(l)) continue;
                    int when = Mathf.Max(sailTurns, (steps + riderMove - 1) / riderMove);
                    int sd = ship.tile.Distance(w);
                    if (when < best || (when == best && (steps < bestWalk || (steps == bestWalk && (sd < bestSail || (sd == bestSail && CombatMath.Precedes(w, water))))))) {
                        best = when; bestWalk = steps; bestSail = sd; water = w; shore = l;
                    }
                }
            }
            return water != null;
        }

        bool? seaLiftPossible;
        /// <summary>We have a transport ship, or a naval yard of ours that builds one (sea lift can carry the army).</summary>
        public bool SeaLiftPossible() {
            lock (cacheLock) {
                if (seaLiftPossible.HasValue) return seaLiftPossible.Value;
                bool can = false;
                foreach (Unit u in ownUnits) if (u != null && LocomotionRules.IsNaval(u.locomotion) && Transporting.IsTransport(u) && u.transportSlots > 0) { can = true; break; }
                if (!can)
                    foreach (Building b in Building.all) {
                        if (b == null || b.owner != player || !b.CanProduce || !AIAssault.ServesShips(b)) continue;
                        foreach (Unit t in b.Purchasable()) if (t != null && LocomotionRules.IsNaval(t.locomotion) && t.transportSlots > 0) { can = true; break; }
                        if (can) break;
                    }
                seaLiftPossible = can;
                return can;
            }
        }

        readonly Dictionary<HexTile, Dictionary<HexTile, int>> seaSteps = new Dictionary<HexTile, Dictionary<HexTile, int>>();
        /// <summary>
        /// The steps to <paramref name="to"/> by sea from every tile not on its land: out from its land over the water,
        /// ashore on any other land, and over that land (a step a tile, bridges by road). Cached for the turn.
        /// </summary>
        Dictionary<HexTile, int> SeaSteps(HexTile to) {
            lock (cacheLock) {
                Dictionary<HexTile, int> r;
                if (seaSteps.TryGetValue(to, out r)) return r;
                r = new Dictionary<HexTile, int>();
                Dictionary<HexTile, int> land = LandSteps(to);
                Dictionary<HexTile, int> cost = new Dictionary<HexTile, int>();
                AIHeap<HexTile> open = new AIHeap<HexTile>();
                foreach (KeyValuePair<HexTile, int> kv in land) { cost[kv.Key] = kv.Value; open.Push(kv.Key, kv.Value); }
                while (open.Count > 0) {
                    HexTile t = open.Pop();
                    int c = cost[t];
                    bool home = land.ContainsKey(t);
                    bool wet = t.terrain != null && t.terrain.elevation < 0;
                    if (!home) { int had; if (!r.TryGetValue(t, out had) || c < had) r[t] = c; }
                    foreach (HexTile n in t.neighbors) {
                        if (n == null || n.terrain == null || land.ContainsKey(n)) continue;
                        bool nWet = n.terrain.elevation < 0;
                        // From its land only out onto the water; on the water anywhere; ashore anywhere; over land by land's rules.
                        if (home && !nWet) continue;
                        if (!wet && !nWet && !LocomotionRules.BridgeStep(t, n, Locomotion.Boots)) continue;
                        int nc = c + 1, prev;
                        if (cost.TryGetValue(n, out prev) && prev <= nc) continue;
                        cost[n] = nc; open.Push(n, nc);
                    }
                }
                seaSteps[to] = r;
                return r;
            }
        }

        /// <summary>Turns before <paramref name="u"/> can strike an enemy combat unit we see (our reach only, not theirs).</summary>
        public int TurnsToEngage(Unit u) {
            if (u == null || u.tile == null || u.Firepower <= 0) return 99;
            int best = 99;
            for (int i = 0; i < enemyUnits.Count; i++) {
                Unit e = enemyUnits[i];
                if (e == null || e.tile == null || e.Firepower <= 0 || e.Hub != null) continue;
                best = Mathf.Min(best, Turns(Travel(u, u.tile, e.tile) - u.AttackRange, u.moveRange));
            }
            return best;
        }

        public int TurnsToContact(Unit u) {
            if (u == null || u.tile == null) return 99;
            int mineBest = 99, theirsBest = 99;
            for (int i = 0; i < enemyUnits.Count; i++) {
                Unit e = enemyUnits[i];
                if (e == null || e.tile == null || e.Firepower <= 0 || e.Hub != null) continue;
                int d = Travel(u, u.tile, e.tile);
                mineBest = Mathf.Min(mineBest, Turns(d - u.AttackRange, u.moveRange));
                theirsBest = Mathf.Min(theirsBest, Turns(d - e.AttackRange, e.moveRange));
            }
            // Their side by real movement, terrain and blocking, where the strike map reaches:
            // water and mountains make the hex-distance estimate optimistic.
            if (strikeTurns.Count > 0) {
                int s = EnemyStrikeTurns(u.tile);
                theirsBest = s < 99 ? Mathf.Min(theirsBest, s) : Mathf.Max(theirsBest, profile.enemyReachTurns + 1);
            }
            return Mathf.Min(mineBest, theirsBest);
        }

        static int Turns(int gap, int move) {
            if (gap <= 0) return 0;
            if (move <= 0) return 99;
            return Mathf.CeilToInt(gap / (float)move);
        }

        /// <summary>Enemy combat units (not hubs, not <paramref name="except"/>) that could fire on a tile tonight.</summary>
        public int CombatFirersAt(HexTile at, Unit except) {
            List<EnemyFire> list;
            if (at == null || !firersAt.TryGetValue(at, out list)) return 0;
            int n = 0;
            for (int i = 0; i < list.Count; i++) {
                Unit e = list[i].unit;
                if (e == null || e == except || e.Hub != null) continue;
                n++;
            }
            return n;
        }

        /// <summary>
        /// Material value of the most valuable attack this enemy could make on us on its next
        /// turn — a tank within reach of our supply hub carries the hub's damage, in full.
        /// </summary>
        public float ThreatBonus(Unit enemy) {
            for (int i = 0; i < enemyFire.Count; i++) if (enemyFire[i].unit == enemy) return enemyFire[i].bestBlow;
            return 0f;
        }

        /// <summary>The enemy units that could fire on a tile tonight.</summary>
        public List<Unit> FirersOf(HexTile at) {
            List<Unit> r = new List<Unit>();
            List<EnemyFire> list;
            if (at != null && firersAt.TryGetValue(at, out list)) foreach (EnemyFire e in list) r.Add(e.unit);
            return r;
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
                    else if (Player.Hostile(kv.Key, player)) theirs += kv.Value.firepower;
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
                SupplyPoint sp = u.Hub;
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
                // Losing the hub costs every unit it sustains a strength a turn once its own supplies run
                // out: one turn of that, in full. (It was a quarter, which priced a hub sustaining five
                // rifles below one rifle that could scratch our tank.)
                supplyImportance[hub.myUnit] = sustained * profile.enemySupplyTargetWeight;
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
            lock (cacheLock) {
                if (thisTurnFields.TryGetValue(u, out f)) return f;
                f = AIPath.ThisTurn(u);
                thisTurnFields[u] = f;
            }
            return f;
        }

        public MoveField ReachHorizon(Unit u) {
            MoveField f;
            lock (cacheLock) {
                if (horizonFields.TryGetValue(u, out f)) return f;
                f = AIPath.MultiTurn(u, profile.planningHorizon);
                horizonFields[u] = f;
            }
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
            approaches.Clear();
            costHome.Clear();
        }

        /// <summary>
        /// Refresh supply after the board has changed mid-turn (a hub moved, a unit died
        /// in a corridor). Cheap relative to a full Rebuild and keeps every supply-weighted
        /// decision for the rest of the turn honest.
        /// </summary>
        public void RefreshSupply() { SolveOurSupply(); }

        /// <summary>Nearest enemy building of any kind, or null.</summary>
        public Building NearestEnemyBuilding(HexTile from) {
            Building best = null;
            int bestDistance = int.MaxValue;
            for (int i = 0; i < Building.all.Count; i++) {
                Building b = Building.all[i];
                if (b == null || b.tile == null || !Player.Hostile(b.owner, player)) continue;
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
            // Under fog of war the reasoning is written only for whoever is looking through this
            // side's eyes: on anyone else's screen it would show where this side's units are going.
            Player viewer = FogOfWar.Viewer;
            if (t.textField != null && (viewer == null || viewer == player)) t.textField.text = tag;
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
