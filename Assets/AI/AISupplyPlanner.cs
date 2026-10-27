using System;
using System.Collections.Generic;
using UnityEngine;

namespace HexTactica.AI {

    /// <summary>
    /// Decides where supply hubs should stand.
    ///
    /// This is the layer the old AIPlayer had no concept of. A hub with AttackRange 0
    /// scored zero attack value on every tile, tied every tiebreak, fell through the
    /// multi-turn fallback, and ended up at objective.evaluateTiles — which with the
    /// default GoTo meant every supply truck in the game drove toward hex (0,0,0).
    ///
    /// The question a hub is actually answering is not "what can I shoot" but "how
    /// much does my army's sustainment improve if I stand here", which is a different
    /// objective needing a different evaluator. Because a full solve per candidate is
    /// O(tiles) each, candidates are ranked by a cheap proxy first and only the top
    /// few get the real re-solve.
    /// </summary>
    public static class AISupplyPlanner {

        /// <summary>
        /// Value of supplying the contested ground the army wants to enter, in gold per
        /// turn. Without it the line and the supply deadlock: line units will not step past
        /// the supply edge, and a hub that only values units already stranded has no reason
        /// to come forward while they all wait at the edge.
        ///
        /// Covering all contested ground is worth one strength of every unit in the army,
        /// times forwardSupplyWeight; <paramref name="suppliedAfter"/> says whether a tile is
        /// supplied in the scenario being scored.
        /// </summary>
        static float ForwardCoverage(AIMap map, AIProfile profile, Unit hubUnit, Func<HexTile, int> depthAfter) {
            float totalHold = 0f, gained = 0f;
            foreach (HexTile t in map.ContestedTiles) {
                float h = AIScore.Hold(map.Conflict(t));
                totalHold += h;
                // Depth, not a yes/no: supply that nominally reaches the contested ground at
                // depth 1 lets nothing advance with any margin, and deepening it is the point.
                gained += (AIScore.Depth(depthAfter(t), profile) - AIScore.Depth(map.SupplyDepth(t), profile)) * h;
            }
            if (totalHold <= 0f) return 0f;

            float army = 0f;
            for (int i = 0; i < map.ownUnits.Count; i++) {
                Unit u = map.ownUnits[i];
                if (u == null || u == hubUnit || u.Hub != null) continue;
                army += AIScore.Worth(u);
            }
            return gained / totalHold * army * profile.forwardSupplyWeight;
        }

        /// <summary>
        /// Value of holding ground (groundSupplyWeight), in gold: each tile of land supplied with the
        /// hub at the place being scored and not without it, and not supplied by the enemy, at that
        /// fraction of a strength point of an average fighting unit. A human spreads supply to claim
        /// the map; without this a supply unit only went where the army already was.
        /// </summary>
        static float GroundCoverage(AIMap map, AIProfile profile, HexTile at, int reach, Func<HexTile, int> before, Func<HexTile, int> after) {
            if (profile.groundSupplyWeight <= 0f || at == null) return 0f;
            float worth = 0f; int n = 0;
            for (int i = 0; i < map.ownUnits.Count; i++) {
                Unit u = map.ownUnits[i];
                if (u == null || u.Hub != null || u.Firepower <= 0) continue;
                worth += AIScore.Worth(u); n++;
            }
            if (n == 0) return 0f;
            int gained = 0;
            foreach (HexTile t in AIPath.TilesInRangeShared(at, 0, reach)) {
                if (t == null || t.terrain == null || t.terrain.elevation < 0) continue;
                if (before(t) > 0 || after(t) <= 0 || map.EnemySupplyShare(t) > 0f) continue;
                gained++;
            }
            return gained * worth / n * profile.groundSupplyWeight;
        }

