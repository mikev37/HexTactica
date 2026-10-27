using System.Collections.Generic;
using UnityEngine;

namespace HexTactica.AI {

    /// <summary>A contested region of the map, with its own posture and priority.</summary>
    public class Front {
        public HexTile center;
        public Posture posture = Posture.Defensive;
        /// <summary>The front's units go in together this turn (see AIMap.SetCommitted).</summary>
        public bool assault;
        /// <summary>
        /// The front wants to attack but its force is not gathered and healthy: it holds at
        /// <see cref="stagingTile"/> (the supplied ground nearest the objective) until it is.
        /// </summary>
        public bool staging;
        public HexTile stagingTile;
        /// <summary>Fraction of the front's full combat power that is healthy and gathered.</summary>
        public float readiness;
        /// <summary>An attack beyond our supply that passed the readiness gate this turn.</summary>
        public bool gatedAssault;
        /// <summary>
        /// The objective is an HQ this front's healthy units could destroy in one volley, and
        /// what defends it is outmatched: go and take the game. See DecidePosture.
        /// </summary>
        public bool siege;
        public float priority;
        public float ownStrength;
        public float enemyStrength;
        /// <summary>Capturable objective, when the front's target is a building.</summary>
        public Building objective;
        /// <summary>Where the front is pushing: a building, or an enemy HQ unit.</summary>
        public HexTile objectiveTile;
        public bool objectiveIsHQ;
        /// <summary>
        /// What taking the objective is worth, in gold. An HQ ends the game, so it is worth
        /// the whole board: every unit on both sides. Other buildings are worth their
        /// income over ten turns, and at least a couple of average units.
        /// </summary>
        public float objectiveValue;
        public readonly List<Unit> assigned = new List<Unit>();
        /// <summary>A campaign objective's own place to gather (AICampaign: on the way that avoids the enemy's fire), instead of the supplied tile nearest the objective.</summary>
        public HexTile campaignStaging;
        /// <summary>The campaign objective this front is the task force of (AICampaign), told when it goes in.</summary>
        public CampaignObjective campaignObjective;
        /// <summary>A campaign task force with the power its objective needs: it goes (aggressive), gathering first as any attack does.</summary>
        public bool campaignReady;
        /// <summary>The combat power the campaign objective needs (AICampaign: the enemy there times objectiveForceRatio).</summary>
        public float campaignNeed;
        /// <summary>How strongly this front's objective, staging and march pull its units (AIProfile.campaignPull for a campaign's; 1 otherwise).</summary>
        /// <summary>A campaign task force's front (AICampaign): its units move toward its goal in the world comparison too (AIProfile.campaignStep).</summary>
        public bool campaign;
        public float pullScale = 1f;
        /// <summary>How large the staging ground is (hexes around the staging tile).</summary>
        public int campaignStagingRadius;
        /// <summary>A campaign task force keeps together: no reward for ending closer to its goal than this (by travel), and a cost for each step beyond. -1: none.</summary>
        public int paceLimit = -1;

        /// <summary>Whole-army combat power ratio, own over enemy (set by BuildFronts).</summary>
        public float armyRatio = 1f;

        /// <summary>
        /// Local balance of force, but never worse than the balance of the whole armies. The local
        /// count is lopsided whenever the front's centre is nearer one side's lines: equal armies
        /// read 0.79-0.89 on every wide map with this AI in the first seat, every front was
        /// defensive, and the armies never met.
        /// </summary>
        public float ForceRatio {
            get {
                float local = enemyStrength <= 0.01f ? 99f : ownStrength / enemyStrength;
                return Mathf.Max(local, armyRatio);
            }
        }

        public override string ToString() {
            return string.Format("Front({0},{1}) {2} ratio {3:0.00} priority {4:0}",
                center != null ? center.x : 0, center != null ? center.y : 0,
                posture, ForceRatio, priority);
        }
    }

