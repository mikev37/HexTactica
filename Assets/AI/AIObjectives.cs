using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace HexTactica.AI {

    public enum ObjectiveKind {
        DefendHQ,     // kill or drive off what threatens our HQ
        KillHQ,       // destroy an enemy HQ: the game
        Mass,         // gather where an enemy group is outmatched: defeat in detail, strategically
        Scout         // look where the fog may hide the enemy (AISigint): cheap units that see far
    }

    /// <summary>A point worth securing, what it is worth, and what it takes.</summary>
    public class Objective {
        public ObjectiveKind kind;
        /// <summary>Where the assigned units are going: the target's tile.</summary>
        public HexTile tile;
        public readonly List<Unit> targets = new List<Unit>();
        /// <summary>What achieving it is worth, in gold.</summary>
        public float value;
        /// <summary>Enemy combat power that could fight there next turn.</summary>
        public float defence;
        /// <summary>Combat power that must be assigned before anyone is committed to it.</summary>
        public float required;
        /// <summary>Damage the assigned units must be able to deal in one volley (HQ, hub), 0 if none.</summary>
        public float volleyNeeded;
        public float assignedPower;
        public float assignedVolley;
        public bool committed;
        public readonly List<Unit> units = new List<Unit>();

        public override string ToString() {
            StringBuilder sb = new StringBuilder();
            sb.Append(kind).Append(tile != null ? "(" + tile.x + "," + tile.y + ")" : "").Append(' ')
              .Append(committed ? "GO" : "no").Append(" value ").Append(value.ToString("0"))
              .Append(" need ").Append(required.ToString("0")).Append(" have ").Append(assignedPower.ToString("0"));
            if (volleyNeeded > 0) sb.Append(" volley ").Append(assignedVolley.ToString("0")).Append('/').Append(volleyNeeded.ToString("0"));
            sb.Append(" units ").Append(units.Count);
            return sb.ToString();
        }
    }

    /// <summary>
    /// The objective assigner and evaluator: the strategic layer the original AttackMove implied
    /// and never had — "we should attack the enemy capital now", "mass up in the south".
    ///
    /// Objectives are structure and tie-breakers, never tactics. They are evaluated last, as a
    /// small pull added to a unit's positional score, and a unit with enemy contact within three
    /// turns ignores its objective altogether: once units are near the enemy, the tactical layer
    /// (risk, counterattack, screens, rotation, targeting) decides everything. An earlier version
    /// used objectives for fine work — defeat in detail, supply raids — and let them set posture
    /// and override staging; that fought the tactical layer exactly where it matters.
    ///
    /// Each turn it lists:
    ///   - DefendHQ: our HQ is in danger; reserves out of contact come home.
    ///   - KillHQ: an enemy HQ the whole army could take now (assigned force beats everything
    ///     that could defend it by objectiveRatio, and could destroy it in one volley).
    ///   - Mass: an enemy group our nearby forces outmatch by objectiveRatio, counting everything
    ///     that could support it — where reserves should gather. Defeat in detail at the scale
    ///     of the map; the fighting itself is tactical.
    /// An objective is only adopted if enough force is assigned to it; closest units first,
    /// stopping when the requirement is met so the rest stay free for the next one.
    /// </summary>
    public static class AIObjectives {

        const int CLUSTER_LINK = 2;
        const int MAX_STRENGTH = 6;

        public static List<Objective> Build(AIMap map, AIProfile profile) {
            List<Objective> list = new List<Objective>();
            float ratio = profile.objectiveRatio * profile.Aggro(1.3f, 0.75f);

            // Our HQ under threat comes first, and is attempted whatever the odds.
            foreach (KeyValuePair<Unit, float> kv in map.hqDanger) {
                if (kv.Value <= 0f || kv.Key == null || kv.Key.tile == null) continue;
                Unit hq = kv.Key;
                Objective o = new Objective { kind = ObjectiveKind.DefendHQ };
                Unit worst = null;
                float worstThreat = 0f;
                for (int i = 0; i < map.enemyUnits.Count; i++) {
                    Unit e = map.enemyUnits[i];
                    float t = map.HQThreat(e);
                    if (t <= 0f) continue;
                    // Sending units out is for threats that could strike next turn (and a hex of
                    // warning); one two turns out is priced in targeting but not chased: the turn-one
                    // tank at its own HQ drew ours across the map into their whole army.
                    int reach = (e.moveAndAttack ? e.moveRange : 0) + e.AttackRange + 1;
                    if (e.tile.Distance(hq.tile) > reach) continue;
                    o.targets.Add(e);
                    o.defence += Power(e);
                    if (t > worstThreat || (t == worstThreat && worst != null && AIMap.CompareUnits(e, worst) < 0)) { worst = e; worstThreat = t; }
                }
                if (worst == null) continue;
                o.tile = hq.tile;
                o.value = kv.Value * map.BoardStake * profile.hqDefenseWeight;
                o.required = o.defence;
                list.Add(o);
            }

            bool attrition = GameController.gc != null && GameController.gc.rules == RuleSet.Attrition;
            for (int i = 0; i < map.enemyHubs.Count && !attrition; i++) {
                SupplyPoint sp = map.enemyHubs[i];
                if (sp == null || !sp.producer || sp.myUnit == null || sp.myUnit.tile == null) continue;
                Unit hub = sp.myUnit;
                Objective o = new Objective { kind = ObjectiveKind.KillHQ, tile = hub.tile };
                o.targets.Add(hub);
                o.defence = DefenceAt(map, hub.tile, null);
                o.value = map.BoardStake;
                o.required = o.defence * ratio;
                // It heals a strength a turn and must be destroyed outright to count.
                o.volleyNeeded = hub.Strength + 1;
                list.Add(o);
            }

            // Isolated enemy groups. Every enemy combat unit belongs to one cluster (linked
            // within CLUSTER_LINK hexes); a cluster's defence is everything that could fight
            // over it next turn, which includes neighbouring clusters that can support it.
            List<List<Unit>> clusters = Clusters(map);
            for (int c = 0; c < clusters.Count; c++) {
                List<Unit> group = clusters[c];
                Objective o = new Objective { kind = ObjectiveKind.Mass };
                float material = 0f;
                for (int i = 0; i < group.Count; i++) {
                    o.targets.Add(group[i]);
                    material += AIScore.Worth(group[i]) * group[i].Strength;
                }
                o.value = material * profile.killBonus;
                o.defence = DefenceAt(map, null, group);
                o.required = o.defence * ratio;
                o.tile = NearestToUs(map, group);
                list.Add(o);
            }
            return list;
        }

        /// <summary>Assign units to objectives. Returns each assigned unit's objective.</summary>
        public static Dictionary<Unit, Objective> Assign(List<Objective> objectives, AIMap map, AIProfile profile) {
            Dictionary<Unit, Objective> result = new Dictionary<Unit, Objective>();

            List<Unit> pool = new List<Unit>();
            for (int i = 0; i < map.ownUnits.Count; i++) {
                Unit u = map.ownUnits[i];
                if (u == null || u.tile == null || u.trasnporter != null) continue;
                if (u.Firepower <= 0 || u.Hub != null) continue;
                if (u.Strength <= profile.withdrawStrength) continue;
                pool.Add(u);
            }

            // Defence of our HQ first; then by value per unit of force required.
            List<Objective> order = new List<Objective>(objectives);
            order.Sort((a, b) => {
                bool da = a.kind == ObjectiveKind.DefendHQ, db = b.kind == ObjectiveKind.DefendHQ;
                if (da != db) return da ? -1 : 1;
                float ea = a.value / Mathf.Max(1f, a.required), eb = b.value / Mathf.Max(1f, b.required);
                int c = eb.CompareTo(ea);
                if (c != 0) return c;
                return a.tile != null && b.tile != null ? CombatMath.CompareTiles(a.tile, b.tile) : 0;
            });

            HashSet<Unit> used = new HashSet<Unit>();
            foreach (Objective o in order) {
                if (o.tile == null) continue;
                List<Unit> candidates = new List<Unit>();
                Dictionary<Unit, int> eta = new Dictionary<Unit, int>();
                foreach (Unit u in pool) {
                    if (used.Contains(u)) continue;
                    int t = Eta(u, o.tile, map);
                    if (t > profile.objectiveReachTurns) continue;
                    // Not a trip it would starve on: out there and back must fit in what it carries.
                    if (profile.objectivesNeedForce && u.suppliesMax > 0 && map.SupplyDepth(o.tile) <= 0 &&
                        t + map.TurnsToSupply(u, o.tile) > u.supplies + 1) continue;
                    if (Damage(u, o) <= 0f) continue;
                    eta[u] = t;
                    candidates.Add(u);
                }
                candidates.Sort((a, b) => {
                    int c = eta[a].CompareTo(eta[b]);
                    if (c != 0) return c;
                    c = Power(b).CompareTo(Power(a));
                    return c != 0 ? c : AIMap.CompareUnits(a, b);
                });

                for (int i = 0; i < candidates.Count; i++) {
                    if (Satisfied(o)) break;
                    Unit u = candidates[i];
                    o.units.Add(u);
                    o.assignedPower += Power(u);
                    if (o.volleyNeeded > 0f && o.targets.Count > 0) {
                        o.assignedVolley += Mathf.Min(o.targets[0].Strength, Damage(u, o));
                    }
                }

                o.committed = Satisfied(o) || (o.kind == ObjectiveKind.DefendHQ && o.units.Count > 0);
                // An objective is a small pull on units out of contact, not an order to attack: the
                // tactical layer decides everything within objectiveContactTurns of the enemy. So by
                // default it gives direction to the army even without the force to take it. (Requiring
                // 2.5x the defenders, and no trip a unit couldn't get back into supply from, meant
                // objectives never applied in an even game.)
                if (!o.committed && (profile.objectivesNeedForce || o.units.Count == 0)) { o.units.Clear(); o.assignedPower = 0f; o.assignedVolley = 0f; continue; }
                foreach (Unit u in o.units) {
                    used.Add(u);
                    // Structure, not tactics: a unit near the enemy keeps its place in the count
                    // but takes no pull from the objective.
                    if (map.TurnsToContact(u) > profile.objectiveContactTurns) result[u] = o;
                }
            }
            return result;
        }

        /// <summary>
        /// Scouting (fog of war, fogThreat; profile.scoutUnits of them): the units that see the most
        /// for the least — vision times move over worth, recon and cheap infantry before tanks — are
        /// each given the tile from which they would see the most of where the fog may hide the
        /// enemy (AISigint.Presence: units we lost track of, ground unknown), the sooner reached the
        /// better, and not one where what the fog may hide would cost them half their strength.
        /// They take a pull toward it (scoutPull, AITactical), whether in contact or not; what they
        /// see on the way is worth what it always is (AIFog.ScoutValue).
        /// </summary>
        public static void AssignScouts(Dictionary<Unit, Objective> result, AIMap map, AIProfile profile) {
            if (map.sigint == null || profile.scoutUnits <= 0) return;
            List<Unit> pool = new List<Unit>();
            foreach (Unit u in map.ownUnits) {
                if (u == null || u.tile == null || u.trasnporter != null || u.Hub != null || u.moveRange <= 0 || u.visionRange <= 0) continue;
                if (u.Strength <= profile.withdrawStrength || u.transported.Count > 0) continue;
                Objective job;
                if (result.TryGetValue(u, out job) && job.kind == ObjectiveKind.DefendHQ) continue;
                if (LocomotionRules.NeedsAirfield(u.locomotion)) continue;   // fuel: not for wandering
                pool.Add(u);
            }
            System.Func<Unit, float> eye = u => (u.visionRange + 1f) * u.moveRange / AIScore.Worth(u);
            pool.Sort((a, b) => { int c = eye(b).CompareTo(eye(a)); return c != 0 ? c : AIMap.CompareUnits(a, b); });
            HashSet<HexTile> taken = new HashSet<HexTile>();
            for (int i = 0; i < pool.Count && i < profile.scoutUnits; i++) {
                Unit u = pool[i];
                HexTile best = null;
                float bestV = 0f;
                foreach (HexTile t in HexGenerator.hg.tiles) {
                    if (t == null || !u.CanEnter(t) || taken.Contains(t)) continue;
                    int travel = map.Travel(u, u.tile, t);
                    if (travel >= 99) continue;
                    if (map.FogLoss(u, t) * 2f > u.Strength) continue;
                    float seen = 0f;
                    foreach (HexTile n in AIPath.TilesInRangeShared(t, 0, FogOfWar.VisionFrom(u, t)))
                        if (FogOfWar.LineOfSight(t, n, FogOfWar.DomainOf(u))) seen += map.sigint.Presence(n);
                    float v = seen / (1f + Mathf.CeilToInt(travel / (float)u.moveRange));
                    if (v > bestV || (v == bestV && best != null && CombatMath.CompareTiles(t, best) < 0)) { bestV = v; best = t; }
                }
                if (best == null) continue;
                taken.Add(best);
                Objective o = new Objective { kind = ObjectiveKind.Scout, tile = best, value = bestV, committed = true };
                o.units.Add(u);
                result[u] = o;
            }
        }

        static bool Satisfied(Objective o) {
            return o.assignedPower >= o.required && o.assignedPower > 0f && o.assignedVolley >= o.volleyNeeded;
        }

        /// <summary>Turns for <paramref name="u"/> to bring its weapon to bear on <paramref name="tile"/>, by hex distance.</summary>
        public static int Eta(Unit u, HexTile tile) { return Eta(u, tile, null); }

        /// <summary>As Eta, measured as the unit travels when <paramref name="map"/>'s profile says so.</summary>
        public static int Eta(Unit u, HexTile tile, AIMap map) {
            int d = Mathf.Max(0, (map != null ? map.Travel(u, u.tile, tile) : u.tile.Distance(tile)) - u.AttackRange);
            if (d == 0) return 0;
            if (u.moveRange <= 0) return 99;
            return Mathf.CeilToInt(d / (float)u.moveRange);
        }

        static float Damage(Unit u, Objective o) {
            float best = 0f;
            for (int i = 0; i < o.targets.Count; i++) {
                Unit t = o.targets[i];
                if (t == null || t.tile == null) continue;
                best = Mathf.Max(best, Unit.calculateDamage(u, t, t.tile, 0));
            }
            return best;
        }

        public static float Power(Unit u) {
            if (u == null || u.Firepower <= 0 || u.Strength <= 0) return 0f;
            if (u.Hub != null) return 0f;
            return AIScore.Worth(u) * u.Strength;
        }

        /// <summary>Enemy combat power that could fight at the tile, or over any unit of the group, next turn.</summary>
        static float DefenceAt(AIMap map, HexTile tile, List<Unit> group) {
            float total = 0f;
            for (int i = 0; i < map.enemyUnits.Count; i++) {
                Unit e = map.enemyUnits[i];
                if (e == null || e.tile == null) continue;
                float p = Power(e);
                if (p <= 0f) continue;
                int reach = (e.moveAndAttack ? e.moveRange : 0) + e.AttackRange;
                bool near = false;
                if (tile != null && e.tile.Distance(tile) <= reach) near = true;
                if (group != null) {
                    for (int j = 0; j < group.Count && !near; j++) {
                        if (group[j] == e || e.tile.Distance(group[j].tile) <= reach) near = true;
                    }
                }
                if (near) total += p;
            }
            return total;
        }

        static List<List<Unit>> Clusters(AIMap map) {
            List<Unit> units = new List<Unit>();
            for (int i = 0; i < map.enemyUnits.Count; i++) {
                Unit e = map.enemyUnits[i];
                if (e != null && e.tile != null && Power(e) > 0f) units.Add(e);
            }
            List<List<Unit>> clusters = new List<List<Unit>>();
            HashSet<Unit> seen = new HashSet<Unit>();
            for (int i = 0; i < units.Count; i++) {
                if (seen.Contains(units[i])) continue;
                List<Unit> group = new List<Unit>();
                Queue<Unit> open = new Queue<Unit>();
                open.Enqueue(units[i]);
                seen.Add(units[i]);
                while (open.Count > 0) {
                    Unit a = open.Dequeue();
                    group.Add(a);
                    for (int j = 0; j < units.Count; j++) {
                        Unit b = units[j];
                        if (seen.Contains(b) || a.tile.Distance(b.tile) > CLUSTER_LINK) continue;
                        seen.Add(b);
                        open.Enqueue(b);
                    }
                }
                clusters.Add(group);
            }
            return clusters;
        }

        static HexTile NearestToUs(AIMap map, List<Unit> group) {
            HexTile best = null;
            int bestD = int.MaxValue;
            for (int i = 0; i < group.Count; i++) {
                int d = int.MaxValue;
                for (int j = 0; j < map.ownUnits.Count; j++) {
                    Unit u = map.ownUnits[j];
                    if (u == null || u.tile == null || Power(u) <= 0f) continue;
                    d = Mathf.Min(d, u.tile.Distance(group[i].tile));
                }
                if (d < bestD || (d == bestD && best != null && CombatMath.Precedes(group[i].tile, best))) { bestD = d; best = group[i].tile; }
            }
            return best;
        }
    }
}
