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
        /// <summary>The strike each rider was put down for this turn (the transport planned it as the rider's own move): the rider carries it out. Cleared each turn.</summary>
        public static readonly Dictionary<Unit, Strike> Planned = new Dictionary<Unit, Strike>();

        /// <summary>Is <paramref name="f"/> where another rider put down this turn is to stand?</summary>
        static bool Claimed(HexTile f, Unit c) {
            foreach (KeyValuePair<Unit, Strike> kv in Planned) if (kv.Key != c && kv.Value != null && kv.Value.stand == f) return true;
            return false;
        }

        /// <summary>The planned strike of <paramref name="c"/>, put down this turn, if it can still be made: hit its target from where it was to stand, or step onto the building. Null: decides as any fresh unit.</summary>
        public static AIAction? CarryOut(Unit c, AIMap map) {
            Strike k;
            if (c == null || c.tile == null || !c.active || !Planned.TryGetValue(c, out k)) return null;
            Planned.Remove(c);
            if (k.stand == null || (k.stand.occupier != null && k.stand.occupier != c)) return null;
            if (k.stand != c.tile) { MoveField w = map.ReachThisTurn(c); bool ok = w.ReachableThisTurn(k.stand); if (!ok) return null; }
            string why = "assault: put down to " + k.what + ", does it";
            if (k.take != null) return k.stand == c.tile ? AIAction.Wait(c, why) : AIAction.Move(c, k.stand, 0f, why);
            if (k.target == null || k.target.Strength <= 0 || k.target.tile == null || k.stand.Distance(k.target.tile) > c.AttackRange) return null;
            return k.stand == c.tile ? AIAction.Strike(c, k.target, 0f, why) : AIAction.MoveStrike(c, k.stand, k.target, 0f, why);
        }

        /// <summary>Transports with riders whose assault move was set aside this turn for the ordinary one (a fallback): their riders decide for themselves. Cleared each turn.</summary>
        public static readonly HashSet<Unit> FellBack = new HashSet<Unit>();

        /// <summary>Harness tracing.</summary>
        public static System.Action<string> Debug;

        /// <summary>
        /// Down to assaultMedevacStrength and not on a building that mends it: a unit to carry to one. Taking a building
        /// (standing on one not ours, or beside one it can step onto) is worth more than mending — when it lives to take
        /// it (TurnsToTake, with <paramref name="map"/>; without one, as if it does).
        /// </summary>
        public static bool Wounded(Unit u, AIProfile profile, AIMap map = null) {
            if (u == null || profile.assaultMedevacStrength <= 0 || u.Strength > profile.assaultMedevacStrength || u.Hub == null == false || u.tile == null) return false;
            if (Building.ServicingUnder(u) != null) return false;
            if (Building.CanCapture(u)) {
                Building on = u.tile.building;
                if (on != null && on.owner != u.owner && on.Capturable && (map == null || TurnsToTake(u, on, map) >= 0)) return false;
                foreach (HexTile n in u.tile.neighbors)
                    if (n != null && n.building != null && n.building.owner != u.owner && n.building.Capturable && n.occupier == null && (map == null || TurnsToTake(u, n.building, map) >= 0)) return false;
            }
            return true;
        }

        /// <summary>
        /// How many of its turns <paramref name="u"/> needs to take <paramref name="b"/> on its own (getting there
        /// included), or -1 if it would be gone first. Turn by turn as the rules play it: its walk there; at the start
        /// of each of its turns out of supply a supply used up, and once they are gone a strength lost (Player.Starve);
        /// at the end of each turn on the building its strength added to the capture (from 0 to CaptureThreshold, owned
        /// or not); each night what the enemy can do to it there
        /// (AIMap.ExpectedLoss). <paramref name="supplied"/>: our supply will be there (null: as it is now).
        /// </summary>
        public static int TurnsToTake(Unit u, Building b, AIMap map, bool? supplied = null, int horizon = 10) {
            if (u == null || b == null || b.tile == null || u.tile == null || !Building.CanCapture(u)) return -1;
            if (b.owner == u.owner) return 0;
            int threshold = b.CaptureThreshold;
            // From 0, or from our own progress there (anyone else's is lost: Building.CaptureStep).
            int need = threshold - (b.capturingPlayer == u.owner ? Mathf.Max(0, b.capturePoints) : 0);
            bool fed = supplied ?? map.SupplyDepth(b.tile) > 0;
            int walk = u.tile == b.tile ? 0 : map.Travel(u, u.tile, b.tile);
            if (walk >= 9999) return -1;
            int arrive = u.tile == b.tile ? 0 : Mathf.Max(0, Mathf.CeilToInt(walk / (float)Mathf.Max(1, u.moveRange)) - 1);   // turns before the one it gets there
            float s = u.Strength, nightly = map.ExpectedLoss(u, b.tile);
            int sup = u.supplies;
            bool fedOnWay = map.SupplyDepth(u.tile) > 0 && fed;
            for (int turn = 0; turn <= horizon; turn++) {
                bool there = turn >= arrive;
                if (turn > 0 && !(there ? fed : fedOnWay) && u.suppliesMax > 0) { sup = Mathf.Max(0, sup - 1); if (sup <= 0) s -= 1f; }
                if (s <= 0.01f) return -1;
                if (there) {
                    need -= Mathf.FloorToInt(s);
                    if (need <= 0) return turn;
                    s -= nightly;
                    if (s <= 0.01f) return -1;
                }
            }
            return -1;
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
            // A ship carrying troops and their supply (a sea-lift convoy, AIProfile.seaLift) is a troop ship: the supply rides
            // along and stays aboard. Any other transport with a supply unit aboard is the supply's.
            bool convoy = LocomotionRules.IsNaval(t.locomotion);
            foreach (Unit c in t.transported) {
                if (c == null) continue;
                if (c.Hub != null) { if (convoy) continue; return new List<Unit>(); }
                if (c.Firepower > 0 || Building.CanCapture(c)) r.Add(c);
            }
            return r;
        }

        /// <summary>What a rider put down does next: from <see cref="stand"/>, hit <see cref="target"/> (null: take <see cref="take"/> by standing on it).</summary>
        public class Strike {
            public HexTile stand; public Unit target; public Building take; public string what; public float score;
        }

        /// <summary>Can <paramref name="c"/>, put down on <paramref name="n"/> with its whole turn ahead, strike this turn: hit an enemy it can hurt, or reach a building to take?</summary>
        public static bool Strikes(Unit c, HexTile n, AIMap map, out string what) {
            Strike k = StrikeFrom(c, n, map, c.carriedThisTurn);
            what = k != null ? k.what : null;
            return k != null;
        }

        /// <summary>
        /// The best strike for <paramref name="c"/> put down on <paramref name="n"/>, its whole turn ahead: a building to
        /// take (stand on it), else the hit worth most (what it does to the target, less what it stands to lose where it
        /// fires from), from a tile where it would not be destroyed tonight. Null: none.
        /// </summary>
        /// <param name="carried">It gets there by riding (its transport moves this turn, or has): a unit that must move or
        /// attack (artillery) can then only take a building, not fire (Unit.carriedThisTurn).</param>
        public static Strike StrikeFrom(Unit c, HexTile n, AIMap map, bool carried) {
            bool fires = c.moveAndAttack || !carried;
            // Nothing within its walk and fire, or a building's walk, as the crow flies: no strike (no field to work out).
            int reach = (c.moveAndAttack ? c.moveRange : 0) + Mathf.Max(1, c.AttackRange);
            bool near = false;
            foreach (Unit e in map.enemyUnits) if (e != null && e.tile != null && e.Strength > 0 && n.Distance(e.tile) <= reach) { near = true; break; }
            if (!near && Building.CanCapture(c))
                foreach (Building b in Building.all) if (Takeable(b, c) && n.Distance(b.tile) <= c.moveRange) { near = true; break; }
            if (!near) return null;
            MoveField walk = AIPath.ThisTurnFrom(c, n, c.moveRange);
            try {
                if (Building.CanCapture(c))
                    foreach (Building b in Building.all) {
                        if (!Takeable(b, c)) continue;
                        if (b.tile != n && !walk.ReachableThisTurn(b.tile)) continue;
                        if (b.tile.occupier != null && b.tile.occupier != c) continue;
                        if (Claimed(b.tile, c)) continue;
                        return new Strike { stand = b.tile, take = b, what = "take " + b.name.Replace("(Clone)", "") + " (" + b.tile.x + "," + b.tile.y + ")", score = float.MaxValue };
                    }
                Strike best = null;
                float worth = AIScore.Worth(c);
                if (fires) foreach (Unit e in map.enemyUnits) {
                    if (e == null || e.tile == null || e.Strength <= 0) continue;
                    if (!c.CanTarget(e)) continue;
                    float dmg = Unit.calculateDamage(c, e, e.tile, 0);
                    if (dmg <= 0) continue;
                    foreach (HexTile f in walk.Tiles) {
                        if (f != n && (!c.moveAndAttack || !walk.ReachableThisTurn(f) || (f.occupier != null && f.occupier != c))) continue;
                        if (Claimed(f, c)) continue;   // another rider was put down to fire from there
                        int d = f.Distance(e.tile);
                        if (d > c.AttackRange || d <= c.MinRange) continue;
                        float loss = map.ExpectedLoss(c, f);
                        if (loss >= c.Strength - 0.01f) continue;   // it would be destroyed there tonight
                        float score = Mathf.Min(dmg, e.Strength) * AIScore.Worth(e) - loss * worth;
                        if (best == null || score > best.score + 0.01f || (Mathf.Abs(score - best.score) <= 0.01f && CombatMath.Precedes(f, best.stand)))
                            best = new Strike { stand = f, target = e, score = score, what = "hit " + e.name.Replace("(Clone)", "") + " (" + e.tile.x + "," + e.tile.y + ") from (" + f.x + "," + f.y + ")" };
                    }
                }
                return best != null && best.score > 0f ? best : null;
            } finally { AIPath.Release(walk); }
        }

        /// <summary>A building <paramref name="c"/> could go and take: not ours, capturable, and nobody of ours already on it.</summary>
        static bool Takeable(Building b, Unit c) {
            return b != null && b.tile != null && b.owner != c.owner && b.Capturable && (b.tile.occupier == null || b.tile.occupier == c || b.tile.occupier.owner != c.owner);
        }

        /// <summary>Is <paramref name="c"/> taking a building: on one not ours that it can capture?</summary>
        public static bool Capturing(Unit c) {
            if (c == null || c.tile == null || c.tile.building == null || !Building.CanCapture(c)) return false;
            Building b = c.tile.building;
            return b.owner != c.owner && b.Capturable;
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
                        if ((x != t.tile && (!near.ReachableThisTurn(x) || x.occupier != null)) || !t.CanEnter(x) || !AIAir.MayEndOn(t, x, map)) continue;
                        if (!Transporting.UnloadOptions(t, riders[0], x).Contains(h.tile)) continue;
                        int d = map.Travel(t, t.tile, x);
                        if (d < vd) { vd = d; via = x; }
                    }
                    string why = "medevac: puts " + riders[0].name.Replace("(Clone)", "") + " (strength " + riders[0].Strength + ") down on " + h.name.Replace("(Clone)", "") + " (" + h.tile.x + "," + h.tile.y + ") to mend";
                    if (via != null) return via == t.tile ? AIAction.Disembark(t, riders[0], h.tile, why) : AIAction.MoveAndDisembark(t, via, riders[0], h.tile, why);
                    HexTile step = AITransportPlanner.SafeStepToward(t, h.tile, map.ReachHorizon(t), map, true);
                    if (step != null && step != t.tile && step != h.tile && AIAir.MayEndOn(t, step, map)) return AIAction.Move(t, step, 0f, "medevac: carries " + riders.Count + " wounded toward " + h.name.Replace("(Clone)", "") + " (" + h.tile.x + "," + h.tile.y + ")");
                }
            }
            MoveField reach = map.ReachThisTurn(t);
            List<HexTile> stops = new List<HexTile> { t.tile };
            foreach (HexTile x in reach.Tiles) if (x != t.tile && reach.ReachableThisTurn(x) && x.occupier == null && t.CanEnter(x) && AIAir.MayEndOn(t, x, map)) stops.Add(x);
            HexTile bestVia = null, bestDrop = null; Unit bestRider = null; int bestCount = 0; float bestRisk = float.MaxValue; Strike bestStrike = null;
            Unit bestShot = null; int bestTook = 0;
            bool firesFirst = profile.assaultFireThenUnload && t.Firepower > 1;
            // Riders' longest strike reach from a stop (a tile beside it, then their walk and fire, or a building's walk).
            int strikeReach = 1;
            foreach (Unit c in riders) strikeReach = Mathf.Max(strikeReach, 1 + (c.moveAndAttack ? c.moveRange : 0) + Mathf.Max(1, c.AttackRange));
            List<HexTile> targets = new List<HexTile>();
            foreach (Unit e in map.enemyUnits) if (e != null && e.tile != null && e.Strength > 0) targets.Add(e.tile);
            foreach (Building b in Building.all) if (b != null && b.tile != null && b.owner != t.owner && b.Capturable && (b.tile.occupier == null || b.tile.occupier.owner != t.owner)) targets.Add(b.tile);
            // (Neighbouring stops share landing tiles: each rider's strike from each tile is worked out once.)
            Dictionary<long, Strike> strikeAt = new Dictionary<long, Strike>();
            foreach (HexTile via in stops) {
                bool inReach = false;
                foreach (HexTile x in targets) if (via.Distance(x) <= strikeReach) { inReach = true; break; }
                if (!inReach) continue;
                float risk = map.ExpectedLoss(t, via);
                int count = 0; Unit first = null; HexTile firstDrop = null; Strike firstStrike = null;
                HashSet<HexTile> taken = new HashSet<HexTile>();
                foreach (Unit c in riders) {
                    if (!c.active || (profile.assaultMedevacStrength > 0 && c.Strength <= profile.assaultMedevacStrength)) continue;   // the wounded ride on to mend
                    foreach (HexTile n in Transporting.UnloadOptions(t, c, via)) {
                        if (taken.Contains(n)) continue;
                        Strike k;
                        long key = ((long)riders.IndexOf(c) << 32) | (uint)(n.viewId >= 0 ? n.viewId : (n.x + 512) * 2048 + (n.y + 512));
                        bool carried = via != t.tile || c.carriedThisTurn;
                        if (carried) key |= 1L << 62;   // (artillery: fires from n only if it did not ride there)
                        if (!strikeAt.TryGetValue(key, out k)) { k = StrikeFrom(c, n, map, carried); strikeAt[key] = k; }
                        if (k == null) continue;
                        taken.Add(n); count++;
                        if (first == null) { first = c; firstDrop = n; firstStrike = k; }
                        break;
                    }
                }
                if (count == 0 || !WaveGoesIn(via, count, frontAttacks, map, profile)) continue;
                // An armed transport would rather stop where it can fire first too (and live through what comes back).
                int took = 0; Unit shot = firesFirst && (t.moveAndAttack || via == t.tile) ? Shot(t, via, map, out took) : null;
                bool better = count > bestCount
                    || (count == bestCount && (shot != null) != (bestShot != null) ? shot != null
                    : count == bestCount && (risk < bestRisk - 0.01f || (Mathf.Abs(risk - bestRisk) <= 0.01f && CombatMath.Precedes(via, bestVia))));
                if (better) {
                    bestCount = count; bestRisk = risk; bestVia = via; bestRider = first; bestDrop = firstDrop; bestStrike = firstStrike; bestShot = shot; bestTook = took;
                }
            }
            if (bestVia != null) {
                // An armed transport fires first and lets its riders out after (they still have their whole turn),
                // unless what it takes back would destroy it, riders and all (assaultFireThenUnload).
                if (bestShot != null) {
                    string fw = "assault: fires on " + bestShot.name.Replace("(Clone)", "") + " (" + bestShot.tile.x + "," + bestShot.tile.y + ") first (takes " + bestTook + " back), then lets its " + riders.Count + " out to strike";
                    return bestVia == t.tile ? AIAction.Strike(t, bestShot, 0f, fw) : AIAction.MoveStrike(t, bestVia, bestShot, 0f, fw);
                }
                Planned[bestRider] = bestStrike;
                string why = "assault: puts " + bestRider.name.Replace("(Clone)", "") + " down to " + bestStrike.what + " (" + bestCount + " of " + riders.Count + " aboard strike from here)";
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
                        if (!horizon.Reachable(x) || (x.occupier != null && x.occupier != t) || !AIAir.MayEndOn(t, x, map)) continue;
                        int d = x.Distance(goal);
                        if (d < nd || (d == nd && CombatMath.Precedes(x, near))) { nd = d; near = x; }
                    }
                }
                HexTile step = near != null && near != t.tile ? AITransportPlanner.SafeStepToward(t, near, horizon, map, true) : null;
                if (Debug != null) Debug("   assault ride: " + t.name.Replace("(Clone)", "") + " at (" + t.tile.x + "," + t.tile.y + ") goal (" + goal.x + "," + goal.y + ") safe step " + (step != null ? "(" + step.x + "," + step.y + ")" : "none"));
                if (step != null && !AIAir.MayEndOn(t, step, map)) step = null;   // a tile held for a supply link or a landing
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

        /// <summary>The armed transport's best shot from <paramref name="via"/>: what it does less what it takes back, never one that would destroy it. Null: none.</summary>
        static Unit Shot(Unit t, HexTile via, AIMap map, out int took) {
            took = 0; Unit shot = null; float best = 0f;
            foreach (Unit e in map.enemyUnits) {
                if (e == null || e.tile == null || e.Strength <= 0 || !t.CanTarget(e)) continue;
                int d = via.Distance(e.tile);
                if (d > t.AttackRange || d <= t.MinRange) continue;
                CombatOutcome o = CombatMath.Resolve(t, e, via);
                if (o.damageToDefender <= 0 || o.attackerDies || o.damageToAttacker >= t.Strength) continue;
                float sc = o.damageToDefender * AIScore.Worth(e) - o.damageToAttacker * AIScore.Worth(t);
                if (sc > best || (Mathf.Abs(sc - best) <= 0.01f && shot != null && CombatMath.Precedes(e.tile, shot.tile))) { shot = e; best = sc; took = o.damageToAttacker; }
            }
            return shot;
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
        public static AIAction? RiderAction(Unit c, bool frontAttacks, AIMap map, AIProfile profile, HexTile ground = null, HexTile goal = null) {
            Unit t = c.trasnporter;
            if (t == null || t.tile == null || Riders(t).Count == 0 || !c.active) return null;
            if (t.active) return AIAction.Wait(c, "rides to the assault");   // its transport moves first
            if (profile.assaultMedevacStrength > 0 && c.Strength <= profile.assaultMedevacStrength) return AIAction.Wait(c, "wounded: rides on to mend");
            foreach (HexTile n in Transporting.UnloadOptions(t, c)) {
                Strike k = StrikeFrom(c, n, map, c.carriedThisTurn);
                if (k == null) continue;
                if (!WaveGoesIn(t.tile, 1, frontAttacks, map, profile)) break;
                Planned[c] = k;
                return AIAction.Disembark(t, c, n, "assault: gets off to " + k.what);
            }
            if (profile.turnOrderSteps) {
                List<HexTile> off = Transporting.UnloadOptions(t, c);
                // Its goal within reach this turn from where it can get off (getting off costs nothing): out, to go there.
                if (goal != null && goal != t.tile) {
                    HexTile via = null;
                    foreach (HexTile n in off) {
                        bool there = n == goal;
                        if (!there) { MoveField f = AIPath.ThisTurnFrom(c, n, AIPath.RangeThisTurn(c)); there = f.ReachableThisTurn(goal) && AIPath.CanFinishTurnOn(c, goal); AIPath.Release(f); }
                        if (there && (via == null || CombatMath.Precedes(n, via))) via = n;
                    }
                    if (via != null) return AIAction.Disembark(t, c, via, "gets off at (" + via.x + "," + via.y + ") to reach its goal (" + goal.x + "," + goal.y + ") this turn");
                }
                // Its transport threatened where it stopped: out, onto the safest tile beside it, if it is safer there than aboard.
                float aboard = map.ExpectedLoss(t, t.tile);
                if (aboard > 0f) {
                    HexTile best = null; float bl = float.MaxValue;
                    foreach (HexTile n in off) { float l = map.ExpectedLoss(c, n); if (l < bl - 0.01f || (Mathf.Abs(l - bl) <= 0.01f && CombatMath.Precedes(n, best))) { bl = l; best = n; } }
                    // Aboard it goes down with the transport: its loss there is the transport's, as a share of the transport's strength.
                    float riskAboard = aboard / Mathf.Max(1f, t.Strength) * Mathf.Max(1, c.Strength);
                    if (best != null && bl < riskAboard) return AIAction.Disembark(t, c, best, "its transport is threatened (" + aboard.ToString("0.0") + " strength): gets off at (" + best.x + "," + best.y + "), safer");
                }
            }
            // Its transport fell back to its ordinary move, or it has reached the front line (in contact next turn): out,
            // and it thinks for itself (assaultFrontlineOut) — the transport's plan no longer speaks for it.
            bool fellBack = FellBack.Contains(t);
            if (fellBack || (profile.assaultFrontlineOut && map.TurnsToContact(t) <= 1)) {
                HexTile best = null; float bl = float.MaxValue;
                foreach (HexTile n in Transporting.UnloadOptions(t, c)) { float l = map.ExpectedLoss(c, n); if (l < bl - 0.01f || (Mathf.Abs(l - bl) <= 0.01f && CombatMath.Precedes(n, best))) { bl = l; best = n; } }
                if (best != null) return AIAction.Disembark(t, c, best, fellBack ? "its transport's move was not its assault ride: gets off at (" + best.x + "," + best.y + ") to decide for itself" : "at the front line: gets off at (" + best.x + "," + best.y + ") to decide for itself");
            }
            // Its front holding its ground, the transport there: out, to hold it.
            if (!frontAttacks && ground != null && t.tile.Distance(ground) <= 2) {
                AIAction? dig = DigIn(t, new List<Unit> { c }, map, "holds the line: gets off at ({1},{2})");
                if (dig.HasValue) { AIAction a = dig.Value; return a; }
            }
            return AIAction.Wait(c, "rides to the assault");
        }

        /// <summary>Does <paramref name="t"/> carry a supply unit?</summary>
        /// <summary>A ship under the sea lift (AIProfile.seaLift): never held for supply duty; it takes troops and their supply together.</summary>
        public static bool SeaLiftShip(Unit t, AIMap map) {
            return t != null && map != null && map.profile != null && map.profile.seaLift && LocomotionRules.IsNaval(t.locomotion) && Transporting.IsTransport(t);
        }

        /// <summary>
        /// Our naval yard <paramref name="u"/> walks to soonest (troops get into a ship only at one: Transporting.CanBoardOnto).
        /// Null: none on its land.
        /// </summary>
        public static Building NearestYard(Unit u, AIMap map) {
            if (u == null || u.tile == null) return null;
            Building best = null; int bd = int.MaxValue;
            foreach (Building b in Building.all) {
                if (b == null || b.tile == null || b.owner != u.owner || !b.Functioning || !ServesShips(b)) continue;
                if (map.AcrossWater(u.tile, b.tile)) continue;
                int d = map.Travel(u, u.tile, b.tile);
                if (d < bd || (d == bd && CombatMath.Precedes(b.tile, best.tile))) { bd = d; best = b; }
            }
            return best;
        }

        /// <summary>
        /// <paramref name="b"/> serves ships (its prefab's services, Building.Services, for a ship): troops get into a
        /// ship there (Transporting.CanBoardOnto), and the sea lift's trips start there (AIMap.SeaTravel).
        /// </summary>
        public static bool ServesShips(Building b) {
            if (b == null) return false;
            foreach (Unit t in ShipKinds()) if (b.Services(t)) return true;
            return false;
        }

        static volatile List<Unit> shipKinds;
        /// <summary>
        /// Load the ship prefabs (GameAssets: Resources) once, on the main thread — AIPlayer.Dispatch calls it before the
        /// AI thinks, since prefabs cannot be loaded from the thread the AI thinks on.
        /// </summary>
        public static void LoadShipKinds() {
            if (shipKinds != null) return;
            List<Unit> l = new List<Unit>();
            foreach (Unit t in GameAssets.AllUnits) if (t != null && LocomotionRules.IsNaval(t.locomotion)) l.Add(t);
            shipKinds = l;
        }

        /// <summary>The ship prefabs (locomotion naval): what "serves ships" is asked with. Loaded by LoadShipKinds.</summary>
        static List<Unit> ShipKinds() {
            if (shipKinds == null) LoadShipKinds();   // main thread only (the AI's thinking has them loaded first)
            return shipKinds;
        }

        public static bool CarriesHub(Unit t) {
            if (t == null || t.transported == null) return false;
            foreach (Unit x in t.transported) if (x != null && x.Hub != null) return true;
            return false;
        }

        /// <summary>
        /// No building on the map, whoever holds it, makes fighting units (an HQ that only makes supply does not count):
        /// the army is what the mission gave, and so are the transports, given for the supply.
        /// </summary>
        public static bool NoBuildMap() {
            foreach (Building b in Building.all) {
                if (b == null || b.buildable == null) continue;
                foreach (Unit u in b.buildable) if (u != null && u.Firepower > 0) return false;
            }
            return true;
        }

        /// <summary>Could <paramref name="t"/> carry one of its side's supply units (by what it carries, not by its name)?</summary>
        public static bool SupplyCapable(Unit t) {
            if (t == null || t.owner == null || !Transporting.IsTransport(t)) return false;
            foreach (Unit u in t.owner.units) if (u != null && u.Hub != null && !u.Hub.producer && Fits(t, u)) return true;
            return false;
        }

        /// <summary>Could <paramref name="t"/> ever carry <paramref name="u"/> (the kind it carries, the room it has when empty)?</summary>
        static bool Fits(Unit t, Unit u) { return Transporting.CarriesKind(t, u) && Mathf.Max(1, u.weight) <= t.transportSlots; }

        /// <summary>
        /// The transports earmarked for supply this turn (supply comes first): those carrying a supply unit; those the
        /// supply logistics has given a duty (<paramref name="duty"/>); one within a move of a supply unit on foot that it
        /// can carry (the supply unit may want the ride: it decides late in the turn, so it is held for it); and, on a map
        /// where nothing is built (NoBuildMap), every one that can carry supply. The rest carry troops.
        /// </summary>
        public static HashSet<Unit> SupplyTransports(Player p, AIMap map, IEnumerable<Unit> duty) {
            HashSet<Unit> s = new HashSet<Unit>();
            if (p == null) return s;
            if (duty != null) foreach (Unit t in duty) if (t != null) s.Add(t);
            bool noBuild = NoBuildMap();
            List<Unit> onFoot = new List<Unit>();
            foreach (Unit u in p.units) if (u != null && u.tile != null && u.Hub != null && !u.Hub.producer && u.trasnporter == null) onFoot.Add(u);
            // One per supply unit (supplyTransportCap): beyond those carrying one or on a duty, each supply unit on foot holds
            // the nearest empty transport that could carry it (within a move and a step; any distance on a no-build map), and
            // no more. (RiverCrossing: all 12 transports held for 10 supply units, 7 idle all turn while the infantry walked.)
            if (map.profile != null && map.profile.supplyTransportCap) {
                List<Unit> free = new List<Unit>();
                foreach (Unit t in p.units) {
                    if (t == null || t.tile == null || !Transporting.IsTransport(t)) continue;
                    if (SeaLiftShip(t, map)) continue;   // a ship's supply goes with its troops (AIPlayer: sea lift)
                    if (CarriesHub(t)) { s.Add(t); continue; }
                    if (t.Firepower > 1 || s.Contains(t)) continue;
                    if (Riders(t).Count > 0) continue;   // carrying troops: on their ride, not held for supply (only empty ones)
                    free.Add(t);
                }
                free.Sort(AIMap.CompareUnits);
                onFoot.Sort(AIMap.CompareUnits);
                foreach (Unit h in onFoot) {
                    Unit best = null; int bd = int.MaxValue;
                    foreach (Unit t in free) {
                        if (s.Contains(t) || Transporting.FreeWeight(t) < Mathf.Max(1, h.weight) || !Fits(t, h)) continue;
                        int d = map.Travel(t, t.tile, h.tile);
                        if (!noBuild && d > Mathf.Max(1, t.moveRange) + 1) continue;
                        if (d < bd) { bd = d; best = t; }
                    }
                    if (best != null) s.Add(best);
                }
                // Never more held than we have supply units: those carrying one first, then duties, then the nearest ones above.
                int units = 0; foreach (Unit u in p.units) if (u != null && u.tile != null && u.Hub != null && !u.Hub.producer) units++;
                if (s.Count > units) {
                    List<Unit> order = new List<Unit>();
                    foreach (Unit t in p.units) if (t != null && s.Contains(t) && CarriesHub(t)) order.Add(t);
                    if (duty != null) foreach (Unit t in duty) if (t != null && s.Contains(t) && !order.Contains(t)) order.Add(t);
                    foreach (Unit h in onFoot) foreach (Unit t in free) if (s.Contains(t) && !order.Contains(t)) { order.Add(t); break; }
                    foreach (Unit t in p.units) if (t != null && s.Contains(t) && !order.Contains(t)) order.Add(t);
                    s = new HashSet<Unit>(order.GetRange(0, units));
                }
                return s;
            }
            foreach (Unit t in p.units) {
                if (t == null || t.tile == null || !Transporting.IsTransport(t)) continue;
                if (SeaLiftShip(t, map)) continue;   // a ship's supply goes with its troops (AIPlayer: sea lift)
                if (CarriesHub(t)) { s.Add(t); continue; }
                if (t.Firepower > 1) continue;   // an armed one fights (and carries troops)
                if (Riders(t).Count > 0) continue;   // carrying troops: on their ride, not held for supply
                if (noBuild && SupplyCapable(t)) { s.Add(t); continue; }
                foreach (Unit h in onFoot)
                    if (Transporting.FreeWeight(t) >= Mathf.Max(1, h.weight) && Fits(t, h) && map.Travel(t, t.tile, h.tile) <= Mathf.Max(1, t.moveRange) + 1) { s.Add(t); break; }
            }
            return s;
        }

        /// <summary>
        /// An empty transport held for supply with no ride planned: beside the nearest of our supply units on foot it
        /// could carry (within a move, ready when it wants a ride), it waits; further, it moves toward it. Null: none to
        /// stay with.
        /// </summary>
        public static AIAction? StayWithSupply(Unit t, AIMap map) {
            if (t == null || t.tile == null || t.owner == null) return null;
            Unit near = null; int nd = int.MaxValue;
            foreach (Unit u in t.owner.units) {
                if (u == null || u.tile == null || u.Hub == null || u.Hub.producer || u.trasnporter != null || !Fits(t, u)) continue;
                int d = map.Travel(t, t.tile, u.tile);
                if (d < nd || (d == nd && CombatMath.Precedes(u.tile, near.tile))) { nd = d; near = u; }
            }
            if (near == null) return null;
            string who = near.name.Replace("(Clone)", "") + " (" + near.tile.x + "," + near.tile.y + ")";
            if (nd <= Mathf.Max(1, t.moveRange)) {
                // Beside it (supplyRideBeside): next turn the supply unit steps aboard without walking and still rides a full move.
                if (map.profile.supplyRideBeside && t.tile.Distance(near.tile) > 1) {
                    MoveField f = map.ReachThisTurn(t);
                    HexTile at = null;
                    foreach (HexTile n in near.tile.neighbors)
                        if (n != null && n.occupier == null && f.ReachableThisTurn(n) && AIAir.MayEndOn(t, n, map) && Transporting.CanBoardOnto(near, near.tile, n, t) && (at == null || CombatMath.Precedes(n, at))) at = n;
                    if (at != null) return AIAction.Move(t, at, 0f, "held for supply: drives up beside " + who);
                }
                return AIAction.Wait(t, "held for supply: waits within a move of " + who);
            }
            HexTile step = AITransportPlanner.SafeStepToward(t, near.tile, map.ReachHorizon(t), map, false);
            if (step == null || step == t.tile || step == near.tile || !AIAir.MayEndOn(t, step, map)) return AIAction.Wait(t, "held for supply: waits for " + who);
            return AIAction.Move(t, step, 0f, "held for supply: keeps near " + who);
        }

        /// <summary>Room aboard <paramref name="t"/> for <paramref name="c"/> and still a supply unit's worth beside it, or a supply unit aboard already: a spare slot.</summary>
        static bool SpareSlot(Unit t, Unit c) {
            if (CarriesHub(t)) return true;   // AvailableFor has checked the room for c
            int supply = 1; foreach (Unit u in t.owner.units) if (u != null && u.Hub != null && !u.Hub.producer && Fits(t, u)) { supply = Mathf.Max(1, u.weight); break; }
            return Transporting.CarriedWeight(t) + Mathf.Max(1, c.weight) + supply <= t.transportSlots;
        }

        /// <summary>
        /// A fighting unit far from the fight (more than assaultBoardTurns from contact) beside, or a walk from, a transport
        /// with room: one carrying troops (armed or not), or the spare slot of one carrying a supply unit; never a transport
        /// held for supply (<paramref name="supply"/>, <paramref name="busy"/>) that has none aboard. The fullest first.
        /// </summary>
        public static AIAction? Board(Unit c, AIMap map, AIProfile profile, HashSet<Unit> busy, HashSet<Unit> supply = null, Dictionary<Unit, HexTile> supplyGoing = null, HexTile ground = null) {
            if (c.trasnporter != null || c.tile == null || c.Hub != null || c.Firepower <= 0) return null;
            if (Capturing(c)) return null;   // taking a building: it stays on it
            bool hurt = Wounded(c, profile, map);
            // Its ground across water: only a ship gets it there, however near the fight looks by sea (AIMap.SeaTravel).
            bool crossing = profile.seaLift && ground != null && map.AcrossWater(c.tile, ground);
            if (!hurt) {
                if (map.TurnsToContact(c) <= profile.assaultBoardTurns && !crossing) return null;
                // Mending on a building of ours: it stays until it is healthy.
                if (c.Strength < 6 && Building.ServicingUnder(c) != null) return null;
            }
            MoveField now = null;
            Unit best = null; HexTile bestAt = null; int bestLoad = -1;
            foreach (Unit t in Transporting.AvailableFor(c.owner, c)) {
                if (t == null || t.tile == null) continue;
                // Earmarked for supply: only a spare slot, and only once its supply unit is aboard (it goes where the supply goes).
                // A ship taking this unit across water to its ground (sea lift): a troop ship, its supply or not.
                bool overWater = SeaLiftShip(t, map) && ground != null && map.AcrossWater(c.tile, ground);
                if (SeaLiftShip(t, map) && !overWater && !hurt) continue;   // ships carry over water, not along the shore
                bool forSupply = !overWater && ((supply != null && supply.Contains(t)) || busy.Contains(t) || CarriesHub(t));
                // A supply transport's spare seat (supplySpareSeat): a rider may take it before its supply unit is aboard, as long as
                // the supply unit still fits beside it (supply units move last: waiting for them, no infantry ever got the seat).
                bool early = profile.supplySpareSeat && forSupply && !CarriesHub(t);
                if (forSupply && (hurt || (!CarriesHub(t) && !early) || !SpareSlot(t, c))) continue;
                // ... and only where the supply's ride takes it at least a move nearer its ground (an early seat too: six riders on
                // RiverCrossing got into supply trucks that held their links, and were let out again having gone nowhere).
                if (forSupply) {
                    HexTile to;
                    if (ground == null || supplyGoing == null || !supplyGoing.TryGetValue(t, out to) || to == null) continue;
                    if (to == t.tile) continue;   // it holds where it stands: no ride (the rider was let out again having gone nowhere)
                    if (map.Travel(c, to, ground) + Mathf.Max(1, c.moveRange) > map.Travel(c, c.tile, ground)) continue;
                }
                if (hurt && (t.transported.Count > 0 || Hospital(c, t, map) == null)) continue;   // the wounded: an empty one, with somewhere to mend
                HexTile at = null;
                if (Transporting.CanBoardFrom(t, c)) at = c.tile;
                else {
                    if (now == null) now = map.ReachThisTurn(c);
                    foreach (HexTile n in t.tile.neighbors) {
                        if (n == null || n == c.tile || !AIPath.BoardsFrom(now, c, t, n)) continue;
                        if (at == null || CombatMath.Precedes(n, at)) at = n;
                    }
                }
                if (at == null) continue;
                int load = t.transported.Count;
                if (load > bestLoad || (load == bestLoad && CombatMath.Precedes(t.tile, best.tile))) { best = t; bestAt = at; bestLoad = load; }
            }
            if (best == null) return null;
            if (hurt) return AIAction.Board(c, best, bestAt, "wounded (strength " + c.Strength + "): gets in to be carried to mend");
            if (SeaLiftShip(best, map)) return AIAction.Board(c, best, bestAt, "sea lift: gets in a ship to cross to its ground");
            if (CarriesHub(best)) return AIAction.Board(c, best, bestAt, "takes the spare slot of a supply transport (" + map.TurnsToContact(c) + " turns from contact on foot)");
            return AIAction.Board(c, best, bestAt, "gets in a transport to ride to the assault (" + map.TurnsToContact(c) + " turns from contact on foot)");
        }

        /// <summary>
        /// An empty transport not on supply duty: back for more riders — to the nearest of our fighting units that could
        /// ride (far from the fight, not aboard, not already sought by another), else beside the nearest building of ours
        /// that makes such units. Null: nothing to fetch.
        /// </summary>
        public static AIAction? Fetch(Unit t, AIMap map, AIProfile profile, HashSet<Unit> sought, System.Func<Unit, HexTile> groundOf = null) {
            bool ship = SeaLiftShip(t, map);
            if (t.tile == null || t.transported.Count > 0) return null;
            // The wounded first (medevac): the nearest, within two moves.
            Unit hurt = null; int hd = int.MaxValue;
            foreach (Unit u in map.ownUnits) {
                if (u == null || u.trasnporter != null || sought.Contains(u) || !Wounded(u, profile, map) || !Transporting.CanCarry(t, u)) continue;
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
                    if (go != null && go != t.tile && AIAir.MayEndOn(t, go, map)) return AIAction.Move(t, go, 0f, "medevac: goes for " + hurt.name.Replace("(Clone)", "") + " (strength " + hurt.Strength + ")");
                }
            }
            Unit pick = null; int pd = int.MaxValue;
            foreach (Unit u in map.ownUnits) {
                if (u == null || u.tile == null || u.trasnporter != null || u.Hub != null || u.Firepower <= 0 || sought.Contains(u)) continue;
                if (!Transporting.CanCarry(t, u)) continue;
                // Sea lift: a ship goes for any land unit whose ground is across the water (reinforcements, a unit cut off
                // from its front), and only those; a land transport for one far from the fight it can drive to its ground.
                HexTile g = groundOf != null ? groundOf(u) : null;
                bool across = g != null && !LocomotionRules.IsAirborne(u.locomotion) && !LocomotionRules.IsNaval(u.locomotion) && map.AcrossWater(u.tile, g);
                if (ship ? !across : (across || map.TurnsToContact(u) <= profile.assaultBoardTurns)) continue;
                int d = map.Travel(t, t.tile, u.tile);
                if (d < pd || (d == pd && CombatMath.Precedes(u.tile, pick.tile))) { pd = d; pick = u; }
            }
            HexTile goal = null; string why = null;
            if (pick != null && ship) {
                // An empty ship sails to meet the troops waiting to cross (AIMap.Meeting: off the shore of their land where
                // they and it get soonest), and waits there; they board from the shore beside it.
                sought.Add(pick);
                HexTile water, shore;
                if (!map.Meeting(t, pick, out water, out shore)) return null;
                string whom = pick.name.Replace("(Clone)", "") + " (" + pick.tile.x + "," + pick.tile.y + ")";
                if (t.tile == water) return AIAction.Wait(t, "sea lift: waits off (" + shore.x + "," + shore.y + ") for " + whom);
                goal = water; why = "sea lift: sails to meet " + whom + " off (" + shore.x + "," + shore.y + ")";
            } else if (pick != null) {
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
            if (step == null || step == t.tile || (step == goal && goal.occupier != null) || !AIAir.MayEndOn(t, step, map)) return AIAction.Wait(t, why);
            return AIAction.Move(t, step, 0f, why);
        }
    }
}
