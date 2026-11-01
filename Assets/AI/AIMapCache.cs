using System.Collections.Generic;
using UnityEngine;

namespace HexTactica.AI {

    /// <summary>
    /// What changed on the board since the map last read it, and which of its caches that leaves standing
    /// (AIProfile.keepCaches). The map used to throw every cache away after every action; most of them only depend on
    /// the ground near one unit, and one action changes the board in a few places.
    ///
    ///   - A unit's mark: its tile, strength, the transport it rides, whether it has acted, fuel, ammunition,
    ///     suppression and supplies. The board's occupancy follows from the marks of every unit.
    ///   - Changed tiles: every tile a unit left or took (moved, killed, retreated, boarded, put down, bought).
    ///   - Terrain (each tile's terrain, damage and road) and buildings (owner, capture, damage, store): any change
    ///     at all throws everything away, as before (a bridge falling reroutes the whole map).
    ///
    /// A cache worked out from a unit's tile and reaching some distance (its reach, its move field, its space) stands
    /// when that unit's mark is unchanged and no changed tile lies within that distance plus one (a tile beside where
    /// it could go can block it). A supply network stands when its supply units' marks, the buildings and the terrain
    /// are unchanged and no changed tile lies on it or beside it (an enemy unit there would cut it, one gone could let
    /// it through).
    ///
    /// AIVERIFY (the harness): every cache kept is also worked out afresh and compared; Mismatches counts the differences.
    /// </summary>
    public partial class AIMap {

        struct Mark {
            public HexTile tile; public int strength, fuel, ammo, suppressed, supplies; public Unit rides; public bool active;
            public bool Same(Mark o) { return tile == o.tile && strength == o.strength && fuel == o.fuel && ammo == o.ammo && suppressed == o.suppressed && supplies == o.supplies && rides == o.rides && active == o.active; }
        }
        Dictionary<Unit, Mark> marks;
        long terrainSig, buildingSig;
        /// <summary>Tiles whose occupant changed since the last read (keepCaches).</summary>
        readonly HashSet<HexTile> changedTiles = new HashSet<HexTile>();
        /// <summary>Units whose mark changed (or that appeared or went) since the last read.</summary>
        readonly HashSet<Unit> changedUnits = new HashSet<Unit>();
        /// <summary>Terrain or buildings changed (or there is no earlier read): nothing is kept.</summary>
        bool everything = true;

        /// <summary>AIVERIFY: check every cache kept against a fresh one.</summary>
        public static bool Verify;
        /// <summary>AIVERIFY: caches kept that differed from a fresh one, and caches kept and checked, by kind.</summary>
        public static readonly Dictionary<string, int> Mismatches = new Dictionary<string, int>(), Checked = new Dictionary<string, int>();
        static void Count(Dictionary<string, int> d, string k) { lock (d) { int n; d.TryGetValue(k, out n); d[k] = n + 1; } }
        /// <summary>AIVERIFY: one check of <paramref name="kind"/>, and whether it differed.</summary>
        public static void CountCheck(string kind, bool differed) { Count(Checked, kind); if (differed) Count(Mismatches, kind); }

        static Mark MarkOf(Unit u) {
            return new Mark { tile = u.tile, strength = u.Strength, fuel = u.fuel, ammo = u.ammo, suppressed = u.suppressed, supplies = u.supplies, rides = u.trasnporter, active = u.active };
        }

