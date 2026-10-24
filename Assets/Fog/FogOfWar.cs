using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fog of war: who sees what (GameController.fogOfWar turns it on; off, everyone sees everything).
///
/// Vision. Every unit and building has a vision range (Unit.visionRange, Building.visionRange): a
/// player sees every tile within that many steps of one of its units on the board (not cargo) or of
/// one of its buildings' tiles. Its own units' tiles are always seen.
///
/// High ground: a land unit sees one tile further from hills (elevation 2), two from mountains (3).
///
/// Line of sight: for land and sea observers (buildings count as land), ground higher than the
/// observer's own blocks the view of what lies behind it. A tile is seen only if no tile on the
/// straight line to it (between, not counting either end) is higher than the tile the observer
/// stands on: a grunt on grassland sees a hill, not the forest behind it. Elevation is the
/// terrain's; water counts as sea level (0), so shallows do not hide deep water from a ship. Where
/// the line runs exactly along the edge between two tiles, either side being clear is enough.
/// Aircraft see over everything.
///
/// Seeing a unit. An enemy unit is seen when it stands on a tile its owner's opponent sees, unless it
/// is stealthy (Unit.stealthRange X): then it is seen only by an observer of its own kind (land —
/// buildings count as land — air or sea) fewer than X steps away. An enemy is also seen (a stealthy
/// one only by its own kind, as above):
///   - when one of the player's units stands next to it;
///   - when it stands on one of the player's buildings (or their runways and docks);
///   - after it fires on one of the player's units, until the end of that player's next turn (or of
///     this one, if it happened during it): a revealed unit (see <see cref="Reveal"/>).
/// Units carried by a transport are seen when the transport is.
///
/// What fog changes in the rules:
///   - an attack needs a target its attacker's owner sees (Unit.validTarget);
///   - movement is planned as if unseen enemies were not there (<see cref="Conceal"/>), so the range
///     shown and the path drawn give nothing away. A unit that runs into one stops on the last tile
///     before it (not on a friend), its move is over, and the hidden unit fires on it (the mover
///     answers as usual if it can): Unit.MoveAlong.
///   - unloading onto a tile with an unseen enemy fails the same way, the enemy firing on the transport;
///   - a unit driven back onto a tile where an enemy it did not see is standing is pinned instead.
///
/// What each player knows is remembered: where enemy units were last seen (the AI plays on that
/// when it plays by the fog, AIProfile.fogFair), when each tile was last seen, and the owner and
/// capture state of every building as it was when last seen (<see cref="ShownOwner"/>).
///
/// The screen shows one player's view (<see cref="Viewer"/>): FogView covers the tiles it does not
/// see; FogFader fades units in and out.
/// </summary>
public static class FogOfWar {

    /// <summary>Is fog of war on?</summary>
    public static bool On { get { return GameController.gc != null && GameController.gc.fogOfWar; } }

    /// <summary>Turns played since the game started: memory ages by it.</summary>
    public static int Turn;

    // ------------------------------------------------------------------ sight

    /// <summary>What a unit is, for sight: on the ground (buildings count as ground), in the air, or at sea.</summary>
    public enum Domain { Land, Air, Sea }

    public static Domain DomainOf(Unit u) {
        if (u == null) return Domain.Land;
        if (LocomotionRules.IsAirborne(u.locomotion)) return Domain.Air;
        if (u.locomotion == Locomotion.Ship) return Domain.Sea;
        return Domain.Land;
    }

    /// <summary>Extra vision for a land unit on high ground: +1 on hills (elevation 2), +2 on mountains (3 and up).</summary>
    public static int HighGroundBonus(Unit u, HexTile at) {
        if (u == null || at == null || at.terrain == null || DomainOf(u) != Domain.Land) return 0;
        int e = at.terrain.elevation;
        return e >= 3 ? 2 : e == 2 ? 1 : 0;
    }

    /// <summary>How far <paramref name="u"/> sees from <paramref name="at"/>: its vision range, plus high ground for a land unit.</summary>
    public static int VisionFrom(Unit u, HexTile at) {
        return u == null ? 0 : Mathf.Max(0, u.visionRange) + HighGroundBonus(u, at);
    }