    /// <summary>
    /// Splits the map into fronts and decides the posture of each.
    ///
    /// This is the layer QuickStrat had and HexTactica did not: AIController.makeFronts
    /// sorted tiles by conflict/(1+distance to capital), planted a front at the peak,
    /// suppressed a radius around it, and repeated. The algorithm ports directly — the
    /// conflict field it needs is the same one AIMap already computes — but unlike the
    /// original it is per-player rather than reading a shared static, so both sides can
    /// have fronts at once and the AI can evaluate a front from the enemy's point of view.
    ///
    /// Posture follows the doc: "Fronts where closer to Home AI or where the enemy
    /// outnumbers the AI units are defensive. Fronts with more enemy buildings, HQ or
    /// where the AI outnumbers the enemy are aggressive."
    /// </summary>
    public static class AIStrategic {

        const int SUPPRESSION_RADIUS = 5;
        const int MAX_FRONTS = 8;

        public static List<Front> BuildFronts(AIMap map, AIProfile profile) {
            return BuildFronts(map, profile, null);
        }

        /// <param name="memory">Posture each objective's front had last turn, owned by the
        /// caller so it persists. Updated in place.</param>
        public static List<Front> BuildFronts(AIMap map, AIProfile profile, Dictionary<HexTile, Posture> memory) {
            return BuildFronts(map, profile, memory, null);
        }

        /// <param name="assaulting">Objectives whose fronts were assaulting last turn, owned by
        /// the caller. An assault keeps going until its force falls below the break-off level,
        /// rather than stopping the moment it is no longer fully ready.</param>
        public static List<Front> BuildFronts(AIMap map, AIProfile profile, Dictionary<HexTile, Posture> memory,
                                              HashSet<HexTile> assaulting) {
            List<Front> fronts = new List<Front>();
            if (HexGenerator.hg == null) return fronts;

            HexTile home = HomeTile(map);

            // Score every tile by contestedness, discounted by distance from home so
            // that a fight on our doorstep outranks an equally hot one across the map.
            List<HexTile> ranked = new List<HexTile>(map.ContestedTiles);

            Dictionary<HexTile, float> weight = new Dictionary<HexTile, float>();
            for (int i = 0; i < ranked.Count; i++) {
                HexTile t = ranked[i];
                int d = home != null ? map.Travel(null, t, home) : 0;
                weight[t] = map.Conflict(t) / (1f + d);
            }

            ranked.Sort((a, b) => {
                int c = weight[b].CompareTo(weight[a]);
                if (c != 0) return c;
                return CombatMath.CompareTiles(a, b);
            });

            HashSet<HexTile> suppressed = new HashSet<HexTile>();
            for (int i = 0; i < ranked.Count && fronts.Count < MAX_FRONTS; i++) {
                HexTile peak = ranked[i];
                if (suppressed.Contains(peak)) continue;

                Front f = new Front { center = peak, priority = weight[peak] };
                fronts.Add(f);

                List<HexTile> radius = AIPath.TilesInRange(peak, -1, SUPPRESSION_RADIUS);
                for (int j = 0; j < radius.Count; j++) suppressed.Add(radius[j]);
                suppressed.Add(peak);
            }

            // There is always a front at home, even in total peace, so that units with
            // nothing to do have somewhere sensible to be.
            if (home != null) {
                bool covered = false;
                for (int i = 0; i < fronts.Count; i++) {
                    if (fronts[i].center != null && map.Travel(null, fronts[i].center, home) <= SUPPRESSION_RADIUS) {
                        covered = true;
                        break;
                    }
                }
                if (!covered) fronts.Add(new Front { center = home, priority = 1f });
            }

            float ownArmy = 0f, enemyArmy = 0f;
            for (int i = 0; i < map.ownUnits.Count; i++) ownArmy += CombatPower(map.ownUnits[i]);
            for (int i = 0; i < map.enemyUnits.Count; i++) enemyArmy += CombatPower(map.enemyUnits[i]);
            float army = enemyArmy <= 0.01f ? 99f : ownArmy / enemyArmy;
            foreach (Front f in fronts) { f.armyRatio = profile.useArmyRatio ? army : 0f; Appraise(f, map, profile); }
            AssignUnits(fronts, map);
            Finish(fronts, map, profile, memory, assaulting);
            return fronts;
        }

