using System;
using System.Collections.Generic;
using UnityEngine;

namespace HexTactica.AI {

    /// <summary>A complete plan for getting one unit somewhere, on foot or by transport.</summary>
    public class TransportPlan {
        public Unit cargo;
        public Unit transport;          // null if walking
        public HexTile destination;     // where the cargo ultimately wants to stand
        public HexTile rendezvous;      // transport's tile at the moment of boarding
        public HexTile dropoff;         // transport's tile at the moment of unloading
        public int eta;                 // turns until the cargo is standing on destination
        public int walkEta;             // turns on foot, for comparison
        public float value;             // destination value, before the time penalty
        public float score;             // value after the time penalty
        public bool usesTransport { get { return transport != null; } }

        public override string ToString() {
            return usesTransport
                ? string.Format("ride to {0} via {1} (T+{2}, walk was T+{3})",
                                Name(destination), Name(rendezvous), eta, walkEta)
                : string.Format("walk to {0} (T+{1})", Name(destination), eta);
        }
        static string Name(HexTile t) { return t == null ? "-" : "(" + t.x + "," + t.y + "," + t.z + ")"; }
    }

    /// <summary>
    /// Joint planning over (rendezvous, route, dropoff) for a cargo unit and the
    /// transports available to carry it.
    ///
    /// The naive formulation is a triple loop — for every destination, for every load
    /// tile, for every unload tile — which is O(n^3) and hopeless. The decomposition
    /// used here is:
    ///
    ///   1. Multi-turn field for the cargo walking.                        (one search)
    ///   2. Multi-turn field for the transport driving empty.              (one search)
    ///   3. For each tile r, boarding happens on the first turn both parties can act
    ///      from position: loadTurn(r) = max(ready(cargo beside r), ready(transport on r)),
    ///      where ready(eta) is the turn a unit can first act from a tile — the same turn
    ///      if it started there, otherwise the turn after it arrives. The cargo acts
    ///      before the transport in the turn order, so the transport drives off with it
    ///      in that same turn.
    ///   4. Seed a *multi-source* search with every r at cost loadTurn(r), using the
    ///      transport's movement rules. Because Dijkstra from many sources yields the
    ///      minimum over sources, this collapses the entire rendezvous loop into one
    ///      search rather than one search per candidate.                   (one search)
    ///   5. The transport unloads on the first turn it can act from a neighbour n of the
    ///      destination: rideEta(x) = min over neighbours n of ready(rideField(n)).
    ///   6. Compare walkEta(x) against rideEta(x) per destination and take the better,
    ///      then score value(x) - turnPenalty * eta(x).
    ///
    /// Three searches total, and the answer is jointly optimal in arrival time rather
    /// than being a greedy "walk to the truck, then drive" heuristic. The winning
    /// rendezvous is recovered by walking the ride field's parent chain back to its
    /// seed, which is why MoveField keeps parents for multi-source searches.
    ///
    /// The same machinery serves a supply hub looking for the best place to project
    /// supply from and an infantry unit looking for an assault landing — the caller
    /// supplies the destination value function, so the planner never needs to know
    /// which it is doing.
    /// </summary>
    public static class AITransportPlanner {

        /// <summary>
        /// First turn a unit can act from a tile, given when it gets there. Arriving during
        /// turn k (by moving) means acting from it on k+1; starting the turn there means now.
        /// Timing must mirror execution exactly or ride-versus-walk comparisons are off by one.
        /// </summary>
        static int Ready(Eta e) {
            if (e.IsUnreachable) return int.MaxValue;
            return e.used == 0 ? e.turns : e.turns + 1;
        }

        /// <summary>
        /// Best plan to move <paramref name="cargo"/> somewhere valuable.
        /// <paramref name="valueOf"/> scores a candidate destination tile.
        /// </summary>
        public static TransportPlan Plan(Unit cargo,
                                         AIMap map,
                                         AIProfile profile,
                                         Func<HexTile, float> valueOf,
                                         IEnumerable<HexTile> candidates) {
            return Plan(cargo, map, profile, valueOf, candidates, null);
        }