    /// <summary>Does <paramref name="dom"/>'s view stop at ground higher than the observer's? Land and sea: yes; air: no.</summary>
    public static bool HasLineOfSight(Domain dom) { return dom != Domain.Air; }

    /// <summary>A tile's height for line of sight: its terrain's elevation, water at sea level (0).</summary>
    public static int SightLevel(HexTile t) {
        return t == null || t.terrain == null ? 0 : Mathf.Max(0, t.terrain.elevation);
    }

    /// <summary>
    /// Can an observer of kind <paramref name="dom"/> on <paramref name="from"/> see <paramref name="to"/>
    /// over the ground between? (Range is not checked here.) Always for aircraft, the observer's own tile
    /// and its neighbours.
    /// </summary>
    public static bool LineOfSight(HexTile from, HexTile to, Domain dom) {
        if (!HasLineOfSight(dom) || from == null || to == null) return true;
        int n = from.Distance(to);
        if (n <= 1) return true;
        int eye = SightLevel(from);
        return ClearLine(from, to, n, eye, 1e-4f) || ClearLine(from, to, n, eye, -1e-4f);
    }

    /// <summary>The line from one tile's centre to another's, nudged by <paramref name="nudge"/> (to fall to one side of an edge it runs along): no tile on it higher than <paramref name="eye"/>?</summary>
    static bool ClearLine(HexTile from, HexTile to, int n, int eye, float nudge) {
        HexGenerator hg = HexGenerator.hg;
        if (hg == null) return true;
        float ax = from.x + nudge, ay = from.y + nudge, az = -from.x - from.y - 2f * nudge;
        float bx = to.x + nudge, by = to.y + nudge, bz = -to.x - to.y - 2f * nudge;
        for (int i = 1; i < n; i++) {
            float k = i / (float)n;
            float fx = ax + (bx - ax) * k, fy = ay + (by - ay) * k, fz = az + (bz - az) * k;
            int rx = Mathf.RoundToInt(fx), ry = Mathf.RoundToInt(fy), rz = Mathf.RoundToInt(fz);
            float dx = Mathf.Abs(rx - fx), dy = Mathf.Abs(ry - fy), dz = Mathf.Abs(rz - fz);
            if (dx > dy && dx > dz) rx = -ry - rz;
            else if (dy > dz) ry = -rx - rz;
            HexTile t = hg.get(rx, ry, -rx - ry);
            if (t != null && SightLevel(t) > eye) return false;
        }
        return true;
    }

    class Sight {
        /// <summary>Each tile seen, and the distance to the nearest observer whose vision covers it.</summary>
        public readonly Dictionary<HexTile, int> cover = new Dictionary<HexTile, int>();
        /// <summary>The same, counting only observers of one kind (land — buildings too — air, sea): only they can spot a stealthy unit of their kind.</summary>
        public readonly Dictionary<HexTile, int>[] byDomain = { new Dictionary<HexTile, int>(), new Dictionary<HexTile, int>(), new Dictionary<HexTile, int>() };

        public int Distance(HexTile t) { int d; return t != null && cover.TryGetValue(t, out d) ? d : -1; }
        public int Distance(HexTile t, Domain dom) { int d; return t != null && byDomain[(int)dom].TryGetValue(t, out d) ? d : -1; }

        public void Merge(Sight o) {
            Add(cover, o.cover);
            for (int i = 0; i < 3; i++) Add(byDomain[i], o.byDomain[i]);
        }
        static void Add(Dictionary<HexTile, int> into, Dictionary<HexTile, int> from) {
            foreach (KeyValuePair<HexTile, int> kv in from) { int had; if (!into.TryGetValue(kv.Key, out had) || kv.Value < had) into[kv.Key] = kv.Value; }
        }
    }

