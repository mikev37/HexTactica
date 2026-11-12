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

        [Header("Campaign (objectives and task forces)")]
        [Tooltip("How evenly the campaign spreads the army. 0: all in — enough at home not to be overrun, everything else on the single best objective. 1: every objective it can meet, and what is left spread over all of them.")]
        [Range(0f, 1f)]
        public float objectiveSpread = 0.5f;
        [Tooltip("A task force needs this many times the enemy combat power that could fight where it is going.")]
        public float objectiveForceRatio = 1.1f;
        [Tooltip("Turns of a building's income its capture is worth.")]
        public int objectiveHorizonTurns = 10;
        [Tooltip("A task force reduced below this share of the power it was formed with sends the campaign back to planning.")]
        [Range(0f, 1f)]
        public float objectiveReplanLoss = 0.5f;
        [Tooltip("Supply chain: a link within this many tiles of another node of ours (a link kept before it, rearmost first, or a supply building) is released as spare, free for other duties. 0: off.")]
        internal int chainLinkSpacing = 2;
        [Tooltip("The way to an objective: steps of detour worth taking to avoid one tile the enemy can strike next turn (so a middle held in strength is gone round).")]
        internal float routeDanger = 3f;
        [Tooltip("Scouting objectives: how much ground one covers (hexes around its centre), and how many at most.")]
        public int scoutRadius = 3;
        internal int scoutAreas = 2;
        [Tooltip("How strongly campaign fronts pull their units toward their objectives and staging grounds, as a multiple of an ordinary front's pull. The units still choose their own moves: this only weighs where they would rather be.")]
        public float campaignPull = 1f;
        /// <summary>
        /// How much the campaign's plan weighs against the fight in front of a unit: each step a move
        /// takes toward its task force's goal (its staging ground while gathering, else its objective;
        /// no further than the force's pace) is worth this many of the unit's strength points in the
        /// simulated outcome the tactical layer chooses by. 0: the plan only breaks ties (campaignPull).
        /// The units choose their own moves either way; this is how hard the plan leans on them.
        /// </summary>
        public float campaignStep = 1f;
        [Tooltip("A task force goes once the healthy power on its staging ground is this share of its need (objectiveForceRatio times the enemy there): it needs 1.5 to one, goes at 0.8 of that (1.2 to one).")]
        [Range(0f, 1f)]
        public float campaignGatherShare = 0.8f;
        [Tooltip("Breadth first: no front further than this many turns away (the slowest unit it needs, riding where a transport can take it) while any objective is nearer. 0: no cutoff.")]
        public int campaignMaxTurns = 3;
        [Tooltip("Cohesion: a task force's unit more than this many of its own turns from the task force's centre drops out of it. 0: no limit.")]
        public int campaignCohesionTurns = 2;
        [Tooltip("A task force's unit this many turns or fewer from contact fights as it will: no lean toward its staging ground or objective, no pace, no dip rule (the campaign sets the stage; the fight is the tactics'). 0: never released.")]
        public int campaignReleaseTurns = 2;
        [Tooltip("Campaign: a task force's unit within this many turns of an enemy unit is engaged (it fights to survive); the others are staged (on the staging ground) or en route.")]
        public int campaignEngagedTurns = 2;
        [Tooltip("Campaign: the rise in a task force's enemy estimate, over what it was formed against, that stops it.")]
        internal float campaignSurgeRatio = 1.5f;
        [Tooltip("Campaign: turns at most that a stopped task force's units pull back to its staging ground before they are planned afresh.")]
        internal int campaignSurgeWithdrawTurns = 2;
        [Tooltip("Campaign: this share of the army's power stays in the reserve: a new task force is taken on only while what is committed stays within the rest.")]
        public float campaignReserveShare = 0.33f;
        [Tooltip("Campaign: the share of its need now below which a task force is broken.")]
        internal float campaignBreakShortRatio = 0.5f;
        [Tooltip("Campaign, mobile reserve: the least movement a ground unit needs to be in the reserve (a tank: 6, a recon: 8; a grunt: 3).")]
        public int campaignReserveMinMove = 5;
        [Tooltip("All out: our fighting strength at least this many times what the enemy could have.")]
        public float knowledgeAllOutRatio = 2f;
        [Tooltip("All out: at least this confidence in our knowledge of the enemy's forces (0..1; 1 without fog).")]
        internal float knowledgeAllOutConfidence = 0.6f;
        [Tooltip("The reserve is released once confidence in our knowledge of the enemy reaches this: the reserve is for an enemy whose potential is unknown.")]
        internal float knowledgeReserveConfidence = 0.6f;
        [Tooltip("Campaign: the share of units on no task force kept with the ordinary fronts as the active reserve (the rest gather as the central reserve).")]
        internal float campaignActiveReserveShare = 0.1f;
        [Tooltip("Regrouping: units costing this much or more pull back without local superiority (grunts and conscripts stay and fight).")]
        internal int regroupExpensiveCost = 3000;
        [Tooltip("Regrouping: only a unit that would expect to lose this many strength points where it stands tonight.")]
        internal float regroupRisk = 2f;
        [Tooltip("A front's posture weighs what we could replace: the edge it needs to press is 1 + this with nothing to buy with, else 1 / (1 + this x R), R the times the gold in hand and strategicReplaceTurns of income would rebuild the army (at most strategicReplaceCap). 0: off.")]
        [Range(0f, 1f)]
        public float strategicReplaceWeight = 0.3f;
        [Tooltip("Turns of income counted as gold to replace losses (strategicReplaceWeight).")]
        internal int strategicReplaceTurns = 3;
        [Tooltip("The most times over the army's cost counted as replaceable (strategicReplaceWeight).")]
        internal float strategicReplaceCap = 10f;
        [Tooltip("Regrouping: the central reserve goes to the front whose units the enemy can strike with more than this many times their own power.")]
        internal float regroupCommitRatio = 1f;
        [Tooltip("Move score: our HQ's worth (and the enemy's) as this many times the board's stake: capture progress on an HQ counts this much more than the rest of the board.")]
        internal float hqCaptureWeight = 2f;
        [Tooltip("Move score: each strength point of enemy infantry within a move of our HQ (after their reply) costs this share of the board's stake.")]
        public float hqThreatNear = 0.02f;
        [Tooltip("Move score: each strength point of enemy infantry on our HQ (after their reply) costs this share of the board's stake.")]
        public float hqThreatOn = 0.1f;
        [Tooltip("Move score: a building not ours that no enemy stands on (after their reply) is worth this share of it; the enemy HQ, worth the board, most.")]
        public float buildingVacantShare = 0.02f;
        [Tooltip("Move score: a building not ours with our infantry on it is worth this share of it, twice that when it is capturing (on it without having attacked).")]
        public float buildingOccupyShare = 0.1f;
        [Tooltip("A front's own supply units: one for every this many of its fighting units (after its chain).")]
        public int campaignUnitsPerSupply = 6;
        [Tooltip("Buying: an enemy unit seen counts for this many days after it is last seen.")]
        internal int productionMemoryDays = 6;
        [Tooltip("Buying: what is bought against an enemy unit counts 1 / (1 + c / this), c the full kills our army could make on it (nothing of ours can hit it: full value).")]
        public float productionCounterUnits = 2f;
        [Tooltip("Buying: each unit of a kind we already have (or have planned this turn) makes another worth this much (1: no variety rule; what we lack against the enemy, AIProduction.Scarcity, already gives diminishing returns).")]
        internal float productionVariety = 0.97f;
        [Tooltip("Production: turns to the fight that halve a unit's worth.")]
        public float productionTimeHorizon = 3f;
        [Tooltip("Buying: a fighting unit is worth at least this share of its own full worth, so a slot is filled when gold allows even if nothing scores against the enemy.")]
        internal float productionFloor = 0.05f;
        [Tooltip("Buying: a unit's presence on the field — actions the enemy must spend on it, ground it holds and sees — as this share of its own full worth, added to what it would do in exchanges.")]
        internal float productionPresence = 0.25f;
        [Tooltip("Buying: what a unit would take from the enemy counts this much against what it would do to them.")]
        internal float productionDefenceWeight = 0.5f;
        [Tooltip("Buying: a unit of a higher tier (productionTierCosts) is hit less the more lower-tier units we have to screen it: what it would take from the enemy is cut by up to productionShieldCut (all of it for a unit that fires from range or flies, half for one that must close), in proportion to how many of its tier productionShieldRatio lower-tier units could each cover. Infantry screens tanks, AA and gunships; those screen bombers, rockets, missiles and fighters. 0: off.")]
        public float productionShieldCut = 0.6f;
        [Tooltip("Buying (productionShieldCut): lower-tier units it takes to screen one unit of a tier.")]
        internal float productionShieldRatio = 2f;
        [Tooltip("Buying (productionShieldCut): the costs from which a unit is tier 1, and tier 2.")]
        public Vector2 productionTierCosts = new Vector2(5000f, 11000f);
        [Tooltip("Buying: how often a unit gets a shot: its reach (attack range, plus its move for one that moves and fires in a turn) over this many tiles, at most 1 — rockets reach six tiles from where they stand every turn, a grunt has to walk up first. 0: off.")]
        internal float productionReachRef = 6f;
        [Tooltip("Buying: the most turns a unit is expected to live.")]
        internal float productionLifeMax = 8f;
        [Tooltip("Buying: turns of life a unit's worth is measured against (a unit expected to live this long keeps the worth of one exchange).")]
        public float productionLifeRef = 3f;
        [Tooltip("Buying: a front's want (the units that do most against its enemy, at its nearest producer) counts this much.")]
        public float productionFrontWeight = 1f;
        [Tooltip("Buying: a supply unit's (or truck's) value — the fighting units it would supply, or the force a front waiting on supply needs — counts this much.")]
        public float productionSupplyWeight = 1f;
        [Tooltip("Buying: supply units beyond one per campaignUnitsPerSupply fighting units, for fronts short of supply (and the campaign's next front waiting on one).")]
        internal int productionSupplyExtra = 2;
        [Tooltip("Place objectives (AICampaign.Places): both ends of chokepoints and road junctions not yet in our supply, at most this many, the nearest our units. 0: none.")]
        internal int campaignPlaces = 12;
        [Tooltip("A place objective's worth, in multiples of what a building with no income is worth.")]
        internal float campaignPlaceWeight = 0.5f;
        [Tooltip("Out of contact, a campaign unit may spend this many rations out of supply (and go no further than its rations bring it back from); past that it is pulled back. 0: never out of supply.")]
        public int campaignDipTurns = 1;

        [Tooltip("Keep options whose simulated world is within this many of the acting unit's strength points of the best.")]
        [Range(0f, 6f)]
        public float worldTie = 0.25f;

        [Tooltip("Floor on the tie margin, in gold, whatever the unit's worth.")]
        public float worldTieMin = 250f;

        [Tooltip("Tier 3: worth of each enemy-reachable tile (two turns, summed over enemy units) taken away, as a fraction of the acting unit's worth.")]
        internal float denialTileWeight = 0.05f;

        [Tooltip("Tier 3: worth of each tile taken out of enemy supply, as a fraction of the acting unit's worth.")]
        internal float supplyTileWeight = 0.05f;

        [Tooltip("Tier 3: worth of cutting an enemy unit off from supply, as a fraction of that unit's worth.")]
        internal float supplyCutWeight = 1f;

        [Header("Supply")]
        [Tooltip("Supply depth treated as fully sustained; the depth signals saturate here.")]
        [Range(1, 10)]
        public int supplyComfort = 3;

        [Tooltip("Worth of bringing supply to all contested ground, as strength points of the whole army. Pulls hubs forward ahead of the line so the line can advance.")]
        internal float forwardSupplyWeight = 0.5f;

        [Tooltip("Hubs: worth of closing the gap between the network and where a friendly unit is heading, as strength points of that unit per turn (full gap closed = 1 x weight).")]
        internal float intentSupplyWeight = 2f;

        [Tooltip("Multiplier on a hub's target worth for the units it sustains (base: a quarter of their combined value).")]
        internal float enemySupplyTargetWeight = 1f;

        [Tooltip("How much predicted retaliation damage discounts an attack.")]
        internal float retaliationWeight = 1f;

        [Header("Combat")]
        [Tooltip("Bonus multiplier when an attack finishes the target.")]
        public float killBonus = 1.35f;

        [Tooltip("Forecast boldness: the enemy's reply in the forecast lands this fraction lighter (0 = full worst case, 0.5 = half). The enemy still picks its targets on full damage; only what it costs us is discounted. Real opponents attack less than the forecast's all-out reply. 0.5 beat 0.3 in 18 of 32 big-map games (+737k; ChanceMeeting 6 of 8, BushWar 2 of 8).")]
        [Range(0f, 0.5f)]
        public float replyDiscount = 0.5f;

        [Tooltip("Forecast a target our shot drives back from its retreat tile: the reply it fires from there, the tile it frees for our units' reach and cover.")]
        internal bool forecastKnockback = true;

        [Tooltip("Score capturing buildings (and the enemy's infantry threat to ours) in the forecast")]
        internal bool forecastBuildings = true;
        [Header("Building values")]
        [Tooltip("What any building is worth to hold, in gold, before what it does")]
        public float buildingValue = 3000f;
        [Tooltip("Turns of a building's income counted in its worth")]
        internal float buildingIncomeTurns = 3f;
        [Tooltip("Added to a building's worth if it supplies the area around it")]
        internal float buildingSupplyValue = 3000f;
        [Tooltip("Added to a building's worth if units can be built at it")]
        internal float buildingProductionValue = 3000f;

        [Tooltip("Gold against a unit of ours that does not land there ending its turn on our runway or dock tiles, while we have units that do")]
        internal float runwayBlockValue = 2000f;

        [Header("Unit values")]

        [Tooltip("How much the forecast's enemy minds being counterattacked (1 = at full value).")]
        [Range(0f, 3f)]
        public float enemyCaution = 1f;

        [Tooltip("A damaged unit left on fewer than this many strength points gets no heal-back credit in the forecast: it is more likely to be finished than to heal.")]
        [Range(1, 6)]
        public int healMinStrength = 2;


        [Tooltip("Worth of damage that will not be finished on a unit that does not fight (an HQ, a supply hub), as a fraction of face value. It does not weaken them, and they heal.")]
        [Range(0f, 1f)]
        public float hqChipFactor = 0.1f;

        [Tooltip("Worth of a hit on an enemy HQ that is not finished this turn but leaves it killable next turn by the units now in reach, as a fraction of the win.")]
        internal float hqSetupFactor = 0.1f;

        [Tooltip("Worth of removing a threat to our own HQ, as a fraction of what the HQ's loss would cost, scaled by how much of the threat the attack removes and how close the HQ is to falling.")]
        [Range(0f, 1f)]
        public float hqDefenseWeight = 0.5f;

        [Tooltip("Extra worth of attacking an enemy supply hub from ground none of their fighting units can reach (a breakthrough), as a multiple of the hub's supply importance.")]
        internal float breakthroughWeight = 1f;

        [Tooltip("Penalty per point of damage wasted beyond what was needed to kill.")]
        internal float overkillPenalty = 0.35f;

        [Tooltip("Value discount on attacks that cannot be delivered this turn.")]
        internal float imminentDiscount = 0.25f;

        [Tooltip("Multiplier on expected gold lost by ending a turn somewhere. 1 = take expected losses at face value; above 1 is cautious, below 1 reckless.")]
        public float riskWeight = 1f;

        [Tooltip("How sharply enemy fire is predicted to focus on the most attractive target in reach: 1 shares it in proportion to attraction, higher concentrates it on the best target, as a greedy opponent does.")]
        internal float focusExponent = 1f;

        [Tooltip("Worth of the counterattack an enemy invites by striking a unit: if it comes forward to hit us, it ends its turn where our others hit it first. Fraction of that expected damage's worth. This is what lets a concentrated force step forward into reach.")]
        [Range(0f, 2f)]
        public float counterWeight = 0.5f;

        [Tooltip("A line unit at or below this strength, inside or next to supply, pulls back to heal when a healthier unit is close enough to take its place.")]
        internal int rotateStrength = 3;

        [Tooltip("Worth of taking away an enemy's next-turn threat: its best possible blow on anything of ours, in material, times this, times the fraction of its strength the attack removes.")]
        internal float threatTargetWeight = 1f;

        [Tooltip("Tiebreak preference for cover, beyond the protection already priced into expected losses.")]
        internal float coverWeight = 1f;

        [Tooltip("On aggressive fronts, worth of standing inside the enemy's supply network (which blocks it), as a fraction of the unit's worth per strength.")]
        internal float cutSupplyWeight = 0.5f;

        [Tooltip("Artillery: worth of each friendly combat unit within range, as a fraction of that unit's worth per strength.")]
        internal float supportWeight = 0.1f;

        [Tooltip("A front counts the units of both sides that could reach it within this many turns when judging the balance of force.")]
        internal int frontReachTurns = 2;

        [Tooltip("Local force ratio (own combat power / enemy) at which an aggressive front's units commit to going in together, pricing risk as a group.")]
        internal float assaultRatio = 1.0f;

        [Tooltip("An attack beyond our supply waits until this fraction of the front's full combat power is healthy and gathered at the staging tile.")]
        internal float assaultReadiness = 0.6f;

        [Header("Posture")]
        [Tooltip("An attack under way breaks off and regroups when the power still able to reach the objective falls below this fraction of the front's full combat power.")]
        [Range(0f, 1f)]
        public float assaultBreakoff = 0.35f;

        [Tooltip("Fraction of full strength at which a unit counts as healthy for readiness.")]
        [Range(0f, 1f)]
        public float readyStrength = 0.8f;

        [Tooltip("How close to the staging tile a unit must be to count as gathered.")]
        internal int stagingRadius = 3;

        [Tooltip("Pull toward a staging front's staging tile, as a fraction of the unit's worth at distance zero, falling off as 1/(1+d).")]
        internal float stagingPull = 1f;

        [Tooltip("In an assault, cost per hex a unit ends ahead of the rest of the assault, as a fraction of the unit's whole worth.")]
        internal float cohesionWeight = 0.15f;

        [Tooltip("Hexes a unit may end ahead of the rest of its assault without cost.")]
        internal int cohesionSlack = 1;

        [Tooltip("Multiplier on risk for units on an aggressive front. Below 1 = willing to take losses to close.")]
        internal float aggressiveRisk = 0.5f;

        [Tooltip("Multiplier on risk for units on a defensive front.")]
        internal float defensiveRisk = 1f;

        [Tooltip("Front is treated as aggressive above this local force ratio (own value / enemy value).")]
        public float aggressionThreshold = 1.15f;

        [Tooltip("A front whose centre is outside our supply is defensive below this force ratio, whatever else says.")]
        internal float outOfSupplyRatio = 1f;

        [Tooltip("Front is treated as defensive below this local force ratio.")]
        internal float defensiveThreshold = 0.85f;

        [Tooltip("Strength at or below which a unit prefers to withdraw and heal.")]
        [Range(0, 6)]
        public int withdrawStrength = 2;

        [Tooltip("Pull toward a front's objective, as a fraction of the objective's value at distance zero, falling off as 1/(1+d).")]
        internal float objectivePull = 0.01f;

        [Tooltip("Multiplier on the objective pull when the front can destroy an outmatched enemy HQ in one volley.")]
        internal float siegePull = 8f;

        [Tooltip("Worth of standing on fully contested ground, as a fraction of the unit's own worth.")]
        internal float holdWeight = 0.4f;

        [Header("Objectives")]

        [Tooltip("Force ratio (assigned combat power / what can defend it) an attack objective needs before units are committed to it. Below this the objective is not attempted, so no unit goes in alone.")]
        internal float objectiveRatio = 2.5f;

        [Tooltip("Pull toward a unit's strategic objective, as a fraction of the unit's own worth at distance zero, falling off as 1/(1+d). A tie-breaker.")]
        internal float assignmentPull = 0.5f;

        [Tooltip("Capture objectives: each step toward the building, for a unit out of contact sent to take it, as a fraction of a strength point of that unit (and six times that for standing on it, for one that can capture).")]
        public float capturePull = 0.3f;

        [Tooltip("Capture objectives only for buildings within this many tiles of our supplied ground: the army's ground, not the whole map.")]
        internal int captureReach = 3;

        [Tooltip("A unit that could exchange fire with an enemy within this many turns ignores its strategic objective.")]
        [Range(0, 6)]
        public int objectiveContactTurns = 3;

        [Tooltip("Turns of travel beyond which a unit is not considered for an objective.")]
        internal int objectiveReachTurns = 8;

        [Tooltip("How many turns ahead approach routes are planned.")]
        internal int planningHorizon = 5;

        [Tooltip("Bonus, as a fraction of the unit's worth, for continuing toward last turn's destination. Stops units flip-flopping between two plans.")]
        internal float commitment = 0.25f;

        [Header("Planning")]
        [Tooltip("Cost per turn of delay when comparing destinations of different value.")]
        public float turnPenalty = 0.12f;

        [Tooltip("How many candidate hub positions get an expensive full supply re-solve.")]
        internal int supplyCandidateCount = 12;

        [Tooltip("Turns of hub autonomy below which the AI treats reconnection as urgent.")]
        [Range(0, 6)]
        public int hubPanicThreshold = 3;

        [Header("Board read")]
        [Tooltip("Worth of a tile a supply unit could cover after one move, as a fraction of real supply (0 = unsupplied, 1 = supplied).")]
        [Range(0f, 1f)]
        public float potentialSupplyValue = 0.25f;

        [Tooltip("How many enemy turns ahead the strike map looks (1 disables the extra turns; the coming turn is the world simulation's).")]
        internal int enemyReachTurns = 3;

        [Header("World score")]
        [Tooltip("Our units' best blow on our next turn, counted in the world score at this fraction. We attack first, so this is a near-certain gain.")]
        [Range(0f, 1f)]
        public float ownStrikeWeight = 0.1f;

        [Tooltip("Enemy units' best blow on their turn after that, counted against us at this fraction.")]
        [Range(0f, 1f)]
        public float enemyStrikeWeight = 0.25f;

        [Header("Coordination")]

        [Tooltip("A group plan must beat 'stay' by this much (gold) in the forecast to be adopted.")]
        internal float coordinationMargin = 500f;

        [Tooltip("Attack options each unit keeps for battle plans.")]
        internal int coordOffensiveOptions = 3;

        [Tooltip("Sea lift: how much a transport ship is worth to buy, times the land units waiting to cross that it would take over (their value, discounted for the turns to get them there). Above 1: the AI puts a ship ahead of a fighting unit of the same value, since without one the units across the water do nothing.")]
        public float seaLiftWeight = 3f;
        [Tooltip("Sea lift: steps added to a trip by sea (AIMap.SeaTravel) for getting into the ship and out on the far shore. An estimate from the rules, not a rule: getting in ends the unit's turn, getting off is free.")]
        internal int seaLiftBoardSteps = 2;
        [Tooltip("Riding to the assault: a fighting unit more than this many turns from contact on foot gets in a transport.")]
        public int assaultBoardTurns = 2;
        [Tooltip("Riding to the assault: production buys transports (helicopters and planes too) while fighting units far from the fight wait without room in one — worth what it adds to the riders it would carry (the nearest of them): each one's value times the turns sooner it reaches the fight riding than walking, over productionLifeRef (at most its whole value) — times this (more than 1: it goes back for more, and carries the wounded, over its life). 0: not bought for riders.")]
        public float assaultTransportWeight = 2f;
        [Tooltip("Riding to the assault: infantry bought at a base beside which an empty troop transport waits (or one is bought this turn) is worth this much more, until the transport's room is filled — it gets in next turn and rides.")]
        public float assaultFillBonus = 0.5f;
        [Tooltip("Riding to the assault: an empty troop transport first picks up a fighting unit of ours down to this strength (not already on a building that services it) and carries it to the nearest of our buildings that does, to mend. 0: no medevac.")]
        public int assaultMedevacStrength = 3;
        [Tooltip("Riding to the assault: riders are put down when their front attacks, or where ours (with them) are at least this many times theirs within three tiles.")]
        internal float assaultWaveRatio = 1f;

        [Tooltip("Forecast only the theatre of the unit deciding: enemies that could strike anywhere it could move or fire this turn (move and range, plus theaterMargin), and our units they could strike. Everything else stands as it is, the same for all of its options. Off: the whole board, for every option.")]
        internal bool theaterForecast = true;

        [Tooltip("Extra tiles around the theatre, for knock-on effects (an enemy driven back, a tile freed by a kill, roads that move units further than their move).")]
        internal int theaterMargin = 6;

        internal float productionBuildingWeight = 3f;
        internal float supplyBuildingWeight = 2f;

        [Tooltip("Strength a supply-tree link may be expected to lose tonight (the share of enemy fire that can reach its tile) and still stand there.")]
        internal float supplyTreeRisk = 1f;

        [Tooltip("Strength a supply-chain link may be forecast to lose tonight (the enemy's reply, or the expected share of its fire) and still stand there. 0: only where nothing can hit it — with fast units about (gunships, recon), nothing forward qualifies, the chain stops forming, and the supply units ahead of it are cut off while the ones behind sit by the HQ.")]
        public float chainRiskTolerance = 0f;

        [Tooltip("March: a fighting unit out of contact gains this much of a strength's worth for each step over land it ends nearer its front's objective (linear, so it pulls from any distance; supply costs still keep it inside supply).")]
        public float marchWeight = 0.05f;

        [Tooltip("A battle plan is adopted only if its forecast beats its units acting alone by more than this (gold).")]
        internal float coordinationPassMargin = 0f;

        [Tooltip("A unit destroyed costs this many more of its strength points' worth than the points themselves: alive, it would heal back, hold ground and fire again.")]
        [Range(0f, 6f)]
        public float deathPremium = 1f;

        [Tooltip("A supply unit's death costs, in the world score, one strength of every unit it alone keeps supplied, times this.")]
        [Range(0f, 3f)]
        public float hubDependentsWeight = 1f;

        [Tooltip("A hit our units still to act this turn could finish counts as this fraction of a kill (its reply removed, its material gone).")]
        [Range(0f, 1f)]
        public float assistWeight = 0.75f;

        [Tooltip("Mutual cover, gold per tile: each tile next to one of our units that the enemy could step onto next turn counts + this if another of our units could strike it next turn, - this if none could.")]
        public float coverTileValue = 25f;

        [Tooltip("Artillery: gold per tile the enemy could move to next turn that the gun covers from where it ends its turn (its zone of control).")]
        public float artilleryCoverValue = 25f;

        internal float speculateAfterShare = 0.7f;
        internal int speculateQuietFrames = 20;

        internal float roleWorthMin = 0.5f;
        public float roleWorthMax = 2f;

        [Tooltip("Space: gold per tile one side's units could move to next turn, and again per tile they could strike. Ours count for us, theirs against us — so taking space from them is a gain, and doing it with a cheap unit is a cheap gain.")]
        public float spaceTileValue = 10f;

        [Tooltip("Gold per enemy unit out of supply in the world after our move (counted at the start of their turn, where the game checks supply).")]
        public float enemyUnsuppliedValue = 100f;

        [Tooltip("Space: gold per tile of enemy supply our unit's position cuts off.")]
        public float spaceSupplyValue = 50f;

        [Header("Supply unit pricing")]
        [Tooltip("With value-added pricing: a move that cuts off another supply unit, connected where this one stands now, costs this fraction of that unit's whole worth (one it reconnects earns as much), and a transport carrying supply units is held rather than cut the network. 0: only a strength, as a relay. 1 beat 0 in 19 of 32 big-map games (ChanceMeeting 6 of 8, by large margins).")]
        public float hubLinkPenalty = 1f;

        [Tooltip("Turns saved that justify the two-turn overhead of loading and unloading.")]
        internal int transportWorthwhileSaving = 0;

        [Header("Transport")]
        [Tooltip("How much more cautious a transport is while carrying cargo. Losing a transport loses its cargo too.")]
        public float loadedTransportRisk = 2.5f;

        [Header("Fog of war")]
        [Tooltip("Under fog of war, play by it: decide on the board as this side knows it (unseen enemies off the board, those seen lately where they were last seen). Off: the AI sees everything.")]
        public bool fogFair = true;
        [Tooltip("Turns an enemy unit is remembered where it was last seen (fog of war, fogFair).")]
        internal int fogMemoryTurns = 2;
        [Tooltip("Gold per tile a stop would bring into sight that is not seen now (more the longer it has gone unseen, up to 4 turns). Recon units count it double.")]
        public float scoutTileValue = 15f;
        [Tooltip("An ambush: ending where the enemy (as known) would not see this unit, within reach of an enemy that may run into it. Fraction of the unit's worth per enemy in reach (up to three).")]
        public float ambushValue = 0.3f;
        [Tooltip("Share of tonight's risk taken back for a stop the enemy (as known) would not see this unit on: it can only be fired on by what stumbles into it.")]
        public float hiddenRiskRelief = 0.5f;

        [Tooltip("Price what the fog may hide (AISigint): enemies seen before and lost track of, somewhere within reach of where they were last seen, and ground not seen lately, which may hide anything the enemy fields.")]
        public bool fogThreat = true;
        [Tooltip("Enemy moves an enemy unit lost track of is still counted near an objective by the campaign (AICampaign). The tactical fog risk (AISigint) keeps it until it is seen again or dies.")]
        internal int sigintMoves = 6;
        [Tooltip("Confidence kept in a lost enemy unit for each move it has had since it was last seen: the campaign's estimate of enemy power near an objective (AICampaign). The tactical fog risk (AISigint) does not fade it: it spreads it over where the unit could be.")]
        internal float sigintDecay = 0.7f;
        [Tooltip("Appetite for ground the fog hides (fogThreat). 0: every unknown tile may hide an enemy unit of a kind it fields (the average of those kinds' damage, by the share of unknown ground near: a threat from anywhere, with no position); 1: unknown ground is empty. Default 1 since lost units are tracked by where they could be (AISigint): the average over every kind feared expansion everywhere while the enemy stood in a known place. (0.2 had beaten fogThreat off on the big maps, 18 of 24, before that.)")]
        [Range(0f, 1f)]
        public float fogAggression = 1f;
        [Tooltip("Unknown ground (never seen, or not for fogUnknownTurns) is feared by the share of the enemy's forces we cannot place this turn (1 - known / estimate, AIKnowledge), not by 1 - fogAggression: wary of every dark tile while the enemy's whereabouts are unknown, less as it is found. The loss is priced at the unit's worth, so a dear unit fears it most.")]
        public bool fogUncertainty = true;
        [Tooltip("Turns a tile has to go unseen to count as unknown again (fogAggression). Tiles never seen always do.")]
        [Range(1, 20)]
        public int fogUnknownTurns = 6;
        [Tooltip("Fog: ground not seen this turn within this many tiles of an enemy unit we see or remember counts as unknown however lately it was seen (0: off).")]
        public int fogStaleRadius = 5;
        [Tooltip("Fog: tiles over which an anchor's share of the unplaced enemy falls off by a factor e.")]
        internal float fogAnchorFalloff = 2f;
        [Tooltip("Fog: an anchor's weight in sharing out the unplaced enemy: each enemy unit seen or remembered 1, the enemy HQ this, its production sites fogProductionWeight (purchases on).")]
        internal float fogHQWeight = 4f;
        internal float fogProductionWeight = 2f;
        [Tooltip("Fog: the weight of what the fog may hide in a move's score (AIFog.FogRisk), against the forecast of the enemy we see or remember (1: as much).")]
        internal float fogRiskWeight = 1f;
        [Tooltip("Procurement: added to a kind's value for each facility (not under fire) that can build it, pushed back by the resistance its front expects.")]
        internal float procFacilityBonus = 1000f;
        [Tooltip("Procurement: the most a unit is saved for (the longest wait is the turns to save this much at 70% of the map's income).")]
        public float procSaveMost = 15000f;
        [Tooltip("Procurement: a front with no resistance multiplies infantry and troop transports by this.")]
        internal float procNoResistance = 1.5f;
        [Tooltip("Procurement: a front short of its units with no resistance: the cheapest fighting unit gains up to this share (cheapest cost over its cost).")]
        internal float procCheapBonus = 1f;
        [Tooltip("Procurement: across water from its facility, a front multiplies aircraft, ships and ranged units by this.")]
        internal float procAcrossWater = 1.5f;
        [Tooltip("Procurement: a front with water mixed in (over a fifth of the ground near it) multiplies aircraft and amphibious units by this.")]
        internal float procMixedWater = 1.3f;
        [Tooltip("Procurement: a stealthy unit's value is multiplied by this.")]
        internal float procStealth = 1.2f;
        [Tooltip("Procurement: past this many lower-tier units to one of the next tier up, the next tier up gains half again.")]
        public float procTierRatio = 5f;
        [Tooltip("Procurement: a production building within two turns of a facility, capturable, adds this to what capturing units built there are worth (plus its income over objectiveHorizonTurns).")]
        internal float procCaptureProducerValue = 5000f;
        [Tooltip("Procurement: below this much gold, an empty reserve asks for troop transports (to fill with infantry).")]
        internal int procReserveRich = 15000;
        [Tooltip("Procurement: a fixed jiggle, up to this, added to a unit's value (by its kind, its facility and the day).")]
        public int procJiggle = 2000;
        [Tooltip("Procurement: fuel or supplies below this share of the most count as running low.")]
        internal float procResupplyLow = 0.5f;
        [Tooltip("Procurement: a facility's weight in the split of the gold is 1, plus this times its front's priority (over the top front's) times how much its best unit helps that front (its factors: time to the fight, a lift needed, morale), plus procWantShare times its best option's value (over the best anywhere).")]
        internal float procFrontShare = 2f;
        [Tooltip("Procurement: see procFrontShare.")]
        internal float procWantShare = 2f;
        [Tooltip("Procurement: an option worth less than this times our number of facilities is not bought (the more facilities, the likelier another builds something good); a facility with none above it buys nothing and gives its gold up.")]
        internal float procMinValuePerFacility = 500f;
        [Tooltip("Procurement: a land fighting unit whose front is across water from its facility (it cannot get there by itself; it needs a lift) is worth this much. 1: off.")]
        public float procNeedsLiftCut = 1f;
        [Tooltip("Procurement: see there.")]
        internal float procMoraleDrop = 0.3f;
        [Tooltip("Procurement: see there.")]
        internal float procMoraleRise = 0.5f;
        [Tooltip("Procurement: see there.")]
        internal float procMoraleRecover = 0.05f;
        [Tooltip("Procurement: see there.")]
        internal float procMoraleFloor = 0.2f;
        [Tooltip("Procurement: a unit saved for is worth its value times procWaitBase^-(procWaitPower x turns to wait ^ procWaitExponent).")]
        internal float procWaitBase = 1.1f;
        [Tooltip("Procurement: see procWaitBase.")]
        internal float procWaitPower = 4f;
        [Tooltip("Procurement: the turns waited are raised to this power in the discount (1: procWaitBase^-(procWaitPower x turns); 2: the first turn costs little, each more far more).")]
        internal float procWaitExponent = 1f;
        [Tooltip("Procurement: what a facility was saving for last turn is worth this much more (the choice holds).")]
        internal float procStick = 1.25f;
        [Tooltip("Procurement: a facility's longest wait is the turns to save procSaveMost on its share of this much of the map's income plus the rest of ours (0.5: half the map's, half ours); never less than the turns to save for a supply unit on its share of our income.")]
        public float procWaitMapShare = 0.5f;
        [Tooltip("Procurement: a facility whose best option is worth more than the mean of every facility's best (what something failed for want of: supply for a front short of it, transports for units waiting to cross) gains this times (its best over the mean, less 1) in weight, beyond procFrontShare and procWantShare. 0: off.")]
        public float procDemandWeight = 1f;
        [Tooltip("Production: an amphibious unit (a marine) is not built at a facility with no water within this many tiles (as anti-air is not built against an enemy with no aircraft). 0: off.")]
        public int productionAmphibiousWater = 12;
        [Tooltip("A scout's pull toward where it will look: a fraction of its worth for every step closer.")]
        internal float scoutPull = 0.3f;
        [Tooltip("Extra worth of a scouting tile where an enemy we lost track of may be standing, or that is unknown (times its confidence): looking there tells us where it is.")]
        public float scoutThreatBonus = 1f;

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