        /// <param name="reserved">Transports already promised to another cargo this turn.
        /// Two hubs planning around the same one-slot helicopter would otherwise both wait
        /// at their rendezvous for a ride that can only take one of them.</param>
        public static TransportPlan Plan(Unit cargo,
                                         AIMap map,
                                         AIProfile profile,
                                         Func<HexTile, float> valueOf,
                                         IEnumerable<HexTile> candidates,
                                         HashSet<Unit> reserved) {

            if (cargo == null || cargo.tile == null) return null;

            int horizon = profile.planningHorizon;

            // ---- 1. On foot.
            MoveField walk = cargo.trasnporter != null
                ? null                                    // already aboard: no walking option
                : AIPath.MultiTurn(cargo, horizon);

            // ---- 2/3/4. By transport.
            List<Unit> transports = CandidateTransports(cargo, map);
            if (reserved != null && cargo.trasnporter == null) transports.RemoveAll(t => reserved.Contains(t));
            RideSolution bestRide = null;

            for (int i = 0; i < transports.Count; i++) {
                RideSolution ride = BuildRide(cargo, transports[i], walk, horizon, profile);
                if (ride == null) continue;
                if (bestRide == null || ride.IsBetterThan(bestRide)) bestRide = ride;
            }

            // ---- 5/6. Score every candidate destination under both options.
            TransportPlan best = null;

            foreach (HexTile dest in candidates) {
                if (dest == null) continue;
                if (!AIPath.CanFinishTurnOn(cargo, dest)) continue;

                // Negative values are not skipped. Staying put has a value too, and when every
                // option is bad the right move is the least bad one: a hub bleeding at -694
                // per turn one tile outside the network should step back to -229, not sit
                // there because both numbers are below zero. Skipping them returned no plan
                // and the hub bled to death in place.
                float value = valueOf(dest);

                int walkEta = walk != null && walk.Reachable(dest)
                            ? walk.Get(dest).turns
                            : int.MaxValue;

                int rideEta = int.MaxValue;
                HexTile dropoff = null;
                if (bestRide != null) {
                    rideEta = bestRide.EtaTo(dest, out dropoff);
                }

                bool ride = rideEta < int.MaxValue
                         && (walkEta == int.MaxValue
                             || rideEta + profile.transportWorthwhileSaving <= walkEta);

                int eta = ride ? rideEta : walkEta;
                if (eta == int.MaxValue) continue;

                float score = value - profile.turnPenalty * eta * Mathf.Abs(value);

                if (best == null || score > best.score) {
                    best = new TransportPlan {
                        cargo = cargo,
                        destination = dest,
                        value = value,
                        score = score,
                        eta = eta,
                        walkEta = walkEta == int.MaxValue ? -1 : walkEta
                    };
                    if (ride) {
                        best.transport = bestRide.transport;
                        best.dropoff = dropoff;
                        best.rendezvous = bestRide.RendezvousFor(dropoff);
                    }
                }
            }

            return best;
        }

        static List<Unit> CandidateTransports(Unit cargo, AIMap map) {
            List<Unit> result = new List<Unit>();
            if (cargo.trasnporter != null) {
                result.Add(cargo.trasnporter);       // already aboard: that is the only ride
                return result;
            }
            List<Unit> available = Transporting.AvailableFor(cargo.owner, cargo);
            AIMap.SortStable(available);
            return available;
        }

        // ------------------------------------------------------------------ ride

        class RideSolution {
            public Unit transport;
            public Unit cargoUnit;
            public MoveField rideField;                      // multi-source: min arrival over all rendezvous
            public Dictionary<HexTile, HexTile> seedOf;      // ride tile -> rendezvous that reached it
            public int bestLoadTurn = int.MaxValue;

            /// <summary>
            /// Turns until the cargo is standing on <paramref name="dest"/>, having been
            /// dropped off by this transport. Disembarking costs one turn and lands the
            /// cargo on a neighbour of the transport's tile.
            /// </summary>
            public int EtaTo(HexTile dest, out HexTile dropoff) {
                dropoff = null;
                if (dest == null) return int.MaxValue;

                // Unload onto dest from a transport standing on a neighbour. (The transport
                // cannot unload onto its own tile: it is occupying it.)
                int best = int.MaxValue;
                List<HexTile> neighbors = dest.neighbors;
                for (int i = 0; i < neighbors.Count; i++) {
                    HexTile n = neighbors[i];
                    if (n == null || !rideField.Reachable(n)) continue;
                    if (!AIPath.CanFinishTurnOn(transport, n)) continue;
                    if (cargoUnit != null && !LocomotionRules.BridgeStep(n, dest, cargoUnit.locomotion)) continue;   // off a bridge only by road
                    int eta = Ready(rideField.Get(n));
                    if (eta < best || (eta == best && dropoff != null && CombatMath.Precedes(n, dropoff))) {
                        best = eta;
                        dropoff = n;
                    }
                }
                return best;
            }

            public HexTile RendezvousFor(HexTile dropoff) {
                if (dropoff == null) return null;
                HexTile seed;
                if (seedOf.TryGetValue(dropoff, out seed)) return seed;
                // Walk the parent chain back to a seed.
                HexTile cur = dropoff;
                int guard = 0;
                while (cur != null && guard++ < 4096) {
                    HexTile parent = rideField.ParentOf(cur);
                    if (parent == null) return cur;
                    cur = parent;
                }
                return null;
            }

