using System.Collections.Generic;
using UnityEngine;

namespace HexTactica.OldAI {

    /// <summary>
    /// What the fog may hide, priced as tonight's risk (AIProfile.fogThreat), for an AI that plays
    /// by the fog (AIProfile.fogFair). Without it the AI treated every tile it could not see as
    /// empty: gunships flew into an anti-air unit it had never seen, and a supply unit parked next
    /// to a wall of fog as if nothing could be in it.
    ///
    ///   - Remembered units (sigint): an enemy unit seen before and not seen now is still about, until
    ///     we see it die — it does not fade with time. After m enemy moves it is on one of the tiles it
    ///     can stand on within m moves of where it was last seen (its movement points and step costs,
    ///     ignoring who is in the way; tiles we see now excluded, since it would be seen there), any of
    ///     them alike. A tile it could fire on tonight from a share of those places is threatened with
    ///     that share of its damage: a move after it was seen the places are few and the estimate sharp;
    ///     the longer it is unseen, the more places, and the thinner its threat on any one tile. Its
    ///     damage is its own kind's against the unit in question (what it can attack, what it does to
    ///     it), at the strength it was last seen with, shared among our units in its reach as a real
    ///     enemy's attack is. The cost of a stop is that strength times the unit's worth, so an
    ///     expensive unit (a bomber) needs much more to go where a cheap one (a conscript) would.
    ///   - Unknown ground: a tile never seen, or not seen for fogUnknownTurns turns, may hide
    ///     anything the enemy has been seen to field (or, before anything has been seen, what we
    ///     field). A stop costs the average damage of those kinds, times the share of the ground
    ///     within their reach that is unknown, times (1 - fogAggression).
    ///
    /// Nothing here is on the board: the reply forecast (AIWorld) still plays only the enemies seen
    /// or believed where they were. This is added to the risk of a stop (AIMap.ExpectedLoss) and so
    /// to every decision that prices it.
    /// </summary>
    public sealed class AISigint {

        sealed class Zone {
            public FogOfWar.Phantom phantom;
            /// <summary>The highest share of its places from which it could fire on a tile (for Presence and diagnostics).</summary>
            public float weight;
            /// <summary>Tiles it could stand on, and the tiles it could fire on tonight.</summary>
            public HashSet<HexTile> positions, fire;
            /// <summary>For each tile it could fire on tonight: the share of its places from which it could.</summary>
            public Dictionary<HexTile, float> share;
            /// <summary>Our units already standing in its reach (its one attack is shared among them).</summary>
            public int exposed;
        }

        readonly List<Zone> zones = new List<Zone>();
        readonly Dictionary<HexTile, List<Zone>> zonesAt = new Dictionary<HexTile, List<Zone>>();
        readonly HashSet<HexTile> unknown = new HashSet<HexTile>();
        readonly List<FogOfWar.Kind> kinds = new List<FogOfWar.Kind>();
        readonly Dictionary<HexTile, float> density = new Dictionary<HexTile, float>();
        readonly AIProfile profile;
        int unknownReach;

        public bool Empty { get { return zones.Count == 0 && (unknown.Count == 0 || kinds.Count == 0); } }

        /// <summary>The threat the fog hides for <paramref name="map"/>'s side, or null when there is none to price.</summary>
        public static AISigint Build(AIMap map) {
            FogOfWar.Knowledge k = FogOfWar.current;
            AIProfile p = map.profile;
            if (k == null || k.player != map.player || p == null || !p.fogThreat) return null;
            AISigint s = new AISigint(p);
            s.map = map;
            s.BuildZones(k, map);
            s.BuildUnknown(k, map);
            return s.Empty ? null : s;
        }

        AISigint(AIProfile p) { profile = p; }
        AIMap map;

        /// <summary>
        /// How much unknown ground is feared: with fogUncertainty and the turn's estimate of the enemy (AIMap.fogUncertainty),
        /// the share of the enemy's forces we cannot place; otherwise 1 - fogAggression.
        /// </summary>
        float UnknownWeight { get { return profile.fogUncertainty && map != null && map.fogUncertainty >= 0f ? map.fogUncertainty : 1f - Mathf.Clamp01(profile.fogAggression); } }