    /// <summary>
    /// Would the side whose observers <paramref name="s"/> sums up (its units: <paramref name="ours"/>;
    /// its buildings' owners as <paramref name="ownerOf"/> says) see <paramref name="u"/> on
    /// <paramref name="at"/>? A unit next to one of theirs, on one of their buildings, or inside
    /// their sight is seen — but a stealthy one only by observers of its own kind (a grunt does not
    /// see a stealth jet, a boat does not see a guerrilla; buildings count as ground), and only from
    /// fewer than its stealthRange tiles away.
    /// </summary>
    static bool Spotted(Sight s, Unit u, HexTile at, Func<Player, bool> ours, Func<Building, Player> ownerOf) {
        bool stealthy = u.stealthRange > 0;
        Domain dom = DomainOf(u);
        if (at.building != null && ours(ownerOf(at.building)) && (!stealthy || dom == Domain.Land)) return true;
        List<HexTile> n = at.neighbors;
        if (n != null) for (int i = 0; i < n.Count; i++) {
            Unit o = n[i] != null ? n[i].occupier : null;
            if (o != null && o.Strength > 0 && ours(o.owner) && (!stealthy || DomainOf(o) == dom)) return true;
        }
        if (!stealthy) return s.Distance(at) >= 0;
        int d = s.Distance(at, dom);
        return d >= 0 && d < u.stealthRange;
    }

    // Replaced whole, never changed in place: a thought on another thread keeps reading the one it was given (BoardView.sights).
    static Dictionary<Player, Sight> sights = new Dictionary<Player, Sight>();
    static int version = 1, computed = 0;

    /// <summary>Something that changes what is seen happened (a move, a death, a capture...). Cheap: sight is worked out again when next asked.</summary>
    public static void Changed() { version++; viewerChanges++; }

    /// <summary>The version of what is seen: goes up whenever sight is worked out again.</summary>
    public static int Version { get { return computed; } }

    /// <summary>Work sight out again if anything changed, and record what each player sees now.</summary>
    public static void Refresh() {
        if (BoardView.current != null) return;   // frozen inside a copy of the board: it has its own
        if (computed == version) return;
        computed = version;
        Dictionary<Player, Sight> next = new Dictionary<Player, Sight>();
        GameController gc = GameController.gc;
        if (gc != null && gc.players != null) foreach (Player p in gc.players) if (p != null) next[p] = Compute(p, null);
        sights = next;
        if (On && gc != null && gc.players != null) foreach (Player p in gc.players) if (p != null) Remember(p);
    }

    /// <summary>Sight as it is now, for a copy of the board to keep (main thread).</summary>
    internal static object SightsNow() { Refresh(); return sights; }

    static Sight SightOf(Player p) {
        if (p == null) return null;
        BoardView v = BoardView.current;
        Dictionary<Player, Sight> map;
        if (v != null && v.sights != null) map = (Dictionary<Player, Sight>)v.sights;
        else { Refresh(); map = sights; }
        Sight s;
        if (map.TryGetValue(p, out s)) return s;
        s = Compute(p, null);
        if (v == null) { Dictionary<Player, Sight> next = new Dictionary<Player, Sight>(map); next[p] = s; sights = next; }
        return s;
    }

    /// <summary>
    /// What <paramref name="p"/>'s units and buildings see. <paramref name="buildingOwner"/> says who
    /// owns each building (null: the real owner).
    /// </summary>
    static Sight Compute(Player p, Func<Building, Player> buildingOwner) {
        Sight s = new Sight();
        if (p == null) return s;
        foreach (Unit u in p.units) {
            if (u == null || u.tile == null || u.Strength <= 0 || u.trasnporter != null) continue;
            Cover(s, u.tile, VisionFrom(u, u.tile), DomainOf(u));
        }
        for (int i = 0; i < Building.all.Count; i++) {
            Building b = Building.all[i];
            if (b == null || b.tile == null || !b.Functioning) continue;   // a destroyed building sees nothing
            Player owner = buildingOwner != null ? buildingOwner(b) : b.owner;
            if (owner != p) continue;
            foreach (HexTile t in b.Footprint) Cover(s, t, Mathf.Max(0, b.visionRange), Domain.Land);
        }
        return s;
    }