        /// <summary>Read the board's marks and work out what changed since the last read.</summary>
        void ReadDelta() {
            changedTiles.Clear();
            changedUnits.Clear();
            Dictionary<Unit, Mark> now = new Dictionary<Unit, Mark>();
            List<Unit> all = UnitController.uc != null ? UnitController.uc.units : null;
            if (all != null) foreach (Unit u in all) if (u != null && (u.tile != null || u.trasnporter != null) && u.Strength > 0) now[u] = MarkOf(u);
            long ts = 17, bs = 17;
            if (HexGenerator.hg != null && HexGenerator.hg.tiles != null)
                foreach (HexTile t in HexGenerator.hg.tiles) if (t != null) ts = ts * 31 + ((t.terrain != null ? t.terrain.GetHashCode() : 0) ^ (t.damage * 7919) ^ (t.road ? 104729 : 0));
            foreach (Building b in Building.all)
                if (b != null) bs = bs * 31 + ((b.owner != null ? b.owner.GetHashCode() : 1) ^ (b.capturePoints * 131) ^ ((b.capturingPlayer != null ? b.capturingPlayer.GetHashCode() : 3) * 7) ^ (b.damage * 7919) ^ (b.health * 31337) ^ (b.supplies * 65537));
            bool first = marks == null;
            everything = first || ts != terrainSig || bs != buildingSig;
            if (!first) {
                foreach (KeyValuePair<Unit, Mark> kv in now) {
                    Mark was;
                    if (!marks.TryGetValue(kv.Key, out was)) { changedUnits.Add(kv.Key); if (kv.Value.tile != null) changedTiles.Add(kv.Value.tile); continue; }
                    if (was.Same(kv.Value)) continue;
                    changedUnits.Add(kv.Key);
                    if (was.tile != kv.Value.tile || was.rides != kv.Value.rides) { if (was.tile != null) changedTiles.Add(was.tile); if (kv.Value.tile != null) changedTiles.Add(kv.Value.tile); }
                }
                foreach (KeyValuePair<Unit, Mark> kv in marks)
                    if (!now.ContainsKey(kv.Key)) { changedUnits.Add(kv.Key); if (kv.Value.tile != null) changedTiles.Add(kv.Value.tile); }
            }
            marks = now; terrainSig = ts; buildingSig = bs;
        }

        /// <summary>No changed tile within <paramref name="reach"/> + 1 of <paramref name="origin"/>.</summary>
        bool Untouched(HexTile origin, int reach) {
            if (origin == null) return false;
            foreach (HexTile t in changedTiles) if (t.Distance(origin) <= reach + 1) return false;
            return true;
        }
        static int Farthest(HexTile origin, IEnumerable<HexTile> tiles) {
            int far = 0;
            if (tiles != null) foreach (HexTile t in tiles) { int d = origin.Distance(t); if (d > far) far = d; }
            return far;
        }
        /// <summary>No changed tile on or beside the network.</summary>
        bool NetworkUntouched(SupplyNetwork.Result r) {
            if (r == null) return false;
            foreach (HexTile t in changedTiles) {
                if (r.depth.ContainsKey(t)) return false;
                foreach (HexTile n in t.neighbors) if (n != null && r.depth.ContainsKey(n)) return false;
            }
            return true;
        }
        bool HubsUnchanged(Player p) {
            foreach (Unit u in changedUnits) if (u != null && u.owner == p && u.Hub != null) return false;
            return true;
        }

        /// <summary>Drop the per-unit caches the last actions could have changed (keepCaches); everything when terrain or buildings changed.</summary>
        void PruneUnitCaches() {
            if (everything) {
                baseReach.Clear(); baseReachBlocked.Clear(); replyCache.positions.Clear(); thisTurnFields.Clear(); horizonFields.Clear(); ourSpace.Clear();
                return;
            }
            PruneSets(baseReach, "reach");
            PruneSets(baseReachBlocked, "reach");
            PruneSets(replyCache.positions, "reply positions");
            PruneFields(thisTurnFields, "move field", f => AIPath.ThisTurn(f.unit));
            PruneFields(horizonFields, "horizon field", f => AIPath.MultiTurn(f.unit, profile.planningHorizon));
            List<Unit> drop = new List<Unit>();
            foreach (KeyValuePair<Unit, HashSet<HexTile>[]> kv in ourSpace) {
                Unit u = kv.Key;
                // (Its move tiles are only those it can finish on: a friend it could pass through stands on others, as far
                // as its movement reaches. A step costs at least one point: no farther than its move range.)
                if (changedUnits.Contains(u) || u.tile == null || !Untouched(u.tile, Mathf.Max(Farthest(u.tile, kv.Value[0]), u.moveRange))) { drop.Add(u); continue; }
                if (Verify) { Count(Checked, "space"); HashSet<HexTile>[] f = SpaceFrom(u, u.tile, null); if (!f[0].SetEquals(kv.Value[0]) || !f[1].SetEquals(kv.Value[1])) Count(Mismatches, "space"); }
            }
            foreach (Unit u in drop) ourSpace.Remove(u);
        }

