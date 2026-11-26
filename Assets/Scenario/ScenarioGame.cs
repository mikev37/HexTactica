using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Which units a trigger or action means: whose, what kind, what name, where.</summary>
public struct UnitFilter {
    public long side;
    public string kind, tag;
    public bool inArea;
    public int x, y, radius;
    /// <summary>Only units on these terrains (comma-separated names; empty: any), and on less cover than coverBelow (0: any).</summary>
    public string terrain;
    public int coverBelow;
    /// <summary>With inArea: outside the area instead of within it.</summary>
    public bool outside;

    public static UnitFilter Of(ScenarioTrigger t) {
        bool area = t.InArea || t.Condition == TriggerCondition.UnitEnters || t.Condition == TriggerCondition.UnitsInArea;
        return new UnitFilter { side = t.Side, kind = t.UnitKind, tag = t.UnitTag, inArea = area, x = t.TileX, y = t.TileY, radius = t.Radius, terrain = t.OnTerrain, coverBelow = t.CoverBelow, outside = t.OutsideArea && area };
    }

    public static UnitFilter Of(TriggerAction a) {
        return new UnitFilter { side = a.UnitSide, kind = a.UnitKind, tag = a.UnitTag, inArea = a.InArea, x = a.TileX, y = a.TileY, radius = a.Radius };
    }
}

/// <summary>Which buildings a trigger or action means: whose, what kind, where.</summary>
public struct BuildingFilter {
    public long side;
    public string kind;
    public bool inArea;
    public int x, y, radius;
    public bool outside;

    /// <summary>A trigger's building filter. Its Side is not the owner here (Building Captured: who took it; Building Owned: checked apart).</summary>
    public static BuildingFilter Of(ScenarioTrigger t) {
        return new BuildingFilter { side = TriggerSide.Any, kind = t.BuildingKind, inArea = t.InArea, x = t.TileX, y = t.TileY, radius = t.Radius, outside = t.OutsideArea && t.InArea };
    }

    public static BuildingFilter Of(TriggerAction a) {
        return new BuildingFilter { side = a.BuildingSide, kind = a.BuildingKind, inArea = a.InArea, x = a.TileX, y = a.TileY, radius = a.Radius };
    }
}

/// <summary>
/// Everything a scenario's script (ScenarioScript) may read of the game and do to it, in one place: the script never
/// reaches into the game anywhere else. Queries read the live board; actions call the same methods play uses
/// (Unit.ChangeOwner, Building.ChangeOwner, HexTile.DestroyTerrain, GameController.EndGame, ...). What is shown on
/// screen (the camera, markers, music) goes through hooks the presentation sets, so the rules run headless.
/// </summary>
public static class ScenarioGame {

    // ------------------------------------------------------------------ presentation hooks (null: nothing shown)

    /// <summary>Move the camera to a world point, zoom (orthographic size; 0 keeps it), over seconds (CameraRig).</summary>
    public static Action<Vector3, float, float> CameraFocusHook;
    /// <summary>The camera on the whole board (CameraRig).</summary>
    public static Action CameraFrameHook;
    /// <summary>Show (or move) a marker at a world point (ScenarioMarkers).</summary>
    public static Action<ShownMarker, Vector3> ShowMarkerHook;
    /// <summary>Remove the marker with an id; empty: every marker (ScenarioMarkers).</summary>
    public static Action<string> HideMarkerHook;
    /// <summary>Play a special track, looping or not; false if there is no such track (MusicManager).</summary>
    public static Func<string, bool, bool> PlayMusicHook;
    /// <summary>Back to the game's own music (MusicManager).</summary>
    public static Action StopMusicHook;

    // ------------------------------------------------------------------ queries

    public static GameController Game { get { return GameController.gc; } }

    /// <summary>The day as the screen shows it (from 1).</summary>
    public static int Day { get { return Game != null ? Game.days + 1 : 0; } }

    public static IEnumerable<Player> Players {
        get { if (Game != null && Game.players != null) foreach (Player p in Game.players) if (p != null) yield return p; }
    }

    /// <summary>Is <paramref name="p"/> the side <paramref name="side"/> names (a player's id, or Any / Human / AI / Neutral)?</summary>
    public static bool SideIs(Player p, long side) {
        switch (side) {
            case TriggerSide.Any: return true;
            case TriggerSide.Neutral: return p == null;
            case TriggerSide.Human: return p != null && FogOfWar.IsHuman(p);
            case TriggerSide.AI: return p != null && !FogOfWar.IsHuman(p) && !p.bystander;
            default: return p != null && p.id == side;
        }
    }