    static void Cover(Sight s, HexTile from, int range, Domain dom) {
        if (from == null || HexGenerator.hg == null) return;
        Dictionary<HexTile, int> mine = s.byDomain[(int)dom];
        bool los = HasLineOfSight(dom);
        foreach (HexTile t in HexTactica.AI.AIPath.TilesInRangeShared(from, -1, range)) {
            if (los && !LineOfSight(from, t, dom)) continue;
            int d = from.Distance(t), had;
            if (!s.cover.TryGetValue(t, out had) || d < had) s.cover[t] = d;
            if (!mine.TryGetValue(t, out had) || d < had) mine[t] = d;
        }
    }

    /// <summary>Does <paramref name="p"/> see tile <paramref name="t"/>? (Always, with fog off.)</summary>
    public static bool TileVisible(Player p, HexTile t) {
        if (!On || p == null) return true;
        if (t == null) return false;
        Knowledge k = current;
        if (k != null && k.player == p) return k.visibleTiles.Contains(t);
        Sight s = SightOf(p);
        return s != null && s.cover.ContainsKey(t);
    }

    /// <summary>Steps from <paramref name="t"/> to the nearest of <paramref name="p"/>'s observers that see it; -1 if none does.</summary>
    public static int CoverDistance(Player p, HexTile t) {
        Sight s = SightOf(p);
        int d;
        return s != null && t != null && s.cover.TryGetValue(t, out d) ? d : -1;
    }

    /// <summary>Does <paramref name="p"/> see <paramref name="u"/> (the rules: where it stands)?</summary>
    public static bool Sees(Player p, Unit u) {
        if (!On || p == null || u == null) return true;
        if (u.owner == p) return true;
        if (KnowsAll(p)) return true;
        Knowledge k = current;
        if (k != null && k.player == p) return k.visible.Contains(u);
        if (u.tile == null || u.Strength <= 0) return false;
        if (u.trasnporter != null && u.trasnporter != u) return Sees(p, u.trasnporter);   // cargo: as its transport
        return SeesAt(p, u, u.tile);
    }

    /// <summary>
    /// Would <paramref name="p"/> see <paramref name="u"/> if it stood on <paramref name="at"/>, with
    /// everything else as it is? (The display asks this for the tile a moving unit's sprite is on.)
    /// </summary>
    public static bool SeesAt(Player p, Unit u, HexTile at) {
        if (!On || p == null || u == null) return true;
        if (u.owner == p) return true;
        if (at == null) return false;
        if (Revealed(u, p)) return true;
        Sight s = SightOf(p);
        return s != null && Spotted(s, u, at, o => o == p, b => b.owner);
    }

    /// <summary>
    /// Does <paramref name="p"/> play without the fog: an AI told to see everything
    /// (AIProfile.fogFair off)? Everything it does is then planned on the real board.
    /// </summary>
    public static bool KnowsAll(Player p) {
        if (p == null) return false;
        BoardView v = BoardView.current;
        if (v != null) return v.knowsAll.Contains(p);   // worked out when the copy was made (Unity is not asked off the main thread)
        AIPlayer ai = p.GetComponent<AIPlayer>();
        return ai != null && ai.enabled && !ai.Profile.fogFair;
    }

    /// <summary>Can units of <paramref name="attacker"/> fire on <paramref name="target"/>: is it seen?</summary>
    public static bool CanTarget(Player attacker, Unit target) {
        return Sees(attacker, target);
    }

    /// <summary>
    /// The unit <paramref name="p"/> knows to be on <paramref name="t"/>: its occupier when seen, else
    /// nobody. (On a concealed board, the occupier as concealed.)
    /// </summary>
    public static Unit ApparentOccupier(HexTile t, Player p) {
        if (t == null) return null;
        Unit o = t.occupier;
        if (o == null || !On || p == null || current != null) return o;   // a side's copy of the board: as it has it
        return Sees(p, o) ? o : null;
    }