        void BuildZones(FogOfWar.Knowledge k, AIMap map) {
            foreach (FogOfWar.Phantom ph in k.phantoms) {
                if (ph.kind == null || ph.last == null) continue;
                if (ph.kind.firepower <= 0) continue;
                HashSet<HexTile> pos = Spread(ph.last, Mathf.Max(1, ph.moves) * Mathf.Max(0, ph.kind.moveRange), ph.kind, k.visibleTiles);
                if (pos.Count == 0) continue;   // nowhere unseen it could be: it is not about
                int reach = (ph.kind.moveAndAttack ? ph.kind.moveRange : 0) + Mathf.Max(0, ph.kind.attackRange);
                // From how many of its places it could fire on each tile tonight.
                Dictionary<HexTile, int> count = new Dictionary<HexTile, int>();
                foreach (HexTile r in pos)
                    foreach (HexTile t in AIPath.TilesInRangeShared(r, 0, reach)) {
                        if (t == null) continue;
                        int c; count.TryGetValue(t, out c); count[t] = c + 1;
                    }
                Dictionary<HexTile, float> share = new Dictionary<HexTile, float>(count.Count);
                float most = 0f;
                foreach (KeyValuePair<HexTile, int> kv in count) { float f = kv.Value / (float)pos.Count; share[kv.Key] = f; if (f > most) most = f; }
                HashSet<HexTile> fire = new HashSet<HexTile>(count.Keys);
                Zone z = new Zone { phantom = ph, weight = most, positions = pos, fire = fire, share = share };
                foreach (Unit u in map.ownUnits)
                    if (u != null && u.tile != null && u.trasnporter == null && fire.Contains(u.tile)) z.exposed++;
                zones.Add(z);
                foreach (HexTile t in fire) {
                    List<Zone> list;
                    if (!zonesAt.TryGetValue(t, out list)) { list = new List<Zone>(); zonesAt[t] = list; }
                    list.Add(z);
                }
            }
        }

        void BuildUnknown(FogOfWar.Knowledge k, AIMap map) {
            if (profile.fogAggression >= 1f && !profile.fogUncertainty) return;
            kinds.AddRange(k.enemyKinds);
            if (kinds.Count == 0) {
                // Nothing of theirs seen yet: assume they field what we do.
                HashSet<string> names = new HashSet<string>();
                foreach (Unit u in map.ownUnits) {
                    if (u == null || u.Firepower <= 0) continue;
                    FogOfWar.Kind kd = FogOfWar.Kind.Of(u);
                    if (names.Add(kd.name)) kinds.Add(kd);
                }
            }
            kinds.RemoveAll(kd => kd.firepower <= 0);
            if (kinds.Count == 0) return;
            float r = 0f;
            foreach (FogOfWar.Kind kd in kinds) r += (kd.moveAndAttack ? kd.moveRange : 0) + kd.attackRange;
            unknownReach = Mathf.Max(1, Mathf.RoundToInt(r / kinds.Count));
            int now = FogOfWar.Turn;
            HexGenerator.forAllTiles(t => {
                if (t == null || t.terrain == null || k.visibleTiles.Contains(t)) return 0;
                int last = FogOfWar.TileLastSeen(k.player, t);
                if (last < 0 || now - last >= profile.fogUnknownTurns) unknown.Add(t);
                return 0;
            });
        }

        /// <summary>Tiles a unit of <paramref name="kind"/> can stand on within <paramref name="budget"/> movement points of <paramref name="from"/> (its step costs), not in <paramref name="seen"/>.</summary>
        static HashSet<HexTile> Spread(HexTile from, int budget, FogOfWar.Kind kind, HashSet<HexTile> seen) {
            HashSet<HexTile> result = new HashSet<HexTile>();
            Dictionary<HexTile, int> dist = new Dictionary<HexTile, int>();
            // Dijkstra over small integer costs: one bucket per cost.
            List<List<HexTile>> buckets = new List<List<HexTile>>();
            for (int i = 0; i <= budget; i++) buckets.Add(new List<HexTile>());
            dist[from] = 0; buckets[0].Add(from);
            for (int c = 0; c <= budget; c++) {
                List<HexTile> b = buckets[c];
                for (int i = 0; i < b.Count; i++) {
                    HexTile t = b[i];
                    if (dist[t] != c) continue;
                    if (!seen.Contains(t) && kind.CanEnter(t)) result.Add(t);
                    if (t.neighbors == null) continue;
                    foreach (HexTile n in t.neighbors) {
                        int step = kind.StepCost(t, n);
                        if (step == int.MaxValue) continue;
                        int nd = c + step, old;
                        if (nd > budget || (dist.TryGetValue(n, out old) && old <= nd)) continue;
                        dist[n] = nd; buckets[nd].Add(n);
                    }
                }
            }
            return result;
        }

