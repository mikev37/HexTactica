using System.Collections.Generic;
using UnityEngine;

namespace HexTactica.AI {

    /// <summary>
    /// Riding to the assault (AIProfile.assaultRides), as a player uses transports: fighting units ride in a transport
    /// and stay aboard while it moves up with the army, out of the enemy's reach; they are put down only where they
    /// can strike this turn (an enemy they can hit, a building they can take, from where they land: getting off costs
    /// nothing, so they still move and fight) and when the wave goes in (their front attacking, or as many of ours
    /// there as of theirs); or when the transport would otherwise be destroyed with them aboard. Empty transports go
    /// back for more: to infantry that is far from the fight, else to a building that makes it.
    /// </summary>
    public static class AIAssault {
        /// <summary>Set by the AI for the decision at hand: the riders belong to a front (its posture decides the wave; else local numbers).</summary>
        [System.ThreadStatic] public static bool HasFront;
        /// <summary>Harness tracing.</summary>
        public static System.Action<string> Debug;

        /// <summary>The fighting units aboard <paramref name="t"/> (none if it carries a supply unit: supply decides for it then).</summary>
        public static List<Unit> Riders(Unit t) {
            List<Unit> r = new List<Unit>();
            if (t == null || t.transported == null) return r;
            foreach (Unit c in t.transported) {
                if (c == null) continue;
                if (c.Hub != null) return new List<Unit>();
                if (c.Firepower > 0 || Building.CanCapture(c)) r.Add(c);
            }
            return r;
        }

        /// <summary>Can <paramref name="c"/>, put down on <paramref name="n"/> with its whole turn ahead, strike this turn: hit an enemy it can hurt, or reach a building to take?</summary>
        public static bool Strikes(Unit c, HexTile n, AIMap map, out string what) {
            what = null;
            // Moving and firing in one turn: as far as it can walk from there and fire; otherwise only what it can hit from where it lands.
            MoveField walk = c.moveAndAttack ? AIPath.ThisTurnFrom(c, n, c.moveRange) : null;
            foreach (Unit e in map.enemyUnits) {
                if (e == null || e.tile == null || e.Strength <= 0) continue;
                if (!c.CanTarget(e) || Unit.calculateDamage(c, e, e.tile, 0) <= 0) continue;
                bool inRange = false;
                int d0 = n.Distance(e.tile);
                if (d0 <= c.AttackRange && d0 > c.MinRange) inRange = true;
                else if (walk != null)
                    foreach (HexTile f in walk.Tiles) {
                        if (f != n && (!walk.ReachableThisTurn(f) || (f.occupier != null && f.occupier != c))) continue;
                        int d = f.Distance(e.tile);
                        if (d <= c.AttackRange && d > c.MinRange) { inRange = true; break; }
                    }
                if (!inRange) continue;
                what = "hit " + e.name.Replace("(Clone)", "") + " (" + e.tile.x + "," + e.tile.y + ")";
                if (walk != null) AIPath.Release(walk);
                return true;
            }
            if (Building.CanCapture(c))
                foreach (Building b in Building.all) {
                    if (b == null || b.tile == null || b.owner == c.owner || !b.Capturable) continue;
                    if (n != b.tile && (walk == null ? n.Distance(b.tile) > 0 : !walk.ReachableThisTurn(b.tile))) {
                        if (walk == null) { MoveField w2 = AIPath.ThisTurnFrom(c, n, c.moveRange); bool ok = w2.ReachableThisTurn(b.tile); AIPath.Release(w2); if (!ok) continue; }
                        else continue;
                    }
                    what = "take " + b.name.Replace("(Clone)", "") + " (" + b.tile.x + "," + b.tile.y + ")";
                    if (walk != null) AIPath.Release(walk);
                    return true;
                }
            if (walk != null) AIPath.Release(walk);
            return false;
        }

        /// <summary>Does the wave go in at <paramref name="at"/>: the front attacking (if they have one), else as many of ours (with <paramref name="landing"/> units landing) as theirs within three tiles?</summary>
        public static bool WaveGoesIn(HexTile at, int landing, bool frontAttacks, AIMap map, AIProfile profile) {
            if (frontAttacks) return true;
            if (HasFront) return false;   // a front holding its line: the riders wait for its assault
            int ours = landing, theirs = 0;
            foreach (Unit u in map.ownUnits) if (u != null && u.tile != null && u.trasnporter == null && u.Firepower > 0 && u.Hub == null && u.tile.Distance(at) <= 3) ours++;
            foreach (Unit e in map.enemyUnits) if (e != null && e.tile != null && e.Firepower > 0 && e.tile.Distance(at) <= 3) theirs++;
            return ours >= Mathf.CeilToInt(theirs * Mathf.Max(0f, profile.assaultWaveRatio));
        }

