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

        [Header("Temperament")]
        [Tooltip("0 = fight only on clearly favourable terms; 1 = trade everything in a furball. At 0.5 every other weight applies exactly as written. It scales risk appetite, how ready an attack must be, the force ratio needed to attack or take on an objective, how far it trusts its counterattack, when an attack breaks off, and when damaged units rotate out. Default 0: across 240 measured games the most cautious setting won the most (177 vs 121 at 1) and drew the least, because furballs end in mutual annihilation, which is a draw.")]
        [Range(0f, 1f)]
        public float aggression = 0f;

        /// <summary>
        /// A multiplier set by <see cref="aggression"/>: <paramref name="atZero"/> at 0, exactly
        /// 1 at 0.5, <paramref name="atOne"/> at 1, linear in between. Every knob the temperament
        /// moves goes through here, so the default profile is unchanged by it.
        /// </summary>
        public float Aggro(float atZero, float atOne) {
            float a = Mathf.Clamp01(aggression);
            return a < 0.5f ? Mathf.Lerp(atZero, 1f, a / 0.5f) : Mathf.Lerp(1f, atOne, (a - 0.5f) / 0.5f);
        }

        [Header("Tile choice")]
        [Tooltip("Choose tiles in tiers, as the original AttackMove did: damage dealt, then damage received, then enemy movement and supply denied plus everything positional. Off: everything is added in gold.")]
        public bool tieredChoice = true;

        [Tooltip("How tiered choice orders its tiers. 0: damage dealt, then damage received, then denial. 1: damage received first, then damage dealt. 2: simulate each option against the enemy's reply and keep the best resulting worlds (AIWorld), then denial and position.")]
        [Range(0, 2)]
        public int choiceMode = 0;

        [Tooltip("choiceMode 2: keep options whose simulated world is within this many of the acting unit's strength points of the best.")]
        [Range(0f, 6f)]
        public float worldTie = 1f;

        [Tooltip("Tier 1: keep every option whose attack value is at least this fraction of the best. 1 = only the best shot; 0.5 = anything doing half as much, which leaves room for the later tiers.")]
        [Range(0f, 1f)]
        public float attackTie = 0.5f;

        [Tooltip("Tier 2: keep every option whose expected damage received is within this many of the acting unit's strength points of the least.")]
        [Range(0f, 6f)]
        public float receivedTie = 1f;

        [Tooltip("Tier 3: worth of each enemy-reachable tile (two turns, summed over enemy units) taken away, as a fraction of the acting unit's worth.")]
        public float denialTileWeight = 0.05f;

        [Tooltip("Tier 3: worth of each tile taken out of enemy supply, as a fraction of the acting unit's worth.")]
        public float supplyTileWeight = 0.05f;

        [Tooltip("Tier 3: worth of cutting an enemy unit off from supply, as a fraction of that unit's worth.")]
        public float supplyCutWeight = 1f;

        [Header("Supply")]
        [Tooltip("Supply depth treated as fully sustained; the depth signals saturate here.")]
        [Range(1, 10)]
        public int supplyComfort = 3;


        [Tooltip("Worth of bringing supply to all contested ground, as strength points of the whole army. Pulls hubs forward ahead of the line so the line can advance.")]
        [Range(0f, 3f)]
        public float forwardSupplyWeight = 0.5f;

        [Tooltip("Hubs: worth of closing the gap between the network and where a friendly unit is heading, as strength points of that unit per turn (full gap closed = 1 x weight).")]
        [Range(0f, 6f)]
        public float intentSupplyWeight = 2f;

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

        [Tooltip("Worth of a hit on an enemy HQ that is not finished this turn but leaves it killable next turn by the units now in reach, as a fraction of the win.")]
        [Range(0f, 1f)]
        public float hqSetupFactor = 0.1f;

        [Tooltip("Worth of removing a threat to our own HQ, as a fraction of what the HQ's loss would cost, scaled by how much of the threat the attack removes and how close the HQ is to falling.")]
        [Range(0f, 1f)]
        public float hqDefenseWeight = 0.5f;

        [Tooltip("Extra worth of attacking an enemy supply hub from ground none of their fighting units can reach (a breakthrough), as a multiple of the hub's supply importance.")]
        [Range(0f, 4f)]
        public float breakthroughWeight = 1f;

        [Tooltip("Worth of standing within striking distance of our HQ's surroundings while it is in danger, as a fraction of the HQ-defence stake times this unit's share of the force needed.")]
        [Range(0f, 2f)]
        public float guardWeight = 0f;

        [Tooltip("Penalty per point of damage wasted beyond what was needed to kill.")]
        public float overkillPenalty = 0.35f;

        [Tooltip("Value discount on attacks that cannot be delivered this turn.")]
        [Range(0f, 1f)]
        public float imminentDiscount = 0.25f;

        [Tooltip("Multiplier on expected gold lost by ending a turn somewhere. 1 = take expected losses at face value; above 1 is cautious, below 1 reckless.")]
        public float riskWeight = 1f;

        [Tooltip("How sharply enemy fire is predicted to focus on the most attractive target in reach: 1 shares it in proportion to attraction, higher concentrates it on the best target, as a greedy opponent does.")]
        [Range(1f, 6f)]
        public float focusExponent = 1f;

        [Tooltip("Worth of the counterattack an enemy invites by striking a unit: if it comes forward to hit us, it ends its turn where our others hit it first. Fraction of that expected damage's worth. This is what lets a concentrated force step forward into reach.")]
        [Range(0f, 2f)]
        public float counterWeight = 0.5f;

        [Tooltip("Worth of the space a unit creates for more valuable friends still to act, by constraining where the enemy can move and strike (AISpace), as a fraction of the threat it spares their best positions.")]
        [Range(0f, 2f)]
        public float screenWeight = 0f;

        [Tooltip("Extra worth of a next-turn attack from a tile no enemy can strike tonight (we strike first), as a fraction of that attack's worth.")]
        [Range(0f, 3f)]
        public float standoffWeight = 0f;

        [Tooltip("A line unit at or below this strength, inside or next to supply, pulls back to heal when a healthier unit is close enough to take its place.")]
        [Range(0, 6)]
        public int rotateStrength = 3;

        [Tooltip("Tiebreak preference for cover, beyond the protection already priced into expected losses.")]
        public float coverWeight = 1f;

        [Tooltip("On aggressive fronts, worth of standing inside the enemy's supply network (which blocks it), as a fraction of the unit's worth per strength.")]
        [Range(0f, 2f)]
        public float cutSupplyWeight = 0.5f;

        [Tooltip("Artillery: worth of each friendly combat unit within range, as a fraction of that unit's worth per strength.")]
        [Range(0f, 1f)]
        public float supportWeight = 0.1f;

        [Header("Posture")]
        [Tooltip("A front counts the units of both sides that could reach it within this many turns when judging the balance of force.")]
        [Range(1, 5)]
        public int frontReachTurns = 2;

        [Tooltip("Local force ratio (own combat power / enemy) at which an aggressive front's units commit to going in together, pricing risk as a group.")]
        public float assaultRatio = 1.0f;

        [Tooltip("An attack beyond our supply waits until this fraction of the front's full combat power is healthy and gathered at the staging tile.")]
        [Range(0f, 1f)]
        public float assaultReadiness = 0.6f;

        [Tooltip("An attack under way breaks off and regroups when the power still able to reach the objective falls below this fraction of the front's full combat power.")]
        [Range(0f, 1f)]
        public float assaultBreakoff = 0.35f;

        [Tooltip("Fraction of full strength at which a unit counts as healthy for readiness.")]
        [Range(0f, 1f)]
        public float readyStrength = 0.8f;

        [Tooltip("How close to the staging tile a unit must be to count as gathered.")]
        [Range(1, 6)]
        public int stagingRadius = 3;

        [Tooltip("Pull toward a staging front's staging tile, as a fraction of the unit's worth at distance zero, falling off as 1/(1+d).")]
        [Range(0f, 3f)]
        public float stagingPull = 1f;

        [Tooltip("In an assault, cost per hex a unit ends ahead of the rest of the assault, as a fraction of the unit's whole worth.")]
        [Range(0f, 1f)]
        public float cohesionWeight = 0.15f;

        [Tooltip("Hexes a unit may end ahead of the rest of its assault without cost.")]
        [Range(0, 6)]
        public int cohesionSlack = 1;

        [Tooltip("Multiplier on risk for units on an aggressive front. Below 1 = willing to take losses to close.")]
        [Range(0f, 2f)]
        public float aggressiveRisk = 0.5f;

        [Tooltip("Multiplier on risk for units on a defensive front.")]
        [Range(0f, 2f)]
        public float defensiveRisk = 1f;

        [Tooltip("A front's force ratio is never below the whole armies' ratio (see Front.ForceRatio).")]
        public bool useArmyRatio = false;

        [Tooltip("Front is treated as aggressive above this local force ratio (own value / enemy value).")]
        public float aggressionThreshold = 1.15f;

        [Tooltip("A front whose centre is outside our supply is defensive below this force ratio, whatever else says.")]
        public float outOfSupplyRatio = 1f;

        [Tooltip("Front is treated as defensive below this local force ratio.")]
        public float defensiveThreshold = 0.85f;

        [Tooltip("Strength at or below which a unit prefers to withdraw and heal.")]
        [Range(0, 6)]
        public int withdrawStrength = 2;

        [Tooltip("Pull toward a front's objective, as a fraction of the objective's value at distance zero, falling off as 1/(1+d).")]
        [Range(0f, 0.5f)]
        public float objectivePull = 0.01f;

        [Tooltip("Multiplier on the objective pull when the front can destroy an outmatched enemy HQ in one volley.")]
        [Range(1f, 30f)]
        public float siegePull = 8f;

        [Tooltip("Worth of standing on fully contested ground, as a fraction of the unit's own worth.")]
        public float holdWeight = 0.4f;

        [Header("Objectives")]
        [Tooltip("Assign units to objectives (AIObjectives). Off: every unit follows its front.")]
        public bool useObjectives = true;

        [Tooltip("Force ratio (assigned combat power / what can defend it) an attack objective needs before units are committed to it. Below this the objective is not attempted, so no unit goes in alone.")]
        public float objectiveRatio = 2.5f;

        [Tooltip("Pull toward a unit's strategic objective, as a fraction of the unit's own worth at distance zero, falling off as 1/(1+d). A tie-breaker.")]
        [Range(0f, 3f)]
        public float assignmentPull = 0.5f;

        [Tooltip("A unit that could exchange fire with an enemy within this many turns ignores its strategic objective.")]
        [Range(0, 6)]
        public int objectiveContactTurns = 3;

        [Tooltip("Turns of travel beyond which a unit is not considered for an objective.")]
        [Range(1, 10)]
        public int objectiveReachTurns = 3;

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
