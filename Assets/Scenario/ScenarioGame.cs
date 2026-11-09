using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Which units a trigger or action means: whose, what kind, what name, where.</summary>
public struct UnitFilter {
    public long side;
    public string kind, tag;
    public bool inArea;
    public int x, y, radius;

    public static UnitFilter Of(ScenarioTrigger t) {
        bool area = t.InArea || t.Condition == TriggerCondition.UnitEnters || t.Condition == TriggerCondition.UnitsInArea;
        return new UnitFilter { side = t.Side, kind = t.UnitKind, tag = t.UnitTag, inArea = area, x = t.TileX, y = t.TileY, radius = t.Radius };
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

    /// <summary>A trigger's building filter. Its Side is not the owner here (Building Captured: who took it; Building Owned: checked apart).</summary>
    public static BuildingFilter Of(ScenarioTrigger t) {
        return new BuildingFilter { side = TriggerSide.Any, kind = t.BuildingKind, inArea = t.InArea, x = t.TileX, y = t.TileY, radius = t.Radius };
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
            case TriggerSide.AI: return p != null && !FogOfWar.IsHuman(p);
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

    /// <summary>Where a tile is in the world: the board's tile, or (no board yet, in the editor) where the board would put it (HexGenerator's layout).</summary>
    public static Vector3 TilePosition(int x, int y) {
        HexTile t = Tile(x, y);
        if (t != null) return t.transform.position;
        Vector3 origin = HexGenerator.hg != null ? HexGenerator.hg.transform.position : Vector3.zero;
        int z = -x - y;
        int j = z + (x - (x & 1)) / 2;
        float offset = x % 2 == 0 ? 3.25f : 0f;
        return origin + new Vector3(x * 4f * 2f / 1.5f, j * 3.25f * 2f - offset, 0f);
    }

    static bool KindIs(string name, string display, string kind) {
        if (string.IsNullOrEmpty(kind)) return true;
        string k = kind.Trim();
        return string.Equals(name, k, StringComparison.OrdinalIgnoreCase) || string.Equals(display, k, StringComparison.OrdinalIgnoreCase);
    }

    public static bool Matches(Unit u, UnitFilter f) {
        if (u == null || u.RealStrength <= 0) return false;
        return MatchesAt(u, u.RealTile, f);
    }

    /// <summary>Would <paramref name="u"/> match <paramref name="f"/> if it stood on <paramref name="at"/>?</summary>
    public static bool MatchesAt(Unit u, HexTile at, UnitFilter f) {
        if (u == null || !SideIs(u.owner, f.side)) return false;
        if (!KindIs(GameController.UnitName(u), GameController.DisplayName(u, u.owner), f.kind)) return false;
        if (!string.IsNullOrEmpty(f.tag) && !string.Equals((u.personal_name ?? "").Trim(), f.tag.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        if (f.inArea && (at == null || Distance(at.x, at.y, f.x, f.y) > Mathf.Max(0, f.radius))) return false;
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
        if (f.inArea && Distance(b.tile.x, b.tile.y, f.x, f.y) > Mathf.Max(0, f.radius)) return false;
        return true;
    }

    public static List<Building> Buildings(BuildingFilter f) {
        List<Building> r = new List<Building>();
        foreach (Building b in Building.all) if (Matches(b, f)) r.Add(b);
        return r;
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

    public static bool PlayMusic(string track, bool loop) { return PlayMusicHook == null || PlayMusicHook(track, loop); }

    public static void StopMusic() { if (StopMusicHook != null) StopMusicHook(); }

    public static void PlaySound(string name) {
        if (string.IsNullOrEmpty(name)) return;
        AudioClip c = Resources.Load<AudioClip>(ScenarioDialogue.SoundFolder + name);
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
        List<Unit> made = new List<Unit>();
        Unit prefab = owner != null ? LevelEditor.UnitFor(owner, kind) : null;
        HexTile at = Tile(x, y);
        if (prefab == null || at == null || UnitController.uc == null) {
            Debug.LogWarning("Scenario: cannot spawn '" + kind + "' for " + (owner != null ? owner.personal_name : "nobody") + " at (" + x + "," + y + ")"
                + (prefab == null ? ": no unit of that name" : at == null ? ": no such tile" : ""));
            return made;
        }
        for (int i = 0; i < Mathf.Max(1, count); i++) {
            HexTile free = FreeTileNear(at, prefab, 4);
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
    static HexTile FreeTileNear(HexTile at, Unit prefab, int steps) {
        List<HexTile> ring = new List<HexTile> { at };
        HashSet<HexTile> seen = new HashSet<HexTile> { at };
        for (int d = 0; d <= steps && ring.Count > 0; d++) {
            foreach (HexTile t in ring) if (t.occupier == null && prefab.CanEnter(t)) return t;
            List<HexTile> next = new List<HexTile>();
            foreach (HexTile t in ring) if (t.neighbors != null) foreach (HexTile n in t.neighbors) if (n != null && seen.Add(n)) next.Add(n);
            ring = next;
        }
        return null;
    }

    public static void GiveUnits(List<Unit> units, Player to) { foreach (Unit u in units) if (u != null) u.ChangeOwner(to); }

    public static void GiveBuildings(List<Building> buildings, Player to) { foreach (Building b in buildings) if (b != null) b.ChangeOwner(to); }

    public static void DestroyUnits(List<Unit> units) { foreach (Unit u in units) if (u != null && u.RealStrength > 0) u.Perish(); }

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

    public static void ReadyUnits(List<Unit> units, bool ready) { foreach (Unit u in units) if (u != null) u.active = ready; }
}