        /// <summary>
        /// Value of bringing the network closer to where friendly units are heading, in gold
        /// per turn: for each unit with a destination outside supply, a strength's worth per
        /// tile the gap closes, scaled by intentSupplyWeight and capped at the gap.
        ///
        /// Without this a hub only valued units already stranded and contested ground. With the
        /// armies apart there is little of either, so the hub stayed home, the supply edge stayed
        /// put, and every unit that advanced had to turn back before it starved.
        /// </summary>
        static float IntentCoverage(AIMap map, AIProfile profile, Unit hubUnit, System.Func<HexTile, int> depthAfter) {
            if (map.intents == null) return 0f;
            const int cap = 12;
            float total = 0f;
            foreach (KeyValuePair<Unit, HexTile> kv in map.intents) {
                Unit u = kv.Key;
                HexTile dest = kv.Value;
                if (u == null || dest == null || u == hubUnit || u.Strength <= 0 || u.owner != map.player) continue;
                if (u.Hub != null) continue;
                int before = AIMap.GapToSupply(dest, map.SupplyDepth, cap);
                if (before == 0) continue;
                int after = AIMap.GapToSupply(dest, depthAfter, cap);
                total += (before - after) / (float)before * AIScore.Worth(u) * profile.intentSupplyWeight;
            }
            return total;
        }

        /// <summary>
        /// Cheap estimate of how useful a hub would be at <paramref name="at"/>:
        /// how many needy friendly units fall inside its radius, weighted by need.
        /// Used only to shortlist candidates for the expensive exact evaluation.
        /// </summary>
        public static float ProxyValue(SupplyPoint hub, HexTile at, AIMap map) {
            return ProxyValue(hub, at, map, SupplyNetwork.SolveWithout(map.player, hub));
        }

        /// <param name="without">The network with this hub removed (SupplyNetwork.SolveWithout).</param>
        public static float ProxyValue(SupplyPoint hub, HexTile at, AIMap map, SupplyNetwork.Result without) {
            if (hub == null || hub.myUnit == null || at == null) return 0;

            // Same quantity as ExactValue — gold per turn — estimated with hex distance
            // instead of a full re-solve. It must agree with ExactValue on what matters
            // most, or the shortlist throws away the right answer before it is scored: an
            // earlier version ignored connection, so its top candidates were all tiles
            // beside the army and outside the network, and the connected relay position
            // never got an exact evaluation.
            float score = 0;
            for (int i = 0; i < map.ownUnits.Count; i++) {
                Unit u = map.ownUnits[i];
                if (u == null || u.tile == null || u == hub.myUnit) continue;
                if (without.DepthAt(u.tile) > 0) continue;            // supplied even without this hub
                if (at.Distance(u.tile) >= hub.range) continue;      // depth would be 0
                float need = 1f + (u.OutOfSupplies ? 2f : 0f) + (u.OutOfAmmo ? 1f : 0f)
                           + (u.OutOfFuel ? 1f : 0f) + (6 - Mathf.Clamp(u.Strength, 0, 6)) / 6f;
                score += AIScore.Worth(u) * need;
            }

            // A relay is connected if it stands where the network reaches *without it*.
            bool connected = without.DepthAt(at) > 0;
            if (!connected) score -= AIScore.Worth(hub.myUnit);

            // Same forward-coverage term ExactValue uses, by hex distance.
            int range = hub.range;
            score += ForwardCoverage(map, map.profile, hub.myUnit,
                                     t => Mathf.Max(without.DepthAt(t), range - at.Distance(t)));
            if (connected) {
                score += IntentCoverage(map, map.profile, hub.myUnit,
                                        t => Mathf.Max(without.DepthAt(t), range - at.Distance(t)));
                score += GroundCoverage(map, map.profile, at, range, t => without.DepthAt(t), t => Mathf.Max(without.DepthAt(t), range - at.Distance(t)));
            }

            score -= AIScore.Risk(hub.myUnit, at, map, map.profile);
            return score;
        }

        /// <summary>
        /// Exact value of putting <paramref name="hub"/> on <paramref name="at"/>:
        /// re-solve the whole network with the hub hypothetically moved and measure
        /// the army-wide change in sustainment. Pure — applyDrain is false, so asking
        /// the question costs nobody any health.
        /// </summary>
        /// <summary>Diagnostic: the shortlist's terms, best first, each time a hub plans.</summary>
        public static System.Action<string> DebugLog;
        /// <summary>True while planning a ghost move (logged as such).</summary>
        public static bool Ghost;
        static readonly Dictionary<HexTile, string> breakdown = new Dictionary<HexTile, string>();