        /// <summary>Posture, staging and the memories of both, for fronts whose units are assigned (BuildFronts; AICampaign).</summary>
        public static void Finish(List<Front> fronts, AIMap map, AIProfile profile, Dictionary<HexTile, Posture> memory, HashSet<HexTile> assaulting) {
            foreach (Front f in fronts) DecidePosture(f, map, profile, memory);
            HashSet<HexTile> wasAssaulting = assaulting != null ? new HashSet<HexTile>(assaulting) : new HashSet<HexTile>();
            DecideStaging(fronts, map, profile, assaulting);
            foreach (Front f in fronts) if (f.campaignStaging != null) {
                if (profile.campaignGather) GatherThenGo(f, map, profile, wasAssaulting);
                else if (f.campaignReady) f.posture = Posture.Aggressive;   // as first built: goes once given what it needs
            }
            // A campaign task force on the move keeps together toward wherever it is going now.
            foreach (Front f in fronts) if (f.pullScale != 1f || f.campaignStaging != null) f.paceLimit = f.staging ? -1 : Pace(f, map);
            // Stalled: a task force's main body no nearer its objective than last turn has its pace
            // lifted, so the units ahead of it go on. (Held for the main body on a bridge one unit wide,
            // the leaders stood on it and the main body, queued behind them, never came: the army stood
            // still for twenty turns.)
            foreach (Front f in fronts) {
                CampaignObjective o = f.campaignObjective;
                if (o == null || f.staging) continue;
                int raw = Pace(f, map);
                bool stall = raw >= 0 && o.lastPace >= 0 && raw >= o.lastPace;
                if (stall) f.paceLimit = -1;
                o.stalled = stall && o.went ? o.stalled + 1 : 0;
                o.lastPace = raw;
            }
            if (memory != null) {
                // Staging is a pause in an attack, not a change of mind: remember it as aggressive.
                foreach (Front f in fronts) if (f.objectiveTile != null) memory[f.objectiveTile] = f.staging ? Posture.Aggressive : f.posture;
            }
            if (assaulting != null) {
                assaulting.Clear();
                foreach (Front f in fronts) if (f.gatedAssault && f.objectiveTile != null) assaulting.Add(f.objectiveTile);
            }
        }

        static HexTile HomeTile(AIMap map) {
            for (int i = 0; i < Building.all.Count; i++) {
                Building b = Building.all[i];
                if (b != null && b.owner == map.player && b.IsHQ && b.tile != null) return b.tile;
            }
            // No HQ: use the centroid of our own units instead.
            if (map.ownUnits.Count > 0) return map.ownUnits[0].tile;
            return null;
        }

        /// <summary>Tally local strength either side of the front, and find its objective.</summary>
        static void Appraise(Front f, AIMap map, AIProfile profile) {
            if (f.center == null) return;

            // Strength that can be brought to bear here within a couple of turns, full weight,
            // for both sides. A fixed radius around the centre compared the enemy's whole army,
            // sitting at home beside a contact point, against only the few of ours that happened
            // to be forward: equal armies read as 0.2-0.9 and every contact front was declared
            // defensive.
            int turns = Mathf.Max(1, profile.frontReachTurns);
            for (int i = 0; i < map.ownUnits.Count; i++) {
                Unit u = map.ownUnits[i];
                if (u.tile != null && CanArrive(u, f.center, turns, map)) f.ownStrength += CombatPower(u);
            }
            for (int i = 0; i < map.enemyUnits.Count; i++) {
                Unit u = map.enemyUnits[i];
                if (u.tile != null && CanArrive(u, f.center, turns, map)) f.enemyStrength += CombatPower(u);
            }

            f.objective = NearestEnemyBuildingTo(f.center, map);
            if (f.objective != null && f.objective.tile != null) {
                f.objectiveTile = f.objective.tile;
                f.objectiveIsHQ = f.objective.IsHQ;
            }

            // The shipped HQ is a unit carrying a producer SupplyPoint. If it is closer than
            // any building, it is this front's objective; it cannot be captured, only destroyed.
            Unit hqUnit = NearestEnemyHQUnit(f.center, map);
            if (hqUnit != null && (f.objectiveTile == null ||
                map.Travel(null, f.center, hqUnit.tile) < map.Travel(null, f.center, f.objectiveTile))) {
                f.objective = null;
                f.objectiveTile = hqUnit.tile;
                f.objectiveIsHQ = true;
            }

            if (f.objectiveTile != null) {
                float board = 0f;
                int count = 0;
                // Worth() is per strength point; a unit on the board is worth its strength times that.
                for (int i = 0; i < map.ownUnits.Count; i++) { Unit u = map.ownUnits[i]; board += AIScore.Worth(u) * u.Strength; count++; }
                for (int i = 0; i < map.enemyUnits.Count; i++) { Unit u = map.enemyUnits[i]; board += AIScore.Worth(u) * u.Strength; count++; }
                if (f.objectiveIsHQ) f.objectiveValue = board;
                else {
                    float average = count > 0 ? board / count : 0f;
                    f.objectiveValue = Mathf.Max(f.objective != null ? f.objective.income * 10f : 0f, average * 2f);
                }
            }

            // "more buildings or HQs in a front, the more priority it has"
            if (f.objectiveTile != null) {
                int d = map.Travel(null, f.center, f.objectiveTile);
                f.priority += (f.objectiveIsHQ ? 500f : 80f) / (1 + d);
            }
        }