    /// <summary>Is <paramref name="u"/> unseen by every enemy still in the game (its owner sees a stealthy one half-faded)?</summary>
    public static bool HiddenFromEnemies(Unit u) {
        if (!On || u == null || u.owner == null || GameController.gc == null) return false;
        bool any = false;
        foreach (Player q in GameController.gc.players) {
            if (q == null || q == u.owner || q.eliminated) continue;
            any = true;
            if (Sees(q, u)) return false;
        }
        return any;
    }

    // ------------------------------------------------------------------ revealed by firing

    static readonly Dictionary<Unit, HashSet<Player>> revealed = new Dictionary<Unit, HashSet<Player>>();

    /// <summary><paramref name="u"/> gave itself away to <paramref name="to"/> (it fired on them, or they ran into it): seen by them until the end of their next turn (or of this one).</summary>
    public static void Reveal(Unit u, Player to) {
        if (u == null || to == null || u.owner == to) return;
        HashSet<Player> set;
        if (!revealed.TryGetValue(u, out set)) { set = new HashSet<Player>(); revealed[u] = set; }
        if (set.Add(to)) Changed();
    }

    public static bool Revealed(Unit u, Player to) {
        HashSet<Player> set;
        return u != null && revealed.TryGetValue(u, out set) && set.Contains(to);
    }

    /// <summary>The end of <paramref name="p"/>'s turn (GameController.EndTurn): what was revealed to it is not any more.</summary>
    public static void OnTurnEnded(Player p) {
        List<Unit> gone = new List<Unit>();
        foreach (KeyValuePair<Unit, HashSet<Player>> kv in revealed) {
            kv.Value.Remove(p);
            if (kv.Value.Count == 0 || kv.Key == null) gone.Add(kv.Key);
        }
        foreach (Unit u in gone) revealed.Remove(u);
        Turn++;
        Changed();
    }

    // ------------------------------------------------------------------ memory

    /// <summary>An enemy unit as it was last seen.</summary>
    public class Sighting {
        public HexTile tile;
        public int strength;
        public int turn;
    }

    /// <summary>A building as it was last seen.</summary>
    public class BuildingSighting {
        public Player owner, capturing;
        public int points;
    }

    static readonly Dictionary<Player, Dictionary<Unit, Sighting>> units = new Dictionary<Player, Dictionary<Unit, Sighting>>();
    static readonly Dictionary<Player, Dictionary<HexTile, int>> tilesSeen = new Dictionary<Player, Dictionary<HexTile, int>>();
    static readonly Dictionary<Player, Dictionary<Building, BuildingSighting>> buildings = new Dictionary<Player, Dictionary<Building, BuildingSighting>>();

    static Dictionary<K, V> For<K, V>(Dictionary<Player, Dictionary<K, V>> all, Player p) {
        Dictionary<K, V> d;
        if (!all.TryGetValue(p, out d)) { d = new Dictionary<K, V>(); all[p] = d; }
        return d;
    }

    static void Remember(Player p) {
        Sight s = sights[p];
        Dictionary<HexTile, int> seenTiles = For(tilesSeen, p);
        foreach (HexTile t in s.cover.Keys) seenTiles[t] = Turn;

        Dictionary<Unit, Sighting> mem = For(units, p);
        if (UnitController.uc != null) {
            foreach (Unit u in UnitController.uc.units) {
                if (u == null || u.owner == p || u.tile == null || u.Strength <= 0) continue;
                if (!Sees(p, u)) continue;
                Sighting g;
                if (!mem.TryGetValue(u, out g)) { g = new Sighting(); mem[u] = g; }
                g.tile = u.tile; g.strength = u.Strength; g.turn = Turn;
            }
        }
        // Forget the dead, and anyone seen not to be where they were last seen.
        List<Unit> drop = new List<Unit>();
        foreach (KeyValuePair<Unit, Sighting> kv in mem) {
            Unit u = kv.Key;
            if (u == null || u.Strength <= 0 || u.tile == null) { drop.Add(u); continue; }
            if (kv.Value.turn != Turn && s.cover.ContainsKey(kv.Value.tile) && !(kv.Value.tile.occupier == u && Sees(p, u))) drop.Add(u);
        }
        foreach (Unit u in drop) mem.Remove(u);

        Dictionary<Building, BuildingSighting> bm = For(buildings, p);
        for (int i = 0; i < Building.all.Count; i++) {
            Building b = Building.all[i];
            if (b == null) continue;
            BuildingSighting g;
            bool known = bm.TryGetValue(b, out g);
            bool look = !known || b.owner == p || g.owner == p;
            if (!look) foreach (HexTile t in b.ServiceTiles()) if (s.cover.ContainsKey(t)) { look = true; break; }
            if (!look) continue;
            if (!known) { g = new BuildingSighting(); bm[b] = g; }
            g.owner = b.owner; g.capturing = b.capturingPlayer; g.points = b.capturePoints;
        }
    }