        public static float ExactValue(SupplyPoint hub, HexTile at, AIMap map, AIProfile profile) {
            if (hub == null || hub.myUnit == null || at == null) return 0;
            float dSupplied = 0f, dForward = 0f, dIntent = 0f, dConnect = 0f, dRisk = 0f;

            SupplyNetwork.Result hypothetical =
                SupplyNetwork.Solve(map.player, false, hub, at);

            // Everything below is gold per turn, so gains and costs are comparable.
            //
            // Supplying a unit is worth roughly one strength per turn, since that is what
            // attrition takes once its own supplies run out,
            // and what resupply heals. Urgency scales it: a unit already out of supplies,
            // ammo or fuel needs it now. (An earlier version priced this as a change in a
            // 0..1 desirability curve times the unit's *entire* value, and compared that
            // with a hub's drain of a sixth of its value per turn — two different units.
            // It sent hubs out of the network to die beside a few cheap squads.)
            if (profile.hubPricing == 1) return ValueAdded(hub, at, map, profile, hypothetical);

            float total = 0;
            for (int i = 0; i < map.ownUnits.Count; i++) {
                Unit u = map.ownUnits[i];
                if (u == null || u.tile == null || u == hub.myUnit) continue;
                SupplyPoint otherHub = u.Hub;
                if (otherHub != null && otherHub.producer) continue;

                int before = map.SupplyDepth(u.tile);
                int after = hypothetical.DepthAt(u.tile);
                float perTurn = AIScore.Worth(u);              // one strength

                if ((before > 0) != (after > 0)) {
                    float need = 1f
                               + (u.OutOfSupplies ? 2f : 0f)
                               + (u.OutOfAmmo ? 1f : 0f)
                               + (u.OutOfFuel ? 1f : 0f)
                               + (6 - Mathf.Clamp(u.Strength, 0, 6)) / 6f;
                    total += (after > 0 ? 1f : -1f) * perTurn * need;
                }

                // Depth inside supply is a margin against falling out of it as the line moves.
                total += (AIScore.Depth(after, profile) - AIScore.Depth(before, profile)) * perTurn * 0.25f;

                // Moving this hub can also strand or reconnect another relay downstream.
                if (otherHub != null) {
                    bool wasConnected = map.supply.connectedHubs.Contains(otherHub);
                    bool isConnected = hypothetical.connectedHubs.Contains(otherHub);
                    if (wasConnected != isConnected) total += (isConnected ? 1f : -1f) * perTurn;
                }
            }

            dSupplied = total;
            dForward = ForwardCoverage(map, profile, hub.myUnit, t => hypothetical.DepthAt(t));
            total += dForward;
            // Following the army is worth something only from positions that keep the hub
            // connected. Crediting it from anywhere sent the hub out of the network to burn
            // through its six-turn fuse in front of the enemy.
            if (hypothetical.connectedHubs.Contains(hub)) {
                dIntent = IntentCoverage(map, profile, hub.myUnit, t => hypothetical.DepthAt(t));
                total += dIntent;
            }
            float beforeConnect = total;

            // Connection, priced as what it actually saves: a stranded hub pays a strength
            // per turn, and that hurts more as it nears burning out.
            // Being connected is worth exactly the absence of that cost. An earlier version
            // also *added* half the hub's worth for being connected, which double-counted
            // the same thing and pinned every hub inside HQ range even when it was
            // sustaining nobody there.
            float hubWorth = AIScore.Worth(hub.myUnit);
            bool connectedAfter = hypothetical.connectedHubs.Contains(hub);
            if (!connectedAfter) {
                int autonomy = Mathf.Max(1, hub.TurnsOfAutonomy);
                total -= hubWorth;
                if (autonomy <= profile.hubPanicThreshold) {
                    total -= hubWorth * (profile.hubPanicThreshold - autonomy + 1) * 0.5f;
                }
            }

            // And it gets shot at like anything else. This term was missing entirely: the
            // planner would park the most expensive mobile unit in the army anywhere that
            // improved coverage.
            dConnect = total - beforeConnect;
            // Risk where it will actually stand. The fire field was computed with the hub on its old
            // tile — which, more than once, was the tile blocking the enemy tank's way to the new
            // one: the planner saw no danger, the hub stepped forward, and the path it had been
            // blocking opened onto itself. So the enemy's reply is simulated with the hub moved,
            // and a real opponent's choice of target (the most valuable thing in reach, which a
            // 25,000-gold truck usually is) replaces the share-of-fire average when it is worse.
            float simulated = 0f;
            AIWorld.Outcome reply = AIWorld.Evaluate(hub.myUnit, at, new AttackAppraisal(), map, map.replyCache, false);
            reply.lost.TryGetValue(hub.myUnit, out simulated);
            dRisk = Mathf.Max(map.ExpectedLoss(hub.myUnit, at), simulated) * AIScore.Worth(hub.myUnit) * profile.riskWeight;
            total -= dRisk;
            if (DebugLog != null)
                breakdown[at] = string.Format("({0},{1}) total {2:0} = supplied {3:0} + forward {4:0} + intent {5:0} + connection {6:0} - risk {7:0} (expected loss {8:0.0}, simulated {9:0.0})",
                    at.x, at.y, total, dSupplied, dForward, dIntent, dConnect, dRisk, map.ExpectedLoss(hub.myUnit, at), simulated);

            // Restore the real field, since the solve above published onto the board.
            SupplyNetwork.Solve(map.player, false);

            return total;
        }