    /// <summary>The one player <paramref name="side"/> means: that id, or the first side a person (Human) or an AI plays. Null for Neutral, Any, or none.</summary>
    public static Player Resolve(long side) {
        if (side == TriggerSide.Any || side == TriggerSide.Neutral) return null;
        foreach (Player p in Players) if (!p.eliminated && SideIs(p, side)) return p;
        foreach (Player p in Players) if (SideIs(p, side)) return p;
        return null;
    }

    /// <summary>A side for the inspector and the logs: its name, or Any / Human / AI / Neutral.</summary>
    public static string SideName(long side) {
        switch (side) {
            case TriggerSide.Any: return "any side";
            case TriggerSide.Human: return "the player";
            case TriggerSide.AI: return "the AI";
            case TriggerSide.Neutral: return "nobody";
        }
        foreach (Player p in Players) if (p.id == side) return p.personal_name;
        return "side " + side;
    }

    public static HexTile Tile(int x, int y) { return HexGenerator.hg != null ? HexGenerator.hg.get(x, y, -x - y) : null; }

    /// <summary>Steps between two tiles by their cube coordinates.</summary>
    public static int Distance(int ax, int ay, int bx, int by) {
        int az = -ax - ay, bz = -bx - by;
        return Mathf.Max(Mathf.Abs(ax - bx), Mathf.Max(Mathf.Abs(ay - by), Mathf.Abs(az - bz)));
    }

    /// <summary>The middle of a tile's hex face above its root (where its highlight sits: HexTile's UI child).</summary>
    const float FaceAbove = 1.88f;

    /// <summary>The middle of a tile's hex face in the world: the board's tile, or (no board yet, in the editor) where the board would put it (HexGenerator's layout).</summary>
    public static Vector3 TilePosition(int x, int y) {
        HexTile t = Tile(x, y);
        if (t != null) return t.overlay != null ? new Vector3(t.overlay.transform.position.x, t.overlay.transform.position.y, t.transform.position.z) : t.transform.position + Vector3.up * FaceAbove;
        Vector3 origin = HexGenerator.hg != null ? HexGenerator.hg.transform.position : Vector3.zero;
        int z = -x - y;
        int j = z + (x - (x & 1)) / 2;
        float offset = x % 2 == 0 ? 3.25f : 0f;
        return origin + new Vector3(x * 4f * 2f / 1.5f, j * 3.25f * 2f - offset + FaceAbove, 0f);
    }