    /// <summary>Where <paramref name="p"/> last saw <paramref name="u"/>, if it remembers it.</summary>
    public static Sighting LastSeen(Player p, Unit u) {
        Refresh();
        Dictionary<Unit, Sighting> mem;
        Sighting g;
        return p != null && u != null && units.TryGetValue(p, out mem) && mem.TryGetValue(u, out g) ? g : null;
    }

    /// <summary>The turn <paramref name="p"/> last saw tile <paramref name="t"/> (-1: never).</summary>
    public static int TileLastSeen(Player p, HexTile t) {
        Knowledge k = current;
        Dictionary<HexTile, int> d;
        int turn;
        if (k != null && k.player == p && k.tileLastSeen != null) return t != null && k.tileLastSeen.TryGetValue(t, out turn) ? turn : -1;
        Refresh();
        return p != null && t != null && tilesSeen.TryGetValue(p, out d) && d.TryGetValue(t, out turn) ? turn : -1;
    }

    static BuildingSighting BuildingAsSeen(Player p, Building b) {
        if (!On || p == null || b == null) return null;
        if (BoardView.current == null) Refresh();
        Dictionary<Building, BuildingSighting> bm;
        BuildingSighting g;
        return buildings.TryGetValue(p, out bm) && bm.TryGetValue(b, out g) ? g : null;
    }

    /// <summary>The owner of <paramref name="b"/> as <paramref name="p"/> last saw it (the real owner with fog off).</summary>
    public static Player OwnerAsSeen(Player p, Building b) {
        Knowledge k = current;
        Player o;
        if (k != null && k.player == p && k.ownerSeen != null && b != null && k.ownerSeen.TryGetValue(b, out o)) return o;
        BuildingSighting g = BuildingAsSeen(p, b);
        return g != null ? g.owner : b != null ? b.owner : null;
    }

    /// <summary>The owner of <paramref name="b"/> as the screen shows it (the viewer's memory).</summary>
    public static Player ShownOwner(Building b) {
        Player v = Viewer;
        return v != null ? OwnerAsSeen(v, b) : b != null ? b.owner : null;
    }

    /// <summary>Who is capturing <paramref name="b"/> and how far, as the screen shows it.</summary>
    public static void ShownCapture(Building b, out Player capturing, out int points) {
        BuildingSighting g = BuildingAsSeen(Viewer, b);
        capturing = g != null ? g.capturing : b != null ? b.capturingPlayer : null;
        points = g != null ? g.points : b != null ? b.capturePoints : 0;
    }

    /// <summary>A new game: forget everything (memory, reveals, the turn count).</summary>
    public static void Reset() {
        sights = new Dictionary<Player, Sight>(); revealed.Clear(); units.Clear(); tilesSeen.Clear(); buildings.Clear();
        Turn = 0; lastHuman = null; viewerChanges++;
        Changed();
    }

    // ------------------------------------------------------------------ the screen

    static Player lastHuman;