        /// <summary>Can this unit bring its weapon to bear on <paramref name="at"/> within <paramref name="turns"/> turns (by hex distance)?</summary>
        static bool CanArrive(Unit u, HexTile at, int turns, AIMap map) {
            // Over land to within weapon range of it: travel to the tile, less the reach of the gun.
            int d = map.Travel(u, u.tile, at);
            int reach = u.moveRange * turns + u.AttackRange;
            return d <= reach;
        }

        /// <summary>
        /// What a unit contributes to a fight: material worth times remaining strength, and
        /// nothing for units that cannot shoot. An HQ priced at 100,000 with no firepower
        /// would otherwise make every front near home look overwhelmingly strong.
        /// </summary>
        static float CombatPower(Unit u) {
            if (u == null || u.Firepower <= 0 || u.Strength <= 0) return 0f;
            return AIScore.Worth(u) * u.Strength;
        }

        static Unit NearestEnemyHQUnit(HexTile from, AIMap map) {
            Unit best = null;
            int bestDistance = int.MaxValue;
            for (int i = 0; i < map.enemyHubs.Count; i++) {
                SupplyPoint sp = map.enemyHubs[i];
                if (sp == null || !sp.producer || sp.myUnit == null || sp.myUnit.tile == null) continue;
                int d = map.Travel(null, from, sp.myUnit.tile);
                if (d < bestDistance) { best = sp.myUnit; bestDistance = d; }
            }
            return best;
        }

        static Building NearestEnemyBuildingTo(HexTile from, AIMap map) {
            Building best = null;
            int bestDistance = int.MaxValue;
            for (int i = 0; i < Building.all.Count; i++) {
                Building b = Building.all[i];
                if (b == null || b.tile == null || b.owner == map.player) continue;
                int d = map.Travel(null, from, b.tile);
                if (d < bestDistance) { best = b; bestDistance = d; }
            }
            return best;
        }

        /// <summary>Each unit belongs to the nearest front, weighted by that front's priority.</summary>
        /// <summary>Units BuildFronts leaves out (AICampaign: the campaign's task forces have fronts of their own).</summary>
        [System.ThreadStatic] public static HashSet<Unit> Exclude;

        static void AssignUnits(List<Front> fronts, AIMap map) {
            if (fronts.Count == 0) return;
            for (int i = 0; i < map.ownUnits.Count; i++) {
                Unit u = map.ownUnits[i];
                if (u.tile == null) continue;
                if (Exclude != null && Exclude.Contains(u)) continue;

                Front best = null;
                float bestScore = float.NegativeInfinity;
                for (int j = 0; j < fronts.Count; j++) {
                    Front f = fronts[j];
                    if (f.center == null) continue;
                    float score = f.priority / (1f + map.Travel(u, u.tile, f.center));
                    if (score > bestScore) { bestScore = score; best = f; }
                }
                if (best != null) best.assigned.Add(u);
            }
        }

