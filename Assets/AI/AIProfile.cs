using UnityEngine;

namespace HexTactica.AI {

    /// <summary>
    /// Every tunable the AI has, in one asset.
    ///
    /// QuickStrat had roughly forty magic numbers scattered through AINation and
    /// the comparator classes — 1100, 3000, 700/2000, a ^5 exponent, an aggression
    /// float that was declared and never read. None of them could be varied without
    /// a recompile, which meant none of them could ever be measured. Putting them
    /// here is what makes the batch harness able to answer "is 5 the right exponent".
    /// </summary>
    [CreateAssetMenu(menuName = "HexTactica/AI Profile")]
    public class AIProfile : ScriptableObject {

        [Header("Identity")]
        public string profileName = "Default";

        [Header("Supply")]
        [Tooltip("Supply depth treated as fully sustained; the depth signals saturate here.")]
        [Range(1, 10)]
        public int supplyComfort = 3;


        [Tooltip("Worth of bringing supply to all contested ground, as strength points of the whole army. Pulls hubs forward ahead of the line so the line can advance.")]
        [Range(0f, 3f)]
        public float forwardSupplyWeight = 0.5f;

        [Tooltip("Multiplier on a hub's target worth for the units it sustains (base: a quarter of their combined value).")]
        public float enemySupplyTargetWeight = 1f;

        [Header("Combat")]
        [Tooltip("How much predicted retaliation damage discounts an attack.")]
        [Range(0f, 2f)]
        public float retaliationWeight = 1f;

        [Tooltip("Bonus multiplier when an attack finishes the target.")]
        public float killBonus = 1.35f;

        [Tooltip("Worth of non-lethal damage on a target inside its own supply, which heals a strength per turn, as a fraction of face value.")]
        [Range(0f, 1f)]
        public float healingDamageFactor = 0.5f;

        [Tooltip("Worth of damage that will not be finished on a unit that does not fight (an HQ, a supply hub), as a fraction of face value. It does not weaken them, and they heal.")]
        [Range(0f, 1f)]
        public float hqChipFactor = 0.1f;

        [Tooltip("Penalty per point of damage wasted beyond what was needed to kill.")]
        public float overkillPenalty = 0.35f;

        [Tooltip("Value discount on attacks that cannot be delivered this turn.")]
        [Range(0f, 1f)]
        public float imminentDiscount = 0.25f;

        [Tooltip("Multiplier on expected gold lost by ending a turn somewhere. 1 = take expected losses at face value; above 1 is cautious, below 1 reckless.")]
        public float riskWeight = 1f;

        [Tooltip("Tiebreak preference for cover, beyond the protection already priced into expected losses.")]
        public float coverWeight = 1f;

        [Tooltip("On aggressive fronts, worth of standing inside the enemy's supply network (which blocks it), as a fraction of the unit's worth per strength.")]
        [Range(0f, 2f)]
        public float cutSupplyWeight = 0.5f;

        [Tooltip("Artillery: worth of each friendly combat unit within range, as a fraction of that unit's worth per strength.")]
        [Range(0f, 1f)]
        public float supportWeight = 0.1f;

        [Header("Posture")]
        [Tooltip("Multiplier on risk for units on an aggressive front. Below 1 = willing to take losses to close.")]
        [Range(0f, 2f)]
        public float aggressiveRisk = 0.5f;

        [Tooltip("Multiplier on risk for units on a defensive front.")]
        [Range(0f, 2f)]
        public float defensiveRisk = 1f;

        [Tooltip("Front is treated as aggressive above this local force ratio (own value / enemy value).")]
        public float aggressionThreshold = 1.15f;

        [Tooltip("Front is treated as defensive below this local force ratio.")]
        public float defensiveThreshold = 0.85f;

        [Tooltip("Strength at or below which a unit prefers to withdraw and heal.")]
        [Range(0, 6)]
        public int withdrawStrength = 2;

        [Tooltip("Pull toward a front's objective, as a fraction of the objective's value at distance zero, falling off as 1/(1+d).")]
        [Range(0f, 0.5f)]
        public float objectivePull = 0.01f;

        [Tooltip("Worth of standing on fully contested ground, as a fraction of the unit's own worth.")]
        public float holdWeight = 0.4f;

        [Header("Planning")]
        [Tooltip("How many turns ahead approach routes are planned.")]
        [Range(1, 12)]
        public int planningHorizon = 5;

        [Tooltip("Bonus, as a fraction of the unit's worth, for continuing toward last turn's destination. Stops units flip-flopping between two plans.")]
        [Range(0f, 1f)]
        public float commitment = 0.25f;

        [Tooltip("Cost per turn of delay when comparing destinations of different value.")]
        public float turnPenalty = 0.12f;

        [Tooltip("How many candidate hub positions get an expensive full supply re-solve.")]
        [Range(1, 40)]
        public int supplyCandidateCount = 12;

        [Tooltip("Turns of hub autonomy below which the AI treats reconnection as urgent.")]
        [Range(0, 6)]
        public int hubPanicThreshold = 3;

        [Header("Transport")]
        [Tooltip("Turns saved that justify the two-turn overhead of loading and unloading.")]
        [Range(0, 6)]
        public int transportWorthwhileSaving = 2;

        [Tooltip("How much more cautious a transport is while carrying cargo. Losing a transport loses its cargo too.")]
        public float loadedTransportRisk = 2.5f;

        [Header("Debug")]
        [Tooltip("Write decision reasoning onto the tiles as text.")]
        public bool annotateBoard = true;

        static AIProfile fallback;

        /// <summary>Profile used when a scene forgot to assign one. Never null.</summary>
        public static AIProfile Default {
            get {
                if (fallback == null) {
                    fallback = ScriptableObject.CreateInstance<AIProfile>();
                    fallback.profileName = "Fallback";
                }
                return fallback;
            }
        }
    }
}
