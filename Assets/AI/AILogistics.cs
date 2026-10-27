using System.Collections.Generic;
using UnityEngine;

namespace HexTactica.AI {

    /// <summary>
    /// Supply for the campaign's fronts (AIProfile.campaignLogistics), planned at the start of each turn.
    ///
    /// The fronts (task forces with units) in order of their size, largest first. For each, the
    /// logistics chain comes first: from the static network (HQ and our buildings) toward the front
    /// (its staging ground while it gathers, its objective once it has gone in), of the supply units
    /// not yet given to an earlier front's chain, and never ahead of that front's units (AISupplyChain).
    /// The chain takes only the supply units that bring supply nearer the front — none when the front
    /// is already on or beside supplied ground; its foremost link is the safest supplied tile nearest
    /// the front, the link the front's own supply hangs from.
    ///
    /// The supply units left over after every chain are the fronts' own, shared out by the number of
    /// units in each front (the largest first takes any remainder): each heads to its front and, once
    /// near it, is placed by the ordinary tactical supply placement (AISupplyPlanner) to serve the units
    /// around it.
    /// </summary>
    public class AILogistics {

        public class Task {
            public CampaignObjective front;
            public HexTile goal;
            public int size;
            public readonly List<SupplyPoint> chain = new List<SupplyPoint>();
            public readonly List<SupplyPoint> tactical = new List<SupplyPoint>();
        }

        public readonly List<Task> tasks = new List<Task>();
        public readonly Dictionary<SupplyPoint, Task> chainOf = new Dictionary<SupplyPoint, Task>();
        public readonly Dictionary<SupplyPoint, Task> tacticalOf = new Dictionary<SupplyPoint, Task>();

        /// <summary>Where a front's supply goes: its staging ground while it gathers, its objective once it has gone in.</summary>
        public static HexTile Goal(CampaignObjective o) { return o.went || o.staging == null ? o.target : o.staging; }

        static int Size(CampaignObjective o) {
            int n = 0;
            foreach (Unit u in o.units) if (u != null && u.tile != null && u.Firepower > 0 && u.Hub == null) n++;
            return n;
        }

        public static AILogistics Make(AIMap map, AICampaign campaign, ICollection<Unit> reservedTrucks) {
            AILogistics l = new AILogistics();
            if (campaign == null) return l;
            foreach (CampaignObjective o in campaign.objectives) {
                if (o.kind == CampaignKind.Scout || o.target == null) continue;
                int n = Size(o);
                if (n > 0) l.tasks.Add(new Task { front = o, goal = Goal(o), size = n });
            }
            // Largest first; between equals, the campaign's priority, then the board.
            l.tasks.Sort((a, b) => {
                int c = b.size.CompareTo(a.size); if (c != 0) return c;
                c = b.front.priority.CompareTo(a.front.priority); if (c != 0) return c;
                return CombatMath.CompareTiles(a.front.target, b.front.target);
            });
            List<SupplyPoint> pool = new List<SupplyPoint>();
            foreach (SupplyPoint h in map.ownHubs)
                if (h != null && !h.producer && h.myUnit != null && h.myUnit.tile != null && !LocomotionRules.IsNaval(h.myUnit.locomotion) && !LocomotionRules.IsAirborne(h.myUnit.locomotion)) pool.Add(h);
            if (l.tasks.Count == 0 || pool.Count == 0) return l;

            // The logistics chains first, largest front first.
            foreach (Task t in l.tasks) {
                if (pool.Count == 0) break;
                AISupplyChain.Plan p = Chain(map, t, pool, null, reservedTrucks);
                foreach (SupplyPoint h in p.links.Keys) { if (!pool.Contains(h)) continue; t.chain.Add(h); l.chainOf[h] = t; }
                foreach (SupplyPoint h in t.chain) pool.Remove(h);
            }

            // The rest: the fronts' own, by their number of units, the largest first taking any remainder.
            int total = 0; foreach (Task t in l.tasks) total += t.size;
            int left = pool.Count;
            Dictionary<Task, int> share = new Dictionary<Task, int>();
            int given = 0;
            foreach (Task t in l.tasks) { int s = Mathf.FloorToInt(left * t.size / (float)Mathf.Max(1, total)); share[t] = s; given += s; }
            for (int i = 0; given < left; i = (i + 1) % l.tasks.Count) { share[l.tasks[i]]++; given++; }
            foreach (Task t in l.tasks) {
                for (int k = 0; k < share[t] && pool.Count > 0; k++) {
                    // The nearest to the front's units.
                    SupplyPoint best = null; int bd = int.MaxValue;
                    foreach (SupplyPoint h in pool) {
                        int d = NearestOf(t, h.myUnit, map);
                        if (d < bd || (d == bd && best != null && AIMap.CompareUnits(h.myUnit, best.myUnit) < 0)) { bd = d; best = h; }
                    }
                    if (best == null) break;
                    t.tactical.Add(best); l.tacticalOf[best] = t; pool.Remove(best);
                }
            }
            return l;
        }

        /// <summary>The chain toward <paramref name="t"/>'s goal of <paramref name="hubs"/>, those in <paramref name="moved"/> where they now stand.</summary>
        public static AISupplyChain.Plan Chain(AIMap map, Task t, ICollection<SupplyPoint> hubs, ICollection<Unit> moved, ICollection<Unit> reservedTrucks) {
            HashSet<Unit> was = AISupplyChain.ReservedTrucks;
            AISupplyChain.ReservedTrucks = reservedTrucks != null ? new HashSet<Unit>(reservedTrucks) : null;
            try { return AISupplyChain.Make(map, moved, t.goal, hubs, t.front.units); }
            finally { AISupplyChain.ReservedTrucks = was; }
        }

        /// <summary>Travel from <paramref name="u"/> to the nearest of the front's units (and the tile it is nearest).</summary>
        public static int NearestOf(Task t, Unit u, AIMap map) { HexTile at; return NearestOf(t, u, map, out at); }

        public static int NearestOf(Task t, Unit u, AIMap map, out HexTile at) {
            at = null; int bd = int.MaxValue;
            HexTile from = u.trasnporter != null ? u.trasnporter.tile : u.tile;
            foreach (Unit v in t.front.units) {
                if (v == null || v.tile == null || v.Firepower <= 0 || v.Hub != null) continue;
                int d = map.Travel(u, from, v.tile);
                if (d < bd || (d == bd && at != null && CombatMath.Precedes(v.tile, at))) { bd = d; at = v.tile; }
            }
            return bd;
        }

        public override string ToString() {
            List<string> s = new List<string>();
            foreach (Task t in tasks) s.Add(t.front.Id + " (" + t.size + " units, to (" + t.goal.x + "," + t.goal.y + ")): chain " + t.chain.Count + ", its own " + t.tactical.Count);
            return "logistics: " + (s.Count > 0 ? string.Join("; ", s.ToArray()) : "no fronts");
        }
    }
}
