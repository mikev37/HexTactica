using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace HexTactica.AI {

    public enum ObjectiveKind {
        DefendHQ,     // kill or drive off what threatens our HQ
        KillHQ,       // destroy an enemy HQ: the game
        RaidSupply,   // destroy an enemy supply hub: exploit a breakthrough
        Strike        // destroy an isolated enemy group: defeat in detail
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
    /// The objective assigner and evaluator: the layer the original AttackMove implied and
    /// never had. AttackMove asked every unit "what is the best thing you can hit?"; nothing
    /// asked "what is worth taking this turn, and who should take it?". Without that, every
    /// unit pursued its own best shot, which is how a tank ended up beside the enemy HQ alone.
    ///
    /// Each turn it lists the points worth securing — our HQ under threat, an enemy HQ, an
    /// enemy supply hub, an enemy group isolated from support — prices each in gold, and
    /// works out what can defend it: every enemy that could fight there next turn. An
    /// objective is only attempted if enough combat power is assigned to beat that defence
    /// by <see cref="AIProfile.objectiveRatio"/> (and, for an HQ or hub, to destroy it in one
    /// volley). Otherwise nobody is sent: that is the whole rule against piecemeal attacks.
    ///
    /// Assignment is greedy and deterministic: objectives in order of value per unit of force
    /// they need, closest units first, stopping as soon as the requirement is met so the
    /// rest stay free for the next objective. That is defeat in detail — a weak enemy group
    /// cut off from the rest is a cheap objective, and the mobile units nearest it take it —
    /// and it is exploitation: an enemy hub left undefended once its line has been passed is
    /// cheap too. Units with no objective follow their front as before.
    ///
    /// Assigned units are told where to go, not how: the tactical layer still prices every
    /// tile, and the assignment adds a pull toward the objective, an aggressive posture, and
    /// membership in a group that prices its risk together and arrives together.
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
                o.tile = worst.tile;
                o.value = kv.Value * map.BoardStake * profile.hqDefenseWeight;
                o.required = o.defence;
                list.Add(o);
            }

            for (int i = 0; i < map.enemyHubs.Count; i++) {
                SupplyPoint sp = map.enemyHubs[i];
                if (sp == null || sp.myUnit == null || sp.myUnit.tile == null) continue;
                Unit hub = sp.myUnit;
                Objective o = new Objective { kind = sp.producer ? ObjectiveKind.KillHQ : ObjectiveKind.RaidSupply, tile = hub.tile };
                o.targets.Add(hub);
                o.defence = DefenceAt(map, hub.tile, null);
                o.value = sp.producer ? map.BoardStake
                                      : map.SupplyImportance(hub) + AIScore.Worth(hub) * hub.Strength * profile.killBonus;
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
                Objective o = new Objective { kind = ObjectiveKind.Strike };
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
                if (u.Firepower <= 0 || u.GetComponent<SupplyPoint>() != null) continue;
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
                    int t = Eta(u, o.tile);
                    if (t > profile.objectiveReachTurns) continue;
                    // Not a trip it would starve on: out there and back must fit in what it carries.
                    if (u.suppliesMax > 0 && map.SupplyDepth(o.tile) <= 0 &&
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
                if (!o.committed) { o.units.Clear(); o.assignedPower = 0f; o.assignedVolley = 0f; continue; }
                foreach (Unit u in o.units) { used.Add(u); result[u] = o; }
            }
            return result;
        }

        static bool Satisfied(Objective o) {
            return o.assignedPower >= o.required && o.assignedPower > 0f && o.assignedVolley >= o.volleyNeeded;
        }

        /// <summary>Turns for <paramref name="u"/> to bring its weapon to bear on <paramref name="tile"/>, by hex distance.</summary>
        public static int Eta(Unit u, HexTile tile) {
            int d = Mathf.Max(0, u.tile.Distance(tile) - u.AttackRange);
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
            if (u.GetComponent<SupplyPoint>() != null) return 0f;
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
