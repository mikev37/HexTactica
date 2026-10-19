using System.Collections.Generic;
using UnityEngine;

namespace HexTactica.AI {

    /// <summary>A contested region of the map, with its own posture and priority.</summary>
    public class Front {
        public HexTile center;
        public Posture posture = Posture.Defensive;
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

        public float ForceRatio {
            get { return enemyStrength <= 0.01f ? 99f : ownStrength / enemyStrength; }
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
            List<Front> fronts = new List<Front>();
            if (HexGenerator.hg == null) return fronts;

            HexTile home = HomeTile(map);

            // Score every tile by contestedness, discounted by distance from home so
            // that a fight on our doorstep outranks an equally hot one across the map.
            List<HexTile> ranked = new List<HexTile>(map.ContestedTiles);

            Dictionary<HexTile, float> weight = new Dictionary<HexTile, float>();
            for (int i = 0; i < ranked.Count; i++) {
                HexTile t = ranked[i];
                int d = home != null ? t.Distance(home) : 0;
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
                    if (fronts[i].center != null && fronts[i].center.Distance(home) <= SUPPRESSION_RADIUS) {
                        covered = true;
                        break;
                    }
                }
                if (!covered) fronts.Add(new Front { center = home, priority = 1f });
            }

            foreach (Front f in fronts) Appraise(f, map, profile);
            AssignUnits(fronts, map);
            foreach (Front f in fronts) DecidePosture(f, map, profile);

            return fronts;
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
            const int reach = SUPPRESSION_RADIUS + 3;

            for (int i = 0; i < map.ownUnits.Count; i++) {
                Unit u = map.ownUnits[i];
                if (u.tile == null) continue;
                int d = u.tile.Distance(f.center);
                if (d <= reach) f.ownStrength += CombatPower(u) * (reach - d + 1) / (float)reach;
            }
            for (int i = 0; i < map.enemyUnits.Count; i++) {
                Unit u = map.enemyUnits[i];
                if (u.tile == null) continue;
                int d = u.tile.Distance(f.center);
                if (d <= reach) f.enemyStrength += CombatPower(u) * (reach - d + 1) / (float)reach;
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
                f.center.Distance(hqUnit.tile) < f.center.Distance(f.objectiveTile))) {
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
                int d = f.center.Distance(f.objectiveTile);
                f.priority += (f.objectiveIsHQ ? 500f : 80f) / (1 + d);
            }
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
                int d = from.Distance(sp.myUnit.tile);
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
                int d = from.Distance(b.tile);
                if (d < bestDistance) { best = b; bestDistance = d; }
            }
            return best;
        }

        /// <summary>Each unit belongs to the nearest front, weighted by that front's priority.</summary>
        static void AssignUnits(List<Front> fronts, AIMap map) {
            if (fronts.Count == 0) return;
            for (int i = 0; i < map.ownUnits.Count; i++) {
                Unit u = map.ownUnits[i];
                if (u.tile == null) continue;

                Front best = null;
                float bestScore = float.NegativeInfinity;
                for (int j = 0; j < fronts.Count; j++) {
                    Front f = fronts[j];
                    if (f.center == null) continue;
                    float score = f.priority / (1f + u.tile.Distance(f.center));
                    if (score > bestScore) { bestScore = score; best = f; }
                }
                if (best != null) best.assigned.Add(u);
            }
        }

        static void DecidePosture(Front f, AIMap map, AIProfile profile) {
            float ratio = f.ForceRatio;

            if (ratio >= profile.aggressionThreshold) f.posture = Posture.Aggressive;
            else if (ratio <= profile.defensiveThreshold) f.posture = Posture.Defensive;
            else f.posture = Posture.Defensive;

            // An enemy HQ within reach overrides caution: the game ends when it falls.
            if (f.objectiveIsHQ && ratio > 0.8f) {
                f.posture = Posture.Aggressive;
            }

            // Never press an attack out of a front whose own supply is collapsing.
            if (f.center != null && map.SupplyDepth(f.center) <= 0 && ratio < 2f) {
                f.posture = Posture.Defensive;
            }
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
