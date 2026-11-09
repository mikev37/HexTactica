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

        [Header("Campaign (objectives and task forces)")]
        [Tooltip("Plan the war as objectives read from the map — defend our HQ, secure a building, clear an enemy group, scout what the fog hides — each with a task force and supply fit to achieve it, and fight fronts made from them (AICampaign). Off: fronts where the fighting is (AIStrategic).")]
        public bool campaign = true;
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
        [Tooltip("Campaign: a building's task force has a capturer only in a unit that lives to take it (its way there, starving out of supply, the capture's points, the enemy there). Off: any unit that can capture.")]
        public bool capturerLives = true;
        [Tooltip("A ground transport never ends its turn on a building of ours that builds units (it would take the slot, and nothing could be bought there).")]
        public bool keepBuildSlotsClear = true;
        [Tooltip("Supply chain: a supply unit aboard a transport is placed as one on foot is — the tile its transport reaches that brings supplied ground nearest the objective, only if better than holding. Off: the furthest linked tile toward the objective.")]
        public bool unifiedLinkPlacement = true;
        [Tooltip("A unit that would wait is first set aside and decides again after every other unit has moved; only if it would wait again does it skip its turn.")]
        public bool waitTwice = true;
        [Tooltip("Turn order: artillery with a target in range acts first to fire; one that decides to do anything but fire there is set aside and decides again after the rest of the line has moved.")]
        public bool artilleryFireOrDefer = true;
        [Tooltip("A supply unit gets into a transport that has already acted this turn as readily as a fresh one (aboard it is linked as cargo, and rides next turn); a fresh one is still preferred.")]
        public bool boardSpentTransports = true;
        [Tooltip("Supply chain: a link within this many tiles of another node of ours (a link kept before it, rearmost first, or a supply building) is released as spare, free for other duties. 0: off.")]
        public int chainLinkSpacing = 2;
        [Tooltip("The way to an objective: steps of detour worth taking to avoid one tile the enemy can strike next turn (so a middle held in strength is gone round).")]
        public float routeDanger = 3f;
        [Tooltip("Scouting objectives: how much ground one covers (hexes around its centre), and how many at most.")]
        public int scoutRadius = 3;
        public int scoutAreas = 2;
        [Tooltip("Campaign task forces gather at their staging tile before going in (AIStrategic.GatherThenGo). Off: aggressive as soon as they are given what they need.")]
        public bool campaignGather = true;
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
        [Tooltip("Gathering: a unit past the staging ground counts as gathered only with the body (within the staging radius of the task force's centre too). Off: anywhere past it counts (a lone unit up front could send the front in).")]
        public bool campaignGatherWithBody = true;
        [Tooltip("How large a staging ground is: hexes around the staging tile. 0: one move of our typical unit.")]
        public int campaignStagingRadius = 0;
        [Tooltip("A task force takes only units within this many turns of its staging tile, and none fighting elsewhere. 0: frontReachTurns.")]
        public int campaignDrawTurns = 0;
        [Tooltip("Time boxes: a task force not gathered on its staging ground by its campaignDrawTurns-th turn is dissolved; one that has not achieved its objective in this many turns (plus a turn for every turn its target is beyond campaignDrawTurns of its staging tile) gives it up. A given-up objective is left alone for this many turns. 0: no time boxes.")]
        public int campaignTimeBox = 6;
        [Tooltip("Breadth first: no front further than this many turns away (the slowest unit it needs, riding where a transport can take it) while any objective is nearer. 0: no cutoff.")]
        public int campaignMaxTurns = 3;
        [Tooltip("A task force that has gone in and whose main body has got no nearer its objective for this many turns in a row is dissolved (its objective given up a while). 0: never.")]
        public int campaignStallTurns = 0;
        [Tooltip("Also give up a task force that has not achieved its objective in campaignTimeBox turns (plus a turn for every turn its target is beyond campaignDrawTurns of its staging tile). Off: only the staging time box applies.")]
        public bool campaignTransitTimeBox = false;
        [Tooltip("A task force given up by a time box leaves its objective alone for this many turns. 0: not at all — its task force is dissolved and the objective formed again from the units best placed for it now.")]
        public int campaignGiveUpTurns = 0;
        [Tooltip("A separate group of our units, too far from every task force's staging ground to join it, forms a task force of its own — another one for an objective already taken on, if that is its best.")]
        public bool campaignWings = true;
        [Tooltip("Cohesion: a task force's unit more than this many of its own turns from the task force's centre drops out of it. 0: no limit.")]
        public int campaignCohesionTurns = 2;
        [Tooltip("A task force's unit this many turns or fewer from contact fights as it will: no lean toward its staging ground or objective, no pace, no dip rule (the campaign sets the stage; the fight is the tactics'). 0: never released.")]
        public int campaignReleaseTurns = 2;
        [Tooltip("Campaign: an objective to defend our HQ with a task force when an enemy can reach it next turn. Off: our HQ is defended through the move score (hqThreatNear, hqThreatOn), by whoever is there.")]
        public bool campaignDefendHQ = false;
        [Tooltip("Campaign: an enemy unit counts at the objectives it is nearest (within a turn) and those whose way in it stands across, not at every objective it could reach in time.")]
        public bool campaignEnemyNearestOnly = true;
        [Tooltip("Logistics: a front tells its chain where its units need supply (its unit out of supply nearest its target, else its lead unit), not its staging ground or target; the chain works out its links.")]
        public bool campaignChainToUnits = true;
        [Tooltip("Campaign: a scouting trip's escort, as this share of the enemy that could fight there times objectiveForceRatio (0: the scout alone; was 0.5).")]
        public float campaignScoutEscort = 0f;
        [Tooltip("Campaign: a task force's unit within this many turns of an enemy unit is engaged (it fights to survive); the others are staged (on the staging ground) or en route.")]
        public int campaignEngagedTurns = 2;
        [Tooltip("Campaign: a task force with most of its units not engaged whose enemy estimate has risen to campaignSurgeRatio times what it was formed against stops: its units that are not engaged pull back to its staging ground (until there, at most campaignSurgeWithdrawTurns), then are planned afresh.")]
        public bool campaignSurgeAbort = true;
        [Tooltip("Campaign: the rise in a task force's enemy estimate, over what it was formed against, that stops it (campaignSurgeAbort).")]
        public float campaignSurgeRatio = 1.5f;
        [Tooltip("Campaign: turns at most that a stopped task force's units pull back to its staging ground before they are planned afresh (campaignSurgeAbort).")]
        public int campaignSurgeWithdrawTurns = 2;
        [Tooltip("Campaign: a building that makes units is worth its own income plus our income divided by the production buildings we have, over objectiveHorizonTurns (instead of weighting by building tier).")]
        public bool productionValueByIncome = true;
        [Tooltip("Campaign: a task force needs no supply unit within one transport move of our supplied ground, and one per transport move beyond that (instead of relays plus spread); spare supply units are no longer spread over task forces.")]
        public bool campaignHubNeedSimple = true;
        [Tooltip("Campaign: an objective with none of our supplied ground on its land counts its supply need over the water, as supply goes there: straight over in the moves of a ship or aircraft that carries a supply unit (ours, or one we can build). Off: the whole route, in truck moves (an island wanted 32 supply units).")]
        public bool campaignSupplyByTransport = true;
        [Tooltip("Campaign: a chokepoint or junction in our vision is held (achieved, released, like a building taken); out of sight it is a Hold objective again only once it has gone dark for fogUnknownTurns, or an enemy unit seen or remembered comes within fogStaleRadius of it.")]
        public bool campaignHoldContested = true;
        [Tooltip("Campaign: this share of the army's power stays in the reserve: a new task force is taken on only while what is committed stays within the rest.")]
        public float campaignReserveShare = 0.33f;
        [Tooltip("Campaign: at most one task force per this many fighting units (after the reserve share), scouting not counted (0: off).")]
        public int campaignMinTaskForce = 0;
        [Tooltip("Campaign: a task force gathers on our supply unit (a chain node) on supplied ground nearest its target along the way, so nearby task forces share it; the supplied tile nearest the target if there is none.")]
        public bool campaignStageOnSupply = true;
        [Tooltip("Campaign: a task force whose power has fallen below campaignBreakShortRatio of its need now (the enemy there times objectiveForceRatio) is broken, as one that lost objectiveReplanLoss of its own power is: formed again only if what can join is enough. Not the HQ's defence, enemy on our ground, scouting, or an all-in task force.")]
        public bool campaignBreakShort = true;
        [Tooltip("Campaign: the share of its need now below which a task force is broken (campaignBreakShort).")]
        public float campaignBreakShortRatio = 0.5f;
        [Tooltip("Campaign: units on no task force and not fighting gather as one central reserve behind the main front, in our supply out of the enemy's reach; the rest (campaignActiveReserveShare, and any fighting) stay with the ordinary fronts.")]
        public bool campaignReserve = true;
        [Tooltip("Campaign: the central reserve is mobile — aircraft, combat ships, loaded transports (with what they carry) and fast ground units (campaignReserveMinMove or more); infantry on foot and other slow units stay with the ordinary fronts. Off: every spare fighting unit on the ground, as before.")]
        public bool campaignMobileReserve = true;
        [Tooltip("Campaign, mobile reserve: the least movement a ground unit needs to be in the reserve (a tank: 6, a recon: 8; a grunt: 3).")]
        public int campaignReserveMinMove = 5;
        [Tooltip("Campaign: estimate how well the enemy's forces are known and how they compare to ours (AIKnowledge): fear of the fog scales down with it, and once both are high enough the army goes all out — every front attacks, task forces leave our supply, the enemy HQ comes first, the reserve is released. The reserve is also released once confidence passes knowledgeReserveConfidence. Off: none of it.")]
        public bool campaignKnowledge = true;
        [Tooltip("All out (campaignKnowledge): our fighting strength at least this many times what the enemy could have.")]
        public float knowledgeAllOutRatio = 2f;
        [Tooltip("All out (campaignKnowledge): at least this confidence in our knowledge of the enemy's forces (0..1; 1 without fog).")]
        public float knowledgeAllOutConfidence = 0.6f;
        [Tooltip("The reserve is released (campaignKnowledge) once confidence in our knowledge of the enemy reaches this: the reserve is for an enemy whose potential is unknown.")]
        public float knowledgeReserveConfidence = 0.6f;
        [Tooltip("Campaign: the share of units on no task force kept with the ordinary fronts as the active reserve (the rest gather as the central reserve).")]
        [Range(0f, 1f)]
        public float campaignActiveReserveShare = 0.1f;
        [Tooltip("Regrouping: an expensive unit (regroupExpensiveCost or more) under fire without local superiority pulls back out of the enemy's reach, while cheap units fight, empty transports screen, and the central reserve is brought up to the front most pressed. It engages with the superiority, or when no tile out of reach is left. Replaces task forces falling back.")]
        public bool regroup = true;
        [Tooltip("Regrouping: units costing this much or more pull back without local superiority (grunts and conscripts stay and fight).")]
        public int regroupExpensiveCost = 3000;
        [Tooltip("Regrouping: only a unit that would expect to lose this many strength points where it stands tonight.")]
        public float regroupRisk = 2f;
        [Tooltip("A front's posture weighs what we could replace: the edge it needs to press is 1 + this with nothing to buy with, else 1 / (1 + this x R), R the times the gold in hand and strategicReplaceTurns of income would rebuild the army (at most strategicReplaceCap). 0: off.")]
        [Range(0f, 1f)]
        public float strategicReplaceWeight = 0.3f;
        [Tooltip("Turns of income counted as gold to replace losses (strategicReplaceWeight).")]
        public int strategicReplaceTurns = 3;
        [Tooltip("The most times over the army's cost counted as replaceable (strategicReplaceWeight).")]
        public float strategicReplaceCap = 10f;
        [Tooltip("Regrouping: task forces' fronts are aggressive (strike first) rather than taking the ordinary fronts' posture by strength ratio.")]
        public bool campaignStrikeFirst = false;
        [Tooltip("Regrouping: empty transports with no job screen the enemy (as idleTransportScreens).")]
        public bool regroupTransportScreens = true;
        [Tooltip("Regrouping: the central reserve goes to the front whose units the enemy can strike with more than this many times their own power.")]
        public float regroupCommitRatio = 1f;
        [Tooltip("Move score: our HQ's worth (and the enemy's) as this many times the board's stake: capture progress on an HQ counts this much more than the rest of the board.")]
        public float hqCaptureWeight = 2f;
        [Tooltip("Move score: each strength point of enemy infantry within a move of our HQ (after their reply) costs this share of the board's stake.")]
        public float hqThreatNear = 0.02f;
        [Tooltip("Move score: each strength point of enemy infantry on our HQ (after their reply) costs this share of the board's stake.")]
        public float hqThreatOn = 0.1f;
        [Tooltip("Move score: a building not ours that no enemy stands on (after their reply) is worth this share of it; the enemy HQ, worth the board, most.")]
        public float buildingVacantShare = 0.02f;
        [Tooltip("Move score: a building not ours with our infantry on it is worth this share of it, twice that when it is capturing (on it without having attacked).")]
        public float buildingOccupyShare = 0.1f;
        [Tooltip("A unit able to capture that stands on the enemy HQ stays and takes it, whatever else (the old hard rule). Off: the move score decides (buildingVacantShare, buildingOccupyShare).")]
        public bool holdEnemyHQRule = false;
        [Tooltip("A front's own supply units: one for every this many of its fighting units (after its chain).")]
        public int campaignUnitsPerSupply = 6;
        [Tooltip("Buying: an enemy unit seen counts for this many days after it is last seen.")]
        public int productionMemoryDays = 6;
        [Tooltip("Buying: what is bought against an enemy unit counts 1 / (1 + c / this), c the full kills our army could make on it (nothing of ours can hit it: full value).")]
        public float productionCounterUnits = 2f;
        [Tooltip("Buying: each unit of a kind we already have (or have planned this turn) makes another worth this much (1: no variety rule; what we lack against the enemy, AIProduction.Scarcity, already gives diminishing returns).")]
        [Range(0f, 1f)]
        public float productionVariety = 0.97f;
        [Tooltip("Production: a fighting unit is worth less the longer it would take to reach the fight from where it is built — 1 / (1 + turns to the nearest known enemy / productionTimeHorizon), by its own movement — relative to the quickest fighting unit we could build anywhere (that one keeps its full worth, so fighting units as a whole lose nothing against supply). A slow unit built far from the front (a conscript at a distant city) no longer outbids one that gets there. Off: where it is built does not matter.")]
        public bool productionTimeToFront = true;
        [Tooltip("Production, productionTimeToFront: turns to the fight that halve a unit's worth.")]
        public float productionTimeHorizon = 3f;
        [Tooltip("Buying: a fighting unit is worth at least this share of its own full worth, so a slot is filled when gold allows even if nothing scores against the enemy.")]
        public float productionFloor = 0.05f;
        [Tooltip("Buying: a unit's presence on the field — actions the enemy must spend on it, ground it holds and sees — as this share of its own full worth, added to what it would do in exchanges.")]
        public float productionPresence = 0.25f;
        [Tooltip("Buying: what a unit would take from the enemy counts this much against what it would do to them.")]
        public float productionDefenceWeight = 0.5f;
        [Tooltip("Buying: what a unit does to an enemy is counted over its whole life — the kills it makes at each strength from 6 down to 1 (damage falls with strength) — so firepower beyond a kill (a bomber, rockets) counts: they still kill when worn down. Off: only at full strength, capped at a kill.")]
        public bool productionOverkill = true;
        [Tooltip("Buying: a unit of a higher tier (productionTierCosts) is hit less the more lower-tier units we have to screen it: what it would take from the enemy is cut by up to productionShieldCut (all of it for a unit that fires from range or flies, half for one that must close), in proportion to how many of its tier productionShieldRatio lower-tier units could each cover. Infantry screens tanks, AA and gunships; those screen bombers, rockets, missiles and fighters. 0: off.")]
        public float productionShieldCut = 0.6f;
        [Tooltip("Buying (productionShieldCut): lower-tier units it takes to screen one unit of a tier.")]
        public float productionShieldRatio = 2f;
        [Tooltip("Buying (productionShieldCut): the costs from which a unit is tier 1, and tier 2.")]
        public Vector2 productionTierCosts = new Vector2(5000f, 11000f);
        [Tooltip("Buying: a fighting unit is worth what it does in a shot times the turns it expects to live — 6 strength over what it would take a turn (each enemy's shot, cut by our screen, productionShieldCut), between 1 and productionLifeMax — over productionLifeRef turns: a rocket behind a screen keeps firing, a grunt in front trades and dies. Replaces the one exchange (what it does less productionDefenceWeight of what it takes) and productionDefenceFade. Off: the one exchange.")]
        public bool productionLifetime = true;
        [Tooltip("Buying (productionLifetime): how often a unit gets a shot: its reach (attack range, plus its move for one that moves and fires in a turn) over this many tiles, at most 1 — rockets reach six tiles from where they stand every turn, a grunt has to walk up first. 0: off.")]
        public float productionReachRef = 6f;
        [Tooltip("Buying (productionLifetime): the most turns a unit is expected to live.")]
        public float productionLifeMax = 8f;
        [Tooltip("Buying (productionLifetime): turns of life a unit's worth is measured against (a unit expected to live this long keeps the worth of one exchange).")]
        public float productionLifeRef = 3f;
        [Tooltip("Buying: the weight on what a unit would take (productionDefenceWeight) falls as our army grows — halved at this many fighting units — so attack counts for more than holding on. 0: off.")]
        public float productionDefenceFade = 20f;
        [Tooltip("Buying: a front's want (the units that do most against its enemy, at its nearest producer) counts this much.")]
        public float productionFrontWeight = 1f;
        [Tooltip("Buying: a supply unit's (or truck's) value — the fighting units it would supply, or the force a front waiting on supply needs — counts this much.")]
        public float productionSupplyWeight = 1f;
        [Tooltip("Buying: supply units beyond one per campaignUnitsPerSupply fighting units, for fronts short of supply (and the campaign's next front waiting on one).")]
        public int productionSupplyExtra = 2;
        [Tooltip("Buying: cap supply units at one per campaignUnitsPerSupply fighting units plus productionSupplyExtra. Off: no cap; supply competes on value.")]
        public bool productionSupplyCap = false;
        [Tooltip("Buying: a supply unit's value scales with how far our army outmatches what we know of theirs (half the edge, 0.25 to 2).")]
        public bool productionSupplySuperiority = true;
        [Tooltip("Buying: spend the gold left after each slot has its best value for cost on upgrades, the most value per extra gold first (every slot filled with the best it can).")]
        public bool productionSpendAll = true;
        [Tooltip("The units no objective needs go to the task forces too (by objectiveSpread). Off: they stay with the ordinary tactics.")]
        public bool campaignLeftoverToFronts = false;
        [Tooltip("Place objectives (AICampaign.Places): both ends of chokepoints and road junctions not yet in our supply, at most this many, the nearest our units. 0: none.")]
        public int campaignPlaces = 12;
        [Tooltip("A place objective's worth, in multiples of what a building with no income is worth.")]
        public float campaignPlaceWeight = 0.5f;
        [Tooltip("Out of contact, a campaign unit may spend this many rations out of supply (and go no further than its rations bring it back from); past that it is pulled back. 0: never out of supply.")]
        public int campaignDipTurns = 1;
        [Tooltip("The world score counts what a unit would lose starving before it got back to supply (AIScore.StrandingCost), for every unit.")]
        public bool worldStarvation = true;
        [Tooltip("Campaign supply by logistics (AILogistics): each front, largest first, is joined to the network by a chain of supply units, and the supply units left over are the fronts' own, by their number of units, placed tactically near them. Off: each task force runs its own chain of the supply units it was given.")]
        public bool campaignLogistics = true;
        [Tooltip("A task force falls back to its staging ground (and the campaign plans again) only when its power is under this share of the enemy that can strike its units next turn: 0.5, outnumbered two to one. Short of that its units fight as their tactics decide (their own retreats included). 0: never.")]
        public float campaignFallbackRatio = 0f;

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

        [Tooltip("Hubs: worth of holding ground — each tile of land newly supplied by us and not by the enemy, as strength points of an average fighting unit. Spreads the network over the map instead of only after the army.")]
        [Range(0f, 1f)]
        public float groundSupplyWeight = 0f;

        [Tooltip("Multiplier on a hub's target worth for the units it sustains (base: a quarter of their combined value).")]
        public float enemySupplyTargetWeight = 1f;

        [Header("Combat")]
        [Tooltip("How much predicted retaliation damage discounts an attack.")]
        [Range(0f, 2f)]
        public float retaliationWeight = 1f;

        [Tooltip("Bonus multiplier when an attack finishes the target.")]
        public float killBonus = 1.35f;

        [Tooltip("Forecast boldness: the enemy's reply in the forecast lands this fraction lighter (0 = full worst case, 0.5 = half). The enemy still picks its targets on full damage; only what it costs us is discounted. Real opponents attack less than the forecast's all-out reply. 0.5 beat 0.3 in 18 of 32 big-map games (+737k; ChanceMeeting 6 of 8, BushWar 2 of 8).")]
        [Range(0f, 0.5f)]
        public float replyDiscount = 0.5f;

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
        [Tooltip("A wounded unit weighs fighting on against withdrawing by the move score (what holding its ground is worth counts: a building it is taking). Off: it withdraws by position alone.")]
        public bool woundedWorldCompare = true;

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

        [Tooltip("Objectives: every building we do not hold is one (AIObjectives.Capture) — the nearest unit that can capture it and an escort for its defence — so the army spreads out to take them in parallel instead of drifting toward one.")]
        public bool captureObjectives = true;

        [Tooltip("Capture objectives: each step toward the building, for a unit out of contact sent to take it, as a fraction of a strength point of that unit (and six times that for standing on it, for one that can capture).")]
        public float capturePull = 0.3f;

        [Tooltip("Capture objectives only for buildings within this many tiles of our supplied ground: the army's ground, not the whole map.")]
        public int captureReach = 3;

        [Tooltip("A unit with an attack open to it does not choose to capture instead: taking a building earns nothing in its choice while it could fight.")]
        public bool fightBeforeCapture = true;

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
        [Tooltip("A unit's decision from the coordination pass (every unit in contact decides once before anyone acts) is kept for its turn, unless something within its reach or the enemy's has moved, been hurt or appeared since, or its tile or target is gone. Off: every unit decides again on its turn.")]
        public bool coordinationReuse = true;
        [Tooltip("A unit that cannot take buildings pays half a building's worth for ending its turn on one that is not ours while a unit of ours that can take it is within two turns of it: it keeps out of the way (AITactical.CaptureBlockCost). Off (the default): only a task force's own objective building is kept clear, by CampaignStep and the objective's pull.")]
        public bool captureBlockCost = false;

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
        [Tooltip("Supply chain: while any of our supply units is cut off, a link goes where it connects the most of them again (still supplied and safe), and no link moves to cut off more.")]
        public bool chainReconnects = true;

        [Tooltip("Buy a supply unit at the HQ when the chain cannot reach as far as the army needs it to.")]
        public bool buySupply = true;

        [Tooltip("Buy fighting units at our buildings with the money left at the end of the turn, when the rules and the scenario allow it (AIProduction).")]
        public bool buyUnits = true;

        [Tooltip("Fighting units out of contact ride a free transport to the front when that gets them there sooner than walking (the same walk-or-ride planner supply units use), and get off where the ride ends.")]
        public bool combatRides = true;

        [Tooltip("Riding to the assault (AIAssault), as a player uses transports: a fighting unit far from the fight gets into a transport with room (one carrying no supply unit, not on supply duty) and stays aboard while it moves up with the army, out of the enemy's reach; riders are put down only where they can strike this turn (getting off costs nothing: they still move and fight) and when the wave goes in; empty transports (not trucks, which carry the supply) go back for more. Replaces combatRides' hop to supplied ground for fighting units.")]
        public bool assaultRides = true;

        [Tooltip("Sea lift: when the objective lies across water (no land way to it), troops whose ground is over the water get into a ship even when they would otherwise hold (it is the only way there), a front counts the units already crossing as gathered, and a supply unit gets into an empty ship and is taken to the landing — beside the shore nearest our troops across, where its supply is linked — so the troops over there are supplied (a convoy).")]
        public bool seaLift = true;
        [Tooltip("The turn order (AIPlayer.TurnOrder): 1 artillery that can fire (one that then would not waits for 3); 2 full transports; 3 the riders of those, everyone on foot, artillery not firing, and supply units not in the logistics chain; 4 transports with room (partly full or empty), so all who get in this turn are in; 5 the chain's supply units, each with its transport; 6 riders of the transports that moved in 4, who get out if they can strike, reach their ground this turn, or are safer off a threatened transport; 7 aircraft landing this turn; 8 builds. A transport chosen for supply goes with its supply unit. Off: the older order (assaultOrderWithLine).")]
        public bool turnOrderSteps = true;
        [Tooltip("Sea lift: how much a transport ship is worth to buy, times the land units waiting to cross that it would take over (their value, discounted for the turns to get them there). Above 1: the AI puts a ship ahead of a fighting unit of the same value, since without one the units across the water do nothing.")]
        public float seaLiftWeight = 3f;
        [Tooltip("Sea lift: steps added to a trip by sea (AIMap.SeaTravel) for getting into the ship at a naval yard and out on the far shore.")]
        public int seaLiftBoardSteps = 2;
        [Tooltip("With assaultRides on, fighting units also take combatRides' hop (in and out the same turn) in an armed transport. Off: assault rides carry them in armed transports too, aboard until they can strike.")]
        public bool assaultArmedRides = false;
        [Tooltip("Riding to the assault: a rider put down to strike makes the strike its transport planned (where to stand, what to hit or take). Off: it decides afresh where it lands.")]
        public bool assaultCarryOut = true;
        [Tooltip("Riding to the assault: getting in a transport is weighed against the unit's own move (only a walk gives way to a ride that gets it nearer; a strike, a capture or a hold stands). Off: it boards before deciding anything else.")]
        public bool assaultBoardWeighs = true;
        [Tooltip("Riding to the assault, turn order: coordinated plans, artillery firing, then fighting units other than infantry that have something in reach (tanks, gunships), then transports with riders, each followed at once by its riders; then the rest. Off: transports with riders first.")]
        public bool assaultOrderWithLine = true;
        [Tooltip("Riding to the assault: a rider whose transport has reached the front line (in contact next turn) gets off and decides for itself; a rider whose transport fell back to its ordinary move always does.")]
        public bool assaultFrontlineOut = true;
        [Tooltip("Riding to the assault: an armed transport (recon, IFV, battle copter) fires first and lets its riders out after, unless what it takes back would destroy it.")]
        public bool assaultFireThenUnload = true;
        [Tooltip("Riding to the assault: a fighting unit more than this many turns from contact on foot gets in a transport.")]
        public int assaultBoardTurns = 2;
        [Tooltip("Riding to the assault: production buys transports (helicopters and planes too) while fighting units far from the fight wait without room in one — worth what it adds to the riders it would carry (the nearest of them): each one's value times the turns sooner it reaches the fight riding than walking, over productionLifeRef (at most its whole value) — times this (more than 1: it goes back for more, and carries the wounded, over its life). 0: not bought for riders.")]
        public float assaultTransportWeight = 2f;
        [Tooltip("Riding to the assault: infantry bought at a base beside which an empty troop transport waits (or one is bought this turn) is worth this much more, until the transport's room is filled — it gets in next turn and rides.")]
        public float assaultFillBonus = 0.5f;
        [Tooltip("Riding to the assault: an empty troop transport first picks up a fighting unit of ours down to this strength (not already on a building that services it) and carries it to the nearest of our buildings that does, to mend. 0: no medevac.")]
        public int assaultMedevacStrength = 3;
        [Tooltip("Riding to the assault: riders are put down when their front attacks, or where ours (with them) are at least this many times theirs within three tiles.")]
        public float assaultWaveRatio = 1f;

        [Tooltip("An empty transport is credited, like a fighting unit, for drawing fire off friends and blocking the enemy's way (FirstStrikeCredit, SpaceDenial). Off: it is not, and keeps out of reach.")]
        public bool idleTransportScreens = false;

        [Tooltip("Plan with getting off costing nothing: a passenger aboard is not spent while it rides, a transport drives and drops in one action, and a passenger put down fresh moves and fights from where it lands (the ride is planned with that move). Off: drop-off waits for the next turn and the passenger's turn is spent.")]
        public bool freshUnload = true;

        [Tooltip("Forecast only the theatre of the unit deciding: enemies that could strike anywhere it could move or fire this turn (move and range, plus theaterMargin), and our units they could strike. Everything else stands as it is, the same for all of its options. Off: the whole board, for every option.")]
        public bool theaterForecast = true;

        [Tooltip("Extra tiles around the theatre, for knock-on effects (an enemy driven back, a tile freed by a kill, roads that move units further than their move).")]
        public int theaterMargin = 6;

        [Tooltip("With freshUnload: plan rides counting the passenger's own move after it is put down (it may be dropped up to a move short of where it is going). Off: rides are planned to a drop beside the destination, and the passenger's move after landing is a bonus.")]
        public bool freshUnloadPlanning = true;

        [Tooltip("A supply-chain link may stand ahead of the army where no enemy can strike for more than two turns (otherwise links never pass the frontmost fighting unit).")]
        public bool chainLeadsWhenClear = false;

        [Tooltip("Supply leaps: a supply unit the chain cannot use rides a truck past the edge of supply and stays aboard, linked while the truck could drive back onto supplied ground in one move — the network crosses ground it could not link on foot.")]
        public bool supplyLeaps = true;

        [Tooltip("Supply as a backbone (AISupplyBackbone): supply units ride trucks and APCs as moving links, a transport's move apart, from the HQ's network to each group of the army; the rest stay on foot by the army.")]
        public bool supplyBackbone = false;

        [Tooltip("Idle trucks follow the army from behind (else they wait beside the supply units).")]
        public bool idleTrucksFollowArmy = true;

        [Tooltip("Supply front first: for each group of the army, the supply unit nearest it is a front-line one, placed by what it supplies there and what it risks (as if linked, wherever it goes); the rest line up behind as a chain to the nearest front-line one; any the chain does not need become front-line too.")]
        public bool supplyFrontFirst = false;

        [Tooltip("Campaign: buildings valued by what they do for the war — those that build units (productionBuildingWeight), then those that make supply (supplyBuildingWeight), above cities and resupply points. The HQ is worth the board either way.")]
        public bool campaignBuildingTiers = true;
        public float productionBuildingWeight = 3f;
        public float supplyBuildingWeight = 2f;

        [Tooltip("Units fighting (in contact) within this many tiles of our HQ are left to the ordinary tactics and supply: no campaign task force, no front-line supply group. 0: off.")]
        public int homeFightRadius = 0;

        [Tooltip("Supply leaps may go ahead of the army where the enemy cannot strike within two turns (else never ahead of it).")]
        public bool leapsAheadWhenClear = false;

        [Tooltip("Supply units as a tree grown out from the HQ to every fighting unit out of supply, nearest the edge of supply first, a link at a time (AISupplyTree), instead of one chain toward the enemy HQ (supplyChain). Units the tree does not need are placed by the ordinary supply planner. Off: it lost the benchmark against the chain (PROBE=bench, ChanceMeeting from both sides and the campaign maps, fog off and on: -70,000 material over 12 games, better in 5, worse in 7).")]
        public bool supplyTree = false;
        [Tooltip("Strength a supply-tree link may be expected to lose tonight (the share of enemy fire that can reach its tile) and still stand there.")]
        public float supplyTreeRisk = 1f;
        [Tooltip("A supply-tree link must also survive the enemy's reply played out (AIWorld) within supplyTreeRisk, not only the expected share of fire. The reply is pessimistic (it lets every enemy pick its best target), so links form less readily.")]
        public bool supplyTreeReply = false;

        [Tooltip("Strength a supply-chain link may be forecast to lose tonight (the enemy's reply, or the expected share of its fire) and still stand there. 0: only where nothing can hit it — with fast units about (gunships, recon), nothing forward qualifies, the chain stops forming, and the supply units ahead of it are cut off while the ones behind sit by the HQ.")]
        public float chainRiskTolerance = 0f;

        [Tooltip("March: a fighting unit out of contact gains this much of a strength's worth for each step over land it ends nearer its front's objective (linear, so it pulls from any distance; supply costs still keep it inside supply).")]
        public float marchWeight = 0.05f;

        [Tooltip("A battle plan is adopted only if its forecast beats its units acting alone by more than this (gold).")]
        public float coordinationPassMargin = 0f;

        [Tooltip("Model retreats in the group-plan forecast: a defender driven back moves, and the tile it held opens.")]
        public bool coordinationRetreats = true;

        [Tooltip("Self-preservation: the acting unit counts every enemy blow that could reach its tile (biggest first, weights 1, 1/2, 1/4 ...), beyond what the forecast reply already takes from it, at this weight. 0 = off.")]
        [Range(0f, 2f)]
        public float selfPreservationWeight = 0f;
        [Tooltip("Self-preservation prices each blow as the reply does: unanswerable fire from range in full, the rest discounted by replyDiscount (and what the reply took from the unit likewise). Off: every possible blow in full, while the blows the reply landed counted at the discount.")]
        public bool selfPreservationDiscount = true;
        [Tooltip("Self-preservation only for units that shield nothing by standing in reach (AIWorld.Shields: artillery, anti-air, aircraft that cannot answer the ground, supply units). Off: every unit.")]
        public bool selfPreservationShieldless = true;

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

        [Tooltip("A gun's zone of control (artilleryCoverValue) and mutual cover count a tile only for the enemies the gun can fire on (its attacksLocomotion): an anti-air battery no longer holds ground it cannot shoot.")]
        public bool coverByTarget = true;
        [Tooltip("Speed only: the space term counts our units' reach from a tally made once per acting unit and board read, adjusted for each option (the acting unit moved, ours killed in the forecast, those near a unit driven back), instead of re-joining every unit's reach for every option. Same answers.")]
        public bool spaceIncremental = true;
        [Tooltip("Speed only: a unit's options are forecast at once on worker threads (AIParallel), each on its own copy of the board. Same answers.")]
        public bool parallelForecast = true;
        [Tooltip("Speed only: the map keeps, after each action and from turn to turn, the caches that what changed on the board leaves standing (reach, move fields, space, supply networks: AIMapCache), instead of working everything out again. Same answers.")]
        public bool keepCaches = true;
        [Tooltip("Speed only: in the reply forecast, where an enemy could go with our unit off its tile is worked out once per decision, and used for every option whose end tile is not one of those (it would block it). Same answers.")]
        public bool vacatedReach = true;
        [Tooltip("A decision made in the coordination pass (and a supply unit's turn-start plan, reuseGhostPlans) is reused when nothing within this many tiles of the unit changed: the unit's own reach plus one, at most this. 0: the full radius (its reach plus the farthest enemy reach: the same answers). Faster, not the same answers.")]
        [Range(0, 20)]
        public int reuseRadiusCap = 0;
        [Tooltip("Speed only: while the opponent plays (once it has moved speculateAfterShare of its units and the board has been still speculateQuietFrames frames), the AI warms its map's caches on a worker thread at low priority; its turn keeps what still stands (keepCaches). Same answers.")]
        public bool speculate = true;
        [Range(0f, 1f)]
        public float speculateAfterShare = 0.7f;
        [Range(1, 600)]
        public int speculateQuietFrames = 20;

        [Tooltip("The forecast prices each unit by its role, not its price alone (AIWorld.RoleWorth): what it does against the other side's units, weighted by how little else can hurt them, against its side's average, as a multiplier from roleWorthMin to roleWorthMax (kills still count double on top). The enemy's reply then goes for what we can least spare.")]
        public bool forecastRoleWorth = false;
        public float roleWorthMin = 0.5f;
        public float roleWorthMax = 2f;

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

        [Tooltip("With value-added pricing: a move that cuts off another supply unit, connected where this one stands now, costs this fraction of that unit's whole worth (one it reconnects earns as much), and a transport carrying supply units is held rather than cut the network. 0: only a strength, as a relay. With chainReconnects, 1 beat 0 in 19 of 32 big-map games (ChanceMeeting 6 of 8, by large margins).")]
        public float hubLinkPenalty = 1f;

        [Tooltip("With value-added pricing, still credit coverage of contested ground and of where units are heading.")]
        public bool hubPricingForward = true;

        [Tooltip("A supply unit aboard a transport stays aboard: aboard, it links as far as its transport can drive and spreads its supply from wherever the transport stands, so it is never put down (a supply unit inside a transport inside another may be, with its own transport). The one exception: its transport would be destroyed where it stops. Off: plans could end in 'carry and drop off'.")]
        public bool supplyStaysAboard = true;
        [Tooltip("Transports held for supply: those carrying a supply unit or on a supply duty, and for each supply unit on foot only the nearest one that could carry it; the rest carry troops. Off: every one that can carry supply on a no-build map.")]
        public bool supplyTransportCap = true;
        [Tooltip("A rider may take a supply transport's spare seat before its supply unit is aboard, as long as the supply unit still fits beside it.")]
        public bool supplySpareSeat = true;
        [Tooltip("A transport that has not moved this turn does not put down a rider that got in this turn (no extra tile from hopping on and off).")]
        public bool noHopOff = true;
        [Tooltip("A supply unit on foot cut off from the network first boards an empty transport beside it, else walks to the tile nearest supplied ground it can reach, before anything else.")]
        public bool cutOffSeeksSupply = true;
        [Tooltip("A supply unit takes a ride whenever it arrives no later than walking (aboard, it is linked from a move away: two to four times its reach), not only when the ride saves transportWorthwhileSaving turns.")]
        public bool supplyPrefersRide = true;
        [Tooltip("An empty transport held for a supply unit within a move drives up beside it, so next turn it steps aboard and still rides a full move (instead of waiting a move away).")]
        public bool supplyRideBeside = true;
        [Tooltip("The central reserve takes every spare fighting unit not in contact, not only fast ones (campaignMobileReserve): the reserve is a share of the army.")]
        public bool reserveTakesSpare = false;
        [Tooltip("Production picks the most valuable unit outright while every other empty slot can still be filled with its cheapest; value for its cost only when gold is short.")]
        public bool productionAbsolute = false;
        [Tooltip("The central reserve drafts an idle transport (on no task force, not held for supply) with enough idle infantry near it to fill it; they board once drafted.")]
        public bool reserveDraftsRiders = true;
        [Tooltip("When no task force faces active opposition, production buys for the central reserve as for a front short by its shortfall of campaignReserveShare, at the producer nearest where it gathers.")]
        public bool reserveDemand = true;

        [Tooltip("Plan each supply unit's move before the line moves and let the line use that supply (a ghost); the supply unit still moves last.")]
        public bool ghostSupply = true;

        [Header("Transport")]
        [Tooltip("Turns saved that justify the two-turn overhead of loading and unloading.")]
        [Range(0, 6)]
        public int transportWorthwhileSaving = 0;

        [Tooltip("How much more cautious a transport is while carrying cargo. Losing a transport loses its cargo too.")]
        public float loadedTransportRisk = 2.5f;

        [Header("Fog of war")]
        [Tooltip("Under fog of war, play by it: decide on the board as this side knows it (unseen enemies off the board, those seen lately where they were last seen). Off: the AI sees everything.")]
        public bool fogFair = true;
        [Tooltip("Turns an enemy unit is remembered where it was last seen (fog of war, fogFair).")]
        public int fogMemoryTurns = 2;
        [Tooltip("Gold per tile a stop would bring into sight that is not seen now (more the longer it has gone unseen, up to 4 turns). Recon units count it double.")]
        public float scoutTileValue = 15f;
        [Tooltip("An ambush: ending where the enemy (as known) would not see this unit, within reach of an enemy that may run into it. Fraction of the unit's worth per enemy in reach (up to three).")]
        public float ambushValue = 0.3f;
        [Tooltip("Share of tonight's risk taken back for a stop the enemy (as known) would not see this unit on: it can only be fired on by what stumbles into it.")]
        public float hiddenRiskRelief = 0.5f;

        [Tooltip("Price what the fog may hide (AISigint): enemies seen before and lost track of, somewhere within reach of where they were last seen, and ground not seen lately, which may hide anything the enemy fields.")]
        public bool fogThreat = true;
        [Tooltip("Enemy moves an enemy unit lost track of is still counted near an objective by the campaign (AICampaign). The tactical fog risk (AISigint) keeps it until it is seen again or dies.")]
        [Range(1, 12)]
        public int sigintMoves = 6;
        [Tooltip("Confidence kept in a lost enemy unit for each move it has had since it was last seen: the campaign's estimate of enemy power near an objective (AICampaign). The tactical fog risk (AISigint) does not fade it: it spreads it over where the unit could be.")]
        [Range(0f, 1f)]
        public float sigintDecay = 0.7f;
        [Tooltip("Appetite for ground the fog hides (fogThreat). 0: every unknown tile may hide an enemy unit of a kind it fields (the average of those kinds' damage, by the share of unknown ground near: a threat from anywhere, with no position); 1: unknown ground is empty. Default 1 since lost units are tracked by where they could be (AISigint): the average over every kind feared expansion everywhere while the enemy stood in a known place. (0.2 had beaten fogThreat off on the big maps, 18 of 24, before that.)")]
        [Range(0f, 1f)]
        public float fogAggression = 1f;
        [Tooltip("Unknown ground (never seen, or not for fogUnknownTurns) is feared by the share of the enemy's forces we cannot place this turn (1 - known / estimate, AIKnowledge), not by 1 - fogAggression: wary of every dark tile while the enemy's whereabouts are unknown, less as it is found. The loss is priced at the unit's worth (its role, with forecastRoleWorth), so a dear unit fears it most.")]
        public bool fogUncertainty = true;
        [Tooltip("Turns a tile has to go unseen to count as unknown again (fogAggression). Tiles never seen always do.")]
        [Range(1, 20)]
        public int fogUnknownTurns = 6;
        [Tooltip("Fog: ground not seen this turn within this many tiles of an enemy unit we see or remember counts as unknown however lately it was seen (0: off).")]
        public int fogStaleRadius = 5;
        [Tooltip("Fog: the enemy we cannot place is spread over unknown ground near where the enemy is known to be (its units seen or remembered, its HQ, its production sites), falling off with distance (fogAnchorFalloff); a stop fears the expected number of those units within their reach of it. Off: evenly over unknown ground, by the share we cannot place.")]
        public bool fogConcentrate = true;
        [Tooltip("Fog (fogConcentrate): tiles over which an anchor's share of the unplaced enemy falls off by a factor e.")]
        public float fogAnchorFalloff = 2f;
        [Tooltip("Fog (fogConcentrate): an anchor's weight in sharing out the unplaced enemy: each enemy unit seen or remembered 1, the enemy HQ this, its production sites fogProductionWeight (purchases on).")]
        public float fogHQWeight = 4f;
        public float fogProductionWeight = 2f;
        [Tooltip("Fog (fogConcentrate): a stop fears at most one unplaced unit's fire, times the chance one is in reach (1 - e^-expected) and the share of the enemy we cannot place. Off: every expected unplaced unit fires in full (the army fled the contact line).")]
        public bool fogConcentrateCapped = true;
        [Tooltip("Fog: units cheaper than this do not fear unknown ground (they screen, scout and hold; the enemy seen or remembered still counts). 0: off.")]
        public int fogScreenBelowCost = 0;
        [Tooltip("Never attack for no damage: an attack that deals 0 becomes a move to (or a hold on) its tile.")]
        public bool noZeroDamageAttacks = true;
        [Tooltip("Move choice: among the options within the world-score tie, one that attacks for damage wins over one that does not.")]
        public bool tieFavorsShot = true;
        [Tooltip("Campaign: on the defensive (aggression under 0.25), the narrows (passages three tiles wide or less) near an enemy we see or remember are Hold objectives, ours or not, in sight or not.")]
        public bool defensiveNarrows = false;
        [Tooltip("Campaign: a unit is in the fight (released from its task force's lean, counted engaged) by its own reach to an enemy, not by an enemy's reach to it.")]
        public bool campaignReleaseOwnReach = true;
        [Tooltip("Fog: the weight of what the fog may hide in a move's score (AIFog.FogRisk), against the forecast of the enemy we see or remember (1: as much).")]
        public float fogRiskWeight = 1f;
        [Tooltip("Production: a fighting unit that can hurt nothing the enemy has or could field (its units at the start, what its buildings make) is not bought (anti-air on a map without air).")]
        public bool productionPossibleFoes = true;
        [Tooltip("Production: a unit is not built on a tile where the enemy we see or remember would destroy it next turn.")]
        public bool productionAvoidFire = true;
        [Tooltip("Procurement: one plan over every facility (the most value in all for the gold), fronts' pressure on what they want, a bonus per facility that can build a kind, saving only for supply, a gap or transit within a wait the map sets. Off: one unit at a time by value per gold.")]
        public bool procurement = true;
        [Tooltip("Procurement: added to a kind's value for each facility (not under fire) that can build it, pushed back by the resistance its front expects.")]
        public float procFacilityBonus = 1000f;
        [Tooltip("Procurement: the most a unit is saved for (the longest wait is the turns to save this much at 70% of the map's income).")]
        public float procSaveMost = 15000f;
        [Tooltip("Procurement: a front with no resistance multiplies infantry and troop transports by this.")]
        public float procNoResistance = 1.5f;
        [Tooltip("Procurement: a front short of its units with no resistance: the cheapest fighting unit gains up to this share (cheapest cost over its cost).")]
        public float procCheapBonus = 1f;
        [Tooltip("Procurement: across water from its facility, a front multiplies aircraft, ships and ranged units by this.")]
        public float procAcrossWater = 1.5f;
        [Tooltip("Procurement: a front with water mixed in (over a fifth of the ground near it) multiplies aircraft and amphibious units by this.")]
        public float procMixedWater = 1.3f;
        [Tooltip("Procurement: a stealthy unit's value is multiplied by this.")]
        public float procStealth = 1.2f;
        [Tooltip("Procurement: past this many lower-tier units to one of the next tier up, the next tier up gains half again.")]
        public float procTierRatio = 5f;
        [Tooltip("Procurement: a production building within two turns of a facility, capturable, adds this to what capturing units built there are worth (plus its income over objectiveHorizonTurns).")]
        public float procCaptureProducerValue = 5000f;
        [Tooltip("Procurement: below this much gold, an empty reserve asks for troop transports (to fill with infantry).")]
        public int procReserveRich = 15000;
        [Tooltip("Procurement: a fixed jiggle, up to this, added to a unit's value (by its kind, its facility and the day).")]
        public int procJiggle = 2000;
        [Tooltip("Procurement: every transport that does not fight (truck, APC, helicopter, plane, ship) is worth what it carries: our units it can carry that are far from where they are going (or across water), each one's value times the turns sooner it gets them there than they get there themselves, on each one's own path over the terrain; and a supply unit waiting for a ride. Off: ground trucks for riders and supply, ships for the sea lift, aircraft only for riders.")]
        public bool procCargo = true;
        [Tooltip("Procurement: an air tanker (or supply ship) is worth our aircraft (or ships) running low on fuel or supplies (below procResupplyLow of their most): each one's value times how low it is, shared with the tankers we have.")]
        public bool procResupply = true;
        [Tooltip("Procurement (procResupply): fuel or supplies below this share of the most count as running low.")]
        public float procResupplyLow = 0.5f;
        [Tooltip("Production: the turns to the fight are counted over the terrain by each unit's own movement (scramble, roads, what it can enter; aircraft ignore terrain), not in tiles.")]
        public bool procTerrainReach = true;
        [Tooltip("Production (productionLifetime): what a unit takes in a turn includes the return fire from each enemy it would attack from beside it (none for one that fires from range, or at one that cannot fire back at it).")]
        public bool productionRetaliation = true;
        [Tooltip("Procurement: each facility has its own budget — the income split evenly, a facility serving a high-priority front or offering what is wanted most getting up to 1 + procPriorityShare times as much — kept from turn to turn. Each buys now, or saves for something better within the longest wait, its value cut by procWaitBase^-(procWaitPower x turns waited). Facilities saving for the same unit pool their savings and the highest-priority one builds it. A facility the enemy holds or can destroy a new unit on gives its savings to the others. Off: one plan over all the gold.")]
        public bool procBudgets = true;
        [Tooltip("Procurement (procBudgets): the most extra share of income a facility gets for its priority (1: up to twice as much).")]
        public float procPriorityShare = 1f;
        [Tooltip("Procurement (procBudgets): a unit saved for is worth its value times procWaitBase^-(procWaitPower x turns to wait ^ procWaitExponent).")]
        public float procWaitBase = 1.1f;
        [Tooltip("Procurement (procBudgets): see procWaitBase.")]
        public float procWaitPower = 4f;
        [Tooltip("Procurement (procBudgets): the turns waited are raised to this power in the discount (1: procWaitBase^-(procWaitPower x turns); 2: the first turn costs little, each more far more).")]
        public float procWaitExponent = 1f;
        [Tooltip("Procurement (procBudgets): what a facility was saving for last turn is worth this much more (the choice holds).")]
        public float procStick = 1.25f;
        [Tooltip("Procurement (procBudgets): a facility's longest wait is the turns to save procSaveMost on its share of this much of the map's income plus the rest of ours (0.5: half the map's, half ours); never less than the turns to save for a supply unit on its share of our income.")]
        public float procWaitMapShare = 0.5f;
        [Tooltip("Procurement (procBudgets): a facility whose best option is worth more than the mean of every facility's best (what something failed for want of: supply for a front short of it, transports for units waiting to cross) gains this times (its best over the mean, less 1) in weight, beyond procPriorityShare. 0: off.")]
        public float procDemandWeight = 1f;
        [Tooltip("Production: an amphibious unit (a marine) is not built at a facility with no water within this many tiles (as anti-air is not built against an enemy with no aircraft). 0: off.")]
        public int productionAmphibiousWater = 12;
        [Tooltip("Every move the AI makes (a walking supply unit, a transport, anyone): it does not end on one of our facilities that can build, while another tile on its way will do. Aircraft and ships the facility services are exempt (only there do they land, rearm or refuel).")]
        public bool offFacilities = true;
        [Tooltip("Units sent scouting (fogThreat): those that see the most for the least, each toward where it would see most of what the fog may hide (AIObjectives.AssignScouts). 0: none.")]
        [Range(0, 4)]
        public int scoutUnits = 0;
        [Tooltip("A scout's pull toward where it will look: a fraction of its worth for every step closer.")]
        public float scoutPull = 0.3f;
        [Tooltip("Extra worth of a scouting tile where an enemy we lost track of may be standing, or that is unknown (times its confidence): looking there tells us where it is.")]
        public float scoutThreatBonus = 1f;

        [Header("Debug")]
        [Tooltip("Harness only: a seed for small deterministic noise in close choices (decisionNoise), so one map can be played many different ways. 0: none.")]
        public int noiseSeed = 0;
        [Tooltip("Harness only: the size of that noise, as a fraction of the acting unit's worth per strength point.")]
        public float decisionNoise = 0.15f;
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