        void PruneSets(Dictionary<Unit, HashSet<HexTile>> d, string kind) {
            List<Unit> drop = new List<Unit>();
            foreach (KeyValuePair<Unit, HashSet<HexTile>> kv in d) {
                Unit e = kv.Key;
                if (changedUnits.Contains(e) || e.tile == null || !Untouched(e.tile, Mathf.Max(Farthest(e.tile, kv.Value), e.moveRange))) { drop.Add(e); continue; }
                if (Verify) {
                    Count(Checked, kind);
                    HashSet<HexTile> f = AIWorld.Positions(e, d == baseReachBlocked);
                    if (d == replyCache.positions) f = AIWorld.Positions(e, profile.forecastOwnSideBlocks);
                    if (!f.SetEquals(kv.Value)) Count(Mismatches, kind);
                }
            }
            foreach (Unit e in drop) d.Remove(e);
        }

        void PruneFields(Dictionary<Unit, MoveField> d, string kind, System.Func<MoveField, MoveField> fresh) {
            List<Unit> drop = new List<Unit>();
            foreach (KeyValuePair<Unit, MoveField> kv in d) {
                Unit u = kv.Key;
                if (changedUnits.Contains(u) || u.tile == null || !Untouched(u.tile, Farthest(u.tile, kv.Value.Tiles))) { drop.Add(u); continue; }
                if (Verify) {
                    Count(Checked, kind);
                    MoveField f = fresh(kv.Value);
                    bool same = f.Count == kv.Value.Count;
                    if (same) foreach (HexTile t in f.Tiles) { Eta a = f.Get(t), b = kv.Value.Get(t); if (a.turns != b.turns || a.used != b.used || a.steps != b.steps) { same = false; break; } }
                    if (!same) Count(Mismatches, kind);
                }
            }
            foreach (Unit u in drop) d.Remove(u);
        }

        /// <summary>Take over last turn's caches (keepCaches): the first read prunes what changed since.</summary>
        void Adopt(AIMap c) {
            marks = c.marks; terrainSig = c.terrainSig; buildingSig = c.buildingSig;
            foreach (KeyValuePair<Unit, HashSet<HexTile>> kv in c.baseReach) baseReach[kv.Key] = kv.Value;
            foreach (KeyValuePair<Unit, HashSet<HexTile>> kv in c.baseReachBlocked) baseReachBlocked[kv.Key] = kv.Value;
            foreach (KeyValuePair<Unit, HashSet<HexTile>> kv in c.replyCache.positions) replyCache.positions[kv.Key] = kv.Value;
            foreach (KeyValuePair<Unit, MoveField> kv in c.thisTurnFields) thisTurnFields[kv.Key] = kv.Value;
            foreach (KeyValuePair<Unit, MoveField> kv in c.horizonFields) horizonFields[kv.Key] = kv.Value;
            foreach (KeyValuePair<Unit, HashSet<HexTile>[]> kv in c.ourSpace) ourSpace[kv.Key] = kv.Value;
            realSupply = c.realSupply; supply = c.supply; staticSupply = c.staticSupply; lastGhosts = c.lastGhosts;
            foreach (KeyValuePair<SupplyPoint, SupplyNetwork.Result> kv in c.hubExtent) hubExtent[kv.Key] = kv.Value;
            foreach (KeyValuePair<Player, SupplyNetwork.Result> kv in c.theirByPlayer) theirByPlayer[kv.Key] = kv.Value;
            foreach (KeyValuePair<HexTile, int> kv in c.distOurSupply) distOurSupply[kv.Key] = kv.Value;
            foreach (KeyValuePair<HexTile, int> kv in c.distTheirSupply) distTheirSupply[kv.Key] = kv.Value;
        }

        /// <summary>Report for the harness: kept caches checked and how many differed, by kind.</summary>
        public static string VerifyReport() {
            List<string> r = new List<string>();
            lock (Checked) foreach (KeyValuePair<string, int> kv in Checked) { int m; Mismatches.TryGetValue(kv.Key, out m); r.Add(kv.Key + " " + kv.Value + " kept, " + m + " differed"); }
            return r.Count == 0 ? "nothing kept" : string.Join("; ", r.ToArray());
        }
    }
}