    /// <summary>
    /// Whose view the screen shows: GameController.fogViewer when set (a player's index), else the
    /// human player (the one whose turn it is, when several share the screen; during an AI's turn,
    /// the last human to play). Null: fog is off, the level editor is open, or nobody is human
    /// (everything is shown).
    /// </summary>
    public static Player Viewer {
        get {
            GameController gc = GameController.gc;
            if (!On || gc == null || gc.players == null) return null;
            // Asked many times a frame (every unit, building and tile): worked out once per frame
            // and whose turn it is.
            if (viewerFrame == Time.frameCount && viewerTurn == gc.activePlayer && viewerVersion == viewerChanges) return viewerCached;
            viewerCached = FindViewer(gc);
            viewerFrame = Time.frameCount; viewerTurn = gc.activePlayer; viewerVersion = viewerChanges;
            return viewerCached;
        }
    }

    static Player viewerCached, viewerTurn;
    static int viewerFrame = -1, viewerVersion, viewerChanges;

    static Player FindViewer(GameController gc) {
        {
            if (LevelEditor.singleton != null && (LevelEditor.singleton.editing || LevelEditor.singleton.unit_placement || LevelEditor.singleton.building_placement)) return null;
            if (gc.fogViewer >= 0 && gc.fogViewer < gc.players.Count) return gc.players[gc.fogViewer];
            if (gc.activePlayer != null && IsHuman(gc.activePlayer)) lastHuman = gc.activePlayer;
            if (lastHuman != null && gc.players.Contains(lastHuman)) return lastHuman;
            foreach (Player p in gc.players) if (p != null && IsHuman(p)) { lastHuman = p; return p; }
            return null;
        }
    }

    public static bool IsHuman(Player p) {
        if (p == null) return false;
        AIPlayer ai = p.GetComponent<AIPlayer>();
        if (ai != null && ai.enabled) return false;
        foreach (MonoBehaviour m in p.GetComponents<MonoBehaviour>())
            if (m != null && m.enabled && m.GetType().Name == "BaselineAIPlayer") return false;
        return true;
    }

    /// <summary>
    /// Is <paramref name="u"/> shown to the viewer, judged where its sprite is (a unit walking out of
    /// the fog appears as it steps into sight, one walking into it fades as it goes)?
    /// </summary>
    public static bool ShownToViewer(Unit u) {
        Player v = Viewer;
        if (v == null || u == null || u.owner == v) return true;
        if (u.trasnporter != null && u.trasnporter != u) return ShownToViewer(u.trasnporter);
        if (u.tile == null) return false;
        return SeesAt(v, u, u.ShownTile);
    }

    // ------------------------------------------------------------------ a side's knowledge

    /// <summary>The knowledge the board copy entered on this thread stands for, if any (see <see cref="Conceal"/>).</summary>
    public static Knowledge current { get { BoardView v = BoardView.current; return v != null ? v.Fog : null; } }

    /// <summary>
    /// The board as <paramref name="p"/> knows it: a copy (BoardView) in which every enemy unit it
    /// does not see is off the board (off its tile, its tile empty), so movement, supply, reach and
    /// threat worked out on it leave it out. With <paramref name="beliefs"/>, an enemy seen in the
    /// last <paramref name="memoryTurns"/> turns stands instead where it was last seen (with the
    /// strength it had then), unless that tile is in sight now and empty, or taken.
    ///
    /// The real board is never touched. Entered (BoardView.Enter), Sees and TileVisible answer for
    /// <paramref name="p"/> as when it was made, and GameController.Targetable refuses enemies only
    /// believed in. Null (nothing to conceal) with fog off, for an AI that sees everything, or inside
    /// another copy (which already stands for someone's knowledge or the plain board). Main thread.
    /// </summary>
    public static BoardView Conceal(Player p, bool beliefs = false, int memoryTurns = 0) {
        if (!On || p == null || BoardView.current != null || KnowsAll(p)) return null;
        Refresh();
        BoardView v = BoardView.Snapshot();
        Knowledge k = new Knowledge(p);
        using (v.Enter()) k.Build(beliefs, memoryTurns);
        v.Fog = k;
        return v;
    }

    /// <summary>For using-blocks: enter <see cref="Conceal"/>'s copy, or nothing when there is nothing to conceal.</summary>
    public static IDisposable Concealing(Player p, bool beliefs = false, int memoryTurns = 0) {
        BoardView v = Conceal(p, beliefs, memoryTurns);
        return v != null ? v.Enter() : Nothing.instance;
    }

