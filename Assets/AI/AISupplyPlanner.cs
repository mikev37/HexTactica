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
                if (u == null || u == hubUnit || u.GetComponent<SupplyPoint>() != null) continue;
                army += AIScore.Worth(u);
            }
            return gained / totalHold * army * profile.forwardSupplyWeight;
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
                if (u.GetComponent<SupplyPoint>() != null) continue;
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
        public static float ExactValue(SupplyPoint hub, HexTile at, AIMap map, AIProfile profile) {
            if (hub == null || hub.myUnit == null || at == null) return 0;

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
            float total = 0;
            for (int i = 0; i < map.ownUnits.Count; i++) {
                Unit u = map.ownUnits[i];
                if (u == null || u.tile == null || u == hub.myUnit) continue;
                SupplyPoint otherHub = u.GetComponent<SupplyPoint>();
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

            total += ForwardCoverage(map, profile, hub.myUnit, t => hypothetical.DepthAt(t));
            // Following the army is worth something only from positions that keep the hub
            // connected. Crediting it from anywhere sent the hub out of the network to burn
            // through its six-turn fuse in front of the enemy.
            if (hypothetical.connectedHubs.Contains(hub)) {
                total += IntentCoverage(map, profile, hub.myUnit, t => hypothetical.DepthAt(t));
            }

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
            total -= AIScore.Risk(hub.myUnit, at, map, profile);

            // Restore the real field, since the solve above published onto the board.
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
