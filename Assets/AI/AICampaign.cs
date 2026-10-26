using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace HexTactica.AI {

    /// <summary>What a task force is for.</summary>
    public enum CampaignKind {
        DefendHQ,   // kill or hold off what can reach our HQ
        Secure,     // take and hold a building (the enemy HQ, where the rules let it be taken, is one)
        Clear,      // defeat an enemy group in an area
        Scout       // look at ground the fog hides
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
        /// <summary>Combat power the task force needs; supply units it needs (to reach the place from supplied ground, and to spread at the contact line).</summary>
        public float needPower;
        public int needHubs;
        /// <summary>Lasting presence (Secure, Clear): its army needs supply to stay. Scouting and defence at home do not.</summary>
        public bool lasting;
        /// <summary>The way there that avoids the enemy's fire (down the middle or round the side), and the tile on it to gather on before going in.</summary>
        public HexTile staging;
        public int routeLength;
        /// <summary>Order of preference: value over what it costs, discounted by the turns to get there.</summary>
        public float priority;
        public readonly List<Unit> units = new List<Unit>();
        public readonly List<SupplyPoint> hubs = new List<SupplyPoint>();
        public float assignedPower;
        /// <summary>The task force's power when it was formed (for telling when it has been broken).</summary>
        public float formedPower;

        /// <summary>The id of last turn's objective this one continues (an enemy group that has moved), if any.</summary>
        public string inherited;

        public string Id {
            get { return inherited ?? kind + "@" + (target != null ? target.x + "," + target.y : "-"); }
        }

        public override string ToString() {
            StringBuilder sb = new StringBuilder();
            sb.Append(kind).Append(target != null ? "(" + target.x + "," + target.y + ")" : "");
            if (building != null) sb.Append(' ').Append(building.name.Replace("(Clone)", ""));
            sb.Append(" value ").Append(value.ToString("0")).Append(" priority ").Append(priority.ToString("0.00"))
              .Append(" enemy ").Append(enemyPower.ToString("0")).Append(" need ").Append(needPower.ToString("0"))
              .Append(" have ").Append(assignedPower.ToString("0")).Append(", ").Append(units.Count).Append(" units, ")
              .Append(hubs.Count).Append('/').Append(needHubs).Append(" supply");
            if (staging != null) sb.Append(" via (").Append(staging.x).Append(',').Append(staging.y).Append(")");
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
        readonly Dictionary<Unit, CampaignObjective> unitOf = new Dictionary<Unit, CampaignObjective>();
        readonly Dictionary<SupplyPoint, CampaignObjective> hubOf = new Dictionary<SupplyPoint, CampaignObjective>();
        /// <summary>Why the last plan was made from scratch, if it was.</summary>
        public string replanned;

        public CampaignObjective ObjectiveOf(Unit u) { CampaignObjective o; return u != null && unitOf.TryGetValue(u, out o) ? o : null; }
        public CampaignObjective ObjectiveOf(SupplyPoint h) { CampaignObjective o; return h != null && hubOf.TryGetValue(h, out o) ? o : null; }

        /// <summary>Plan this turn's campaign on <paramref name="map"/>, keeping what <paramref name="last"/> (last turn's) still holds.</summary>
        public static AICampaign Plan(AIMap map, AIProfile p, AICampaign last) {
            AICampaign c = new AICampaign();
            List<CampaignObjective> candidates = Candidates(map, p);
            foreach (CampaignObjective o in candidates) Appraise(o, map, p);
            candidates.Sort((a, b) => {
                if ((a.kind == CampaignKind.DefendHQ) != (b.kind == CampaignKind.DefendHQ)) return a.kind == CampaignKind.DefendHQ ? -1 : 1;
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
            c.replanned = last == null ? "first plan" : last.ReplanReason(candidates, map, p);
            c.Allocate(candidates, map, p, c.replanned == null ? last : null);
            return c;
        }

        // ------------------------------------------------------------------ objectives

        static List<CampaignObjective> Candidates(AIMap map, AIProfile p) {
            List<CampaignObjective> list = new List<CampaignObjective>();
            Building home = GameController.HQOf(map.player);
            bool attrition = GameController.gc != null && GameController.gc.rules == RuleSet.Attrition;

            // Our HQ, when anything can reach it.
            if (home != null && home.tile != null) {
                CampaignObjective d = new CampaignObjective { kind = CampaignKind.DefendHQ, target = home.tile, building = home };
                foreach (Unit e in map.enemyUnits) if (e != null && e.tile != null && Power(e) > 0f && CanArrive(e, home.tile, Mathf.Max(1, p.frontReachTurns), map)) d.enemies.Add(e);
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

            // Enemy groups.
            foreach (List<Unit> group in AIObjectives.Clusters(map)) {
                if (group.Count == 0) continue;
                CampaignObjective o = new CampaignObjective { kind = CampaignKind.Clear, lasting = true };
                o.enemies.AddRange(group);
                o.target = Nearest(group, home != null ? home.tile : null);
                list.Add(o);
            }

            // Ground to look at (fog of war).
            if (map.sigint != null) {
                HashSet<HexTile> near = new HashSet<HexTile>();
                List<KeyValuePair<HexTile, float>> spots = new List<KeyValuePair<HexTile, float>>();
                foreach (HexTile t in HexGenerator.hg.tiles) {
                    if (t == null || t.terrain == null || t.terrain.elevation < 0) continue;
                    float v = 0f;
                    foreach (HexTile n in AIPath.TilesInRangeShared(t, 0, p.scoutRadius)) v += map.sigint.Presence(n);
                    if (v > 0f) spots.Add(new KeyValuePair<HexTile, float>(t, v));
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

        /// <summary>Value, the enemy there, what it needs, the way there and its priority.</summary>
        static void Appraise(CampaignObjective o, AIMap map, AIProfile p) {
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
            o.staging = from != null ? Staging(from, route, map) : null;
            if (o.kind != CampaignKind.DefendHQ) turns += Mathf.CeilToInt(Mathf.Min(o.routeLength, 30) / (float)Mathf.Max(1, TypicalMove(map)));
            int units = map.ownUnits.Count + map.enemyUnits.Count;
            float average = units > 0 ? board / units : 0f;

            // The enemy that can fight there: what we see, and those we lost track of nearby.
            if (o.kind == CampaignKind.Clear || o.kind == CampaignKind.DefendHQ) { foreach (Unit e in o.enemies) o.enemyPower += Power(e); }
            if (o.kind != CampaignKind.DefendHQ)
                foreach (Unit e in map.enemyUnits)
                    if (e != null && e.tile != null && !o.enemies.Contains(e) && Power(e) > 0f && CanArrive(e, o.target, turns, map)) o.enemyPower += Power(e);
            FogOfWar.Knowledge k = FogOfWar.current;
            if (k != null && map.sigint != null)
                foreach (FogOfWar.Phantom ph in k.phantoms) {
                    if (ph.kind == null || ph.last == null || ph.moves > p.sigintMoves) continue;
                    int reach = ph.kind.moveRange * (turns + ph.moves) + ph.kind.attackRange;
                    if (ph.last.Distance(o.target) <= reach)
                        o.enemyPower += Mathf.Pow(p.sigintDecay, ph.moves - 1) * Mathf.Max(1f, ph.kind.cost / 6f) * ph.strength;
                }

            switch (o.kind) {
                case CampaignKind.DefendHQ:
                    o.value = board * p.hqDefenseWeight;
                    o.needPower = o.enemyPower * p.objectiveForceRatio;
                    break;
                case CampaignKind.Secure: {
                    Building b = o.building;
                    if (b.IsHQ) o.value = board;
                    else {
                        o.value = Mathf.Max(b.income * p.objectiveHorizonTurns, average * 2f);
                        if (b.producesSupply) o.value += average * 2f;
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
                    o.needPower = o.enemyPower * p.objectiveForceRatio;
                    break;
                }
                case CampaignKind.Scout:
                    o.needPower = o.enemyPower * p.objectiveForceRatio * 0.5f;   // a look, not a fight: enough not to be caught alone
                    break;
            }


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
            o.priority = o.value / cost * Mathf.Pow(Mathf.Clamp01(1f - p.turnPenalty), o.routeLength / 4f);
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

        /// <summary>Walking the route from <paramref name="from"/> toward the target: the last tile the enemy cannot strike next turn.</summary>
        static HexTile Staging(HexTile from, Dictionary<HexTile, int> route, AIMap map) {
            HexTile at = from, last = from;
            for (int guard = 0; guard < 200; guard++) {
                int here;
                if (!route.TryGetValue(at, out here) || here == 0) break;
                HexTile next = null; int nd = here;
                foreach (HexTile n in at.neighbors) {
                    int v;
                    if (n != null && route.TryGetValue(n, out v) && (v < nd || (v == nd && next != null && CombatMath.Precedes(n, next)))) { nd = v; next = n; }
                }
                if (next == null || nd >= here) break;
                at = next;
                if (map.EnemyStrikeTurns(at) > 1) last = at; else break;
            }
            return last;
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
                if (!ids.Contains(o.Id)) return o.Id + " achieved or gone";
                float power = 0f;
                foreach (Unit u in o.units) if (u != null && u.tile != null && u.Strength > 0) power += Power(u);
                if (o.formedPower > 0f && power < o.formedPower * p.objectiveReplanLoss) return o.Id + " task force broken (" + power.ToString("0") + " of " + o.formedPower.ToString("0") + ")";
            }
            return null;
        }

        // ------------------------------------------------------------------ task forces

        void Allocate(List<CampaignObjective> candidates, AIMap map, AIProfile p, AICampaign keep) {
            List<Unit> pool = new List<Unit>();
            foreach (Unit u in map.ownUnits) {
                if (u == null || u.tile == null || u.Hub != null || u.Firepower <= 0) continue;
                if (Transporting.IsTransport(u) && u.Firepower <= 1) continue;
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
                    foreach (Unit u in was.units) if (pool.Contains(u)) { Give(now, u); pool.Remove(u); }
                    foreach (SupplyPoint h in was.hubs) if (hubPool.Contains(h)) { now.hubs.Add(h); hubPool.Remove(h); }
                    now.formedPower = was.formedPower;
                    objectives.Add(now);
                }
            }

            float poolPower = 0f;
            foreach (Unit u in pool) poolPower += Power(u);

            // Our HQ first, whatever the odds; then the best objectives that can be met.
            int pursued = 0;
            List<CampaignObjective> feasible = new List<CampaignObjective>();
            foreach (CampaignObjective o in candidates) {
                if (o.kind == CampaignKind.DefendHQ) { feasible.Add(o); continue; }
                float still = Mathf.Max(0f, o.needPower - o.assignedPower);
                if (still <= poolPower || o.assignedPower > 0f) feasible.Add(o);
            }
            int others = 0;
            foreach (CampaignObjective o in feasible) if (o.kind != CampaignKind.DefendHQ) others++;
            int maxPursued = 1 + Mathf.RoundToInt(spread * Mathf.Max(0, others - 1));
            foreach (CampaignObjective o in feasible) {
                bool home = o.kind == CampaignKind.DefendHQ;
                if (!home && pursued >= maxPursued) break;
                Fill(o, pool, map, p);
                if (!objectives.Contains(o)) objectives.Add(o);
                if (!home) pursued++;
                poolPower = 0f; foreach (Unit u in pool) poolPower += Power(u);
                if (pool.Count == 0) break;
            }

            // What is left: (1 - spread) of it to the best objective, the rest over all in proportion to need.
            List<CampaignObjective> active = objectives.FindAll(o => o.kind != CampaignKind.DefendHQ && o.kind != CampaignKind.Scout);
            if (active.Count > 0 && pool.Count > 0) {
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

            foreach (CampaignObjective o in objectives) {
                if (o.formedPower <= 0f) o.formedPower = o.assignedPower;
                foreach (Unit u in o.units) unitOf[u] = o;
                foreach (SupplyPoint h in o.hubs) hubOf[h] = o;
            }
        }

        static void Give(CampaignObjective o, Unit u) {
            if (o.units.Contains(u)) return;
            o.units.Add(u);
            o.assignedPower += Power(u);
        }

        /// <summary>Give <paramref name="o"/> the units that fit it best until its need is met.</summary>
        static void Fill(CampaignObjective o, List<Unit> pool, AIMap map, AIProfile p) {
            // A building needs someone who can take it.
            if (o.kind == CampaignKind.Secure && !o.units.Exists(Building.CanCapture)) {
                Unit c = null; int ct = int.MaxValue;
                foreach (Unit u in pool) {
                    if (!Building.CanCapture(u)) continue;
                    int t = Turns(u, o, map);
                    if (t < ct || (t == ct && c != null && AIMap.CompareUnits(u, c) < 0)) { ct = t; c = u; }
                }
                if (c != null) { Give(o, c); pool.Remove(c); }
            }
            if (o.kind == CampaignKind.Scout) {
                // Those that see the most for the least.
                Unit s = null; float sv = 0f;
                foreach (Unit u in pool) {
                    if (u.visionRange <= 0 || u.moveRange <= 0 || LocomotionRules.NeedsAirfield(u.locomotion)) continue;
                    float v = (u.visionRange + 1f) * u.moveRange / AIScore.Worth(u) / (1f + Turns(u, o, map));
                    if (v > sv || (v == sv && s != null && AIMap.CompareUnits(u, s) < 0)) { sv = v; s = u; }
                }
                if (s != null) { Give(o, s); pool.Remove(s); }
            }
            while (o.assignedPower < o.needPower && pool.Count > 0) {
                Unit best = null; float bestScore = float.NegativeInfinity;
                foreach (Unit u in pool) {
                    float s = Fit(u, o, map) - Turns(u, o, map);
                    if (s > bestScore || (s == bestScore && best != null && AIMap.CompareUnits(u, best) < 0)) { bestScore = s; best = u; }
                }
                if (best == null) break;
                Give(o, best); pool.Remove(best);
            }
        }

        /// <summary>Turns for <paramref name="u"/> to reach the objective's staging tile.</summary>
        static int Turns(Unit u, CampaignObjective o, AIMap map) {
            HexTile to = o.staging ?? o.target;
            if (to == null || u.tile == null || u.moveRange <= 0) return 99;
            return Mathf.CeilToInt(map.Travel(u, u.tile, to) / (float)u.moveRange);
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
                f.assigned.AddRange(o.units);
                foreach (SupplyPoint h in o.hubs) if (h.myUnit != null) f.assigned.Add(h.myUnit);
                if (o.kind == CampaignKind.DefendHQ) { f.objective = null; f.objectiveIsHQ = false; f.objectiveValue = 0f; }
                fronts.Add(f);
            }
            Building homeB = GameController.HQOf(map.player);
            Front home = new Front { center = homeB != null ? homeB.tile : (map.ownUnits.Count > 0 ? map.ownUnits[0].tile : null), priority = 1f };
            foreach (Unit u in map.ownUnits) {
                if (u == null || u.tile == null) continue;
                bool placed = false;
                foreach (Front f in fronts) if (f.assigned.Contains(u)) { placed = true; break; }
                if (!placed) home.assigned.Add(u);
            }
            if (home.center != null) fronts.Add(home);
            AIStrategic.Finish(fronts, map, p, memory, assaulting);
            return fronts;
        }

        public IEnumerable<string> Describe() {
            if (replanned != null) yield return "campaign planned afresh: " + replanned;
            foreach (CampaignObjective o in objectives) yield return "objective " + o;
        }
    }
}