        /// <summary>
        /// Value-added pricing. For every unit supplied only because the supply unit stands on
        /// <paramref name="at"/> (in supply with it there, out of supply without it):
        ///   a tenth of the unit's cost for the resupply (ammo, fuel, supplies);
        ///   a sixth of its cost — one strength, exactly what the game heals — if it is wounded;
        ///   the attrition it is spared: a strength if it is dry, a quarter of one if its own
        ///   supplies are running down, and a strength if it is out of ammunition and so can't fight.
        /// Relays it connects or strands count a strength each. Minus a strength of its own if it
        /// would be stranded (that is what a stranded hub pays), and minus its own loss in the
        /// simulated enemy reply, in full.
        /// </summary>
        static float ValueAdded(SupplyPoint hub, HexTile at, AIMap map, AIProfile profile, SupplyNetwork.Result hypothetical) {
            SupplyNetwork.Result without = SupplyNetwork.SolveWithout(map.player, hub);
            float resupply = 0f, heal = 0f, spared = 0f, relays = 0f, forward = 0f, intent = 0f, ground = 0f, stranded = 0f;
            int supplied = 0;
            for (int i = 0; i < map.ownUnits.Count; i++) {
                Unit u = map.ownUnits[i];
                if (u == null || u.tile == null || u == hub.myUnit) continue;
                SupplyPoint other = u.Hub;
                if (other != null && other.producer) continue;
                float worth = AIScore.Worth(u);           // one strength = a sixth of the unit
                if (other != null) {
                    bool was = without.connectedHubs.Contains(other), now = hypothetical.connectedHubs.Contains(other);
                    if (was != now) relays += (now ? 1f : -1f) * worth;
                    continue;
                }
                if (without.DepthAt(u.tile) > 0 || hypothetical.DepthAt(u.tile) <= 0) continue;
                supplied++;
                resupply += u.cost / 10f;
                if (u.Strength < 6) heal += worth;
                if (u.suppliesMax > 0) spared += u.supplies <= 1 ? worth : worth * 0.25f;
                if (u.ammoMax > 0 && u.ammo <= 0) spared += worth;
            }
            // Supply units this move would cut off that are connected where it stands now
            // (hubLinkPenalty): each costs that much of its whole worth. Pricing a relay at one
            // strength let the root of the network step aside for a little forward coverage and
            // strand everything beyond it (on ChanceMeeting a truck put the root down one tile off
            // supplied ground and cut off five of seven), and when the enemy cut the network a
            // supply unit gained little from reconnecting the ones beyond it.
            float cutLinks = 0f;
            if (profile.hubLinkPenalty > 0f && hub.myUnit.tile != null && at != hub.myUnit.tile) {
                SupplyNetwork.Result here = SupplyNetwork.Solve(map.player, false);
                foreach (SupplyPoint other in map.ownHubs) {
                    if (other == null || other == hub || other.producer || other.myUnit == null) continue;
                    bool now = here.connectedHubs.Contains(other), then = hypothetical.connectedHubs.Contains(other);
                    // ...and one it would connect again earns as much.
                    if (now != then) cutLinks += (now ? 1f : -1f) * AIScore.Worth(other.myUnit) * 6f * profile.hubLinkPenalty;
                }
            }
            float total = resupply + heal + spared + relays - cutLinks;
            if (profile.hubPricingForward) {
                forward = ForwardCoverage(map, profile, hub.myUnit, t => hypothetical.DepthAt(t));
                if (hypothetical.connectedHubs.Contains(hub)) {
                    intent = IntentCoverage(map, profile, hub.myUnit, t => hypothetical.DepthAt(t));
                    ground = GroundCoverage(map, profile, at, hub.range, t => without.DepthAt(t), t => hypothetical.DepthAt(t));
                }
                total += forward + intent + ground;
            }
            float hubWorth = AIScore.Worth(hub.myUnit);
            if (!hypothetical.connectedHubs.Contains(hub)) {
                stranded = hubWorth;
                int autonomy = Mathf.Max(1, hub.TurnsOfAutonomy);
                if (autonomy <= profile.hubPanicThreshold) stranded += hubWorth * (profile.hubPanicThreshold - autonomy + 1) * 0.5f;
                total -= stranded;
            }
            float simulated = 0f;
            AIWorld.Outcome reply = AIWorld.Evaluate(hub.myUnit, at, new AttackAppraisal(), map, map.replyCache, false);
            reply.lost.TryGetValue(hub.myUnit, out simulated);
            float risk = simulated * hubWorth;
            total -= risk;
            if (DebugLog != null)
                breakdown[at] = string.Format("({0},{1}) total {2:0} = resupply {3:0} + heal {4:0} + spared {5:0} ({6} units) + relays {7:0} + forward {8:0} + intent {9:0} + ground {14:0} - stranded {10:0} - hit {11:0} ({12:0.0} strength) [{13}]",
                    at.x, at.y, total, resupply, heal, spared, supplied, relays, forward, intent, stranded, risk, simulated, map.TileRead(at), ground);
            SupplyNetwork.Solve(map.player, false);
            return total;
        }