        static void DecidePosture(Front f, AIMap map, AIProfile profile, Dictionary<HexTile, Posture> memory) {
            float ratio = f.ForceRatio;
            // Temperament: a cautious AI needs a bigger edge to call a front aggressive.
            float need = profile.Aggro(1.3f, 0.75f);

            // Hysteresis: between the two thresholds a front keeps last turn's posture for the
            // same objective. Rebuilding fronts from scratch every turn with a single cut-off
            // flipped the posture whenever the ratio sat near it, and units drove six hexes
            // forward and six back for twenty turns.
            Posture previous = Posture.Defensive;
            bool remembered = memory != null && f.objectiveTile != null && memory.TryGetValue(f.objectiveTile, out previous);
            if (!remembered) previous = Posture.Defensive;

            if (ratio >= profile.aggressionThreshold * need) f.posture = Posture.Aggressive;
            else if (ratio <= profile.defensiveThreshold * need) f.posture = Posture.Defensive;
            else f.posture = previous;

            // An enemy HQ within reach overrides caution: the game ends when it falls.
            if (f.objectiveIsHQ && ratio > 0.8f * need) {
                f.posture = Posture.Aggressive;
            }

            // Do not press out of supply without the strength to win there. (This used to demand
            // a 2:1 ratio. The contact point with an opponent who holds near home is always
            // beyond our supply, so it made every such front defensive; units already price the
            // supply they would give up.)
            if (f.center != null && map.SupplyDepth(f.center) <= 0 && ratio < profile.outOfSupplyRatio * need) {
                f.posture = Posture.Defensive;
            }

            // Strong enough to go in together.
            f.assault = f.posture == Posture.Aggressive && ratio >= profile.assaultRatio * need;

            // An HQ we can kill in one volley, with nothing able to stop us, is the game. Its
            // pull is a fraction of the board spread over the distance, and the supply cost of
            // the march outweighed it: three grunts sat in supply for fifty turns, thirteen hexes
            // from an empty HQ they could have destroyed in one turn.
            if (f.posture == Posture.Aggressive && f.objectiveIsHQ && f.objectiveTile != null && f.objectiveTile.occupier != null) {
                Unit hq = f.objectiveTile.occupier;
                float volley = 0f;
                for (int i = 0; i < f.assigned.Count; i++) {
                    Unit u = f.assigned[i];
                    if (u == null || u.tile == null || u.Firepower <= 0 || u.Strength <= profile.withdrawStrength) continue;
                    volley += Mathf.Min(hq.Strength, Unit.calculateDamage(u, hq, hq.tile, 0));
                }
                float defence = 0f;
                int turns = Mathf.Max(1, profile.frontReachTurns);
                for (int i = 0; i < map.enemyUnits.Count; i++) {
                    Unit e = map.enemyUnits[i];
                    if (e != null && e.tile != null && CanArrive(e, f.objectiveTile, turns, map)) defence += CombatPower(e);
                }
                float own = 0f;
                for (int i = 0; i < f.assigned.Count; i++) own += CombatPower(f.assigned[i]);
                f.siege = volley >= hq.Strength + 1 && own >= defence * profile.aggressionThreshold * need;
            }
        }

        const int MAX_STRENGTH = 6;

        /// <summary>
        /// How close to its objective a task force's units may end (AITactical: no reward for closer,
        /// and a cost for each step closer): one move short of where its main body — the middle of its
        /// power — already is, at the pace of its slower half. So the tanks keep pace with the infantry
        /// instead of arriving alone. -1: nothing to keep together.
        /// </summary>
        static int Pace(Front f, AIMap map) {
            HexTile goal = f.objectiveTile;
            if (goal == null) return -1;
            List<KeyValuePair<int, float>> d = new List<KeyValuePair<int, float>>();
            List<int> moves = new List<int>();
            float total = 0f;
            foreach (Unit u in f.assigned) {
                if (u == null || u.tile == null || u.Firepower <= 0 || u.Hub != null) continue;
                float w = CombatPower(u);
                d.Add(new KeyValuePair<int, float>(map.Travel(u, u.tile, goal), w)); total += w;
                if (u.moveRange > 0) moves.Add(u.moveRange);
            }
            if (d.Count < 2 || moves.Count == 0) return -1;
            d.Sort((a, b) => a.Key.CompareTo(b.Key));
            float acc = 0f; int body = d[d.Count - 1].Key;
            foreach (KeyValuePair<int, float> kv in d) { acc += kv.Value; if (acc >= total * 0.5f) { body = kv.Key; break; } }
            moves.Sort();
            return Mathf.Max(0, body - moves[moves.Count / 2]);
        }