    sealed class Nothing : IDisposable {
        public static readonly Nothing instance = new Nothing();
        public void Dispose() { }
    }

    /// <summary>What one side knows, in a copy of the board made for it (<see cref="Conceal"/>).</summary>
    public sealed class Knowledge {
        public readonly Player player;
        /// <summary>Enemies seen when it was made (so targetable).</summary>
        public readonly HashSet<Unit> visible = new HashSet<Unit>();
        /// <summary>Enemies standing where they were last seen.</summary>
        public readonly HashSet<Unit> believed = new HashSet<Unit>();
        /// <summary>Tiles seen when it was made.</summary>
        public readonly HashSet<HexTile> visibleTiles = new HashSet<HexTile>();
        /// <summary>When each tile was last seen, and each building's owner as last seen (kept for a thought elsewhere; with beliefs only).</summary>
        internal Dictionary<HexTile, int> tileLastSeen;
        internal Dictionary<Building, Player> ownerSeen;
        /// <summary>What the enemy is thought to see: its units where they are thought to be, its buildings as last seen.</summary>
        readonly Sight enemySight = new Sight();

        internal Knowledge(Player p) { player = p; }

        /// <summary>Inside the copy: take off what is not seen, put back what is believed.</summary>
        internal void Build(bool beliefs, int memoryTurns) {
            Sight mine = SightOf(player);
            foreach (HexTile t in mine.cover.Keys) visibleTiles.Add(t);
            if (beliefs) {
                Dictionary<HexTile, int> d;
                tileLastSeen = tilesSeen.TryGetValue(player, out d) ? new Dictionary<HexTile, int>(d) : new Dictionary<HexTile, int>();
                ownerSeen = new Dictionary<Building, Player>();
                foreach (Building b in Building.all) if (b != null) ownerSeen[b] = OwnerAsSeenNow(player, b);
            }
            List<Unit> all = UnitController.uc != null ? UnitController.uc.units : new List<Unit>();
            List<Unit> toBelieve = new List<Unit>();
            foreach (Unit u in all) {
                if (u == null || u.owner == player || u.tile == null) continue;
                if (u.Strength > 0 && Sees(player, u)) { visible.Add(u); continue; }
                if (u.tile.occupier == u) u.tile.occupier = null;
                u.tile = null;
                if (beliefs && u.trasnporter == null && u.Strength > 0) toBelieve.Add(u);
            }
            foreach (Unit u in toBelieve) {
                Sighting g = LastSeen(player, u);
                if (g == null || g.tile == null || Turn - g.turn > memoryTurns) continue;
                if (visibleTiles.Contains(g.tile) || g.tile.occupier != null) continue;
                g.tile.occupier = u;
                u.tile = g.tile;
                u.Strength = Mathf.Max(1, g.strength);
                believed.Add(u);
            }
            // The enemy's sight as known: its units where they are thought to be, its buildings as last seen.
            foreach (Player q in GameController.gc.players) {
                if (q == null || q == player) continue;
                Player me = player;
                enemySight.Merge(Compute(q, b => OwnerAsSeenNow(me, b)));
            }
        }

        /// <summary>
        /// Would the enemy see <paramref name="u"/> (ours) on <paramref name="at"/>, as far as we know:
        /// next to an enemy unit we know of, on a building of theirs, or in their sight within its stealth.
        /// </summary>
        public bool EnemySees(Unit u, HexTile at) {
            if (u == null || at == null) return true;
            Player me = player;
            return Spotted(enemySight, u, at, o => o != null && o != me, b => OwnerAsSeen(me, b));
        }
    }

    /// <summary>OwnerAsSeen from the shared memory (main thread), whatever copy is entered.</summary>
    static Player OwnerAsSeenNow(Player p, Building b) {
        BuildingSighting g = null;
        Dictionary<Building, BuildingSighting> bm;
        if (On && p != null && b != null && buildings.TryGetValue(p, out bm)) bm.TryGetValue(b, out g);
        return g != null ? g.owner : b != null ? b.owner : null;
    }
}