        /// <summary>
        /// Full plan for one hub: shortlist candidates cheaply, evaluate the best few
        /// exactly, and hand the winner to the transport planner so that riding a truck
        /// or a helicopter is considered alongside walking.
        /// </summary>
        public static TransportPlan PlanHub(SupplyPoint hub, AIMap map, AIProfile profile) {
            return PlanHub(hub, map, profile, null);
        }

        /// <param name="reserved">Transports already promised to another cargo this turn.</param>
        public static TransportPlan PlanHub(SupplyPoint hub, AIMap map, AIProfile profile, HashSet<Unit> reserved) {
            if (hub == null || hub.myUnit == null) return null;
            // Producers are the roots of the network (the shipped HQ is one). They do not reposition.
            if (hub.producer) return null;
            Unit unit = hub.myUnit;

            MoveField reach = map.ReachHorizon(unit);

            // Shortlist by proxy.
            List<HexTile> candidates = new List<HexTile>();
            foreach (HexTile t in reach.Tiles) {
                if (!AIPath.CanFinishTurnOn(unit, t)) continue;
                candidates.Add(t);
            }
            if (unit.tile != null && !candidates.Contains(unit.tile)) candidates.Add(unit.tile);
            if (candidates.Count == 0) return null;

            SupplyNetwork.Result without = SupplyNetwork.SolveWithout(map.player, hub);
            Dictionary<HexTile, float> proxy = new Dictionary<HexTile, float>();
            for (int i = 0; i < candidates.Count; i++) {
                proxy[candidates[i]] = ProxyValue(hub, candidates[i], map, without);
            }

            candidates.Sort((a, b) => {
                int c = proxy[b].CompareTo(proxy[a]);
                if (c != 0) return c;
                return CombatMath.CompareTiles(a, b);
            });

            int keep = Mathf.Min(profile.supplyCandidateCount, candidates.Count);
            List<HexTile> shortlist = candidates.GetRange(0, keep);
            if (unit.tile != null && !shortlist.Contains(unit.tile)) shortlist.Add(unit.tile);

            // Exact evaluation of the shortlist only.
            Dictionary<HexTile, float> exact = new Dictionary<HexTile, float>();
            for (int i = 0; i < shortlist.Count; i++) {
                exact[shortlist[i]] = ExactValue(hub, shortlist[i], map, profile);
            }

            if (DebugLog != null) {
                List<HexTile> order = new List<HexTile>(exact.Keys);
                order.Sort((a, b) => exact[b].CompareTo(exact[a]));
                DebugLog((Ghost ? "[ghost plan] " : "") + unit.owner.personal_name + " " + unit.name + " at (" + unit.tile.x + "," + unit.tile.y + ") plans, top of " + shortlist.Count + ":");
                for (int i = 0; i < Mathf.Min(5, order.Count); i++) {
                    string b;
                    if (breakdown.TryGetValue(order[i], out b)) DebugLog("   " + b);
                }
                breakdown.Clear();
            }

            // Tonight's stop, too. A destination two turns away was priced where it ends up, and the
            // walk there stopped overnight wherever it got to — a truck heading for a safe tile in
            // the rear stopped beside the enemy grunts on the way. (Line units had the same bug and
            // the same fix in AITactical.Choose.)
            // (Only when walking: with a transport free, the ride decides where it spends the night.)
            bool rideAvailable = false;
            for (int i = 0; i < map.ownUnits.Count; i++) {
                Unit tr = map.ownUnits[i];
                if (tr != null && tr != unit && Transporting.IsTransport(tr) && (reserved == null || !reserved.Contains(tr))) rideAvailable = true;
            }
            foreach (HexTile t in shortlist) {
                if (rideAvailable) break;
                if (unit.tile == null || t == unit.tile || !exact.ContainsKey(t)) continue;
                Eta eta = reach.Get(t);
                if (eta.ThisTurn) continue;
                HexTile stop = reach.StepToward(t);
                if (stop == null || stop == unit.tile) continue;
                float lost = 0f;
                AIWorld.Outcome reply = AIWorld.Evaluate(unit, stop, new AttackAppraisal(), map, map.replyCache, false);
                reply.lost.TryGetValue(unit, out lost);
                lost = Mathf.Max(lost, map.ExpectedLoss(unit, stop));
                if (lost > 0f) {
                    exact[t] -= lost * AIScore.Worth(unit) * profile.riskWeight;
                    if (DebugLog != null) DebugLog(string.Format("   ({0},{1}) stops tonight at ({2},{3}): -{4:0} for {5:0.0} strength there", t.x, t.y, stop.x, stop.y, lost * AIScore.Worth(unit), lost));
                }
            }

            // Standing still has to beat moving by a margin, or hubs jitter.
            if (unit.tile != null && exact.ContainsKey(unit.tile)) {
                exact[unit.tile] += AIScore.StayBonus(unit, 0.02f);
            }

            Func<HexTile, float> valueOf = t => {
                float v;
                return exact.TryGetValue(t, out v) ? v : 0f;
            };

            return AITransportPlanner.Plan(unit, map, profile, valueOf, shortlist, reserved);
        }

        /// <summary>
        /// Tiles that would restore a stranded hub's connection to HQ, for the case
        /// where the AI needs to prioritise reconnection over coverage.
        /// </summary>
        public static List<HexTile> ReconnectionTiles(SupplyPoint hub, AIMap map) {
            List<HexTile> result = new List<HexTile>();
            if (hub == null || hub.myUnit == null) return result;

            MoveField reach = map.ReachHorizon(hub.myUnit);
            foreach (HexTile t in reach.Tiles) {
                if (!AIPath.CanFinishTurnOn(hub.myUnit, t)) continue;
                if (map.SupplyDepth(t) > 0) result.Add(t);
            }
            result.Sort((a, b) => {
                int da = reach.Get(a).turns, db = reach.Get(b).turns;
                if (da != db) return da.CompareTo(db);
                return CombatMath.CompareTiles(a, b);
            });
            return result;
        }
    }
}
