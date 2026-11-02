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

        /// <summary>Down to assaultMedevacStrength and not on a building that mends it: a unit to carry to one.</summary>
        public static bool Wounded(Unit u, AIProfile profile) {
            if (u == null || profile.assaultMedevacStrength <= 0 || u.Strength > profile.assaultMedevacStrength || u.Hub == null == false || u.tile == null) return false;
            if (Building.ServicingUnder(u) != null) return false;
            // Taking a building (standing on one not ours, or beside one it can step onto): that is worth more than mending.
            if (Building.CanCapture(u)) {
                Building on = u.tile.building;
                if (on != null && on.owner != u.owner && on.Capturable) return false;
                foreach (HexTile n in u.tile.neighbors) if (n != null && n.building != null && n.building.owner != u.owner && n.building.Capturable && n.occupier == null) return false;
            }
            return true;
        }

        /// <summary>The nearest of our buildings that would mend <paramref name="u"/> (services it, heals), as <paramref name="t"/> drives; null if none.</summary>
        public static Building Hospital(Unit u, Unit t, AIMap map) {
            Building best = null; int bd = int.MaxValue;
            foreach (Building b in Building.all) {
                if (b == null || b.tile == null || b.owner != u.owner || !b.Functioning || !b.Services(u) || b.healPerTurn <= 0) continue;
                if (b.tile.occupier != null && b.tile.occupier != u) continue;
                int d = map.Travel(t, t.tile, b.tile);
                if (d < bd || (d == bd && CombatMath.Precedes(b.tile, best.tile))) { bd = d; best = b; }
            }
            return best;
        }

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
            // Nothing within its walk and fire, or a building's walk, as the crow flies: no strike (no field to work out).
            int reach = (c.moveAndAttack ? c.moveRange : 0) + Mathf.Max(1, c.AttackRange);
            bool near = false;
            foreach (Unit e in map.enemyUnits) if (e != null && e.tile != null && e.Strength > 0 && n.Distance(e.tile) <= reach) { near = true; break; }
            if (!near && Building.CanCapture(c))
                foreach (Building b in Building.all) if (b != null && b.tile != null && b.owner != c.owner && b.Capturable && n.Distance(b.tile) <= c.moveRange) { near = true; break; }
            if (!near) return false;
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
        public static AIAction? TransportAction(Unit t, HexTile goal, bool frontAttacks, AIMap map, AIProfile profile, bool holding = false) {
            List<Unit> riders = Riders(t);
            if (riders.Count == 0 || t.tile == null) return null;
            // Carrying the wounded only: to the nearest building of ours that mends them, and put them down on it.
            if (riders.TrueForAll(c => c.Strength <= profile.assaultMedevacStrength && profile.assaultMedevacStrength > 0)) {
                Building h = Hospital(riders[0], t, map);
                if (h != null) {
                    MoveField near = map.ReachThisTurn(t);
                    HexTile via = null; int vd = int.MaxValue;
                    foreach (HexTile x in near.Tiles) {
                        if ((x != t.tile && (!near.ReachableThisTurn(x) || x.occupier != null)) || !t.CanEnter(x)) continue;
                        if (!Transporting.UnloadOptions(t, riders[0], x).Contains(h.tile)) continue;
                        int d = map.Travel(t, t.tile, x);
                        if (d < vd) { vd = d; via = x; }
                    }
                    string why = "medevac: puts " + riders[0].name.Replace("(Clone)", "") + " (strength " + riders[0].Strength + ") down on " + h.name.Replace("(Clone)", "") + " (" + h.tile.x + "," + h.tile.y + ") to mend";
                    if (via != null) return via == t.tile ? AIAction.Disembark(t, riders[0], h.tile, why) : AIAction.MoveAndDisembark(t, via, riders[0], h.tile, why);
                    HexTile step = AITransportPlanner.SafeStepToward(t, h.tile, map.ReachHorizon(t), map, true);
                    if (step != null && step != t.tile && step != h.tile) return AIAction.Move(t, step, 0f, "medevac: carries " + riders.Count + " wounded toward " + h.name.Replace("(Clone)", "") + " (" + h.tile.x + "," + h.tile.y + ")");
                }
            }
            MoveField reach = map.ReachThisTurn(t);
            List<HexTile> stops = new List<HexTile> { t.tile };
            foreach (HexTile x in reach.Tiles) if (x != t.tile && reach.ReachableThisTurn(x) && x.occupier == null && t.CanEnter(x)) stops.Add(x);
            HexTile bestVia = null, bestDrop = null; Unit bestRider = null; int bestCount = 0; float bestRisk = float.MaxValue; string bestWhat = null;
            // Riders' longest strike reach from a stop (a tile beside it, then their walk and fire, or a building's walk).
            int strikeReach = 1;
            foreach (Unit c in riders) strikeReach = Mathf.Max(strikeReach, 1 + (c.moveAndAttack ? c.moveRange : 0) + Mathf.Max(1, c.AttackRange));
            List<HexTile> targets = new List<HexTile>();
            foreach (Unit e in map.enemyUnits) if (e != null && e.tile != null && e.Strength > 0) targets.Add(e.tile);
            foreach (Building b in Building.all) if (b != null && b.tile != null && b.owner != t.owner && b.Capturable) targets.Add(b.tile);
            // (Neighbouring stops share landing tiles: each rider's strike from each tile is worked out once.)
            Dictionary<long, string> strikeAt = new Dictionary<long, string>();
            foreach (HexTile via in stops) {
                bool inReach = false;
                foreach (HexTile x in targets) if (via.Distance(x) <= strikeReach) { inReach = true; break; }
                if (!inReach) continue;
                float risk = map.ExpectedLoss(t, via);
                int count = 0; Unit first = null; HexTile firstDrop = null; string firstWhat = null;
                HashSet<HexTile> taken = new HashSet<HexTile>();
                foreach (Unit c in riders) {
                    if (!c.active || (profile.assaultMedevacStrength > 0 && c.Strength <= profile.assaultMedevacStrength)) continue;   // the wounded ride on to mend
                    foreach (HexTile n in Transporting.UnloadOptions(t, c, via)) {
                        if (taken.Contains(n)) continue;
                        string what;
                        long key = ((long)riders.IndexOf(c) << 32) | (uint)(n.viewId >= 0 ? n.viewId : (n.x + 512) * 2048 + (n.y + 512));
                        if (!strikeAt.TryGetValue(key, out what)) { if (!Strikes(c, n, map, out what)) what = null; strikeAt[key] = what; }
                        if (what == null) continue;
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
            // Their front holds its ground and they are there: out, onto the safest ground beside the transport (aboard,
            // they neither fight nor hold anything).
            if (holding && goal != null && t.tile.Distance(goal) <= 2) {
                AIAction? dig = DigIn(t, riders, map, "holds the line: puts {0} down at ({1},{2})");
                if (dig.HasValue) return dig;
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

        /// <summary>The first fresh rider put down on the free tile beside <paramref name="t"/> where it is safest tonight.</summary>
        static AIAction? DigIn(Unit t, List<Unit> riders, AIMap map, string why) {
            foreach (Unit c in riders) {
                if (!c.active) continue;
                HexTile best = null; float bl = float.MaxValue;
                foreach (HexTile n in Transporting.UnloadOptions(t, c)) { float l = map.ExpectedLoss(c, n); if (l < bl || (l == bl && CombatMath.Precedes(n, best))) { bl = l; best = n; } }
                if (best != null) return AIAction.Disembark(t, c, best, string.Format(why, c.name.Replace("(Clone)", ""), best.x, best.y));
            }
            return null;
        }

        /// <summary>
        /// A rider whose transport has already moved this turn: put itself down where it can strike, if the wave goes in
        /// there (the transport puts down one; the rest get off themselves, still fresh). Null: stays aboard.
        /// </summary>
        public static AIAction? RiderAction(Unit c, bool frontAttacks, AIMap map, AIProfile profile, HexTile ground = null) {
            Unit t = c.trasnporter;
            if (t == null || t.tile == null || Riders(t).Count == 0 || !c.active) return null;
            if (t.active) return AIAction.Wait(c, "rides to the assault");   // its transport moves first
            if (profile.assaultMedevacStrength > 0 && c.Strength <= profile.assaultMedevacStrength) return AIAction.Wait(c, "wounded: rides on to mend");
            foreach (HexTile n in Transporting.UnloadOptions(t, c)) {
                string what;
                if (!Strikes(c, n, map, out what)) continue;
                if (!WaveGoesIn(t.tile, 1, frontAttacks, map, profile)) break;
                return AIAction.Disembark(t, c, n, "assault: gets off to " + what);
            }
            // Its front holding its ground, the transport there: out, to hold it.
            if (!frontAttacks && ground != null && t.tile.Distance(ground) <= 2) {
                AIAction? dig = DigIn(t, new List<Unit> { c }, map, "holds the line: gets off at ({1},{2})");
                if (dig.HasValue) { AIAction a = dig.Value; return a; }
            }
            return AIAction.Wait(c, "rides to the assault");
        }

        /// <summary>
        /// A fighting unit far from the fight (more than assaultBoardTurns from contact) beside, or a walk from, a transport
        /// with room that carries no supply unit and is not on supply duty: it gets in (the fullest such transport first).
        /// </summary>
        public static AIAction? Board(Unit c, AIMap map, AIProfile profile, HashSet<Unit> busy) {
            if (c.trasnporter != null || c.tile == null || c.Hub != null || c.Firepower <= 0) return null;
            bool hurt = Wounded(c, profile);
            if (!hurt) {
                if (map.TurnsToContact(c) <= profile.assaultBoardTurns) return null;
                // Mending on a building of ours: it stays until it is healthy.
                if (c.Strength < 6 && Building.ServicingUnder(c) != null) return null;
            }
            MoveField now = null;
            Unit best = null; HexTile bestAt = null; int bestLoad = -1;
            foreach (Unit t in Transporting.AvailableFor(c.owner, c)) {
                if (t == null || t.tile == null || busy.Contains(t) || t.Firepower > 1) continue;   // a fighting unit that carries one (a recon) fights
                if (hurt && (t.transported.Count > 0 || Hospital(c, t, map) == null)) continue;   // the wounded: an empty one, with somewhere to mend
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
            if (hurt) return AIAction.Board(c, best, bestAt, "wounded (strength " + c.Strength + "): gets in to be carried to mend");
            return AIAction.Board(c, best, bestAt, "gets in a transport to ride to the assault (" + map.TurnsToContact(c) + " turns from contact on foot)");
        }

        /// <summary>
        /// An empty transport not on supply duty: back for more riders — to the nearest of our fighting units that could
        /// ride (far from the fight, not aboard, not already sought by another), else beside the nearest building of ours
        /// that makes such units. Null: nothing to fetch.
        /// </summary>
        public static AIAction? Fetch(Unit t, AIMap map, AIProfile profile, HashSet<Unit> sought) {
            if (t.tile == null || t.transported.Count > 0) return null;
            // The wounded first (medevac): the nearest, within two moves.
            Unit hurt = null; int hd = int.MaxValue;
            foreach (Unit u in map.ownUnits) {
                if (u == null || u.trasnporter != null || sought.Contains(u) || !Wounded(u, profile) || !Transporting.CanCarry(t, u)) continue;
                int d = map.Travel(t, t.tile, u.tile);
                if (d > 2 * Mathf.Max(1, t.moveRange)) continue;
                if (d < hd || (d == hd && CombatMath.Precedes(u.tile, hurt.tile))) { hd = d; hurt = u; }
            }
            if (hurt != null && Hospital(hurt, t, map) != null) {
                sought.Add(hurt);
                if (t.tile.Distance(hurt.tile) <= 1) return AIAction.Wait(t, "medevac: waits for " + hurt.name.Replace("(Clone)", "") + " (strength " + hurt.Strength + ") to get in");
                // Beside it: the free tile next to it nearest us.
                MoveField horizon = map.ReachHorizon(t);
                HexTile beside = null; int bd2 = int.MaxValue;
                foreach (HexTile n in hurt.tile.neighbors) { if (n == null || n.occupier != null || !horizon.Reachable(n)) continue; int d = map.Travel(t, t.tile, n); if (d < bd2) { bd2 = d; beside = n; } }
                if (beside != null) {
                    HexTile go = AITransportPlanner.SafeStepToward(t, beside, horizon, map, false);
                    if (go != null && go != t.tile) return AIAction.Move(t, go, 0f, "medevac: goes for " + hurt.name.Replace("(Clone)", "") + " (strength " + hurt.Strength + ")");
                }
            }
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
