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
        public int choiceMode = 2;

        [Tooltip("choiceMode 2: keep options whose simulated world is within this many of the acting unit's strength points of the best.")]
        [Range(0f, 6f)]
        public float worldTie = 0.25f;

        [Tooltip("Floor on the tie margin, in gold, whatever the unit's worth.")]
        public float worldTieMin = 250f;

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

        [Tooltip("Forecast boldness: the enemy's reply in the forecast lands this fraction lighter (0 = full worst case, 0.5 = half). The enemy still picks its targets on full damage; only what it costs us is discounted. Real opponents attack less than the forecast's all-out reply.")]
        [Range(0f, 0.5f)]
        public float replyDiscount = 0.3f;

        [Tooltip("The forecast's enemy weighs its own risk: an attack counts only if it is worth more than what our counterattack would cost the attacker on its firing tile (beyond what it risks staying put), times enemyCaution.")]
        public bool enemyWeighsRisk = true;

        [Tooltip("Reply at full damage, so kills land where they really would, with the enemy focusing fire (assists counted as ours are); replyDiscount then applies to what that reply costs us, except unanswered ranged fire (artillery), which is certain.")]
        public bool replyBreakpoints = true;

        [Tooltip("Forecast a target our shot drives back from its retreat tile: the reply it fires from there, the tile it frees for our units' reach and cover.")]
        public bool forecastKnockback = true;

        [Header("Building values")]
        [Tooltip("Score capturing buildings (and the enemy's infantry threat to ours) in the forecast")]
        public bool forecastBuildings = true;
        [Tooltip("What any building is worth to hold, in gold, before what it does")]
        public float buildingValue = 3000f;
        [Tooltip("Turns of a building's income counted in its worth")]
        public float buildingIncomeTurns = 3f;
        [Tooltip("Added to a building's worth if it supplies the area around it")]
        public float buildingSupplyValue = 3000f;
        [Tooltip("Added to a building's worth if units can be built at it")]
        public float buildingProductionValue = 3000f;

        [Tooltip("Gold against a unit of ours that does not land there ending its turn on our runway or dock tiles, while we have units that do")]
        public float runwayBlockValue = 2000f;

        [Header("Unit values")]
        [Tooltip("What a grunt is worth to the AI, in gold for a full-strength unit (0 = its cost). Grunts block roads and screen beyond what they cost.")]
        public float gruntWorth = 4000f;

        [Tooltip("What a supply unit is worth to the AI as material (0 = its cost). What it keeps supplied is priced separately (hubDependentsWeight).")]
        public float supplyWorth = 10000f;

        [Tooltip("How much the forecast's enemy minds being counterattacked (1 = at full value).")]
        [Range(0f, 3f)]
        public float enemyCaution = 1f;

        [Tooltip("A damaged unit left on fewer than this many strength points gets no heal-back credit in the forecast: it is more likely to be finished than to heal.")]
        [Range(1, 6)]
        public int healMinStrength = 2;

        [Tooltip("Strength a unit in its own supply heals back by its next turn (the game's rule: 1). That much of the damage done to it doesn't count; kills, and damage beyond it, count in full.")]
        [Range(0f, 6f)]
        public float healedPerTurn = 1f;

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

        [Tooltip("How expected loss on a tile is estimated. 0: each enemy's one attack is shared among everything of ours it could hit, in proportion to how attractive each is. 1: every enemy that can reach the tile counts in full. 2: each enemy fires its whole attack at the most valuable thing it can reach.")]
        [Range(0, 2)]
        public int dangerModel = 2;

        [Tooltip("Worth of taking away an enemy's next-turn threat: its best possible blow on anything of ours, in material, times this, times the fraction of its strength the attack removes.")]
        [Range(0f, 2f)]
        public float threatTargetWeight = 1f;

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

        [Tooltip("A unit at or below withdrawStrength withdraws only to a tile that heals it and the enemy cannot reach next turn, and only when fighting on is not worth more by the forecast; otherwise it fights.")]
        public bool woundedNeedsSafety = false;

        [Tooltip("With woundedNeedsSafety: off = skip the safe-and-supplied test and just compare fighting with withdrawing.")]
        public bool woundedSafetyGate = true;

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

        [Tooltip("Old rule: only pull units to an objective once they have objectiveRatio times its defenders, and never on a trip they couldn't get back into supply from. Off: objectives give direction to units out of contact regardless.")]
        public bool objectivesNeedForce = false;

        [Tooltip("A unit that could exchange fire with an enemy within this many turns ignores its strategic objective.")]
        [Range(0, 6)]
        public int objectiveContactTurns = 3;

        [Tooltip("Turns of travel beyond which a unit is not considered for an objective.")]
        [Range(1, 10)]
        public int objectiveReachTurns = 8;

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

        [Header("Board read")]
        [Tooltip("Worth of a tile a supply unit could cover after one move, as a fraction of real supply (0 = unsupplied, 1 = supplied).")]
        [Range(0f, 1f)]
        public float potentialSupplyValue = 0.25f;

        [Tooltip("How many enemy turns ahead the strike map looks (1 disables the extra turns; the coming turn is the world simulation's).")]
        [Range(1, 5)]
        public int enemyReachTurns = 3;

        [Header("World score")]
        [Tooltip("Our units' best blow on our next turn, counted in the world score at this fraction. We attack first, so this is a near-certain gain.")]
        [Range(0f, 1f)]
        public float ownStrikeWeight = 0.1f;

        [Tooltip("Enemy units' best blow on their turn after that, counted against us at this fraction.")]
        [Range(0f, 1f)]
        public float enemyStrikeWeight = 0.25f;

        [Tooltip("Reply forecast: a defender driven back with nowhere to go takes the pinned point (an HQ always). Off: the old forecast, which never counted it.")]
        public bool forecastPins = true;

        [Tooltip("Reply forecast: the enemy's own units block it — it cannot end on a tile another of its units holds. Off (the fix): they pass through and make room for each other.")]
        public bool forecastOwnSideBlocks = false;

        [Tooltip("Reply forecast: a unit of ours destroyed during the reply keeps blocking enemy movement. Off (the fix): its tile opens for every enemy still to act.")]
        public bool forecastDeadBlock = false;

        [Tooltip("An assist needs a finisher with a free firing tile it can reach this turn by real movement. Off: the old check, hex distance only.")]
        public bool assistNeedsReach = true;

        [Header("Coordination")]
        [Tooltip("Before units act one at a time, give each group in contact a whole-group plan (strike / withdraw / stay), forecast with all of its moves at once (AIPlan).")]
        public bool coordination = false;

        [Tooltip("A group plan must beat 'stay' by this much (gold) in the forecast to be adopted.")]
        public float coordinationMargin = 500f;

        [Tooltip("Coordinated strikes in passes (AICoordinator): each unit's best attacks, a survive move and a retreat; battle plans of two or more units on one target from non-conflicting tiles, forecast as one board; best plans act first.")]
        public bool coordinationPasses = true;

        [Tooltip("Attack options each unit keeps for battle plans.")]
        public int coordOffensiveOptions = 3;

        [Tooltip("Coordination: a unit whose chosen attack counted on an assist, with no battle plan committing the finisher, decides again without assist credit.")]
        public bool coordNoAssist = true;

        [Tooltip("Coordination: only plans that kill their target (and, as they run, can still kill it; otherwise the rest of the plan is dropped).")]
        public bool coordKillsOnly = true;

        [Tooltip("Coordination: after kill plans, cover swaps — a unit guarding a friend's approach versus the friend stepping to a safer tile, taken when acting together beats both units' own choices.")]
        public bool coordCoverSwaps = false;

        [Tooltip("Strategy measures distance as a ground unit travels it (over land, around water), not as the crow flies: the pull toward objectives and staging, the staging tile, which units can reach a front, turns to contact.")]
        public bool travelDistance = true;

        [Tooltip("Enemy reply forecast: a unit of ours driven back by one of their blows is where it retreated to for the blows after (and the tile it left is open to them), so chains of hits that push a unit back and follow it up are seen.")]
        public bool forecastRetreatChain = false;

        [Tooltip("Supply units form a chain along the road from the HQ toward the objective, moved rear to front so the chain never breaks (AISupplyChain).")]
        public bool supplyChain = true;

        [Tooltip("Buy a supply unit at the HQ when the chain cannot reach as far as the army needs it to.")]
        public bool buySupply = true;

        [Tooltip("Buy fighting units at our buildings with the money left at the end of the turn, when the rules and the scenario allow it (AIProduction).")]
        public bool buyUnits = true;

        [Tooltip("Fighting units out of contact ride a free transport to the front when that gets them there sooner than walking (the same walk-or-ride planner supply units use), and get off where the ride ends.")]
        public bool combatRides = true;

        [Tooltip("An empty transport is credited, like a fighting unit, for drawing fire off friends and blocking the enemy's way (FirstStrikeCredit, SpaceDenial). Off: it is not, and keeps out of reach.")]
        public bool idleTransportScreens = false;

        [Tooltip("Plan with getting off costing nothing: a passenger aboard is not spent while it rides, a transport drives and drops in one action, and a passenger put down fresh moves and fights from where it lands (the ride is planned with that move). Off: drop-off waits for the next turn and the passenger's turn is spent.")]
        public bool freshUnload = true;

        [Tooltip("With freshUnload: plan rides counting the passenger's own move after it is put down (it may be dropped up to a move short of where it is going). Off: rides are planned to a drop beside the destination, and the passenger's move after landing is a bonus.")]
        public bool freshUnloadPlanning = true;

        [Tooltip("A supply-chain link may stand ahead of the army where no enemy can strike for more than two turns (otherwise links never pass the frontmost fighting unit).")]
        public bool chainLeadsWhenClear = false;

        [Tooltip("March: a fighting unit out of contact gains this much of a strength's worth for each step over land it ends nearer its front's objective (linear, so it pulls from any distance; supply costs still keep it inside supply).")]
        public float marchWeight = 0.05f;

        [Tooltip("A battle plan is adopted only if its forecast beats its units acting alone by more than this (gold).")]
        public float coordinationPassMargin = 0f;

        [Tooltip("Model retreats in the group-plan forecast: a defender driven back moves, and the tile it held opens.")]
        public bool coordinationRetreats = true;

        [Tooltip("Self-preservation: the acting unit counts every enemy blow that could reach its tile (biggest first, weights 1, 1/2, 1/4 ...), beyond what the forecast reply already takes from it, at this weight. 0 = off.")]
        [Range(0f, 2f)]
        public float selfPreservationWeight = 0f;

        [Tooltip("A unit destroyed costs this many more of its strength points' worth than the points themselves: alive, it would heal back, hold ground and fire again.")]
        [Range(0f, 6f)]
        public float deathPremium = 1f;

        [Tooltip("A supply unit's death costs, in the world score, one strength of every unit it alone keeps supplied, times this.")]
        [Range(0f, 3f)]
        public float hubDependentsWeight = 1f;

        [Tooltip("A hit our units still to act this turn could finish counts as this fraction of a kill (its reply removed, its material gone).")]
        [Range(0f, 1f)]
        public float assistWeight = 0.75f;

        [Tooltip("Supply forecast: strength points of each enemy unit our move or kill puts out of supply (re-solving their network with our unit moved and any supply unit we destroy removed). Replaces the dependents price of their supply units' deaths.")]
        public float supplyForecastValue = 0f;

        [Tooltip("Mutual cover, gold per tile: each tile next to one of our units that the enemy could step onto next turn counts + this if another of our units could strike it next turn, - this if none could.")]
        public float coverTileValue = 25f;

        [Tooltip("Artillery: gold per tile the enemy could move to next turn that the gun covers from where it ends its turn (its zone of control).")]
        public float artilleryCoverValue = 25f;

        [Tooltip("Space: gold per tile one side's units could move to next turn, and again per tile they could strike. Ours count for us, theirs against us — so taking space from them is a gain, and doing it with a cheap unit is a cheap gain.")]
        public float spaceTileValue = 10f;

        [Tooltip("Count our space only on contested ground: tiles the enemy could strike within enemyReachTurns. Off: every tile, including the empty rear.")]
        public bool spaceContestedOnly = true;

        [Tooltip("Gold per enemy unit out of supply in the world after our move (counted at the start of their turn, where the game checks supply).")]
        public float enemyUnsuppliedValue = 100f;

        [Tooltip("Space: gold per tile of enemy supply our unit's position cuts off.")]
        public float spaceSupplyValue = 50f;

        [Header("Supply unit pricing")]
        [Tooltip("0 = coverage pricing (forward/intent/need); 1 = value added: heals 1/6 of each wounded unit, supplies 1/10 of each unit, attrition it prevents, minus its own simulated loss.")]
        [Range(0, 1)]
        public int hubPricing = 1;

        [Tooltip("With value-added pricing, still credit coverage of contested ground and of where units are heading.")]
        public bool hubPricingForward = true;

        [Tooltip("Plan each supply unit's move before the line moves and let the line use that supply (a ghost); the supply unit still moves last.")]
        public bool ghostSupply = true;

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