    static bool KindIs(string name, string display, string kind) {
        if (string.IsNullOrEmpty(kind)) return true;
        string k = kind.Trim();
        return string.Equals(name, k, StringComparison.OrdinalIgnoreCase) || string.Equals(display, k, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Is <paramref name="u"/> of this kind (its prefab's name, or the name a player sees: "Grunt" for a faction's grunt)?</summary>
    public static bool IsKind(Unit u, string kind) { return u != null && KindIs(GameController.UnitName(u), GameController.DisplayName(u, u.owner), kind); }

    public static bool Matches(Unit u, UnitFilter f) {
        if (u == null || u.RealStrength <= 0) return false;
        return MatchesAt(u, u.RealTile, f);
    }

    /// <summary>Would <paramref name="u"/> match <paramref name="f"/> if it stood on <paramref name="at"/>?</summary>
    public static bool MatchesAt(Unit u, HexTile at, UnitFilter f) {
        if (u == null || !SideIs(u.owner, f.side)) return false;
        if (!KindIs(GameController.UnitName(u), GameController.DisplayName(u, u.owner), f.kind)) return false;
        if (!string.IsNullOrEmpty(f.tag) && !string.Equals((u.personal_name ?? "").Trim(), f.tag.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        if (f.inArea) {
            bool within = at != null && Distance(at.x, at.y, f.x, f.y) <= Mathf.Max(0, f.radius);
            if (within == f.outside) return false;
        }
        if (f.coverBelow > 0 && (at == null || at.terrain == null || at.terrain.cover >= f.coverBelow)) return false;
        if (!string.IsNullOrEmpty(f.terrain) && f.terrain.Trim().Length > 0) {
            if (at == null || at.terrain == null) return false;
            bool any = false;
            foreach (string n in f.terrain.Split(',')) if (string.Equals(n.Trim(), at.terrain.name, StringComparison.OrdinalIgnoreCase)) { any = true; break; }
            if (!any) return false;
        }
        return true;
    }

    /// <summary>The living units that match (a copy: actions may change the roster).</summary>
    public static List<Unit> Units(UnitFilter f) {
        List<Unit> r = new List<Unit>();
        if (UnitController.uc == null || UnitController.uc.units == null) return r;
        foreach (Unit u in UnitController.uc.units) if (Matches(u, f)) r.Add(u);
        return r;
    }

    public static int CountUnits(UnitFilter f, Func<Unit, bool> also = null) {
        int n = 0;
        if (UnitController.uc == null || UnitController.uc.units == null) return 0;
        foreach (Unit u in UnitController.uc.units) if (Matches(u, f) && (also == null || also(u))) n++;
        return n;
    }

    public static bool Matches(Building b, BuildingFilter f, bool checkSide = true) {
        if (b == null || b.tile == null) return false;
        if (checkSide && !SideIs(b.RealOwner, f.side)) return false;
        if (!KindIs(b.name.Replace("(Clone)", "").Trim(), b.type.ToString(), f.kind)) return false;
        if (f.inArea && (Distance(b.tile.x, b.tile.y, f.x, f.y) <= Mathf.Max(0, f.radius)) == f.outside) return false;
        return true;
    }

    public static List<Building> Buildings(BuildingFilter f) {
        List<Building> r = new List<Building>();
        foreach (Building b in Building.all) if (Matches(b, f)) r.Add(b);
        return r;
    }

    /// <summary>The living unit going by <paramref name="name"/> (its personal name), or null.</summary>
    public static Unit Named(string name) {
        if (string.IsNullOrEmpty(name) || UnitController.uc == null || UnitController.uc.units == null) return null;
        foreach (Unit u in UnitController.uc.units)
            if (u != null && u.RealStrength > 0 && string.Equals((u.personal_name ?? "").Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)) return u;
        return null;
    }

    /// <summary>Can any enemy of <paramref name="owner"/> see <paramref name="t"/> (fog of war; with no fog, every tile is seen)?</summary>
    public static bool SeenByEnemy(Player owner, HexTile t) {
        if (t == null) return false;
        if (!FogOfWar.On) return true;
        foreach (Player p in Players) if (p != null && p != owner && !p.eliminated && Player.Hostile(p, owner) && FogOfWar.TileVisible(p, t)) return true;
        return false;
    }

    /// <summary>The living unit of <paramref name="side"/> nearest <paramref name="from"/> standing in at least <paramref name="minCover"/> terrain cover, or null.</summary>
    public static Unit NearestOf(Unit from, long side, int minCover) {
        if (from == null || from.RealTile == null) return null;
        Unit best = null; int bestD = int.MaxValue;
        foreach (Unit u in Units(new UnitFilter { side = side })) {
            if (u == from || u.RealTile == null || u.owner == from.owner || u.RealTile.terrain == null || u.RealTile.terrain.cover < minCover) continue;
            int d = Distance(u.RealTile.x, u.RealTile.y, from.RealTile.x, from.RealTile.y);
            if (d < bestD) { bestD = d; best = u; }
        }
        return best;
    }

    /// <summary>The living unit of <paramref name="side"/> nearest <paramref name="from"/> left out in the open: it could use <paramref name="cover"/> cover (its concealment is that much or more: infantry) but stands on less. Null if none.</summary>
    public static Unit NearestExposed(Unit from, long side, int cover) {
        if (from == null || from.RealTile == null) return null;
        Unit best = null; int bestD = int.MaxValue;
        foreach (Unit u in Units(new UnitFilter { side = side })) {
            if (u == from || u.RealTile == null || u.owner == from.owner || u.RealTile.terrain == null || u.RealTile.terrain.cover >= cover || u.Concealment < cover) continue;
            int d = Distance(u.RealTile.x, u.RealTile.y, from.RealTile.x, from.RealTile.y);
            if (d < bestD) { bestD = d; best = u; }
        }
        return best;
    }

    /// <summary>
    /// The strongest unit of <paramref name="side"/> that <paramref name="u"/> can reach and attack this turn (Order Units,
    /// Target Fresh: "the tank goes for a fresh squad"); ties: one in <paramref name="minCover"/> terrain cover or more,
    /// then the nearest. Null if none is in reach.
    /// </summary>
    public static Unit StrongestInReach(Unit u, long side, int minCover) {
        if (u == null || u.tile == null) return null;
        List<HexTile> from = new List<HexTile> { u.tile };
        if (u.moveAndAttack) {
            HexTactica.AI.MoveField f = HexTactica.AI.AIPath.ThisTurn(u);
            foreach (HexTile t in f.Tiles) if (t != u.tile && f.ReachableThisTurn(t) && t.occupier == null && t.CanEndTurnHere(u)) from.Add(t);
        }
        Unit best = null; int bestS = -1, bestC = -1, bestD = int.MaxValue;
        foreach (Unit e in Units(new UnitFilter { side = side })) {
            if (e.owner == u.owner || e.RealTile == null || !u.CanAttack(e)) continue;
            bool reach = false;
            foreach (HexTile t in from) if (u.CanAttackFrom(t, e)) { reach = true; break; }
            if (!reach) continue;
            int st = e.RealStrength, c = e.RealTile.terrain != null && e.RealTile.terrain.cover >= minCover && minCover > 0 ? 1 : 0;
            int d = Distance(e.RealTile.x, e.RealTile.y, u.tile.x, u.tile.y);
            if (st > bestS || (st == bestS && (c > bestC || (c == bestC && d < bestD)))) { best = e; bestS = st; bestC = c; bestD = d; }
        }
        return best;
    }

    /// <summary>The tiles <paramref name="u"/> can move to and stop on this turn (not its own) with terrain cover of <paramref name="minCover"/> or more, buildings left out (Mark Reach: "the forests in reach"). The board's tiles are left as they are.</summary>
    public static List<HexTile> Reach(Unit u, int minCover) {
        List<HexTile> r = new List<HexTile>();
        if (u == null || u.tile == null || u.RealStrength <= 0) return r;
        HexTactica.AI.MoveField f = HexTactica.AI.AIPath.ThisTurn(u);
        foreach (HexTile t in f.Tiles)
            if (t != u.tile && f.ReachableThisTurn(t) && t.terrain != null && t.terrain.cover >= minCover && t.building == null && t.occupier == null && t.CanEndTurnHere(u)) r.Add(t);
        r.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        return r;
    }

    /// <summary>The living unit of <paramref name="side"/> nearest <paramref name="from"/> (not itself), or null.</summary>
    public static Unit NearestOf(Unit from, long side) {
        if (from == null || from.RealTile == null) return null;
        Unit best = null; int bestD = int.MaxValue;
        foreach (Unit u in Units(new UnitFilter { side = side })) {
            if (u == from || u.RealTile == null || u.owner == from.owner) continue;
            int d = Distance(u.RealTile.x, u.RealTile.y, from.RealTile.x, from.RealTile.y);
            if (d < bestD) { bestD = d; best = u; }
        }
        return best;
    }

    /// <summary>
    /// A scripted order, as the player gives one with clicks: <paramref name="u"/> moves (this turn's reach, the way
    /// the click-to-move flow does) to the nearest tile it can fire on <paramref name="target"/> from, and fires; if it
    /// cannot reach one, it moves as near the target (or, with none, <paramref name="goal"/>) as it can. It has then
    /// used its turn. False if it did nothing (it has used its turn already, or cannot get closer).
    /// </summary>
    public static bool Order(Unit u, Unit target, HexTile goal) { return Order(u, target, goal, null, false); }

    public static bool Order(Unit u, Unit target, HexTile goal, HexTile from) { return Order(u, target, goal, from, false); }

    /// <summary>
    /// Tiles units ordered by one Order Units action are going to (their moves play out after the order: the next unit
    /// ordered must not pick the same tile). Cleared by the script before each action.
    /// </summary>
    public static readonly HashSet<HexTile> Claimed = new HashSet<HexTile>();

    /// <summary>
    /// As above; <paramref name="from"/>: with a target, the tile to attack it from if it can get there and fire from it.
    /// A target of its own side (or an ally's) that can carry it: it gets in (walks to it and boards).
    /// </summary>
    /// <summary>As above; <paramref name="force"/>: the whole way this turn, however far (no movement or fuel limit; the fuel it had is kept).</summary>
    public static bool Order(Unit u, Unit target, HexTile goal, HexTile from, bool force) {
        if (u == null || u.tile == null || u.RealStrength <= 0 || !u.active) return false;
        int fuelWas = u.fuel;
        if (force && u.fuelMax > 0) u.fuel = 99999;
        try { return OrderNow(u, target, goal, from, force); }
        finally { if (force && u.fuelMax > 0 && u != null) u.fuel = fuelWas; }
    }

    static bool OrderNow(Unit u, Unit target, HexTile goal, HexTile from, bool force) {
        if (target != null && target != u && target.tile != null && target.owner != null && Player.Friendly(target.owner, u.owner)) {
            List<HexTile> steps = HexTile.BoardingPath(u, target, force);
            if (steps != null && u.WalkAndBoard(steps, target, true)) { u.FinishActive(); return true; }
        }
        HexTile start = u.tile;
        // (The tiles' search state only: a scripted order shows nothing on the board.)
        Func<HexTile, int> clear = x => { x.G = 0; x.parent = null; return 0; };
        HexGenerator.forAllTiles(clear);
        List<HexTile> stops = force ? start.CheckMovementRange(u, 999) : start.CheckMovementRange(u);   // (leaves the path on the tiles for CompleteMove)
        stops.RemoveAll(x => x == start || !x.CanEndTurnHere(u) || (x.occupier != null && x.occupier != u) || Claimed.Contains(x));
        stops.Insert(0, start);
        HexTile to = null;
        bool fire = false;
        if (target != null && target.tile != null && u.CanAttack(target)) {
            if (from != null && stops.Contains(from) && (from == start || u.moveAndAttack) && u.CanAttackFrom(from, target)) to = from;
            else foreach (HexTile s in stops) {
                if (s != start && !u.moveAndAttack) continue;
                if (u.CanAttackFrom(s, target) && (to == null || s.G < to.G)) to = s;
            }
            fire = to != null;
        }
        HexTile aim = target != null && target.tile != null ? target.tile : goal;
        if (to == null && aim != null) {
            int best = start.Distance(aim);
            foreach (HexTile s in stops) { int d = s.Distance(aim); if (d < best || (d == best && to != null && s.G < to.G)) { best = d; to = s; } }
        }
        if (to == null) { HexGenerator.forAllTiles(clear); return false; }
        Claimed.Add(to);
        bool arrived = true;
        if (to != start) {
            start.parent = null;
            List<HexTile> path = to.buildpath();
            HexGenerator.forAllTiles(clear);
            arrived = u.MoveAlong(path, fire, true);   // walked on screen, as the player's and the AI's moves are
        }
        HexGenerator.forAllTiles(clear);
        if (fire && arrived && u.RealStrength > 0 && target.RealStrength > 0 && target.tile != null) u.OrderAttack(target);
        if (u.RealStrength > 0) u.FinishActive();
        return true;
    }

    /// <summary>
    /// A scripted strike, as the player orders one: <paramref name="u"/> (a unit that targets ground, or whose attack
    /// lands later) strikes <paramref name="target"/> from where it stands, and has then used its turn. False if it did
    /// not (it has used its turn, it is not such a unit, or the tile is out of its range).
    /// </summary>
    public static bool Strike(Unit u, HexTile target) {
        if (u == null || u.tile == null || u.RealStrength <= 0 || !u.active || target == null) return false;
        if (!Strikes.Order(u, target)) return false;
        u.FinishActive();
        return true;
    }

    /// <summary>Is a strike (whose, by what kind of unit, aimed where) the one a trigger means?</summary>
    public static bool MatchesStrike(Strike s, UnitFilter f) {
        if (s == null || !SideIs(s.owner, f.side)) return false;
        if (!KindIs(s.kind, s.weapon != null ? GameController.DisplayName(s.weapon, s.owner) : s.kind, f.kind)) return false;
        if (f.inArea && (s.target == null || Distance(s.target.x, s.target.y, f.x, f.y) > Mathf.Max(0, f.radius))) return false;
        return true;
    }

    public static int Money(long side) { Player p = Resolve(side); return p != null ? p.money : 0; }

    /// <summary>Out of supplies: a unit that carries them and has none left loses strength every turn.</summary>
    public static bool Starving(Unit u) { return u.suppliesMax > 0 && u.supplies <= 0; }

    /// <summary>Has fuel, and <paramref name="percent"/> of a full tank or less.</summary>
    public static bool LowOnFuel(Unit u, int percent) { return u.fuelMax > 0 && u.fuel > 0 && u.fuel * 100 <= u.fuelMax * Mathf.Max(0, percent); }

    /// <summary>Standing (or carried) outside its side's supply.</summary>
    public static bool CutOff(Unit u) { HexTile t = u.RealTile; return t != null && u.owner != null && !t.isSupplied(u.owner); }

    // ------------------------------------------------------------------ actions

    public static void FocusCamera(int x, int y, float zoom, float seconds) {
        if (CameraFocusHook != null) CameraFocusHook(TilePosition(x, y), zoom, seconds);
    }

    public static void FrameBoard() { if (CameraFrameHook != null) CameraFrameHook(); }

    public static void ShowMarker(ShownMarker m) { if (ShowMarkerHook != null) ShowMarkerHook(m, TilePosition(m.TileX, m.TileY)); }

    public static void HideMarker(string id) { if (HideMarkerHook != null) HideMarkerHook(id ?? ""); }

    /// <summary>Set by UIHighlights (the UI side): outline and flash the screen element named (On), or stop the highlight of that id (off; empty id: all).</summary>
    public static System.Action<string, string, bool> HighlightUIHook;

    public static void HighlightUI(string what, string id, bool on) { if (HighlightUIHook != null) HighlightUIHook(what ?? "", id ?? "", on); }


    public static bool PlayMusic(string track, bool loop) { return PlayMusicHook == null || PlayMusicHook(track, loop); }

    public static void StopMusic() { if (StopMusicHook != null) StopMusicHook(); }

    public static void PlaySound(string name) {
        if (string.IsNullOrEmpty(name)) return;
        AudioClip c = ScenarioAssets.Load<AudioClip>(name, ScenarioDialogue.SoundFolder);
        if (c == null) { Debug.LogWarning("Scenario: no sound '" + name + "' in Resources/" + ScenarioDialogue.SoundFolder); return; }
        GameSounds.Play(c);
    }

    /// <summary><paramref name="winner"/> wins now (null: a draw).</summary>
    public static void Win(Player winner) { if (Game != null) Game.EndGame(winner); }

    /// <summary><paramref name="loser"/> loses now: the side left wins (an AI side before a human one if several are left; a draw if none is).</summary>
    public static void Lose(Player loser) {
        if (Game == null) return;
        Player winner = null;
        foreach (Player p in Players) if (p != loser && !p.eliminated && !FogOfWar.IsHuman(p)) { winner = p; break; }
        if (winner == null) foreach (Player p in Players) if (p != loser && !p.eliminated) { winner = p; break; }
        Game.EndGame(winner);
    }

    /// <summary>
    /// <paramref name="count"/> units of <paramref name="kind"/> for <paramref name="owner"/> (its faction's version),
    /// on the tile, else each on the nearest free tile it can stand on (within 4 steps). The units made.
    /// </summary>
    public static List<Unit> Spawn(Player owner, string kind, int x, int y, int count, int strength, int veterancy, string tag, bool ready) {
        return Spawn(owner, kind, x, y, count, strength, veterancy, tag, ready, false);
    }

    /// <summary>As above; <paramref name="outOfSight"/>: each on the nearest free tile no enemy of <paramref name="owner"/> can see (within 8 steps), else as above.</summary>
    public static List<Unit> Spawn(Player owner, string kind, int x, int y, int count, int strength, int veterancy, string tag, bool ready, bool outOfSight) {
        List<Unit> made = new List<Unit>();
        Unit prefab = owner != null ? LevelEditor.UnitFor(owner, kind) : null;
        HexTile at = Tile(x, y);
        if (prefab == null || at == null || UnitController.uc == null) {
            Debug.LogWarning("Scenario: cannot spawn '" + kind + "' for " + (owner != null ? owner.personal_name : "nobody") + " at (" + x + "," + y + ")"
                + (prefab == null ? ": no unit of that name" : at == null ? ": no such tile" : ""));
            return made;
        }
        for (int i = 0; i < Mathf.Max(1, count); i++) {
            HexTile free = outOfSight ? FreeTileNear(at, prefab, 8, t => !SeenByEnemy(owner, t)) : null;
            if (free == null) free = FreeTileNear(at, prefab, 4);
            if (free == null) { Debug.LogWarning("Scenario: no room to spawn '" + kind + "' near (" + x + "," + y + ")"); break; }
            Unit u = BuildingController.Spawner != null ? BuildingController.Spawner(owner, prefab, free) : UnitController.uc.CreateUnit(owner, prefab, free);
            if (u == null) break;
            u.Strength = Mathf.Clamp(strength, 1, 6);
            u.Veterancy = Mathf.Clamp(veterancy, 0, 3);
            if (!string.IsNullOrEmpty(tag)) u.personal_name = tag;
            u.active = ready;
            made.Add(u);
        }
        if (made.Count > 0 && Game != null && Game.players != null) foreach (Player p in Game.players) if (p != null) p.Resupply();
        return made;
    }

    /// <summary>The nearest tile to <paramref name="at"/> (itself first) with nothing on it that <paramref name="prefab"/> can stand on, within <paramref name="steps"/>.</summary>
    static HexTile FreeTileNear(HexTile at, Unit prefab, int steps) { return FreeTileNear(at, prefab, steps, null); }

    /// <summary>As above, only a tile <paramref name="ok"/> says yes to (null: any).</summary>
    static HexTile FreeTileNear(HexTile at, Unit prefab, int steps, Func<HexTile, bool> ok) {
        List<HexTile> ring = new List<HexTile> { at };
        HashSet<HexTile> seen = new HashSet<HexTile> { at };
        for (int d = 0; d <= steps && ring.Count > 0; d++) {
            foreach (HexTile t in ring) if (t.occupier == null && prefab.CanEnter(t) && (ok == null || ok(t))) return t;
            List<HexTile> next = new List<HexTile>();
            foreach (HexTile t in ring) if (t.neighbors != null) foreach (HexTile n in t.neighbors) if (n != null && seen.Add(n)) next.Add(n);
            ring = next;
        }
        return null;
    }

    /// <summary>
    /// Lift each unit onto (x, y), or the nearest free tile it can stand on (within 4 steps), at once (Unit.PlaceAt):
    /// nothing walked, nobody dies. <paramref name="ready"/>: it may act again this turn. How many were placed.
    /// </summary>
    /// <summary>Units Place Units has lifted since the script last looked: not a move of theirs (Unit Moved).</summary>
    public static readonly HashSet<Unit> Lifted = new HashSet<Unit>();

    public static int Place(List<Unit> units, int x, int y, bool ready) {
        HexTile at = Tile(x, y);
        int n = 0;
        if (at == null) return 0;
        foreach (Unit u in units) {
            if (u == null || u.RealStrength <= 0 || u.tile == null || u.trasnporter != null) continue;
            Lifted.Add(u);
            HexTile to = at.occupier == u ? at : FreeTileNear(at, u, 4);
            if (to == null) continue;
            u.PlaceAt(to);
            u.active = ready;
            n++;
        }
        return n;
    }

    public static void GiveUnits(List<Unit> units, Player to) { foreach (Unit u in units) if (u != null) u.ChangeOwner(to); }

    public static void GiveBuildings(List<Building> buildings, Player to) { foreach (Building b in buildings) if (b != null) b.ChangeOwner(to); }

    public static void DestroyUnits(List<Unit> units) { foreach (Unit u in units) if (u != null && u.RealStrength > 0) u.Perish(); }

    /// <summary>The units leave the board without dying (Unit.Withdraw).</summary>
    public static void WithdrawUnits(List<Unit> units) { foreach (Unit u in units) if (u != null && u.RealStrength > 0) u.Withdraw(); }

    /// <summary>A building of <paramref name="kind"/> on (x, y) for <paramref name="owner"/> (null: nobody's). Null if there is no such building or the tile is taken by one.</summary>
    public static Building PlaceBuilding(string kind, int x, int y, Player owner) {
        HexTile t = Tile(x, y);
        if (kind != null && kind.Trim() == RemoveKind) { RemoveBuilding(t); return null; }
        Building prefab = string.IsNullOrEmpty(kind) ? null : LevelEditor.loadBuilding(kind);
        if (t == null || prefab == null || t.building != null || BuildingController.bc == null) return null;
        return BuildingController.bc.Place(owner, prefab, t);
    }

    /// <summary>Place Building's kind that takes the building off the tile instead (its own ground shows again).</summary>
    public const string RemoveKind = "None";

    /// <summary>Take the building on <paramref name="t"/> off the board, leaving the ground under it. False if there is none.</summary>
    public static bool RemoveBuilding(HexTile t) {
        if (t == null || t.building == null) return false;
        Building b = t.building;
        b.Release();
        if (BuildingController.bc != null) BuildingController.bc.buildings.Remove(b);
        Building.all.Remove(b);
        if (b.gameObject != null) UnityEngine.Object.Destroy(b.gameObject);
        FogOfWar.Changed();
        return true;
    }

    /// <summary>Destroy the terrain on every tile within <paramref name="radius"/> of (x, y) that can be (one with a destroyedInto).</summary>
    public static int DestroyTiles(int x, int y, int radius) {
        int n = 0;
        foreach (HexTile t in TilesNear(x, y, radius)) if (t.terrain != null && t.terrain.destroyedInto != null) { t.DestroyTerrain(); n++; }
        return n;
    }

    public static int RepairTiles(int x, int y, int radius) {
        int n = 0;
        foreach (HexTile t in TilesNear(x, y, radius)) if (t.RepairFully()) n++;
        if (n > 0) {
            if (Game != null && Game.players != null) foreach (Player p in Game.players) if (p != null) p.Resupply();
            FogOfWar.Changed();
        }
        return n;
    }

    public static List<HexTile> TilesNear(int x, int y, int radius) {
        List<HexTile> r = new List<HexTile>();
        if (HexGenerator.hg == null || HexGenerator.hg.tiles == null) return r;
        foreach (HexTile t in HexGenerator.hg.tiles) if (t != null && Distance(t.x, t.y, x, y) <= Mathf.Max(0, radius)) r.Add(t);
        return r;
    }

    public static void DestroyBuildings(List<Building> buildings) {
        foreach (Building b in buildings) if (b != null && !b.Destroyed) {
            if (b.health <= 0) { Debug.LogWarning("Scenario: " + b.name + " cannot be destroyed (it has no health)"); continue; }
            b.Damage(b.health - b.damage);
        }
    }

    public static void RepairBuildings(List<Building> buildings) { foreach (Building b in buildings) if (b != null && b.damage > 0) b.Repair(b.damage); }

    static int Apply(int now, int value, ValueMode mode) { return mode == ValueMode.Add ? now + value : value; }

    public static void SetVeterancy(List<Unit> units, int value, ValueMode mode) {
        foreach (Unit u in units) if (u != null) u.Veterancy = Mathf.Clamp(Apply(u.Veterancy, value, mode), 0, 3);
    }

    public static void SetStat(List<Unit> units, UnitStat stat, int value, ValueMode mode) {
        bool supplyChanged = false;
        foreach (Unit u in units) {
            if (u == null || u.RealStrength <= 0) continue;
            switch (stat) {
                case UnitStat.Health: {
                    int s = Mathf.Clamp(Apply(u.Strength, value, mode), 0, 6);
                    if (s <= 0) { u.Perish(); continue; }
                    u.Strength = s;
                    u.suppressed = Mathf.Clamp(u.suppressed, 0, s);
                    if (u.Hub != null) supplyChanged = true;
                    break;
                }
                case UnitStat.Ammo: u.ammo = Mathf.Clamp(Apply(u.ammo, value, mode), 0, Mathf.Max(0, u.ammoMax)); break;
                case UnitStat.Fuel: u.fuel = Mathf.Clamp(Apply(u.fuel, value, mode), 0, Mathf.Max(0, u.fuelMax)); break;
                case UnitStat.Supplies: u.supplies = Mathf.Clamp(Apply(u.supplies, value, mode), 0, Mathf.Max(0, u.suppliesMax)); break;
            }
        }
        if (supplyChanged && Game != null && Game.players != null) foreach (Player p in Game.players) if (p != null) p.Resupply();
    }

    public static void GiveMoney(Player p, int value, ValueMode mode) {
        if (p == null) return;
        p.money = Mathf.Max(0, mode == ValueMode.Set ? value : p.money + value);
    }

    /// <summary>Buying units at buildings on or off (a save keeps it: LevelEditor.BoardJson writes the game's setting).</summary>
    public static void SetPurchases(bool on) { if (Game != null) Game.purchasesAllowed = on; }

    /// <summary>Fog of war on or off, for everyone (a save keeps it), seen again at once.</summary>
    public static void SetFog(bool on) {
        if (Game == null || Game.fogOfWar == on) return;
        Game.fogOfWar = on;
        FogOfWar.Reset();
        FogOfWar.Changed();
        FogOfWar.Refresh();
    }

    public static void ReadyUnits(List<Unit> units, bool ready) { foreach (Unit u in units) if (u != null) u.active = ready; }
}