            public bool IsBetterThan(RideSolution other) {
                if (other == null) return true;
                if (bestLoadTurn != other.bestLoadTurn) return bestLoadTurn < other.bestLoadTurn;
                return rideField.Count > other.rideField.Count;
            }
        }

        /// <summary>
        /// Steps 2-4: compute when boarding could happen at every tile, then run one
        /// multi-source search over the transport's movement seeded with those times.
        /// </summary>
        static RideSolution BuildRide(Unit cargo, Unit transport, MoveField cargoWalk, int horizon, AIProfile profile) {
            if (transport == null || transport.tile == null) return null;
            if (!Transporting.IsTransport(transport)) return null;

            bool alreadyAboard = cargo.trasnporter == transport;

            List<KeyValuePair<HexTile, Eta>> seeds = new List<KeyValuePair<HexTile, Eta>>();
            Dictionary<HexTile, HexTile> seedOf = new Dictionary<HexTile, HexTile>();
            int bestLoadTurn = int.MaxValue;

            if (alreadyAboard) {
                // The rendezvous already happened; the ride starts here and now.
                seeds.Add(new KeyValuePair<HexTile, Eta>(transport.tile, Eta.Origin));
                seedOf[transport.tile] = transport.tile;
                bestLoadTurn = 0;
            } else {
                if (cargoWalk == null) return null;
                MoveField drive = AIPath.MultiTurn(transport, horizon);

                foreach (HexTile r in drive.Tiles) {
                    if (!AIPath.CanFinishTurnOn(transport, r)) continue;

                    int transportReady = Ready(drive.Get(r));

                    // The cargo boards from a neighbour of r.
                    int cargoReady = int.MaxValue;
                    List<HexTile> neighbors = r.neighbors;
                    for (int i = 0; i < neighbors.Count; i++) {
                        HexTile n = neighbors[i];
                        if (n == null || !cargoWalk.Reachable(n)) continue;
                        if (n != cargo.tile && !AIPath.CanFinishTurnOn(cargo, n)) continue;
                        if (!Transporting.CanBoardOnto(cargo, n, r, transport)) continue;   // able to step aboard there (or at a dock)
                        cargoReady = Mathf.Min(cargoReady, Ready(cargoWalk.Get(n)));
                    }
                    if (cargoReady == int.MaxValue) continue;

                    int loadTurn = Mathf.Max(transportReady, cargoReady);
                    if (loadTurn > horizon) continue;

                    seeds.Add(new KeyValuePair<HexTile, Eta>(
                        r, new Eta { turns = loadTurn, used = 0, steps = 0 }));
                    seedOf[r] = r;
                    if (loadTurn < bestLoadTurn) bestLoadTurn = loadTurn;
                }
            }

            if (seeds.Count == 0) return null;

            // One multi-source search stands in for one search per rendezvous.
            MoveField rideField = AIPath.Compute(transport, seeds, horizon, false);

            return new RideSolution {
                transport = transport,
                cargoUnit = cargo,
                rideField = rideField,
                seedOf = seedOf,
                bestLoadTurn = bestLoadTurn
            };
        }

        // -------------------------------------------------------------- execution

        /// <summary>
        /// Turn a multi-turn plan into the single legal action to take right now.
        /// This is where a plan becomes a move.
        /// </summary>
        public static AIAction NextAction(TransportPlan plan, AIMap map, AIProfile profile) {
            if (plan == null || plan.cargo == null) return AIAction.Wait(null, "no plan");

            Unit cargo = plan.cargo;

            // --- Walking plan: just step along it.
            if (!plan.usesTransport) {
                if (cargo.trasnporter != null) {
                    // Aboard but the plan says walk: get off.
                    List<HexTile> options = Transporting.UnloadOptions(cargo.trasnporter, cargo);
                    HexTile best = PickClosest(options, plan.destination);
                    if (best != null) {
                        return AIAction.Disembark(cargo.trasnporter, cargo, best, "disembark to walk");
                    }
                    return AIAction.Wait(cargo, "aboard, nowhere to disembark");
                }
                MoveField field = map.ReachHorizon(cargo);
                HexTile step = field.StepToward(plan.destination);
                if (step == null || step == cargo.tile) return AIAction.Wait(cargo, "already positioned");
                return AIAction.Move(cargo, step, plan.score, "walk: " + plan.ToString());
            }

            Unit transport = plan.transport;

            // --- Aboard already: the transport drives and disembarks in its own slot.
            if (cargo.trasnporter == transport) {
                return AIAction.Wait(cargo, "riding: " + plan.ToString());
            }

            // --- Not aboard yet: converge on the rendezvous, and board when adjacent.
            if (Transporting.CanBoardFrom(transport, cargo)) {
                return AIAction.Board(cargo, transport, transport.tile, "board: " + plan.ToString());
            }

            // In reach of a tile beside the transport this turn: walk there and board (as a
            // player can: move next to it, then get in).
            if (Transporting.CanCarry(transport, cargo) && transport.tile != null) {
                MoveField now = map.ReachThisTurn(cargo);
                HexTile beside = null;
                foreach (HexTile n in transport.tile.neighbors) {
                    if (n == null || n.occupier != null || !now.ReachableThisTurn(n)) continue;
                    if (!Transporting.CanBoardOnto(cargo, n, transport.tile, transport)) continue;
                    if (beside == null || CombatMath.Precedes(n, beside)) beside = n;
                }
                if (beside != null) return AIAction.Board(cargo, transport, beside, "walk and board: " + plan.ToString());
            }

            MoveField cargoField = map.ReachHorizon(cargo);
            HexTile toward = cargoField.StepToward(plan.rendezvous);
            if (toward != null && toward != cargo.tile) {
                return AIAction.Move(cargo, toward, plan.score, "to rendezvous: " + plan.ToString());
            }

            // Cargo is in position; the transport is the one that has to close.
            return AIAction.Wait(cargo, "waiting for transport at rendezvous");
        }

