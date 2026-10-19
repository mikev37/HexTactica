using UnityEngine;

namespace HexTactica.AI {

    /// <summary>
    /// The evaluator's single currency: gold, the same unit Unit.value() is in.
    ///
    /// Attack value was already gold (damage x target value). Risk, cover, holding
    /// ground and supply depth were raw map signals — firepower points, cover points,
    /// tile counts — added straight onto gold. On the shipped units (costs 2,000 to
    /// 100,000) that put risk roughly three orders of magnitude below reward: a tile
    /// where a Grunt expects to lose ~1,800 gold of strength scored a risk penalty of 2.
    /// The AI was risk-blind, and fixtures with costs of 80-400 hid it.
    ///
    /// Every additive term now goes through here, expressed as a fraction of the acting
    /// unit's own worth or as an expected gold loss, so the profile weights mean what
    /// they say regardless of how expensive the roster is.
    /// </summary>
    public static class AIScore {

        /// <summary>
        /// A unit's MATERIAL worth in gold per strength point: build cost plus the veterancy
        /// premium, over six. Losing one strength costs exactly this.
        ///
        /// Deliberately not Unit.value(), which adds a tenth of the best attack the unit
        /// currently threatens. That positional term is right for choosing *targets* — a unit
        /// threatening your HQ is worth shooting — but not for a unit's own stakes. Near the
        /// shipped HQ (16,667 per strength) it inflated a Tank from ~1,166 to ~7,800, and every
        /// term priced in worth — risk, supply cost, holding ground, commitment — swung by
        /// thousands as units moved, sending a Tank to starve in an empty corner.
        /// </summary>
        public static float Worth(Unit u) {
            if (u == null) return 1f;
            float material = u.cost + u.Veterancy * u.cost * 0.2f;
            return Mathf.Max(1f, material / 6f);
        }

        /// <summary>A unit's worth as a target: material plus what it currently threatens (Unit.value()).</summary>
        public static float TargetWorth(Unit u) {
            return u == null ? 1f : Mathf.Max(1f, u.value());
        }

        /// <summary>
        /// Strength this unit should expect to lose if it ends its turn here. See
        /// AIMap.ExpectedLoss: real damage per enemy unit, on this tile's cover, with each
        /// enemy's single attack shared among the friendly units it could pick from.
        /// </summary>
        public static float ExpectedLossF(Unit unit, HexTile at, AIMap map) {
            return map.ExpectedLoss(unit, at);
        }

        public static int ExpectedLoss(Unit unit, HexTile at, AIMap map) {
            return Mathf.RoundToInt(ExpectedLossF(unit, at, map));
        }

        /// <summary>Expected loss priced in gold.</summary>
        public static float Risk(Unit unit, HexTile at, AIMap map, AIProfile p) {
            return ExpectedLossF(unit, at, map) * Worth(unit) * p.riskWeight;
        }

        /// <summary>
        /// Cost, in gold, of ending a turn outside supply: one turn of attrition pressure.
        /// Small while the unit still carries supplies, a full strength once it has none.
        /// </summary>
        public static float OutOfSupplyCost(Unit unit, HexTile at, AIMap map) {
            if (unit == null || at == null || map.SupplyDepth(at) > 0) return 0f;
            // Dry: attrition takes a strength per turn. Still carrying supplies: a quarter of
            // that, for the turn of endurance spent and the healing forgone.
            float strength = Worth(unit);
            return unit.suppliesMax > 0 && unit.supplies <= 1 ? strength : strength * 0.25f;
        }

        /// <summary>
        /// Strength a unit will lose to attrition if it goes to <paramref name="at"/>, arriving
        /// in <paramref name="turnsToArrive"/> turns, and then has to get back into supply.
        ///
        /// A unit out of supply spends a point of supplies a turn and then a point of strength
        /// a turn. If the way home takes longer than the supplies it will have left, every turn
        /// beyond that is a lost strength point. Pricing one night at a time missed this: a
        /// tank chased a fleeing supply hub into a corner, each night's cost smaller than the
        /// bait, until it starved to death unable to get back.
        /// </summary>
        public static float StrandingCost(Unit unit, HexTile at, int turnsToArrive, AIMap map) {
            if (unit == null || at == null || unit.suppliesMax <= 0) return 0f;
            if (map.SupplyDepth(at) > 0) return 0f;
            int home = map.TurnsToSupply(unit, at);
            int left = unit.supplies - Mathf.Max(0, turnsToArrive);
            int starving = Mathf.Max(0, home - Mathf.Max(0, left));
            return Mathf.Min(unit.Strength, starving) * Worth(unit);
        }

        /// <summary>
        /// Fraction of its firepower a unit still has after tonight. Attacker damage in
        /// calculateDamage scales with current strength, so an attack planned for tomorrow is
        /// worth only what the unit has left to make it with.
        /// </summary>
        public static float SurvivingFraction(Unit unit, HexTile tonight, AIMap map, Unit excluded) {
            if (unit == null || unit.Strength <= 0) return 0f;
            float loss = map.ExpectedLoss(unit, tonight, excluded);
            return Mathf.Clamp01((unit.Strength - loss) / unit.Strength);
        }

        /// <summary>Appetite for risk on a front of this posture (see AIProfile).</summary>
        public static float PostureRisk(AIProfile p, Posture posture) {
            return posture == Posture.Aggressive ? p.aggressiveRisk : p.defensiveRisk;
        }

        /// <summary>Contestedness as a 0..1 signal. Conflict is a product of two fields, so it is unbounded.</summary>
        public static float Hold(float conflict) {
            return conflict <= 0f ? 0f : conflict / (conflict + 25f);
        }

        /// <summary>Distress as a 0..1 signal.</summary>
        public static float Relief(float distress) {
            return distress <= 0f ? 0f : distress / (distress + 2f);
        }

        /// <summary>Supply depth as a 0..1 signal, saturating at the comfort depth.</summary>
        public static float Depth(int depth, AIProfile p) {
            return Mathf.Clamp01((float)depth / Mathf.Max(1, p.supplyComfort));
        }

        /// <summary>Small, worth-scaled preference for cover. A tiebreak: real protection is in ExpectedLoss.</summary>
        public static float CoverPreference(Unit unit, HexTile at, AIMap map, AIProfile p) {
            return map.UsableCover(unit, at) / 3f * Worth(unit) * 0.05f * p.coverWeight;
        }

        /// <summary>Reluctance to move at all, so near-ties do not make units jitter.</summary>
        public static float StayBonus(Unit unit, float factor) {
            return Worth(unit) * factor;
        }

        /// <summary>Prefer arriving sooner. Scale-free, so it means the same at any cost level.</summary>
        public static float Delay(float score, int turns, AIProfile p) {
            return score - p.turnPenalty * turns * Mathf.Abs(score);
        }
    }
}