        /// <summary>
        /// The transport's move with fighting units aboard: drive where riders can be put down to strike (the most of
        /// them, then the safest stop) and put the first down; else move up toward <paramref name="goal"/>, as far as is
        /// safe tonight, riders aboard. Null: not for this (no riders).
        /// </summary>
        public static AIAction? TransportAction(Unit t, HexTile goal, bool frontAttacks, AIMap map, AIProfile profile) {
            List<Unit> riders = Riders(t);
            if (riders.Count == 0 || t.tile == null) return null;
            MoveField reach = map.ReachThisTurn(t);
            List<HexTile> stops = new List<HexTile> { t.tile };
            foreach (HexTile x in reach.Tiles) if (x != t.tile && reach.ReachableThisTurn(x) && x.occupier == null && t.CanEnter(x)) stops.Add(x);
            HexTile bestVia = null, bestDrop = null; Unit bestRider = null; int bestCount = 0; float bestRisk = float.MaxValue; string bestWhat = null;
            foreach (HexTile via in stops) {
                float risk = map.ExpectedLoss(t, via);
                int count = 0; Unit first = null; HexTile firstDrop = null; string firstWhat = null;
                HashSet<HexTile> taken = new HashSet<HexTile>();
                foreach (Unit c in riders) {
                    if (!c.active) continue;
                    foreach (HexTile n in Transporting.UnloadOptions(t, c, via)) {
                        if (taken.Contains(n)) continue;
                        string what;
                        if (!Strikes(c, n, map, out what)) continue;
                        taken.Add(n); count++;
                        if (first == null) { first = c; firstDrop = n; firstWhat = what; }
                        break;
                    }
                }
                if (count == 0 || !WaveGoesIn(via, count, frontAttacks, map, profile)) continue;
                if (count > bestCount || (count == bestCount && (risk < bestRisk - 0.01f || (Mathf.Abs(risk - bestRisk) <= 0.01f && CombatMath.Precedes(via, bestVia))))) {
                    bestCount = count; bestRisk = risk; bestVia = via; bestRider = first; bestDrop = firstDrop; bestWhat = firstWhat;
                }
            }
            if (bestVia != null) {
                string why = "assault: puts " + bestRider.name.Replace("(Clone)", "") + " down to " + bestWhat + " (" + bestCount + " of " + riders.Count + " aboard strike from here)";
                return bestVia == t.tile ? AIAction.Disembark(t, bestRider, bestDrop, why) : AIAction.MoveAndDisembark(t, bestVia, bestRider, bestDrop, why);
            }
            // Not yet: up toward the fight, riders aboard, as far as is safe tonight.
            if (goal != null && goal != t.tile) {
                // (An enemy's tile is never reached: the tile it can reach nearest it.)
                MoveField horizon = map.ReachHorizon(t);
                HexTile near = goal;
                if (!horizon.Reachable(goal)) {
                    near = null; int nd = int.MaxValue;
                    foreach (HexTile x in horizon.Tiles) {
                        if (!horizon.Reachable(x) || (x.occupier != null && x.occupier != t)) continue;
                        int d = x.Distance(goal);
                        if (d < nd || (d == nd && CombatMath.Precedes(x, near))) { nd = d; near = x; }
                    }
                }
                HexTile step = near != null && near != t.tile ? AITransportPlanner.SafeStepToward(t, near, horizon, map, true) : null;
                if (Debug != null) Debug("   assault ride: " + t.name.Replace("(Clone)", "") + " at (" + t.tile.x + "," + t.tile.y + ") goal (" + goal.x + "," + goal.y + ") safe step " + (step != null ? "(" + step.x + "," + step.y + ")" : "none"));
                if (step != null && step != t.tile) return AIAction.Move(t, step, 0f, "assault ride: moves up with " + riders.Count + " aboard toward (" + goal.x + "," + goal.y + ")");
            }
            // Would be destroyed where it stands, riders and all: put one down where it is safest.
            if (map.ExpectedLoss(t, t.tile) >= t.Strength - 0.01f) {
                foreach (Unit c in riders) {
                    if (!c.active) continue;
                    HexTile safest = null; float sl = float.MaxValue;
                    foreach (HexTile n in Transporting.UnloadOptions(t, c)) { float l = map.ExpectedLoss(c, n); if (l < sl) { sl = l; safest = n; } }
                    if (safest != null) return AIAction.Disembark(t, c, safest, "assault ride: would be destroyed here with them aboard");
                }
            }
            return AIAction.Wait(t, "assault ride: holds with " + riders.Count + " aboard");
        }