        /// <summary>
        /// The transport's own action, given that it has agreed to carry someone.
        /// Called when the transport's turn comes up in the ordering — after its
        /// cargo, so the destination is already settled.
        /// </summary>
        public static AIAction TransportAction(Unit transport, TransportPlan plan, AIMap map) {
            if (transport == null || plan == null || plan.cargo == null) return AIAction.Wait(transport, "no assignment");

            Unit cargo = plan.cargo;
            bool aboard = cargo.trasnporter == transport;

            if (aboard && transport.tile == plan.dropoff) {
                // Arrived: put the cargo on the destination, or the nearest free tile to it.
                HexTile onto = plan.destination;
                if (onto == null || onto.occupier != null || !cargo.CanEnter(onto) ||
                    !(transport.tile.neighbors.Contains(onto) || onto == transport.tile)) {
                    onto = PickClosest(Transporting.UnloadOptions(transport, cargo), plan.destination);
                }
                if (onto != null) return AIAction.Disembark(transport, cargo, onto, "drop off: " + plan.ToString());
                return AIAction.Wait(transport, "dropoff blocked");
            }

            HexTile goal = aboard ? plan.dropoff : plan.rendezvous;
            if (goal == null) return AIAction.Wait(transport, "no goal");
            if (transport.tile == goal) return AIAction.Wait(transport, aboard ? "in position" : "waiting at rendezvous");

            MoveField field = map.ReachHorizon(transport);
            HexTile step = SafeStepToward(transport, goal, field, map, aboard);
            if (step == null || step == transport.tile) return AIAction.Wait(transport, "holding: route unsafe or blocked");
            return AIAction.Move(transport, step, plan.score, (aboard ? "carry: " : "to rendezvous: ") + plan.ToString());
        }

        /// <summary>
        /// The furthest tile along the route the transport can stop on tonight without
        /// expecting to be destroyed. A transport carries no guns of its own and, loaded, it
        /// carries its cargo's fate too, so the bar is raised by loadedTransportRisk. (It used
        /// to take the furthest reachable tile regardless, and would park a loaded helicopter
        /// wherever the route happened to end that turn.)
        /// </summary>
        static HexTile SafeStepToward(Unit transport, HexTile goal, MoveField field, AIMap map, bool loaded) {
            List<HexTile> path = field.PathTo(goal);
            float caution = loaded ? Mathf.Max(1f, map.profile.loadedTransportRisk) : 1f;
            HexTile best = null;
            for (int i = 0; i < path.Count; i++) {
                HexTile t = path[i];
                if (!field.ReachableThisTurn(t)) break;
                if (!AIPath.CanFinishTurnOn(transport, t)) continue;
                if (map.ExpectedLoss(transport, t) * caution >= transport.Strength) continue;
                best = t;
            }
            return best;
        }

        static HexTile PickClosest(List<HexTile> options, HexTile to) {
            HexTile best = null;
            int bestDistance = int.MaxValue;
            for (int i = 0; i < options.Count; i++) {
                HexTile t = options[i];
                if (t == null) continue;
                int d = to != null ? t.Distance(to) : 0;
                if (d < bestDistance || (d == bestDistance && best != null && CombatMath.Precedes(t, best))) {
                    best = t;
                    bestDistance = d;
                }
            }
            return best;
        }
    }
}