        /// <summary>
        /// A campaign task force (AICampaign) gathers before it goes: it holds at its staging tile
        /// (short of the objective, out of the enemy's reach next turn) until the healthy power
        /// gathered within a move of it (or already past it) meets what the objective needs — then
        /// the whole of it goes in together (aggressive, assaulting), and keeps going while what is
        /// left is assaultBreakoff of that need. Without this each unit walked to the objective at its
        /// own speed: the tank arrived alone. (Gating on a share of the whole force instead held a
        /// force at its staging tile for good, with five times what it needed, because the last few
        /// units never got there.)
        /// </summary>
        static void GatherThenGo(Front f, AIMap map, AIProfile profile, HashSet<HexTile> wasAssaulting) {
            HexTile staging = f.campaignStaging, objective = f.objectiveTile;
            if (staging == null || objective == null) return;
            int healthy = Mathf.CeilToInt(MAX_STRENGTH * profile.readyStrength);
            int stagingToGo = map.Travel(null, staging, objective);
            int radius = Mathf.Max(1, f.campaignStagingRadius);
            // Gathered: healthy, on the staging ground (within its radius of the staging tile) or already past it.
            float gathered = 0f, committed = 0f, force = 0f;
            foreach (Unit u in f.assigned) {
                if (u == null || u.tile == null || u.Firepower <= 0 || u.Hub != null) continue;
                float w = AIScore.Worth(u);
                if (u.Strength >= healthy) force += w * u.Strength;
                bool there = map.Travel(u, u.tile, staging) <= radius || map.Travel(u, u.tile, objective) < stagingToGo;
                if (there && u.Strength >= healthy) gathered += w * u.Strength;
                if (u.Strength > profile.withdrawStrength) committed += w * u.Strength;
            }
            float need = Mathf.Max(1f, f.campaignNeed);
            bool going = wasAssaulting.Contains(objective);
            // It goes once the healthy power on the staging ground is campaignGatherShare of its need (0.8: so
            // it does not wait for the last of it), and keeps going while what is left is assaultBreakoff of
            // that need; it falls back only when outnumbered two to one (campaignFallbackRatio, AICampaign).
            f.readiness = going ? committed / need : Mathf.Max(gathered / Mathf.Max(1f, force), gathered / need);
            bool go = going ? committed >= need * profile.assaultBreakoff * profile.Aggro(1.4f, 0.5f)
                            : gathered >= need * Mathf.Clamp01(profile.campaignGatherShare);
            // (It went in once 60% of its force had gathered — 0.9 to one — and fell back at one to one: it
            // attacked under strength and retreated as soon as it met an even enemy, turn after turn.)
            f.stagingTile = staging;
            if (go) {
                f.staging = false; f.posture = Posture.Aggressive; f.assault = true; f.gatedAssault = true;
                if (f.campaignObjective != null) f.campaignObjective.went = true;
            } else {
                // Gathering is not defending: the force is moving up, its units still fight as they see fit.
                f.staging = true; f.assault = false; f.gatedAssault = false;
            }
        }