        /// <summary>Every tile within <paramref name="range"/> steps of any of <paramref name="from"/>.</summary>
        static HashSet<HexTile> Grow(HashSet<HexTile> from, int range) {
            HashSet<HexTile> result = new HashSet<HexTile>(from);
            List<HexTile> frontier = new List<HexTile>(from);
            for (int i = 0; i < range && frontier.Count > 0; i++) {
                List<HexTile> next = new List<HexTile>();
                foreach (HexTile t in frontier) {
                    if (t.neighbors == null) continue;
                    foreach (HexTile n in t.neighbors) if (n != null && result.Add(n)) next.Add(n);
                }
                frontier = next;
            }
            return result;
        }

        /// <summary>Strength <paramref name="u"/> should expect to lose tonight on <paramref name="at"/> to what the fog may hide.</summary>
        public float Loss(Unit u, HexTile at) { return Loss(u, at, true); }

        /// <param name="unknownGround">Count ground we have not seen as possibly hiding the enemy (false: only the enemy we remember).</param>
        public float Loss(Unit u, HexTile at, bool unknownGround) {
            if (u == null || at == null) return 0f;
            float loss = 0f;
            List<Zone> list;
            if (zonesAt.TryGetValue(at, out list)) {
                foreach (Zone z in list) {
                    int dmg = Mathf.Min(u.Strength, z.phantom.kind.Damage(z.phantom.strength, u, at));
                    if (dmg <= 0) continue;
                    float f; if (!z.share.TryGetValue(at, out f) || f <= 0f) continue;
                    bool inAlready = u.tile != null && u.trasnporter == null && z.fire.Contains(u.tile);
                    int sharers = z.exposed + (inAlready ? 0 : 1);
                    loss += dmg * f / Mathf.Max(1, sharers);
                }
            }
            float uw = UnknownWeight;
            if (unknownGround && kinds.Count > 0 && unknown.Count > 0 && uw > 0f) {
                float d = Density(at);
                if (d > 0f) {
                    float dmg = 0f;
                    foreach (FogOfWar.Kind kd in kinds) dmg += Mathf.Min(u.Strength, kd.Damage(6, u, at));
                    loss += dmg / kinds.Count * d * uw;
                }
            }
            return Mathf.Min(u.Strength, loss);
        }

        /// <summary>Share of the ground within the enemy's usual reach of <paramref name="at"/> that is unknown.</summary>
        float Density(HexTile at) {
            float d;
            lock (density) if (density.TryGetValue(at, out d)) return d;
            List<HexTile> near = AIPath.TilesInRangeShared(at, 0, unknownReach);
            int n = 0, dark = 0;
            for (int i = 0; i < near.Count; i++) {
                if (near[i] == null || near[i].terrain == null) continue;
                n++;
                if (unknown.Contains(near[i])) dark++;
            }
            d = n > 0 ? (float)dark / n : 0f;
            lock (density) density[at] = d;
            return d;
        }

        /// <summary>Ground that may hide anything: never seen, or not for fogUnknownTurns turns.</summary>
        public bool Unknown(HexTile t) { return t != null && unknown.Contains(t); }

        /// <summary>Could an enemy we have lost track of be standing on <paramref name="t"/>? (Its confidence, the highest.)</summary>
        public float Presence(HexTile t) {
            float best = 0f;
            // The share of a lost unit's places that is this tile, the highest of them.
            foreach (Zone z in zones) if (z.positions.Contains(t)) best = Mathf.Max(best, 1f / z.positions.Count);
            if (best <= 0f && unknown.Contains(t)) best = UnknownWeight;
            return best;
        }

        /// <summary>For diagnostics: one line per remembered unit and the unknown ground.</summary>
        public IEnumerable<string> Describe() {
            foreach (Zone z in zones)
                yield return string.Format("{0} last seen ({1},{2}) str {3}, {4} moves ago: at most {5:0.00} of its places reach a tile, {6} tiles to hide on, fires on {7}, {8} of ours exposed",
                    z.phantom.kind.name, z.phantom.last.x, z.phantom.last.y, z.phantom.strength, z.phantom.moves, z.weight, z.positions.Count, z.fire.Count, z.exposed);
            yield return string.Format("unknown ground: {0} tiles, {1} kinds, reach {2}", unknown.Count, kinds.Count, unknownReach);
        }
    }
}
