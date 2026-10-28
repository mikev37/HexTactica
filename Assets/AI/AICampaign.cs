using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace HexTactica.AI {

    /// <summary>What a task force is for.</summary>
    public enum CampaignKind {
        DefendHQ,   // kill or hold off what can reach our HQ
        Secure,     // take and hold a building (the enemy HQ, where the rules let it be taken, is one)
        Clear,      // defeat an enemy group in an area
        Scout,      // look at ground the fog hides
        Hold        // bring a place into our supply and hold it: an end of a chokepoint (a bridge, a strip of land one tile wide), a road junction
    }

    /// <summary>One objective of the campaign and the task force given to it.</summary>
    public class CampaignObjective {
        public CampaignKind kind;
        /// <summary>Where it is: the building, the enemy group's nearest unit, the middle of the ground to look at.</summary>
        public HexTile target;
        public Building building;
        public readonly List<Unit> enemies = new List<Unit>();
        /// <summary>What achieving it is worth, in gold.</summary>
        public float value;
        /// <summary>Enemy combat power that could fight there within frontReachTurns (known units, and those the fog may hide).</summary>
        public float enemyPower;
        /// <summary>Its own enemies (a group's, a threat's, the fog's) and the others that could arrive (each unit's power).</summary>
        public float owned;
        public readonly Dictionary<Unit, float> arriving = new Dictionary<Unit, float>();
        /// <summary>Unknown ground within the enemy's reach of it: the part of their army we have not seen could be coming. Its share of that army.</summary>
        public bool hiddenReach;
        public float hiddenPower;
        /// <summary>The share of the ground around it we cannot see (0..1): the unseen enemy is priced at that share.</summary>
        public float hiddenShare;
        /// <summary>Combat power the task force needs; supply units it needs (to reach the place from supplied ground, and to spread at the contact line).</summary>
        public float needPower;
        public int needHubs;
        /// <summary>Lasting presence (Secure, Clear): its army needs supply to stay. Scouting and defence at home do not.</summary>
        public bool lasting;
        /// <summary>Contact: an enemy group already on our ground (in our supply) — met first, whatever else is planned.</summary>
        public bool respond;
        /// <summary>Its task force fell back this turn, outmatched.</summary>
        public bool fellBack;
        /// <summary>The enemy power its task force met when it fell back (Outmatched).</summary>
        public float metPower;
        /// <summary>Need at least this: the enemy it met there before (AICampaign.opposition).</summary>
        public float minNeed;
        /// <summary>The way there that avoids the enemy's fire (down the middle or round the side), and the tile on it to gather on before going in.</summary>
        public HexTile staging;
        public int routeLength;
        /// <summary>Beyond our supply with no supply of its own this turn (AILogistics): it stays within our supply.</summary>
        public bool noSupply;
        /// <summary>Not the campaign's: an ordinary front, made a supply front by the logistics (AILogistics).</summary>
        public bool ordinaryFront;
        /// <summary>Hold: what kind of place (chokepoint end, road junction).</summary>
        public string placeKind;
        /// <summary>Turns this task force has been out (0 the turn it formed), whether it has gone in yet, and the turns from its staging tile to its target when it formed (time boxes: AIProfile.campaignTimeBox).</summary>
        public int age;
        public bool went;
        /// <summary>Last turn's pace (AIStrategic.Pace) for this task force: no nearer since, it has stalled.</summary>
        public int lastPace = -1;
        /// <summary>Turns in a row its main body has got no nearer its objective (AIStrategic.Finish).</summary>
        public int stalled;
        /// <summary>Turns for the slowest of the units it needs to reach it (on a transport where one can carry it): the front's distance.</summary>
        public int turnsAway = 99;
        /// <summary>Taken on all in, with everything that could join, because nothing was within reach (its need is what it was given).</summary>
        public bool allIn;
        public int routeTurns;
        /// <summary>Order of preference: value over what it costs, discounted by the turns to get there.</summary>
        public float priority;
        public readonly List<Unit> units = new List<Unit>();
        public readonly List<SupplyPoint> hubs = new List<SupplyPoint>();
        public float assignedPower;
        /// <summary>The task force's power when it was formed (for telling when it has been broken).</summary>
        public float formedPower;

        /// <summary>The id of last turn's objective this one continues (an enemy group that has moved), if any.</summary>
        public string inherited;

        /// <summary>A second (third...) task force for the same objective, from a separate group of our units: 1, 2...; 0 the first.</summary>
        public int wing;

        /// <summary>The objective's own id, whichever of its task forces this is.</summary>
        public string BaseId {
            get { return inherited ?? kind + "@" + (target != null ? target.x + "," + target.y : "-"); }
        }

        public string Id {
            get { return BaseId + (wing > 0 ? "#" + (wing + 1) : ""); }
        }

        /// <summary>Another task force for the same objective (its wing number <paramref name="w"/>): the objective, without its units, supply or staging.</summary>
        public CampaignObjective Wing(int w) {
            CampaignObjective o = new CampaignObjective { kind = kind, target = target, building = building, lasting = lasting, respond = respond, placeKind = placeKind, inherited = inherited, wing = w };
            o.enemies.AddRange(enemies);
            return o;
        }

        public override string ToString() {
            StringBuilder sb = new StringBuilder();
            sb.Append(kind).Append(target != null ? "(" + target.x + "," + target.y + ")" : "");
            if (wing > 0) sb.Append(" wing ").Append(wing + 1);
            if (placeKind != null) sb.Append(' ').Append(placeKind);
            if (turnsAway < 99) sb.Append(" away ").Append(turnsAway);
            if (building != null) sb.Append(' ').Append(building.name.Replace("(Clone)", ""));
            sb.Append(" value ").Append(value.ToString("0")).Append(" priority ").Append(priority.ToString("0.00"))
              .Append(" enemy ").Append(enemyPower.ToString("0")).Append(hiddenPower > 0f ? " (unseen " + hiddenPower.ToString("0") + ", " + (hiddenShare * 100f).ToString("0") + "% dark)" : "").Append(" need ").Append(needPower.ToString("0"))
              .Append(" have ").Append(assignedPower.ToString("0")).Append(", ").Append(units.Count).Append(" units, ")
              .Append(hubs.Count).Append('/').Append(needHubs).Append(" supply");
            if (staging != null) sb.Append(" via (").Append(staging.x).Append(',').Append(staging.y).Append(")");
            sb.Append(" turn ").Append(age + 1).Append(went ? " gone in" : "");
            if (units.Count > 0) {
                sb.Append(":");
                foreach (Unit u in units) if (u != null && u.tile != null) sb.Append(' ').Append(u.name.Replace("(Clone)", "").Replace(" Variant", "")).Append('(').Append(u.tile.x).Append(',').Append(u.tile.y).Append(")s").Append(u.Strength);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// The campaign (AIProfile.campaign): objectives read from the map, each given a task force fit to
    /// achieve it, with supply to sustain it, and fronts made from them for the tactical layer to fight.
    ///
    /// Without it, fronts were wherever the fighting happened to be hottest near home, and every unit
    /// joined the one with the most priority over distance: the front at home took the whole army, the
    /// army sat on its peninsula, and the enemy had the rest of the map.
    ///
    ///   - Objectives: our HQ when something can reach it (DefendHQ); every building not ours (Secure:
    ///     take and hold it — the enemy HQ too, unless the rules keep HQs out of play); every enemy
    ///     group (Clear); under fog, the ground where an enemy we lost track of, or anything, may be
    ///     (Scout). Each is worth what it is: income over objectiveHorizonTurns (and its supply, if it
    ///     gives any), the whole board for an HQ, the material of a group, what looking there is worth.
    ///   - The way there avoids the enemy's fire: a path over land on which every tile the enemy can
    ///     strike next turn costs routeDanger steps more, so a middle held in strength sends the force
    ///     round a side; the force gathers on it at the last tile the enemy cannot strike next turn
    ///     (staging), then goes in.
    ///   - What it needs: combat power to match what can fight there, times objectiveForceRatio (and
    ///     at least enough to do the job: someone who can capture, for a building); for a lasting
    ///     presence, supply units enough to carry supply from our supplied ground to it (the gap over
    ///     a supply unit's range) and to spread at the contact line (the force's frontage over that
    ///     range). Scouting needs units that see far for little (vision times move over worth).
    ///   - Achievable only: an objective is pursued if what is left could meet its need.
    ///   - How evenly (objectiveSpread): 0 is all in — enough at home not to be overrun, and
    ///     everything else on the single best objective; 1 meets every objective it can, and spreads
    ///     what is left over all of them. In between, in proportion.
    ///   - Units are chosen by fit: nearness (turns to the staging tile), and what they can do against
    ///     the enemy kinds there (damage for their worth); a capturer first for a building.
    ///   - It is kept from turn to turn — the same units on the same objective — and planned again when
    ///     an objective is achieved or gone, when a task force falls below objectiveReplanLoss of its
    ///     power, or when our HQ comes under threat.
    ///
    /// The fronts it makes are the ordinary kind (AIStrategic): posture, staging and assault work as
    /// they always have, around these objectives. Supply units given to an objective form its own
    /// chain (AISupplyChain, toward its staging tile and not ahead of its own army).
    /// </summary>
    public class AICampaign {

        public readonly List<CampaignObjective> objectives = new List<CampaignObjective>();
        /// <summary>Every objective considered this turn, as appraised (those pursued are in <see cref="objectives"/>).</summary>
        public List<CampaignObjective> considered = new List<CampaignObjective>();
        /// <summary>Diagnostics: every candidate objective as appraised.</summary>
        public static System.Action<string> Debug;
        readonly Dictionary<Unit, CampaignObjective> unitOf = new Dictionary<Unit, CampaignObjective>();
        readonly Dictionary<SupplyPoint, CampaignObjective> hubOf = new Dictionary<SupplyPoint, CampaignObjective>();
        /// <summary>Why the last plan was made from scratch, if it was.</summary>
        public string replanned;
        /// <summary>
        /// What each objective's task force met when it fell back, remembered (fading a little each
        /// turn): the objective needs at least that times objectiveForceRatio before it is tried again.
        /// Without it the campaign went straight back in at the same odds the next turn, fell back
        /// again under fire, and spent its army in the coming and going.
        /// </summary>
        public readonly Dictionary<string, float> opposition = new Dictionary<string, float>();
        /// <summary>Objectives given up (time boxes), each until the turn it may be taken on again; and this campaign's turn.</summary>
        public readonly Dictionary<string, int> givenUp = new Dictionary<string, int>();
        /// <summary>
        /// What one more supply unit would be worth to the campaign: of the objectives passed over this turn for
        /// want of supply units, the best worth per supply unit missing (AIProduction buys supply on it). Fronts
        /// must bring supply; with all of it committed, the next front is a supply unit away.
        /// </summary>
        public float supplyDemand;
        /// <summary>The force (combat power) each missing supply unit would let go to supplyDemandFor.</summary>
        public float supplyDemandNeed;
        public string supplyDemandFor;
        public int turn;

        public CampaignObjective ObjectiveOf(Unit u) { CampaignObjective o; return u != null && unitOf.TryGetValue(u, out o) ? o : null; }
        public CampaignObjective ObjectiveOf(SupplyPoint h) { CampaignObjective o; return h != null && hubOf.TryGetValue(h, out o) ? o : null; }

        /// <summary>Plan this turn's campaign on <paramref name="map"/>, keeping what <paramref name="last"/> (last turn's) still holds.</summary>
        public static AICampaign Plan(AIMap map, AIProfile p, AICampaign last) {
            AICampaign c = new AICampaign();
            c.turn = last != null ? last.turn + 1 : 0;
            // Seats on our free ground transports, for the travel times below (Turns).
            seats = new Dictionary<Unit, int>();
            planMap = map;
            foreach (Unit t in map.ownUnits) if (IsSeatCarrier(t)) seats[t] = Mathf.Max(1, t.transportSlots);
            List<CampaignObjective> candidates = Candidates(map, p);

            // Time boxes (AIProfile.campaignTimeBox): a task force that has not gathered on its staging
            // ground by its campaignDrawTurns-th turn is dissolved; one that has not achieved its
            // objective in campaignTimeBox turns (more for one further off) gives it up. Either way the
            // objective is left alone for campaignTimeBox turns and its units go to other objectives
            // and to the fighting.
            string timedOut = null;
            HashSet<string> timedOutIds = new HashSet<string>();
            if (last != null) {
                foreach (KeyValuePair<string, int> kv in last.givenUp) if (kv.Value > c.turn) c.givenUp[kv.Key] = kv.Value;
                int draw = DrawTurns(p);
                foreach (CampaignObjective o in last.objectives) {
                    if (o.kind == CampaignKind.DefendHQ || o.respond || p.campaignTimeBox <= 0) continue;
                    // Being taken (one of ours on it, or our capture under way): never given up now.
                    // (The HQ push was given up on the turn its grunt was finishing the capture.)
                    if (o.building != null && o.building.tile != null && ((o.building.tile.occupier != null && o.building.tile.occupier.owner == map.player) || (o.building.capturingPlayer == map.player && o.building.capturePoints > 0))) continue;
                    int age = o.age + 1;
                    string why = null;
                    if (!o.went && o.staging != null && age >= draw && !GatheredEnough(o, map, p)) why = "did not gather in " + draw + " turns";
                    else if (o.went && p.campaignStallTurns > 0 && o.stalled >= p.campaignStallTurns) why = "stalled " + o.stalled + " turns";
                    else if (p.campaignTransitTimeBox && age >= p.campaignTimeBox + Mathf.Max(0, o.routeTurns - draw)) why = "not achieved in " + age + " turns";
                    if (why == null) continue;
                    // Left alone for campaignGiveUpTurns (0: not at all — the task force is dissolved and the
                    // objective formed again from whatever units are best placed for it now).
                    if (p.campaignGiveUpTurns > 0) c.givenUp[o.Id] = c.turn + p.campaignGiveUpTurns;
                    else timedOutIds.Add(o.Id);
                    if (timedOut == null) timedOut = o.Id + " " + why + ", given up";
                }
                candidates.RemoveAll(o => o.kind != CampaignKind.DefendHQ && !o.respond && c.givenUp.ContainsKey(o.Id));
                // Its other task forces (wings) are its own as long as the objective is there.
                foreach (CampaignObjective was in last.objectives) {
                    if (was.wing <= 0 || c.givenUp.ContainsKey(was.Id)) continue;
                    CampaignObjective first = candidates.Find(x => x.wing == 0 && x.BaseId == was.BaseId);
                    if (first != null && !candidates.Exists(x => x.Id == was.Id)) candidates.Add(first.Wing(was.wing));
                }
            }

            // Staging: near where our free units already are (AICampaign.Staging); a task force out
            // keeps the one it gathers on while that is still ours and safe.
            List<Unit> free = new List<Unit>();
            foreach (Unit u in map.ownUnits)
                if (u != null && u.tile != null && Power(u) > 0f && !(Transporting.IsTransport(u) && u.Firepower <= 1) && map.TurnsToContact(u) > 1 && !HomeFight(u, map, p)) free.Add(u);
            foreach (CampaignObjective o in candidates) {
                CampaignObjective was = last != null ? last.objectives.Find(x => x.Id == o.Id) : null;
                HexTile keepStaging = was != null && was.staging != null && map.SupplyDepth(was.staging) > 0 && map.EnemyStrikeTurns(was.staging) > 1 ? was.staging : null;
                Reach(o, map, p, keepStaging, free);
            }
            Dictionary<Unit, int> reaches = new Dictionary<Unit, int>();
            foreach (CampaignObjective o in candidates) {
                if (o.kind == CampaignKind.DefendHQ || o.respond) continue;
                foreach (Unit e in o.arriving.Keys) { int k; reaches.TryGetValue(e, out k); reaches[e] = k + 1; }
            }
            // The enemy splits at most as we do: an enemy unit is shared among no more objectives than
            // we had task forces out last turn (one, the first turn). Shared among every candidate, the
            // enemy's whole army in front of its HQ was priced at half its strength because there were
            // also three cities it could have gone to, and the one push we made went in at even odds.
            int split = 1;
            if (last != null) { split = 0; foreach (CampaignObjective o in last.objectives) if ((o.kind == CampaignKind.Secure || o.kind == CampaignKind.Clear) && o.units.Count > 0) split++; split = Mathf.Max(1, split); }
            foreach (Unit e in new List<Unit>(reaches.Keys)) reaches[e] = Mathf.Min(reaches[e], split);
            // The enemy we have not seen is still out there (fog of war): what they field that we cannot
            // account for — as much as we field (all we know), less what we see or remember — could be at
            // any objective with unknown ground in reach, shared out as a seen unit's power is. Without
            // it every city looked undefended under the fog, the army went off in pairs to nine of them,
            // and the enemy's whole army met each pair alone.
            float hidden = Hidden(map, p);
            if (hidden > 0f) {
                int k = 0;
                foreach (CampaignObjective o in candidates) if (o.hiddenReach && o.kind != CampaignKind.DefendHQ && !o.respond && o.kind != CampaignKind.Scout) k++;
                // Shared over every objective it could be at (not capped at our task forces: with one task
                // force out, every city was priced against the whole unseen enemy — as large as our own army
                // and growing with every unit we bought — and one city's task force took the whole army).
                // Their HQ, and ours, it defends in full.
                foreach (CampaignObjective o in candidates)
                    if (o.hiddenReach && o.kind != CampaignKind.Scout) {
                        bool full = o.kind == CampaignKind.DefendHQ || o.respond || k <= 1 || (o.building != null && o.building.IsHQ);
                        o.hiddenPower = hidden * o.hiddenShare * (full ? 1f : 1f / k);
                    }
            }
            if (last != null) {
                foreach (KeyValuePair<string, float> kv in last.opposition) if (kv.Value * 0.85f > 1f) c.opposition[kv.Key] = kv.Value * 0.85f;
                foreach (CampaignObjective o in last.objectives) if (o.fellBack) { float had; c.opposition.TryGetValue(o.Id, out had); c.opposition[o.Id] = Mathf.Max(had, o.metPower); }
            }
            foreach (CampaignObjective o in candidates) { float met; if (c.opposition.TryGetValue(o.Id, out met)) o.minNeed = met * p.objectiveForceRatio; }
            foreach (CampaignObjective o in candidates) Price(o, map, p, o.kind == CampaignKind.DefendHQ || o.respond ? null : reaches);
            // Breadth first (AIProfile.campaignMaxTurns): each objective's distance is the turns the
            // slowest of the units it would need takes to get there (riding where a transport can take
            // it), and its priority is its worth for what it takes per turn to achieve it; none further
            // than campaignMaxTurns while any objective is nearer. (Fronts were sent across the map for
            // the enemy HQ — the ordinary tactics' job — while cities stood a few turns off.)
            List<Unit> forces = new List<Unit>();
            foreach (Unit u in map.ownUnits)
                if (u != null && u.tile != null && u.Hub == null && u.Firepower > 0 && !(Transporting.IsTransport(u) && u.Firepower <= 1) && !HomeFight(u, map, p)) forces.Add(u);
            foreach (CampaignObjective o in candidates) {
                if (o.kind == CampaignKind.DefendHQ || o.respond) continue;
                o.turnsAway = TurnsAway(o, forces, map);
                o.priority = o.priority / Mathf.Max(1f, o.turnsAway + 1f);   // +1: taking it
            }
            List<CampaignObjective> beyond = new List<CampaignObjective>(candidates);
            // The band widens step by step: campaignMaxTurns, or the nearest objective's distance if that is
            // further — never everything at once. An objective kept from last turn that is now beyond the
            // band is dropped too.
            if (p.campaignMaxTurns > 0) {
                int nearest = int.MaxValue;
                foreach (CampaignObjective o in candidates) if (o.kind != CampaignKind.DefendHQ && !o.respond && o.kind != CampaignKind.Scout) nearest = Mathf.Min(nearest, o.turnsAway);
                int band = Mathf.Max(p.campaignMaxTurns, nearest == int.MaxValue ? p.campaignMaxTurns : nearest);
                if (Debug != null) foreach (CampaignObjective o in candidates) if (o.kind != CampaignKind.DefendHQ && !o.respond && o.kind != CampaignKind.Scout && o.turnsAway > band && (o.building != null && (o.building.IsHQ || o.building.producesSupply))) Debug("beyond the band (" + band + " turns): " + o);
                candidates.RemoveAll(o => o.kind != CampaignKind.DefendHQ && !o.respond && o.kind != CampaignKind.Scout && o.turnsAway > band);
            }
            candidates.Sort((a, b) => {
                if ((a.kind == CampaignKind.DefendHQ) != (b.kind == CampaignKind.DefendHQ)) return a.kind == CampaignKind.DefendHQ ? -1 : 1;
                if (a.respond != b.respond) return a.respond ? -1 : 1;
                int k = b.priority.CompareTo(a.priority);
                return k != 0 ? k : CombatMath.CompareTiles(a.target, b.target);
            });
            // An enemy group moves: it is the same objective while it has any of the same units.
            if (last != null)
                foreach (CampaignObjective o in candidates) {
                    if (o.kind != CampaignKind.Clear) continue;
                    CampaignObjective was = last.objectives.Find(x => x.kind == CampaignKind.Clear && x.enemies.Exists(e => o.enemies.Contains(e)));
                    if (was != null) o.inherited = was.Id;
                }
            c.considered = new List<CampaignObjective>(candidates);
            foreach (CampaignObjective o in beyond) if (!candidates.Contains(o)) c.considered.Add(o);   // too far for now: considered, not pursued
            c.replanned = last == null ? "first plan" : timedOut ?? last.ReplanReason(candidates, map, p);
            if (Debug != null) foreach (CampaignObjective o in candidates) Debug("candidate " + o);
            // What happened to one objective is that objective's business: it is dropped (achieved, gone,
            // given up) or formed again (broken, fell back), and every other task force carries on. Only the
            // first plan and a new threat to our HQ start everything afresh. (Planned afresh whenever any
            // objective was achieved — nearly every turn once they were many and small — no task force
            // across the map lasted two turns: the FOB beyond the lake was set out for six times.)
            bool afresh = last == null || (c.replanned != null && c.replanned.StartsWith("our HQ"));
            HashSet<string> restart = new HashSet<string>(timedOutIds);
            if (last != null && !afresh)
                foreach (CampaignObjective o in last.objectives) {
                    float power = 0f;
                    foreach (Unit u in o.units) if (u != null && u.tile != null && u.Strength > 0) power += Power(u);
                    if (o.fellBack || (o.formedPower > 0f && power < o.formedPower * p.objectiveReplanLoss)) restart.Add(o.Id);
                }
            c.Allocate(candidates, map, p, afresh ? null : last, restart);
            return c;
        }

        // ------------------------------------------------------------------ objectives

        static List<CampaignObjective> Candidates(AIMap map, AIProfile p) {
            List<CampaignObjective> list = new List<CampaignObjective>();
            Building home = GameController.HQOf(map.player);
            bool attrition = GameController.gc != null && GameController.gc.rules == RuleSet.Attrition;

            // Our HQ, when anything can reach it (campaignDefendHQ; off: the move score defends it).
            if (p.campaignDefendHQ && home != null && home.tile != null) {
                CampaignObjective d = new CampaignObjective { kind = CampaignKind.DefendHQ, target = home.tile, building = home };
                // Only what can strike or reach it on its next turn: a threat two turns off is met
                // by the campaign's fighting, not by units held back at home.
                foreach (Unit e in map.enemyUnits) if (e != null && e.tile != null && Power(e) > 0f && CanArrive(e, home.tile, 1, map)) d.enemies.Add(e);
                if (d.enemies.Count > 0) list.Add(d);
            }

            // Buildings not ours (as this side last saw them).
            foreach (Building b in Building.all) {
                if (b == null || b.tile == null) continue;
                Player owner = FogOfWar.OwnerAsSeen(map.player, b);
                if (owner == map.player) continue;
                if (b.IsHQ && attrition) continue;   // out of play
                list.Add(new CampaignObjective { kind = CampaignKind.Secure, target = b.tile, building = b, lasting = true });
            }

            // Enemy groups (one that can reach our HQ too: holding it off at home is not beating it).
            foreach (List<Unit> group in AIObjectives.Clusters(map)) {
                if (group.Count == 0) continue;
                CampaignObjective o = new CampaignObjective { kind = CampaignKind.Clear, lasting = true };
                o.enemies.AddRange(group);
                o.respond = group.Exists(e => map.SupplyDepth(e.tile) > 0);
                o.target = Nearest(group, home != null ? home.tile : null);
                list.Add(o);
            }

            // Places (AIProfile.campaignPlaces): both ends of each chokepoint — a bridge or a strip of land
            // one tile wide — and the junctions where three or more roads meet, where supply placed covers
            // the most road; each until it is in our supply. (With only buildings for objectives, the far
            // side of a lake was only cities a long way off; nothing said "take the crossing".)
            if (p.campaignPlaces > 0) {
                List<CampaignObjective> places = new List<CampaignObjective>();
                foreach (KeyValuePair<HexTile, string> kv in Places(map)) {
                    if (map.SupplyDepth(kv.Key) > 0) continue;   // ours already
                    places.Add(new CampaignObjective { kind = CampaignKind.Hold, target = kv.Key, lasting = true, placeKind = kv.Value });
                }
                // The nearest to our units only (each costs a route to plan).
                places.Sort((a, b) => { int c = NearestOwn(a.target, map).CompareTo(NearestOwn(b.target, map)); return c != 0 ? c : CombatMath.CompareTiles(a.target, b.target); });
                if (places.Count > p.campaignPlaces) places.RemoveRange(p.campaignPlaces, places.Count - p.campaignPlaces);
                list.AddRange(places);
            }

            // Ground to look at (fog of war).
            if (map.sigint != null) {
                int scoutMove = 0; foreach (Unit u in map.ownUnits) if (u != null && u.visionRange > 0 && u.Firepower > 0 && u.Hub == null) scoutMove = Mathf.Max(scoutMove, u.moveRange);
                int scoutReach = Mathf.Max(1, scoutMove) * Mathf.Max(1, p.campaignDipTurns);
                HashSet<HexTile> near = new HashSet<HexTile>();
                List<KeyValuePair<HexTile, float>> spots = new List<KeyValuePair<HexTile, float>>();
                foreach (HexTile t in HexGenerator.hg.tiles) {
                    if (t == null || t.terrain == null || t.terrain.elevation < 0) continue;
                    float v = 0f;
                    foreach (HexTile n in AIPath.TilesInRangeShared(t, 0, p.scoutRadius)) v += map.sigint.Presence(n);
                    // Only near our supply (campaignDipTurns, in moves of our scouts): a look is not worth a
                    // scout — two recons sent across the map starved two turns out.
                    if (v > 0f && AIMap.GapToSupply(t, x => map.SupplyDepth(x), 99) <= scoutReach) spots.Add(new KeyValuePair<HexTile, float>(t, v));
                }
                spots.Sort((a, b) => { int k = b.Value.CompareTo(a.Value); return k != 0 ? k : CombatMath.CompareTiles(a.Key, b.Key); });
                int made = 0;
                foreach (KeyValuePair<HexTile, float> s in spots) {
                    if (made >= p.scoutAreas) break;
                    if (near.Contains(s.Key)) continue;
                    foreach (HexTile n in AIPath.TilesInRangeShared(s.Key, 0, p.scoutRadius * 2)) near.Add(n);
                    near.Add(s.Key);
                    list.Add(new CampaignObjective { kind = CampaignKind.Scout, target = s.Key, value = s.Value * p.scoutTileValue * 4f });
                    made++;
                }
            }
            return list;
        }

        static HexTile Nearest(List<Unit> group, HexTile from) {
            HexTile best = null; int bd = int.MaxValue;
            foreach (Unit u in group) {
                if (u == null || u.tile == null) continue;
                int d = from != null ? from.Distance(u.tile) : 0;
                if (d < bd || (d == bd && CombatMath.Precedes(u.tile, best))) { best = u.tile; bd = d; }
            }
            return best;
        }

        /// <summary>Ground an army moves over: bridges (elevation 0), land and hills, not water or mountains.</summary>
        static bool ArmyGround(HexTile t) { return t != null && t.terrain != null && t.terrain.elevation >= 0 && t.terrain.elevation <= 2; }

        /// <summary>
        /// Chokepoint ends and road junctions. A choke tile is a bridge, or army ground with exactly two
        /// army-ground neighbours that do not touch (a strip one tile wide); the chokepoint is a run of them,
        /// and its ends the army ground just off each end of the run. A junction is a road tile with three
        /// or more road neighbours.
        /// </summary>
        public static List<KeyValuePair<HexTile, string>> Places(AIMap map) {
            List<KeyValuePair<HexTile, string>> r = new List<KeyValuePair<HexTile, string>>();
            if (HexGenerator.hg == null) return r;
            HashSet<HexTile> choke = new HashSet<HexTile>();
            foreach (HexTile t in HexGenerator.hg.tiles) {
                if (!ArmyGround(t) || t.neighbors == null) continue;
                if (t.terrain.elevation == 0) { choke.Add(t); continue; }
                List<HexTile> g = new List<HexTile>();
                foreach (HexTile n in t.neighbors) if (ArmyGround(n)) g.Add(n);
                if (g.Count == 2 && g[0].Distance(g[1]) > 1) choke.Add(t);
            }
            HashSet<HexTile> ends = new HashSet<HexTile>();
            foreach (HexTile t in choke)
                foreach (HexTile n in t.neighbors)
                    if (ArmyGround(n) && !choke.Contains(n) && n.terrain.elevation > 0) ends.Add(n);
            foreach (HexTile e in ends) r.Add(new KeyValuePair<HexTile, string>(e, "chokepoint end"));
            foreach (HexTile t in HexGenerator.hg.tiles) {
                if (t == null || t.terrain == null || !t.terrain.road || t.neighbors == null || ends.Contains(t) || choke.Contains(t)) continue;
                int roads = 0; foreach (HexTile n in t.neighbors) if (n != null && n.terrain != null && n.terrain.road) roads++;
                if (roads >= 3) r.Add(new KeyValuePair<HexTile, string>(t, "road junction"));
            }
            r.Sort((a, b) => CombatMath.CompareTiles(a.Key, b.Key));
            return r;
        }

        static int NearestOwn(HexTile t, AIMap map) {
            int d = int.MaxValue;
            foreach (Unit u in map.ownUnits) if (u != null && u.tile != null && u.Firepower > 0) d = Mathf.Min(d, u.tile.Distance(t));
            return d;
        }

        /// <summary>The enemy's combat power we cannot see or account for (0 without the fog).</summary>
        static float Hidden(AIMap map, AIProfile p) {
            FogOfWar.Knowledge k = FogOfWar.current;
            if (k == null || map.sigint == null) return 0f;
            float ours = 0f, seen = 0f;
            foreach (Unit u in map.ownUnits) if (u != null && u.tile != null) ours += Power(u);
            foreach (Unit e in map.enemyUnits) if (e != null && e.tile != null) seen += Power(e);
            foreach (FogOfWar.Phantom ph in k.phantoms)
                if (ph.kind != null && ph.last != null && ph.moves <= p.sigintMoves) seen += Mathf.Max(1f, ph.kind.cost / 6f) * ph.strength;
            return Mathf.Max(0f, ours - seen);
        }

        /// <summary>Value, the enemy there, what it needs, the way there and its priority.</summary>
        static void Appraise(CampaignObjective o, AIMap map, AIProfile p) { Reach(o, map, p, null, null); Price(o, map, p, null); }

        /// <summary>The way there, and the enemy units that could fight there (each with its power).</summary>
        static void Reach(CampaignObjective o, AIMap map, AIProfile p, HexTile keepStaging, List<Unit> free) {
            int turns = Mathf.Max(1, p.frontReachTurns);
            float board = map.BoardStake;
            // The way there first: the enemy that matters is whatever can be there by the time we are.
            Dictionary<HexTile, int> route = Route(o.target, map, p);
            HexTile from = null; int best = int.MaxValue;
            foreach (KeyValuePair<HexTile, int> kv in route) {
                if (map.SupplyDepth(kv.Key) <= 0) continue;
                if (kv.Value < best || (kv.Value == best && CombatMath.Precedes(kv.Key, from))) { best = kv.Value; from = kv.Key; }
            }
            if (from == null) {
                foreach (Unit u in map.ownUnits) {
                    int d;
                    if (u == null || u.tile == null || !route.TryGetValue(u.tile, out d)) continue;
                    if (d < best || (d == best && CombatMath.Precedes(u.tile, from))) { best = d; from = u.tile; }
                }
            }
            o.routeLength = from != null ? best : 99;
            o.staging = keepStaging ?? (from != null ? Staging(route, map, o.target, p, free) : null);
            o.routeTurns = o.staging != null ? Mathf.CeilToInt(map.Travel(null, o.staging, o.target) / (float)Mathf.Max(1, TypicalMove(map))) : 0;
            // The enemy that matters is what can be there soon after we are (frontReachTurns and one
            // more), not the whole army that could converge in the turns it takes us to get there: that
            // priced every far objective out, and the army spent its first turns on a city at home.
            if (o.kind != CampaignKind.DefendHQ) turns += 1;

            // The enemy that can fight there: what we see (each unit, with its power), and those we lost track of nearby.
            if (o.kind == CampaignKind.Clear || o.kind == CampaignKind.DefendHQ) { foreach (Unit e in o.enemies) o.owned += Power(e); }
            // ... and whatever stands in the way, or can get onto the way in time: the road from the
            // staging ground in. (Counting only what could reach the target itself, the push on the
            // enemy HQ priced the enemy's whole army, drawn up across its path, at a quarter of what
            // it was, went in at what it thought was twice their strength, and was destroyed.)
            List<HexTile> way = new List<HexTile>();
            if (o.staging != null && o.kind != CampaignKind.DefendHQ) {
                HexTile at = o.staging; int left; int guard = 0;
                while (at != null && route.TryGetValue(at, out left) && left > 0 && guard++ < 200) {
                    way.Add(at);
                    HexTile next = null; int nb = left;
                    if (at.neighbors != null) foreach (HexTile n in at.neighbors) { int v; if (n != null && route.TryGetValue(n, out v) && v < nb) { nb = v; next = n; } }
                    at = next;
                }
            }
            if (o.kind != CampaignKind.DefendHQ)
                foreach (Unit e in map.enemyUnits) {
                    if (e == null || e.tile == null || o.enemies.Contains(e) || Power(e) <= 0f) continue;
                    bool comes = CanArrive(e, o.target, turns, map);
                    for (int i = 0; i < way.Count && !comes; i += 2) comes = CanArrive(e, way[i], turns, map);
                    if (comes) o.arriving[e] = Power(e);
                }
            FogOfWar.Knowledge k = FogOfWar.current;
            if (k != null && map.sigint != null) {
                int move = 0, n = 0;
                foreach (Unit u in map.ownUnits) if (u != null && u.Firepower > 0 && u.moveRange > 0) { move += u.moveRange; n++; }
                int far = n > 0 ? move / n * turns : 0;
                // How much of the ground around it we cannot see (what scouting narrows): the unseen enemy is
                // priced at that share, none when all of it is in view (then what we see is what is there).
                int all = 0, dark = 0;
                foreach (HexTile t in AIPath.TilesInRangeShared(o.target, 0, far)) { if (t == null || t.terrain == null || t.terrain.elevation < 0) continue; all++; if (map.sigint.Unknown(t)) dark++; }
                o.hiddenReach = dark > 0;
                o.hiddenShare = all > 0 ? dark / (float)all : 0f;
            }
            if (k != null && map.sigint != null)
                foreach (FogOfWar.Phantom ph in k.phantoms) {
                    if (ph.kind == null || ph.last == null || ph.moves > p.sigintMoves) continue;
                    int reach = ph.kind.moveRange * (turns + ph.moves) + ph.kind.attackRange;
                    if (ph.last.Distance(o.target) <= reach)
                        o.owned += Mathf.Pow(p.sigintDecay, ph.moves - 1) * Mathf.Max(1f, ph.kind.cost / 6f) * ph.strength;
                }
        }

        /// <summary>
        /// The enemy power there, and so what it needs and how much we want it. <paramref name="reaches"/>:
        /// how many of the objectives each enemy unit could fight at. The enemy cannot be everywhere: a
        /// unit that could fight at k of them counts 1/k^objectiveSpread at each — in full when all in
        /// (as if the whole enemy will be wherever we go), shared out when spread (it must split too).
        /// </summary>
        static void Price(CampaignObjective o, AIMap map, AIProfile p, Dictionary<Unit, int> reaches) {
            float board = map.BoardStake;
            int units = map.ownUnits.Count + map.enemyUnits.Count;
            float average = units > 0 ? board / units : 0f;
            float spreadK = Mathf.Clamp01(p.objectiveSpread);
            o.enemyPower = o.owned + o.hiddenPower;
            foreach (KeyValuePair<Unit, float> kv in o.arriving) {
                int k;
                float share = reaches != null && reaches.TryGetValue(kv.Key, out k) && k > 1 ? 1f / Mathf.Pow(k, spreadK) : 1f;
                o.enemyPower += kv.Value * share;
            }

            switch (o.kind) {
                case CampaignKind.DefendHQ:
                    o.value = board * p.hqDefenseWeight;
                    // All in: just enough not to lose it (as strong as what threatens it); spread: the
                    // margin every objective gets.
                    o.needPower = o.enemyPower * Mathf.Lerp(1f, p.objectiveForceRatio, Mathf.Clamp01(p.objectiveSpread));
                    break;
                case CampaignKind.Secure: {
                    Building b = o.building;
                    if (b.IsHQ) o.value = board;
                    else {
                        o.value = Mathf.Max(b.income * p.objectiveHorizonTurns, average * 2f);
                        if (b.producesSupply) o.value += average * 2f;
                        // What it does for the war (campaignBuildingTiers): a building that makes units,
                        // then one that makes supply, above cities and resupply points.
                        if (p.campaignBuildingTiers) {
                            bool produces = b.buildable != null && b.buildable.Count > 0;
                            o.value *= produces ? p.productionBuildingWeight : b.producesSupply ? p.supplyBuildingWeight : 1f;
                        }
                    }
                    // At least someone who can take it, at full strength.
                    float capturer = float.MaxValue;
                    foreach (Unit u in map.ownUnits) if (u != null && Building.CanCapture(u)) capturer = Mathf.Min(capturer, AIScore.Worth(u) * 6f);
                    if (capturer == float.MaxValue) capturer = average;
                    o.needPower = Mathf.Max(capturer, o.enemyPower * p.objectiveForceRatio);
                    break;
                }
                case CampaignKind.Clear: {
                    float material = 0f;
                    foreach (Unit e in o.enemies) material += AIScore.Worth(e) * e.Strength;
                    o.value = material * p.killBonus;
                    // Contact on our ground: as defending, just enough when all in, the full margin when spread.
                    o.needPower = o.enemyPower * (o.respond ? Mathf.Lerp(1f, p.objectiveForceRatio, Mathf.Clamp01(p.objectiveSpread)) : p.objectiveForceRatio);
                    break;
                }
                case CampaignKind.Hold: {
                    // What holding it opens up (campaignPlaceWeight of an average unit's worth of the board,
                    // as a building with no income is), against what can fight there; someone to stand there.
                    o.value = average * 2f * p.campaignPlaceWeight;
                    float one = float.MaxValue;
                    foreach (Unit u in map.ownUnits) if (u != null && u.Firepower > 0 && u.Hub == null) one = Mathf.Min(one, Power(u));
                    o.needPower = Mathf.Max(one == float.MaxValue ? average : one, o.enemyPower * p.objectiveForceRatio);
                    break;
                }
                case CampaignKind.Scout:
                    o.needPower = o.enemyPower * p.objectiveForceRatio * 0.5f;   // a look, not a fight: enough not to be caught alone
                    break;
            }


            if (o.kind != CampaignKind.DefendHQ) o.needPower = Mathf.Max(o.needPower, o.minNeed);

            // Supply for a lasting presence: from our supplied ground to it, and spread at the contact line.
            if (o.lasting && o.kind != CampaignKind.DefendHQ) {
                int range = HubRange(map);
                Dictionary<HexTile, int> plain = map.LandSteps(o.target);
                int gap = 99;
                if (plain != null) foreach (KeyValuePair<HexTile, int> kv in plain) if (map.SupplyDepth(kv.Key) > 0 && kv.Value < gap) gap = kv.Value;
                if (gap >= 99) gap = o.routeLength;
                if (gap > 0) {
                    float avgWorth = average > 0f ? average : 1f;
                    float forceUnits = o.needPower / Mathf.Max(1f, avgWorth);
                    int relay = Mathf.CeilToInt(Mathf.Max(0, gap - range) / (float)Mathf.Max(1, range));
                    int spread = Mathf.CeilToInt(Mathf.Sqrt(Mathf.Max(1f, forceUnits)) / Mathf.Max(1, range));
                    o.needHubs = relay + spread;
                }
            }

            // Preference: worth over what it takes, the further the less.
            float cost = Mathf.Max(1f, o.needPower) + o.needHubs * AIScore.supplyWorth * 0.5f;
            o.priority = o.value / cost;   // per turn to achieve it: Plan
        }

        /// <summary>Steps over land from every tile to <paramref name="target"/>, a tile the enemy can strike next turn costing routeDanger more.</summary>
        static Dictionary<HexTile, int> Route(HexTile target, AIMap map, AIProfile p) {
            Dictionary<HexTile, int> d = new Dictionary<HexTile, int>();
            if (target == null) return d;
            int danger = Mathf.Max(0, Mathf.RoundToInt(p.routeDanger));
            List<List<HexTile>> buckets = new List<List<HexTile>>();
            System.Action<HexTile, int> push = (t, c) => { while (buckets.Count <= c) buckets.Add(new List<HexTile>()); buckets[c].Add(t); };
            d[target] = 0; push(target, 0);
            for (int c = 0; c < buckets.Count; c++) {
                List<HexTile> b = buckets[c];
                for (int i = 0; i < b.Count; i++) {
                    HexTile t = b[i];
                    if (d[t] != c || t.neighbors == null) continue;
                    foreach (HexTile n in t.neighbors) {
                        if (n == null || n.terrain == null) continue;
                        if (n.terrain.elevation < 0 && !(n.building != null && n.building.IsServiceTile(n))) continue;
                        if (!LocomotionRules.BridgeStep(n, t, Locomotion.Wheels)) continue;
                        int step = 1 + (map.EnemyStrikeTurns(n) <= 1 && n != target ? danger : 0);
                        int nd = c + step, old;
                        if (d.TryGetValue(n, out old) && old <= nd) continue;
                        d[n] = nd; push(n, nd);
                    }
                }
            }
            return d;
        }

        /// <summary>
        /// The staging tile: on ground we hold (supplied, and out of the enemy's reach next turn), no
        /// nearer the target than one move of our army, and close to where our free units already are
        /// (not in a fight, not on a task force): of the tiles with nearly the most of their power
        /// within campaignDrawTurns of it (four fifths of the best), the one nearest the target. Null
        /// when there is no such ground — better none than one in the firing line. (It was the supplied
        /// tile nearest the target, wherever the army was: often in the enemy's reach, and often too
        /// far from the units for them to gather on it.)
        /// </summary>
        static HexTile Staging(Dictionary<HexTile, int> route, AIMap map, HexTile target, AIProfile p, List<Unit> free) {
            int bound = TypicalMove(map), draw = DrawTurns(p);
            List<HexTile> safe = new List<HexTile>();
            foreach (KeyValuePair<HexTile, int> kv in route) {
                HexTile t = kv.Key;
                if (t == null || map.SupplyDepth(t) <= 0 || map.EnemyStrikeTurns(t) <= 1) continue;
                if (target != null && t.Distance(target) < bound) continue;
                safe.Add(t);
            }
            if (safe.Count == 0) return null;
            Dictionary<HexTile, float> near = new Dictionary<HexTile, float>();
            float most = 0f;
            foreach (HexTile t in safe) {
                float v = 0f;
                if (free != null) foreach (Unit u in free) if (u.tile.Distance(t) <= u.moveRange * draw) v += Power(u);
                near[t] = v; most = Mathf.Max(most, v);
            }
            HexTile best = null; int bestD = int.MaxValue;
            foreach (HexTile t in safe) {
                if (near[t] < most * 0.8f) continue;
                int d = route[t];
                if (d < bestD || (d == bestD && CombatMath.Precedes(t, best))) { bestD = d; best = t; }
            }
            return best;
        }

        static int DrawTurns(AIProfile p) { return p.campaignDrawTurns > 0 ? p.campaignDrawTurns : Mathf.Max(1, p.frontReachTurns); }

        /// <summary>Whether the healthy power on the task force's staging ground (or past it) is campaignGatherShare of its need, as AIStrategic.GatherThenGo counts it.</summary>
        static bool GatheredEnough(CampaignObjective o, AIMap map, AIProfile p) {
            if (o.staging == null || o.target == null) return true;
            int healthy = Mathf.CeilToInt(6 * p.readyStrength);
            int radius = p.campaignStagingRadius > 0 ? p.campaignStagingRadius : TypicalMove(map);
            int stagingToGo = map.Travel(null, o.staging, o.target);
            float gathered = 0f;
            foreach (Unit u in o.units) {
                if (u == null || u.tile == null || u.Strength < healthy) continue;
                if (map.Travel(u, u.tile, o.staging) <= radius || map.Travel(u, u.tile, o.target) < stagingToGo) gathered += Power(u);
            }
            return gathered >= Mathf.Max(1f, o.needPower) * Mathf.Clamp01(p.campaignGatherShare);
        }

        /// <summary>The middle move range of our fighting units.</summary>
        static int TypicalMove(AIMap map) {
            List<int> m = new List<int>();
            foreach (Unit u in map.ownUnits) if (u != null && u.Firepower > 0 && u.Hub == null && u.moveRange > 0) m.Add(u.moveRange);
            if (m.Count == 0) return 3;
            m.Sort();
            return m[m.Count / 2];
        }

        static int HubRange(AIMap map) {
            List<int> r = new List<int>();
            foreach (SupplyPoint h in map.ownHubs) if (h != null && !h.producer) r.Add(h.range);
            if (r.Count == 0) return 4;
            r.Sort();
            return Mathf.Max(1, r[r.Count / 2]);
        }

        // ------------------------------------------------------------------ keeping or replanning

        /// <summary>Why last turn's plan cannot stand (null: it can).</summary>
        string ReplanReason(List<CampaignObjective> now, AIMap map, AIProfile p) {
            HashSet<string> ids = new HashSet<string>();
            foreach (CampaignObjective o in now) ids.Add(o.Id);
            foreach (CampaignObjective o in now)
                if (o.kind == CampaignKind.DefendHQ && !objectives.Exists(x => x.kind == CampaignKind.DefendHQ)) return "our HQ is threatened";
            foreach (CampaignObjective o in objectives) {
                // A scout that has had its look is simply done: it does not throw out every task force's
                // plan. (It did, nearly every turn: no task force lasted more than one.)
                if (o.kind == CampaignKind.Scout) continue;
                if (!ids.Contains(o.Id)) return o.Id + " achieved or gone";
                float power = 0f;
                foreach (Unit u in o.units) if (u != null && u.tile != null && u.Strength > 0) power += Power(u);
                if (o.formedPower > 0f && power < o.formedPower * p.objectiveReplanLoss) return o.Id + " task force broken (" + power.ToString("0") + " of " + o.formedPower.ToString("0") + ")";
                if (o.fellBack) return o.Id + " task force fell back, outmatched";
            }
            return null;
        }

        /// <summary>In contact within homeFightRadius of our HQ: left to the ordinary tactics (and supply).</summary>
        public static bool HomeFight(Unit u, AIMap map, AIProfile p) {
            if (p.homeFightRadius <= 0 || u == null || u.tile == null) return false;
            Building home = GameController.HQOf(map.player);
            return home != null && home.tile != null && u.tile.Distance(home.tile) <= p.homeFightRadius && map.TurnsToContact(u) <= p.objectiveContactTurns;
        }

        // ------------------------------------------------------------------ task forces

        void Allocate(List<CampaignObjective> candidates, AIMap map, AIProfile p, AICampaign keep, HashSet<string> restart = null) {
            List<Unit> pool = new List<Unit>();
            foreach (Unit u in map.ownUnits) {
                if (u == null || u.tile == null || u.Hub != null || u.Firepower <= 0) continue;
                if (Transporting.IsTransport(u) && u.Firepower <= 1) continue;
                // Fighting close to home (homeFightRadius): the ordinary tactics, not a task force.
                if (p.homeFightRadius > 0 && HomeFight(u, map, p)) continue;
                pool.Add(u);
            }
            List<SupplyPoint> hubPool = new List<SupplyPoint>();
            foreach (SupplyPoint h in map.ownHubs)
                if (h != null && !h.producer && h.myUnit != null && h.myUnit.tile != null && !LocomotionRules.IsNaval(h.myUnit.locomotion) && !LocomotionRules.IsAirborne(h.myUnit.locomotion)) hubPool.Add(h);
            float spread = Mathf.Clamp01(p.objectiveSpread);

            // Kept from last turn: the same units on the same objectives.
            if (keep != null) {
                foreach (CampaignObjective now in candidates) {
                    CampaignObjective was = keep.objectives.Find(x => x.Id == now.Id);
                    if (was == null) continue;
                    if (restart != null && restart.Contains(now.Id)) continue;   // broken or fell back: formed again
                    // Not the HQ's defence: whoever can get there soonest, chosen afresh every turn.
                    if (now.kind == CampaignKind.DefendHQ) continue;
                    // ... except one that has gone off to fight elsewhere, or is now too far to join: it
                    // fights where it is by the ordinary tactics, and the task force no longer waits for it.
                    List<Unit> stay = new List<Unit>();
                    foreach (Unit u in was.units) if (pool.Contains(u) && CanJoin(u, now, map, p)) stay.Add(u);
                    // Cohesion (campaignCohesionTurns): the task force's centre is the unit with the least
                    // total distance to the rest; a unit more than that many of its own turns from it drops
                    // out (the ordinary tactics take it, or another objective). (Half a task force on each
                    // side of a lake, one with supply and one without.)
                    HexTile centre = Centre(stay);
                    foreach (Unit u in stay) {
                        if (p.campaignCohesionTurns > 0 && centre != null && u.moveRange > 0
                            && Mathf.CeilToInt(map.Travel(u, u.tile, centre) / (float)u.moveRange) > p.campaignCohesionTurns) continue;
                        Give(now, u); pool.Remove(u);
                    }
                    // A building's task force that has lost its one who could take it gets another (the HQ's
                    // task force stood a turn from the HQ for seven turns with nobody to capture it).
                    if (now.kind == CampaignKind.Secure && !now.units.Exists(Building.CanCapture)) Fill(now, pool, map, p, 0f);
                    foreach (SupplyPoint h in was.hubs) if (hubPool.Contains(h)) { now.hubs.Add(h); hubPool.Remove(h); }
                    now.formedPower = was.formedPower;
                    now.age = was.age + 1; now.went = was.went; now.routeTurns = was.routeTurns; now.lastPace = was.lastPace; now.stalled = was.stalled;
                    now.allIn = was.allIn;
                    objectives.Add(now);
                }
            }

            // Our HQ first, and enemy on our ground, whatever the odds.
            foreach (CampaignObjective o in candidates) {
                if (o.kind != CampaignKind.DefendHQ && !o.respond) continue;
                Fill(o, pool, map, p, o.needPower);
                if (!objectives.Contains(o)) objectives.Add(o);
            }
            float poolPower = 0f;
            foreach (Unit u in pool) poolPower += Power(u);

            // Then the best that can be achieved, in order of priority: each taken on only if what is left
            // could still give it all it needs (a task force short of its need is how armies are lost
            // piecemeal), and no more of them than the spread allows (one when all in, every one that
            // fits when spread). Each gets its need, nearest and best-fitting units first.
            int candidatesLeft = candidates.FindAll(o => o.kind != CampaignKind.DefendHQ).Count;
            int maxPursued = 1 + Mathf.RoundToInt(spread * Mathf.Max(0, candidatesLeft - 1));
            int pursued = 0;
            // Supply decides how far the army can spread: an objective beyond our supply is taken on
            // only if the supply units it needs are left after those before it. (Seven task forces at
            // once on BigMapLakes, most with no supply at all, starved one after another.)
            // (Only supply units joined to the network count: one cut off serves no front.)
            SupplyNetwork.Result joined = SupplyNetwork.SolveAll(map.player, new Dictionary<SupplyPoint, HexTile>());
            int hubsLeft = 0; foreach (SupplyPoint h in hubPool) if (joined.connectedHubs.Contains(h)) hubsLeft++;
            foreach (CampaignObjective o in objectives) if (o.lasting) hubsLeft -= o.needHubs;
            foreach (CampaignObjective o in candidates) {
                if (o.kind == CampaignKind.DefendHQ || o.respond) continue;
                if (pursued >= maxPursued || pool.Count == 0) break;
                bool kept = objectives.Contains(o);
                if (!kept && o.lasting && o.needHubs > 0 && o.needHubs > hubsLeft) {
                    float per = o.value / Mathf.Max(1, o.needHubs - Mathf.Max(0, hubsLeft));
                    if (per > supplyDemand) { supplyDemand = per; supplyDemandFor = o.Id; supplyDemandNeed = o.needPower / Mathf.Max(1, o.needHubs - Mathf.Max(0, hubsLeft)); }
                    if (Debug != null) Debug("not taken on: " + o.Id + " needs " + o.needHubs + " supply units, " + hubsLeft + " free");
                    continue;
                }
                float still = Mathf.Max(0f, o.needPower - o.assignedPower);
                float near = 0f; int joinable = 0; foreach (Unit u in pool) if (CanJoin(u, o, map, p)) { near += Power(u); joinable++; }
                if (still > near && o.assignedPower <= 0f) {
                    if (Debug != null) {
                        float all = 0f; List<string> why = new List<string>();
                        foreach (Unit u in pool) { all += Power(u); if (!CanJoin(u, o, map, p)) why.Add(u.name.Replace("(Clone)", "") + "(" + u.tile.x + "," + u.tile.y + ") " + JoinWhy(u, o, map, p)); }
                        Debug("not taken on: " + o.Id + " needs " + still.ToString("0") + ", " + joinable + " units that can join have " + near.ToString("0") + " (" + pool.Count + " free have " + all.ToString("0") + ")"
                              + (o.staging != null ? "; staging (" + o.staging.x + "," + o.staging.y + ")" : "") + (why.Count > 0 ? "; left out: " + string.Join(", ", why.ToArray()) : ""));
                    }
                    continue;
                }
                Fill(o, pool, map, p, o.needPower);
                if (!objectives.Contains(o)) { objectives.Add(o); if (o.lasting) hubsLeft -= o.needHubs; }
                pursued++;
                poolPower = 0f; foreach (Unit u in pool) poolPower += Power(u);
            }
            // Separate groups of what is left (AIProfile.campaignWings): a group too far from every task
            // force's staging ground to join it forms a task force of its own — another one for an
            // objective already taken on if that is its best, staged near it. (One front per objective
            // left a second group of ours idle across the map, the other front short.)
            if (p.campaignWings && pool.Count > 0) {
                int band = p.campaignMaxTurns > 0 ? p.campaignMaxTurns : 99;
                foreach (List<Unit> g in Groups(pool, map, p)) {
                    if (pursued >= maxPursued) break;
                    List<Unit> group = g.FindAll(pool.Contains);
                    if (group.Count == 0) continue;
                    CampaignObjective best = null; float bestPriority = 0f;
                    foreach (CampaignObjective first in new List<CampaignObjective>(objectives)) {
                        if (first.wing != 0 || first.units.Count == 0 || first.target == null) continue;
                        if (first.kind != CampaignKind.Secure && first.kind != CampaignKind.Hold) continue;
                        if (group.Exists(u => CanJoin(u, first, map, p))) continue;   // it can join the one there is
                        int w = 1; while (objectives.Exists(x => x.BaseId == first.BaseId && x.wing == w)) w++;
                        CampaignObjective wing = first.Wing(w);
                        Reach(wing, map, p, null, group);
                        Price(wing, map, p, null);
                        // Its share: what the objective needs beyond its other task forces, at least someone to hold or take it.
                        float others = 0f; foreach (CampaignObjective x in objectives) if (x.BaseId == first.BaseId) others += x.assignedPower;
                        float least = 0f; foreach (Unit u in group) if (first.kind != CampaignKind.Secure || Building.CanCapture(u)) least = least <= 0f ? Power(u) : Mathf.Min(least, Power(u));
                        wing.needPower = Mathf.Max(least, wing.needPower - others);
                        wing.turnsAway = TurnsAway(wing, group, map);
                        if (wing.turnsAway > band) continue;
                        if (wing.lasting && wing.needHubs > 0 && wing.needHubs > hubsLeft) continue;
                        float near = 0f; foreach (Unit u in group) if (CanJoin(u, wing, map, p)) near += Power(u);
                        if (near < wing.needPower) continue;
                        wing.priority = wing.priority / Mathf.Max(1f, wing.turnsAway + 1f);
                        if (best == null || wing.priority > bestPriority) { best = wing; bestPriority = wing.priority; }
                    }
                    if (best == null) continue;
                    Fill(best, pool, map, p, best.needPower);
                    if (best.units.Count == 0) continue;
                    objectives.Add(best); if (best.lasting) hubsLeft -= best.needHubs;
                    pursued++;
                }
            }

            // Nothing within reach of what we have: everything on the best of them (all in), not nothing —
            // scouting does not count as something. (Under the fog every objective needed more than our
            // whole army, two scouts counted as "pursued", and the army sat at home for 24 rounds.) All in,
            // its need is what it was given: it goes once that has gathered, rather than waiting for a
            // force we do not have.
            if (!objectives.Exists(o => o.kind != CampaignKind.DefendHQ && o.kind != CampaignKind.Scout && o.units.Count > 0) && pool.Count > 0) {
                // The best that our supply can carry: of those needing the fewest supply units beyond what we
                // have, the first by priority. (The best by priority alone sent the whole army, with no supply
                // units, at the enemy HQ rather than the city in front of it.)
                CampaignObjective top = null; int topShort = int.MaxValue;
                foreach (CampaignObjective o in candidates) {
                    if (o.kind == CampaignKind.DefendHQ || o.respond || o.kind == CampaignKind.Scout) continue;
                    int shortBy = o.lasting ? Mathf.Max(0, o.needHubs - Mathf.Max(0, hubsLeft)) : 0;
                    if (shortBy < topShort) { topShort = shortBy; top = o; }
                }
                if (top != null) {
                    Fill(top, pool, map, p, top.needPower);
                    if (!objectives.Contains(top)) objectives.Add(top);
                    if (top.assignedPower > 0f) { top.needPower = Mathf.Min(top.needPower, top.assignedPower); top.allIn = true; }
                }
            }

            // What is left: (1 - spread) of it to the best objective, the rest over all in proportion to need.
            // (Off by default, campaignLeftoverToFronts: what is left stays with the ordinary tactics, which
            // fight and push on better than a task force does. Given to the task forces, a city that needed
            // three units got twenty-four, and the crowd jammed the one bridge on the way.)
            List<CampaignObjective> active = objectives.FindAll(o => o.kind != CampaignKind.DefendHQ && o.kind != CampaignKind.Scout);
            if (p.campaignLeftoverToFronts && active.Count > 0 && pool.Count > 0) {
                float leftover = 0f; foreach (Unit u in pool) leftover += Power(u);
                float needSum = 0f; foreach (CampaignObjective o in active) needSum += Mathf.Max(1f, o.needPower);
                Dictionary<CampaignObjective, float> target = new Dictionary<CampaignObjective, float>();
                for (int i = 0; i < active.Count; i++) {
                    float share = Mathf.Max(1f, active[i].needPower) / needSum;
                    target[active[i]] = leftover * (spread * share + (i == 0 ? 1f - spread : 0f));
                }
                Dictionary<CampaignObjective, float> got = new Dictionary<CampaignObjective, float>();
                foreach (CampaignObjective o in active) got[o] = 0f;
                pool.Sort((a, b) => AIMap.CompareUnits(a, b));
                foreach (Unit u in pool) {
                    CampaignObjective best = null; float bestScore = float.NegativeInfinity;
                    foreach (CampaignObjective o in active) {
                        if (!CanJoin(u, o, map, p)) continue;
                        float deficit = target[o] - got[o];
                        float s = deficit / Mathf.Max(1f, Power(u)) - Turns(u, o, map) * 0.1f;
                        if (s > bestScore) { bestScore = s; best = o; }
                    }
                    if (best == null) continue;
                    Give(best, u); got[best] += Power(u);
                }
                pool.Clear();
            }

            // Supply: to each lasting objective what it needs, nearest first.
            foreach (CampaignObjective o in objectives) {
                if (!o.lasting) continue;
                while (o.hubs.Count < o.needHubs && hubPool.Count > 0) {
                    HexTile to = o.staging ?? o.target;
                    SupplyPoint best = null; int bd = int.MaxValue;
                    foreach (SupplyPoint h in hubPool) {
                        int d = map.Travel(h.myUnit, h.myUnit.tile, to);
                        if (d < bd || (d == bd && best != null && AIMap.CompareUnits(h.myUnit, best.myUnit) < 0)) { bd = d; best = h; }
                    }
                    if (best == null) break;
                    o.hubs.Add(best); hubPool.Remove(best);
                }
            }

            // Spread: the rest of the supply units out with the task forces too, in proportion to how far
            // each is from supplied ground — supply that covers ground rather than sitting at home.
            List<CampaignObjective> far = objectives.FindAll(x => x.lasting && x.units.Count > 0 && x.routeLength > 0);
            if (spread > 0f && far.Count > 0) {
                int extra = Mathf.RoundToInt(hubPool.Count * spread);
                for (int i = 0; i < extra && hubPool.Count > 0; i++) {
                    CampaignObjective o = null; float most = -1f;
                    foreach (CampaignObjective x in far) { float want = x.routeLength / (float)(1 + x.hubs.Count); if (want > most) { most = want; o = x; } }
                    if (o == null) break;
                    HexTile to = o.staging ?? o.target;
                    SupplyPoint best = null; int bd = int.MaxValue;
                    foreach (SupplyPoint h in hubPool) {
                        int d = map.Travel(h.myUnit, h.myUnit.tile, to);
                        if (d < bd || (d == bd && best != null && AIMap.CompareUnits(h.myUnit, best.myUnit) < 0)) { bd = d; best = h; }
                    }
                    if (best == null) break;
                    o.hubs.Add(best); hubPool.Remove(best);
                }
            }

            foreach (CampaignObjective o in objectives) if (o.allIn && o.assignedPower > 0f) o.needPower = Mathf.Min(o.needPower, o.assignedPower);
            // A new task force's time box counts its own units' travel to the target (the middle of them),
            // not a typical unit's: five grunts walking through forest to the enemy HQ were given up after
            // 11 turns, still 10 tiles short of it.
            foreach (CampaignObjective o in objectives) {
                if (o.age > 0 || o.units.Count == 0 || o.target == null) continue;
                List<int> t = new List<int>();
                foreach (Unit u in o.units) if (u != null && u.tile != null && u.moveRange > 0) t.Add(Mathf.CeilToInt(map.Travel(u, u.tile, o.target) / (float)u.moveRange));
                if (t.Count > 0) { t.Sort(); o.routeTurns = t[t.Count / 2]; }
            }
            foreach (CampaignObjective o in objectives) {
                if (o.formedPower <= 0f) o.formedPower = o.assignedPower;
                foreach (Unit u in o.units) unitOf[u] = o;
                foreach (SupplyPoint h in o.hubs) hubOf[h] = o;
            }
        }

        /// <summary>Our units in groups: one unit is in another's group if it is within campaignDrawTurns of its moves of it (largest group by power first).</summary>
        static List<List<Unit>> Groups(List<Unit> units, AIMap map, AIProfile p) {
            List<List<Unit>> groups = new List<List<Unit>>();
            List<Unit> left = new List<Unit>(units);
            left.Sort(AIMap.CompareUnits);
            int reach = TypicalMove(map) * DrawTurns(p);
            while (left.Count > 0) {
                List<Unit> g = new List<Unit> { left[0] }; left.RemoveAt(0);
                for (int i = 0; i < g.Count; i++)
                    for (int j = left.Count - 1; j >= 0; j--)
                        if (left[j].tile.Distance(g[i].tile) <= reach) { g.Add(left[j]); left.RemoveAt(j); }
                groups.Add(g);
            }
            groups.Sort((a, b) => {
                float pa = 0f, pb = 0f; foreach (Unit u in a) pa += Power(u); foreach (Unit u in b) pb += Power(u);
                int k = pb.CompareTo(pa); return k != 0 ? k : AIMap.CompareUnits(a[0], b[0]);
            });
            return groups;
        }

        static void Give(CampaignObjective o, Unit u) {
            if (o.units.Contains(u)) return;
            o.units.Add(u);
            o.assignedPower += Power(u);
            // Its seat on the transport that would carry it there, if riding is quicker.
            Unit ride;
            if (planMap != null) TurnsTo(u, o.staging ?? o.target, planMap, out ride); else ride = null;
            if (ride != null && seats != null && seats.ContainsKey(ride)) seats[ride]--;
        }

        /// <summary>Give <paramref name="o"/> the units that fit it best until it has <paramref name="upTo"/> combat power (someone to capture, or to scout, first).</summary>
        static void Fill(CampaignObjective o, List<Unit> pool, AIMap map, AIProfile p, float upTo) {
            // A building needs someone who can take it.
            if (o.kind == CampaignKind.Secure && !o.units.Exists(Building.CanCapture)) {
                Unit c = null; int ct = int.MaxValue;
                foreach (Unit u in pool) {
                    if (!Building.CanCapture(u) || !CanJoin(u, o, map, p)) continue;
                    int t = Turns(u, o, map);
                    if (t < ct || (t == ct && c != null && AIMap.CompareUnits(u, c) < 0)) { ct = t; c = u; }
                }
                if (c != null) { Give(o, c); pool.Remove(c); }
            }
            if (o.kind == CampaignKind.Scout) {
                // Those that see the most for the least.
                Unit s = null; float sv = 0f;
                foreach (Unit u in pool) {
                    if (u.visionRange <= 0 || u.moveRange <= 0 || LocomotionRules.NeedsAirfield(u.locomotion) || !CanJoin(u, o, map, p)) continue;
                    float v = (u.visionRange + 1f) * u.moveRange / AIScore.Worth(u) / (1f + Turns(u, o, map));
                    if (v > sv || (v == sv && s != null && AIMap.CompareUnits(u, s) < 0)) { sv = v; s = u; }
                }
                if (s != null) { Give(o, s); pool.Remove(s); }
            }
            while (o.assignedPower < upTo && pool.Count > 0) {
                Unit best = null; float bestScore = float.NegativeInfinity;
                foreach (Unit u in pool) {
                    if (!CanJoin(u, o, map, p)) continue;
                    // The nearest first; how well it fights the enemy there only between equals (under a turn).
                    float s = Fit(u, o, map) * 0.25f - Turns(u, o, map);
                    if (s > bestScore || (s == bestScore && best != null && AIMap.CompareUnits(u, best) < 0)) { bestScore = s; best = u; }
                }
                if (best == null) break;
                Give(o, best); pool.Remove(best);
            }
        }

        /// <summary>
        /// Whether <paramref name="u"/> can be part of <paramref name="o"/>'s task force: near enough to
        /// join it (campaignDrawTurns of its staging tile — a task force never takes units from far
        /// away), and not in a fight of its own elsewhere (in contact next turn, and neither on the
        /// staging ground nor past it toward the objective). A task force that kept such a unit waited
        /// at its staging ground for a unit that was not coming.
        /// </summary>
        static bool CanJoin(Unit u, CampaignObjective o, AIMap map, AIProfile p) {
            if (u == null || u.tile == null) return false;
            // On its way: past the staging ground toward the target. However far it has gone, it is
            // the task force's (fighting there is the task force's fight). (Measured from the staging
            // tile alone, every unit that had marched two turns past it was dropped, and the HQ's task
            // force was made of whatever had just been bought at home.)
            if (o.staging != null && o.target != null && o.kind != CampaignKind.DefendHQ
                && map.Travel(u, u.tile, o.target) < map.Travel(null, o.staging, o.target)) return true;
            if (Turns(u, o, map) > DrawTurns(p)) return false;
            if (o.staging == null || o.kind == CampaignKind.DefendHQ) return true;
            if (map.TurnsToContact(u) > 1) return true;
            int radius = p.campaignStagingRadius > 0 ? p.campaignStagingRadius : TypicalMove(map);
            return map.Travel(u, u.tile, o.staging) <= radius;
        }

        /// <summary>For diagnostics: why CanJoin says no.</summary>
        static string JoinWhy(Unit u, CampaignObjective o, AIMap map, AIProfile p) {
            int t = Turns(u, o, map);
            if (t > DrawTurns(p)) return t + " turns from staging (draw " + DrawTurns(p) + ")";
            return "in contact (" + map.TurnsToContact(u) + ") off the staging ground";
        }

        /// <summary>Turns for <paramref name="u"/> to reach the objective's staging tile.</summary>
        static int Turns(Unit u, CampaignObjective o, AIMap map) {
            HexTile to = o.staging ?? o.target;
            Unit ride;
            return TurnsTo(u, to, map, out ride);
        }

        /// <summary>Of <paramref name="units"/>' tiles, the one with the least total distance to the others.</summary>
        public static HexTile Centre(List<Unit> units) {
            HexTile best = null; int bd = int.MaxValue;
            foreach (Unit a in units) {
                if (a == null || a.tile == null) continue;
                int d = 0; foreach (Unit b in units) if (b != null && b.tile != null) d += a.tile.Distance(b.tile);
                if (d < bd || (d == bd && CombatMath.Precedes(a.tile, best))) { bd = d; best = a.tile; }
            }
            return best;
        }

        /// <summary>Seats left on our free ground transports this plan (Plan; Give takes them).</summary>
        [System.ThreadStatic] static Dictionary<Unit, int> seats;
        [System.ThreadStatic] static AIMap planMap;

        static bool IsSeatCarrier(Unit t) {
            return t != null && t.tile != null && Transporting.IsTransport(t) && !LocomotionRules.IsAirborne(t.locomotion) && !LocomotionRules.IsNaval(t.locomotion) && t.transported.Count == 0 && t.Hub == null;
        }

        /// <summary>
        /// Turns for <paramref name="u"/> to reach <paramref name="to"/>: walking, or — if a transport with a
        /// seat left can come to it within one of its moves and carry it — a turn to board and then the
        /// transport's speed, whichever is quicker (<paramref name="ride"/>: that transport).
        /// </summary>
        static int TurnsTo(Unit u, HexTile to, AIMap map, out Unit ride) {
            ride = null;
            if (to == null || u.tile == null || u.moveRange <= 0) return 99;
            int walk = Mathf.CeilToInt(map.Travel(u, u.tile, to) / (float)u.moveRange);
            if (seats == null || Transporting.IsTransport(u)) return walk;
            int best = walk;
            foreach (KeyValuePair<Unit, int> kv in seats) {
                Unit t = kv.Key;
                if (kv.Value <= 0 || t == u || !Transporting.CanCarry(t, u) || t.moveRange <= 0) continue;
                if (map.Travel(t, t.tile, u.tile) > t.moveRange + 1) continue;
                int by = 1 + Mathf.CeilToInt(map.Travel(t, u.tile, to) / (float)t.moveRange);
                if (by < best || (by == best && ride != null && AIMap.CompareUnits(t, ride) < 0)) { best = by; ride = t; }
            }
            return best;
        }

        /// <summary>The front's distance: of the free units nearest it (by their turns to it) enough for its need, the turns of the slowest.</summary>
        static int TurnsAway(CampaignObjective o, List<Unit> free, AIMap map) {
            if (o.target == null) return 99;
            Dictionary<Unit, int> was = seats;
            seats = was != null ? new Dictionary<Unit, int>(was) : null;
            try {
                List<KeyValuePair<int, Unit>> t = new List<KeyValuePair<int, Unit>>();
                foreach (Unit u in free) { Unit r; t.Add(new KeyValuePair<int, Unit>(TurnsTo(u, o.target, map, out r), u)); }
                t.Sort((a, b) => { int c = a.Key.CompareTo(b.Key); return c != 0 ? c : AIMap.CompareUnits(a.Value, b.Value); });
                float got = 0f; int slowest = 99;
                bool capturer = o.kind != CampaignKind.Secure;
                foreach (KeyValuePair<int, Unit> kv in t) {
                    Unit r; int turns = TurnsTo(kv.Value, o.target, map, out r);
                    if (r != null) seats[r]--;
                    got += Power(kv.Value); slowest = turns;
                    if (Building.CanCapture(kv.Value)) capturer = true;
                    if (got >= o.needPower && capturer) break;
                }
                return slowest;
            } finally { seats = was; }
        }

        /// <summary>How well <paramref name="u"/> fights the enemy kinds there: its damage to them for its worth, in turns-of-travel units (0..3).</summary>
        static float Fit(Unit u, CampaignObjective o, AIMap map) {
            List<Unit> foes = o.enemies.Count > 0 ? o.enemies : null;
            if (foes == null) {
                foes = new List<Unit>();
                foreach (Unit e in map.enemyUnits) if (e != null && e.tile != null && o.target != null && e.tile.Distance(o.target) <= 6) foes.Add(e);
            }
            if (foes.Count == 0) return 0f;
            float dmg = 0f;
            foreach (Unit e in foes) dmg += Mathf.Min(e.Strength, Unit.calculateDamage(u, e, e.tile, 0)) * AIScore.Worth(e);
            dmg /= foes.Count;
            float per = dmg / Mathf.Max(1f, AIScore.Worth(u));
            return Mathf.Clamp(per, 0f, 3f);
        }

        static float Power(Unit u) {
            if (u == null || u.Firepower <= 0 || u.Strength <= 0 || u.Hub != null) return 0f;
            return AIScore.Worth(u) * u.Strength;
        }

        static bool CanArrive(Unit u, HexTile at, int turns, AIMap map) {
            if (u == null || u.tile == null || at == null) return false;
            return map.Travel(u, u.tile, at) <= u.moveRange * turns + u.AttackRange;
        }

        // ------------------------------------------------------------------ fronts

        /// <summary>
        /// The fronts the tactical layer fights: one for every objective with a task force, its units
        /// (and supply units) assigned, gathering at the objective's staging tile; and one at home
        /// for anything not given an objective.
        /// </summary>
        public List<Front> Fronts(AIMap map, AIProfile p, Dictionary<HexTile, Posture> memory, HashSet<HexTile> assaulting) {
            List<Front> fronts = new List<Front>();
            float own = 0f, enemy = 0f;
            foreach (Unit u in map.ownUnits) own += Power(u);
            foreach (Unit u in map.enemyUnits) enemy += Power(u);
            float army = enemy <= 0.01f ? 99f : own / enemy;
            foreach (CampaignObjective o in objectives) {
                if (o.units.Count == 0 && o.hubs.Count == 0) continue;
                Front f = new Front { center = o.target, objectiveTile = o.target, objective = o.building != null && o.kind == CampaignKind.Secure ? o.building : null, priority = o.priority };
                f.objectiveIsHQ = o.building != null && o.building.IsHQ && o.kind == CampaignKind.Secure;
                f.objectiveValue = o.value;
                f.ownStrength = o.assignedPower;
                f.enemyStrength = o.enemyPower;
                f.armyRatio = p.useArmyRatio ? army : 0f;
                f.campaignStaging = o.kind == CampaignKind.DefendHQ ? null : o.staging;
                f.campaignReady = o.assignedPower >= o.needPower && o.kind != CampaignKind.DefendHQ;
                f.campaignNeed = o.needPower;
                f.pullScale = p.campaignPull;
                f.campaign = true;
                f.campaignObjective = o;
                f.campaignStagingRadius = p.campaignStagingRadius > 0 ? p.campaignStagingRadius : TypicalMove(map);
                // Outmatched where it stands (campaignFallbackRatio): the enemy that can strike its units
                // next turn is more than it can take. It falls back to its staging ground and the campaign
                // plans again, rather than being destroyed in detail — the south task force on ChanceMeeting
                // lost its tanks one by one to the enemy's whole army.
                float met;
                if (o.kind != CampaignKind.DefendHQ && o.staging != null && Outmatched(o, map, p, out met)) {
                    o.metPower = met;
                    f.objectiveTile = o.staging; f.objective = null; f.objectiveIsHQ = false; f.objectiveValue = o.value * 0.5f;
                    f.campaignStaging = null; f.campaignReady = false; f.posture = Posture.Defensive;
                    o.fellBack = true;
                }
                // A look, not a fight: a scouting task force in contact falls back to where it gathered.
                if (o.kind == CampaignKind.Scout && o.staging != null && o.units.Exists(u => u != null && u.tile != null && map.TurnsToContact(u) <= 1)) {
                    f.objectiveTile = o.staging; f.objective = null; f.objectiveValue = o.value * 0.5f; f.campaignStaging = null; f.campaignReady = false;
                }
                f.assigned.AddRange(o.units);
                foreach (SupplyPoint h in o.hubs) if (h.myUnit != null) f.assigned.Add(h.myUnit);
                // Defending the HQ: no building to take, but the HQ's worth pulls its task force home. (With
                // no pull at all, the five units given to it fought on where they were, 15 tiles away, while
                // the enemy took the HQ.)
                if (o.kind == CampaignKind.DefendHQ) { f.objective = null; f.objectiveIsHQ = false; f.objectiveValue = o.value; }
                fronts.Add(f);
            }
            AIStrategic.Finish(fronts, map, p, memory, assaulting);
            // No holding and waiting (AIProfile.regroup): a task force strikes first; its expensive units pull
            // back when they lack the superiority (AITactical.Regrouping), and the reserve comes up.
            if (p.regroup && p.campaignStrikeFirst) foreach (Front f in fronts) if (f.campaign) f.posture = Posture.Aggressive;
            // Everything not on a task force: the ordinary fronts (AIStrategic.BuildFronts), which fight
            // and push on as the ordinary AI does. (A single front at home with no objective left them
            // standing there.)
            HashSet<Unit> taken = new HashSet<Unit>();
            foreach (Front f in fronts) foreach (Unit u in f.assigned) taken.Add(u);
            // The reserve (AIProfile.campaignReserve): of the units on no task force, those not fighting gather
            // as one group — the central reserve — in our supply out of the enemy's reach, behind the main
            // front; campaignActiveReserveShare of them (the nearest the enemy), and any already fighting, stay
            // with the ordinary fronts as the active reserve. (Spare units spread over the ordinary AI's
            // captures and fights did not clump, with the enemy's whole army somewhere in the fog.)
            reserveFront = null;
            if (p.campaignReserve) {
                List<Unit> spare = new List<Unit>();
                foreach (Unit u in map.ownUnits)
                    if (u != null && u.tile != null && !taken.Contains(u) && u.Hub == null && u.Firepower > 0 && !(Transporting.IsTransport(u) && u.Firepower <= 1) && u.trasnporter == null && !LocomotionRules.IsAirborne(u.locomotion) && !LocomotionRules.IsNaval(u.locomotion)) spare.Add(u);
                int release = Mathf.Max(1, p.campaignReleaseTurns);
                List<Unit> calm = spare.FindAll(u => map.TurnsToContact(u) > release);
                int active = spare.Count - calm.Count;
                int wantActive = Mathf.CeilToInt(spare.Count * Mathf.Clamp01(p.campaignActiveReserveShare));
                calm.Sort((a, b) => { int c = map.TurnsToContact(a).CompareTo(map.TurnsToContact(b)); return c != 0 ? c : AIMap.CompareUnits(a, b); });
                while (active < wantActive && calm.Count > 0) { calm.RemoveAt(0); active++; }
                HexTile rally = calm.Count > 0 ? RallyTile(map, fronts) : null;
                // Committed (AIProfile.regroup): to the front most pressed — the enemy that can strike its units
                // tonight more than regroupCommitRatio of their power — all of it, toward that front's units.
                Front pressed = null; float worst = 0f;
                if (p.regroup && calm.Count > 0)
                    foreach (Front f in fronts) {
                        float ours = 0f, theirs = 0f; HashSet<Unit> seenE = new HashSet<Unit>(); List<Unit> mine = new List<Unit>();
                        foreach (Unit u in f.assigned) {
                            if (u == null || u.tile == null || u.Hub != null || u.Firepower <= 0) continue;
                            ours += AIScore.Worth(u) * u.Strength; mine.Add(u);
                            foreach (Unit e in map.FirersOf(u.tile)) if (e != null && seenE.Add(e)) theirs += AIScore.Worth(e) * e.Strength;
                        }
                        if (theirs <= ours * Mathf.Max(0f, p.regroupCommitRatio)) continue;
                        float press = theirs - ours;
                        HexTile c = Centre(mine);
                        if (c != null && (pressed == null || press > worst)) { pressed = f; worst = press; rally = c; }
                    }
                if (rally != null) {
                    Front r = new Front { center = rally, objectiveTile = rally, priority = 0.5f };
                    r.campaign = true; r.pullScale = p.campaignPull; r.campaignStagingRadius = p.campaignStagingRadius > 0 ? p.campaignStagingRadius : TypicalMove(map);
                    r.reserve = true;
                    r.assigned.AddRange(calm);
                    foreach (Unit u in calm) taken.Add(u);
                    AIStrategic.Finish(new List<Front> { r }, map, p, memory, assaulting);
                    fronts.Add(r);
                    reserveFront = r;
                }
                reserveNote = "reserve: central " + calm.Count + (rally != null ? (pressed != null ? " committed to the front at (" : " gathering at (") + rally.x + "," + rally.y + ")" : "") + ", active " + active + " (of " + spare.Count + " on no task force)";
            }
            HashSet<Unit> was = AIStrategic.Exclude;
            AIStrategic.Exclude = taken;
            List<Front> ordinary;
            try { ordinary = AIStrategic.BuildFronts(map, p, memory, assaulting); } finally { AIStrategic.Exclude = was; }
            // The fallback: it does not reach past our supply. An objective of theirs outside it becomes the
            // supplied tile nearest it (pulls end at the edge of supply; the campaign's fronts carry it further).
            foreach (Front f in ordinary) {
                if (f.assigned.Count == 0) continue;
                if (f.objectiveTile != null && map.SupplyDepth(f.objectiveTile) <= 0) {
                    Dictionary<HexTile, int> to = map.LandSteps(f.objectiveTile);
                    HexTile edge = null; int ed = int.MaxValue;
                    if (to != null) foreach (KeyValuePair<HexTile, int> kv in to) if (map.SupplyDepth(kv.Key) > 0 && (kv.Value < ed || (kv.Value == ed && CombatMath.Precedes(kv.Key, edge)))) { ed = kv.Value; edge = kv.Key; }
                    if (edge != null) { f.objectiveTile = edge; f.objective = null; f.objectiveIsHQ = false; }
                }
                fronts.Add(f);
            }
            if (ordinary.Count == 0 || !ordinary.Exists(f => f.assigned.Count > 0)) {
                Building homeB = GameController.HQOf(map.player);
                Front home = new Front { center = homeB != null ? homeB.tile : (map.ownUnits.Count > 0 ? map.ownUnits[0].tile : null), priority = 1f };
                foreach (Unit u in map.ownUnits) if (u != null && u.tile != null && !taken.Contains(u)) home.assigned.Add(u);
                if (home.center != null) { fronts.Add(home); AIStrategic.Finish(new List<Front> { home }, map, p, memory, assaulting); }
            }
            return fronts;
        }

        /// <summary>The central reserve this turn (AIProfile.campaignReserve), and a line for the trace.</summary>
        public Front reserveFront;
        public string reserveNote;

        /// <summary>
        /// Where the central reserve gathers: a tile in our supply that no enemy can strike for more than two
        /// turns, the nearest such to the main front (the task force with the most units; else the nearest
        /// enemy to our HQ).
        /// </summary>
        static HexTile RallyTile(AIMap map, List<Front> fronts) {
            Front main = null;
            foreach (Front f in fronts) if (f.campaign && f.objectiveTile != null && (main == null || f.assigned.Count > main.assigned.Count)) main = f;
            HexTile toward = main != null ? (main.campaignStaging ?? main.objectiveTile) : null;
            if (toward == null) {
                Building home = GameController.HQOf(map.player);
                int bd = int.MaxValue;
                foreach (Unit e in map.enemyUnits) if (e != null && e.tile != null && home != null && home.tile != null) { int d = e.tile.Distance(home.tile); if (d < bd) { bd = d; toward = e.tile; } }
            }
            if (toward == null) return null;
            Dictionary<HexTile, int> steps = map.LandSteps(toward);
            if (steps == null) return null;
            HexTile best = null; int bs = int.MaxValue;
            foreach (KeyValuePair<HexTile, int> kv in steps) {
                HexTile t = kv.Key;
                if (map.SupplyDepth(t) <= 0 || map.EnemyStrikeTurns(t) <= 2 || (t.building != null && t.building.owner == map.player && t.building.Purchasable().Count > 0)) continue;
                if (kv.Value < bs || (kv.Value == bs && CombatMath.Precedes(t, best))) { bs = kv.Value; best = t; }
            }
            return best;
        }

        /// <summary>Is the enemy that can strike <paramref name="o"/>'s units next turn more than campaignFallbackRatio of their power?</summary>
        static bool Outmatched(CampaignObjective o, AIMap map, AIProfile p, out float theirs) {
            theirs = 0f;
            if (p.campaignFallbackRatio <= 0f) return false;
            float ours = 0f;
            HashSet<Unit> near = new HashSet<Unit>();
            foreach (Unit u in o.units) {
                if (u == null || u.tile == null) continue;
                ours += Power(u);
                foreach (Unit e in map.enemyUnits)
                    if (e != null && e.tile != null && !near.Contains(e) && CanArrive(e, u.tile, 1, map)) { near.Add(e); theirs += Power(e); }
            }
            return theirs > 0f && ours < theirs * p.campaignFallbackRatio;
        }

        public IEnumerable<string> Describe() {
            if (replanned != null) yield return "campaign planned afresh: " + replanned;
            foreach (CampaignObjective o in objectives) yield return "objective " + o;
        }
    }
}