        /// <summary>
        /// Gate an attack beyond our supply on the force being gathered and healthy.
        ///
        /// Against an opponent who holds, the contact point is beyond our supply and theirs
        /// heals. Units that went the moment the front turned aggressive arrived one at a time
        /// in order of speed — the tank alone, then the grunts, then the guns — and each was
        /// destroyed in turn by a defender that was whole again the next morning. Wounded units
        /// pressed on, because the HQ was worth the board and nothing said "not yet". The
        /// front now waits on the edge of its own supply, where the wounded heal, until enough
        /// of its power is there to go in together; and once in, keeps going until that power
        /// is spent below the break-off level, then falls back and does it again.
        /// </summary>
        static void DecideStaging(List<Front> fronts, AIMap map, AIProfile profile, HashSet<HexTile> assaulting) {
            // Every attacking front aimed at the same objective is one attack: a lone tank on a
            // flanking front is not "ready" because it is all of its own front.
            Dictionary<HexTile, List<Front>> byObjective = new Dictionary<HexTile, List<Front>>();
            List<HexTile> order = new List<HexTile>();
            for (int i = 0; i < fronts.Count; i++) {
                Front f = fronts[i];
                if (f.posture != Posture.Aggressive || f.objectiveTile == null || f.center == null) continue;
                if (f.campaignStaging != null) continue;   // a campaign task force gathers its own way (GatherThenGo)
                // A fight already inside our supply is not an attack to stage: it is a defence.
                if (map.SupplyDepth(f.center) > 0) continue;
                List<Front> group;
                if (!byObjective.TryGetValue(f.objectiveTile, out group)) {
                    group = new List<Front>();
                    byObjective[f.objectiveTile] = group;
                    order.Add(f.objectiveTile);
                }
                group.Add(f);
            }

            int turns = Mathf.Max(1, profile.frontReachTurns);
            int healthy = Mathf.CeilToInt(MAX_STRENGTH * profile.readyStrength);
            foreach (HexTile objective in order) {
                List<Front> group = byObjective[objective];
                HexTile staging = null;
                foreach (Front f in group) if (f.campaignStaging != null) { staging = f.campaignStaging; break; }
                if (staging == null) staging = StagingTile(map, objective);
                if (staging == null) continue;

                float full = 0f, gathered = 0f, committedPower = 0f;
                foreach (Front f in group) {
                    for (int i = 0; i < f.assigned.Count; i++) {
                        Unit u = f.assigned[i];
                        if (u == null || u.tile == null || u.Firepower <= 0 || u.Hub != null) continue;
                        float w = AIScore.Worth(u);
                        full += w * MAX_STRENGTH;
                        if (u.Strength >= healthy && map.Travel(u, u.tile, staging) <= profile.stagingRadius) gathered += w * u.Strength;
                        if (u.Strength > profile.withdrawStrength) committedPower += w * u.Strength;
                    }
                }
                if (full <= 0f) continue;

                bool wasAssaulting = assaulting != null && assaulting.Contains(objective);
                float readiness = wasAssaulting ? committedPower / full : gathered / full;
                bool go = wasAssaulting ? readiness >= profile.assaultBreakoff * profile.Aggro(1.4f, 0.5f)
                                         : readiness >= Mathf.Min(0.95f, profile.assaultReadiness * profile.Aggro(1.25f, 0.7f));
                foreach (Front f in group) {
                    f.stagingTile = staging;
                    f.readiness = readiness;
                    if (go) { f.gatedAssault = f.assault; continue; }
                    f.staging = true;
                    f.assault = false;
                    f.posture = Posture.Defensive;
                }
            }
        }

        /// <summary>The tile inside our own supply nearest the objective.</summary>
        static HexTile StagingTile(AIMap map, HexTile objective) {
            HexTile best = null;
            int bestD = int.MaxValue;
            foreach (HexTile t in HexGenerator.hg.tiles) {
                if (t == null || map.SupplyDepth(t) <= 0) continue;
                int d = map.Travel(null, t, objective);
                if (d < bestD || (d == bestD && CombatMath.Precedes(t, best))) { best = t; bestD = d; }
            }
            return best;
        }

        /// <summary>Each combat unit on an assaulting front and that front's objective, for AIMap.CohesionCost.</summary>
        public static Dictionary<Unit, HexTile> AssaultObjectives(List<Front> fronts) {
            Dictionary<Unit, HexTile> result = new Dictionary<Unit, HexTile>();
            for (int i = 0; i < fronts.Count; i++) {
                Front f = fronts[i];
                if (!f.assault || f.objectiveTile == null) continue;
                for (int j = 0; j < f.assigned.Count; j++) {
                    Unit u = f.assigned[j];
                    if (u == null || u.Firepower <= 0 || u.Hub != null) continue;
                    result[u] = f.objectiveTile;
                }
            }
            return result;
        }

        /// <summary>Every unit on an assaulting front, for AIMap.SetCommitted.</summary>
        public static List<Unit> AssaultUnits(List<Front> fronts) {
            List<Unit> units = new List<Unit>();
            for (int i = 0; i < fronts.Count; i++) if (fronts[i].assault) units.AddRange(fronts[i].assigned);
            return units;
        }

        /// <summary>Which front a unit belongs to, for posture lookup.</summary>
        public static Front FrontFor(Unit u, List<Front> fronts) {
            for (int i = 0; i < fronts.Count; i++) {
                if (fronts[i].assigned.Contains(u)) return fronts[i];
            }
            return fronts.Count > 0 ? fronts[0] : null;
        }
    }
}