        /// <summary>
        /// A rider whose transport has already moved this turn: put itself down where it can strike, if the wave goes in
        /// there (the transport puts down one; the rest get off themselves, still fresh). Null: stays aboard.
        /// </summary>
        public static AIAction? RiderAction(Unit c, bool frontAttacks, AIMap map, AIProfile profile) {
            Unit t = c.trasnporter;
            if (t == null || t.tile == null || Riders(t).Count == 0 || !c.active) return null;
            if (t.active) return AIAction.Wait(c, "rides to the assault");   // its transport moves first
            foreach (HexTile n in Transporting.UnloadOptions(t, c)) {
                string what;
                if (!Strikes(c, n, map, out what)) continue;
                if (!WaveGoesIn(t.tile, 1, frontAttacks, map, profile)) break;
                return AIAction.Disembark(t, c, n, "assault: gets off to " + what);
            }
            return AIAction.Wait(c, "rides to the assault");
        }

        /// <summary>
        /// A fighting unit far from the fight (more than assaultBoardTurns from contact) beside, or a walk from, a transport
        /// with room that carries no supply unit and is not on supply duty: it gets in (the fullest such transport first).
        /// </summary>
        public static AIAction? Board(Unit c, AIMap map, AIProfile profile, HashSet<Unit> busy) {
            if (c.trasnporter != null || c.tile == null || c.Hub != null || c.Firepower <= 0) return null;
            if (map.TurnsToContact(c) <= profile.assaultBoardTurns) return null;
            MoveField now = null;
            Unit best = null; HexTile bestAt = null; int bestLoad = -1;
            foreach (Unit t in Transporting.AvailableFor(c.owner, c)) {
                if (t == null || t.tile == null || busy.Contains(t) || t.Firepower > 1) continue;   // a fighting unit that carries one (a recon) fights
                bool hub = false; foreach (Unit x in t.transported) if (x != null && x.Hub != null) hub = true;
                if (hub) continue;
                HexTile at = null;
                if (Transporting.CanBoardFrom(t, c)) at = c.tile;
                else {
                    if (now == null) now = map.ReachThisTurn(c);
                    foreach (HexTile n in t.tile.neighbors) {
                        if (n == null || n.occupier != null || !now.ReachableThisTurn(n) || !Transporting.CanBoardOnto(c, n, t.tile, t)) continue;
                        if (at == null || CombatMath.Precedes(n, at)) at = n;
                    }
                }
                if (at == null) continue;
                int load = t.transported.Count;
                if (load > bestLoad || (load == bestLoad && CombatMath.Precedes(t.tile, best.tile))) { best = t; bestAt = at; bestLoad = load; }
            }
            if (best == null) return null;
            return AIAction.Board(c, best, bestAt, "gets in a transport to ride to the assault (" + map.TurnsToContact(c) + " turns from contact on foot)");
        }

        /// <summary>
        /// An empty transport not on supply duty: back for more riders — to the nearest of our fighting units that could
        /// ride (far from the fight, not aboard, not already sought by another), else beside the nearest building of ours
        /// that makes such units. Null: nothing to fetch.
        /// </summary>
        public static AIAction? Fetch(Unit t, AIMap map, AIProfile profile, HashSet<Unit> sought) {
            if (t.tile == null || t.transported.Count > 0) return null;
            Unit pick = null; int pd = int.MaxValue;
            foreach (Unit u in map.ownUnits) {
                if (u == null || u.tile == null || u.trasnporter != null || u.Hub != null || u.Firepower <= 0 || sought.Contains(u)) continue;
                if (!Transporting.CanCarry(t, u) || map.TurnsToContact(u) <= profile.assaultBoardTurns) continue;
                int d = map.Travel(t, t.tile, u.tile);
                if (d < pd || (d == pd && CombatMath.Precedes(u.tile, pick.tile))) { pd = d; pick = u; }
            }
            HexTile goal = null; string why = null;
            if (pick != null) {
                sought.Add(pick);
                if (t.tile.Distance(pick.tile) <= 1) return AIAction.Wait(t, "assault ride: waits for " + pick.name.Replace("(Clone)", "") + " to get in");
                goal = pick.tile; why = "assault ride: goes to fetch " + pick.name.Replace("(Clone)", "") + " (" + pick.tile.x + "," + pick.tile.y + ")";
            } else {
                int bd = int.MaxValue;
                foreach (Building b in Building.all) {
                    if (b == null || b.tile == null || b.owner != t.owner || !b.CanProduce) continue;
                    bool makes = false; foreach (Unit u in b.Purchasable()) if (u != null && u.Firepower > 0 && Transporting.CanCarry(t, u)) { makes = true; break; }
                    if (!makes) continue;
                    int d = map.Travel(t, t.tile, b.tile);
                    if (d < bd) { bd = d; goal = b.tile; }
                }
                if (goal == null) return null;
                if (t.tile.Distance(goal) <= 1 && t.tile != goal) return AIAction.Wait(t, "assault ride: waits beside a base for riders");
                why = "assault ride: back to a base for riders";
            }
            HexTile step = AITransportPlanner.SafeStepToward(t, goal, map.ReachHorizon(t), map, false);
            if (step == null || step == t.tile || step == goal) return AIAction.Wait(t, why);
            return AIAction.Move(t, step, 0f, why);
        }
    }
}
