using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Where each unit could move this turn, for the game's threat field (GameController.RecalculateThreat, after every
/// action of either side, human or AI), kept from one recalculation to the next while nothing near the unit changed.
///
/// Every recalculation used to work out every unit's movement afresh. Kept per side as that side sees the board
/// (fog of war): a unit's reach stands while its tile, its movement this turn (fuel) and the transport it rides are
/// unchanged (and its fuel's step budget) and no tile within its reach plus one changed occupant (a step costs at least one point, so its reach is
/// no farther than its movement); any change of terrain (a bridge falling, a road turned to badlands) or of a
/// building drops everything. Only on the real board (a thought's board copy works its own out).
///
/// AIVERIFY (the harness): every reach kept is compared with a fresh one.
/// </summary>
public static class ThreatReach {
    public static bool Enabled = true;

    sealed class Entry { public HexTile tile; public int budget, steps, extent; public Unit rides; public List<HexTile> reach; }
    sealed class Side {
        public readonly Dictionary<Unit, Entry> entries = new Dictionary<Unit, Entry>();
        public Dictionary<HexTile, Unit> occupancy;
        public long terrain, buildings;
    }
    static readonly Dictionary<object, Side> sides = new Dictionary<object, Side>();
    static readonly object NoSide = new object();
    /// <summary>The side being recalculated on the real board (RecalculateThreat), null: no caching.</summary>
    [System.ThreadStatic] static Side current;

    /// <summary>
    /// Start recalculating <paramref name="side"/>'s units (null: every unit, without fog) on the board as that side
    /// sees it: drops the reaches that what changed since its last recalculation could have changed. Real board only.
    /// </summary>
    public static void Begin(Player side) {
        current = null;
        if (!Enabled) return;
        Side s;
        object key = side != null ? (object)side : NoSide;
        if (!sides.TryGetValue(key, out s)) { s = new Side(); sides[key] = s; }
        Dictionary<HexTile, Unit> occ = new Dictionary<HexTile, Unit>();
        if (UnitController.uc != null) foreach (Unit u in UnitController.uc.units) if (u != null && u.tile != null && u.Strength > 0) occ[u.tile] = u;
        long ts = 17, bs = 17;
        if (HexGenerator.hg != null && HexGenerator.hg.tiles != null)
            foreach (HexTile t in HexGenerator.hg.tiles) if (t != null) ts = ts * 31 + ((t.terrain != null ? t.terrain.GetHashCode() : 0) ^ (t.damage * 7919) ^ (t.road ? 104729 : 0));
        foreach (Building b in Building.all)
            if (b != null) bs = bs * 31 + ((b.owner != null ? b.owner.GetHashCode() : 1) ^ (b.capturePoints * 131) ^ (b.damage * 7919) ^ (b.health * 31337));
        if (s.occupancy == null || ts != s.terrain || bs != s.buildings) s.entries.Clear();
        else {
            List<HexTile> changed = new List<HexTile>();
            foreach (KeyValuePair<HexTile, Unit> kv in occ) { Unit was; if (!s.occupancy.TryGetValue(kv.Key, out was) || was != kv.Value) changed.Add(kv.Key); }
            foreach (KeyValuePair<HexTile, Unit> kv in s.occupancy) if (!occ.ContainsKey(kv.Key)) changed.Add(kv.Key);
            List<Unit> drop = new List<Unit>();
            foreach (KeyValuePair<Unit, Entry> kv in s.entries) {
                Unit u = kv.Key; Entry e = kv.Value;
                if (u == null || u.Strength <= 0 || u.tile != e.tile || u.trasnporter != e.rides) { drop.Add(u); continue; }
                foreach (HexTile t in changed) if (t.Distance(e.tile) <= e.extent + 1) { drop.Add(u); break; }
            }
            foreach (Unit u in drop) s.entries.Remove(u);
        }
        s.occupancy = occ; s.terrain = ts; s.buildings = bs;
        current = s;
    }

    /// <summary>Stop caching (the recalculation is over).</summary>
    public static void End() { current = null; }

    /// <summary><paramref name="u"/>'s reach this turn (HexTile.ReachableThisTurn), kept while nothing near it changed.</summary>
    public static List<HexTile> Of(Unit u) {
        Side s = current;
        if (s == null || u == null || u.tile == null) return u.tile.ReachableThisTurn(u);
        // (Its movement this turn, and how many tiles its fuel lets it enter: a helicopter burning two a tile reaches half as far.)
        int range = HexTactica.AI.AIPath.RangeThisTurn(u), steps = HexTactica.AI.AIPath.StepBudget(u);
        Entry e;
        if (s.entries.TryGetValue(u, out e) && e.tile == u.tile && e.budget == range && e.steps == steps && e.rides == u.trasnporter) {
            if (HexTactica.AI.AIMap.Verify) {
                List<HexTile> f = u.tile.ReachableThisTurn(u);
                bool differs = !new HashSet<HexTile>(f).SetEquals(e.reach) || f.Count != e.reach.Count;
                HexTactica.AI.AIMap.CountCheck("threat reach", differs);
                if (differs) {
                    HashSet<HexTile> a = new HashSet<HexTile>(f), b = new HashSet<HexTile>(e.reach);
                    List<string> gained = new List<string>(), lost = new List<string>();
                    foreach (HexTile t in a) if (!b.Contains(t)) gained.Add(t.x + "," + t.y + (t.occupier != null ? "(" + t.occupier.name + ")" : ""));
                    foreach (HexTile t in b) if (!a.Contains(t)) lost.Add(t.x + "," + t.y + (t.occupier != null ? "(" + t.occupier.name + ")" : ""));
                    System.Console.WriteLine("AIVERIFY threat reach differs: " + u.name + " at " + u.tile.x + "," + u.tile.y + " range " + range + " fuel " + u.fuel + " active " + u.active + ": now also " + string.Join(" ", gained.ToArray()) + "; no longer " + string.Join(" ", lost.ToArray()));
                }
            }
            return e.reach;
        }
        List<HexTile> r = u.tile.ReachableThisTurn(u);
        s.entries[u] = new Entry { tile = u.tile, budget = range, steps = steps, extent = Mathf.Max(range, u.moveRange), rides = u.trasnporter, reach = r };
        return r;
    }
}
