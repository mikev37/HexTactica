using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// The statistics of the game being played, gathered as it goes from the same events the game log records
/// (purchases, attacks, deaths, captures, the start of every turn), for the post-game screen (GameInsights reads them).
///
/// Starts when a board is loaded (ScenarioLoader: a scenario, a save); a saved game keeps its statistics beside the
/// save (SaveBeside, LoadBeside: "&lt;save&gt;.stats.json"), so a game played over several sittings is read as one.
/// At the end of the game the report is also written into the game log's folder (GameLog.Folder).
///
/// Unit kinds go by the prefab's name less a trailing " Variant"; a faction's version that differs from the base unit
/// only in looks (Faction.Cosmetic) is counted as the base unit.
/// </summary>
[Serializable]
public class GameStats {

    [Serializable] public class Side { public long id; public string name; public string faction; public bool ai; }

    [Serializable] public class Kind {
        public string name; public int cost; public int range; public int minRange; public int maxStrength;
        public Locomotion locomotion; public bool supply;
        public bool Air { get { return LocomotionRules.IsAirborne(locomotion); } }
    }

    /// <summary>One side at the start of a turn.</summary>
    [Serializable] public class SideSnap {
        public long id;
        /// <summary>What its units are worth as they stand: each unit's cost times the share of its full strength it has (HQs not counted).</summary>
        public int material;
        public int units, cities, buildings, money, income;
        /// <summary>Where its units stand on the line from the first side's HQ (0) to the second's (1), on average; -1 if none.</summary>
        public float front = -1f;
    }

    [Serializable] public class Snap { public int round; public long mover; public List<SideSnap> sides = new List<SideSnap>(); }
    [Serializable] public class Count { public long side; public string kind; public int n; }
    [Serializable] public class Buy { public int round; public long side; public string kind; public int cost; }

    /// <summary>A unit lost: to whom, how, and when.</summary>
    [Serializable] public class Loss {
        public int round; public long side; public string kind; public int cost; public int strength;
        /// <summary>"attack" (destroyed by an attacker), "return fire" (destroyed attacking), "supply" (starved), "fuel" (ditched), "aboard" (went down with its transport), "other".</summary>
        public string cause = "other";
        public long killerSide = -1; public string killerKind = ""; public long turnOf = -1;
    }

    [Serializable] public class Attack {
        public int round; public long side; public string kind; public long targetSide; public string targetKind;
        public int dealt, taken, distance, targetStrength; public bool killed, attackerDied; public long turnOf;
    }

    [Serializable] public class Capture { public int round; public long side; public string building; public long from = -1; }

    public string source = "";
    public string started = "";
    public int axisLength;
    public List<Side> sides = new List<Side>();
    public List<Kind> kinds = new List<Kind>();
    public List<Count> start = new List<Count>();
    /// <summary>The kinds each side could buy at some point (at a building it held, with the money aside).</summary>
    public List<Count> canBuy = new List<Count>();
    public List<Snap> snaps = new List<Snap>();
    public List<Buy> buys = new List<Buy>();
    public List<Loss> losses = new List<Loss>();
    public List<Attack> attacks = new List<Attack>();
    public List<Capture> captures = new List<Capture>();
    public bool over;
    public long winner = -1;
    public int lastRound;

    /// <summary>The game being recorded, or null.</summary>
    public static GameStats Current { get; private set; }

    [NonSerialized] Vector3 hqA, hqB;
    // (Made when first used: a record read back from JSON may not have run its initializers.)
    [NonSerialized] Dictionary<Unit, Loss> dyingNow;
    [NonSerialized] HashSet<Unit> ditchedNow;
    Dictionary<Unit, Loss> dying { get { return dyingNow ?? (dyingNow = new Dictionary<Unit, Loss>()); } }
    HashSet<Unit> ditched { get { return ditchedNow ?? (ditchedNow = new HashSet<Unit>()); } }

    // ------------------------------------------------------------------ recording

    /// <summary>Start recording the board just loaded from <paramref name="from"/> (the file's path, for the report).</summary>
    public static GameStats Begin(string from) {
        Stop();
        GameStats s = new GameStats { source = from ?? "", started = DateTime.Now.ToString("yyyy-MM-dd HH:mm") };
        s.Setup(true);
        Current = s;
        Subscribe();
        return s;
    }

    /// <summary>Stop recording (the statistics so far stay readable in Current).</summary>
    public static void Stop() {
        Unit.Attacked -= OnAttack;
        Unit.Died -= OnDied;
        Unit.Ditched -= OnDitch;
        Building.OwnerChanged -= OnOwnerChanged;
        BuildingController.Produced -= OnProduced;
        GameController.TurnStarted -= OnTurnStarted;
    }

    static void Subscribe() {
        Unit.Attacked += OnAttack;
        Unit.Died += OnDied;
        Unit.Ditched += OnDitch;
        Building.OwnerChanged += OnOwnerChanged;
        BuildingController.Produced += OnProduced;
        GameController.TurnStarted += OnTurnStarted;
    }

    /// <summary>The sides, the axis between the first two HQs, and (a new game) the units each side starts with and a first snapshot.</summary>
    void Setup(bool fresh) {
        GameController gc = GameController.gc;
        if (gc == null) return;
        if (fresh) {
            sides.Clear();
            foreach (Player p in gc.players) {
                if (p == null) continue;
                sides.Add(new Side { id = p.id, name = p.personal_name, faction = p.faction != null ? p.faction.name : "", ai = AIControl.Of(p) != null });
            }
        } else {
            // A save loaded: who plays each side now (a side may have been handed to an AI or back).
            foreach (Player p in gc.players) { Side sd = SideOf(p); if (sd != null) sd.ai = AIControl.Of(p) != null; }
        }
        Building a = sides.Count > 0 ? GameController.HQOf(PlayerOf(sides[0].id)) : null;
        Building b = sides.Count > 1 ? GameController.HQOf(PlayerOf(sides[1].id)) : null;
        hqA = a != null && a.tile != null ? Cube(a.tile) : Vector3.zero;
        hqB = b != null && b.tile != null ? Cube(b.tile) : Vector3.zero;
        axisLength = a != null && b != null ? Distance(hqA, hqB) : 0;
        if (!fresh) return;
        foreach (Player p in gc.players) {
            if (p == null) continue;
            foreach (Unit u in p.units) {
                if (u == null || u.Strength <= 0 || IsHQUnit(u)) continue;
                string k = KindOf(u);
                Count c = start.Find(x => x.side == p.id && x.kind == k);
                if (c == null) start.Add(c = new Count { side = p.id, kind = k });
                c.n++;
            }
        }
        TakeSnap(gc.activePlayer);
    }

    static void OnTurnStarted(Player p) {
        GameStats s = Current;
        GameController gc = GameController.gc;
        if (s == null || gc == null) return;
        s.TakeSnap(p);
        if (gc.gameOver && !s.over) s.Finish();
    }

    void TakeSnap(Player mover) {
        GameController gc = GameController.gc;
        Snap snap = new Snap { round = gc.days, mover = mover != null ? mover.id : -1 };
        lastRound = Mathf.Max(lastRound, gc.days);
        foreach (Player p in gc.players) {
            if (p == null) continue;
            SideSnap ss = new SideSnap { id = p.id, money = p.money };
            float along = 0f, material = 0f; int n = 0;
            foreach (Unit u in p.units) {
                if (u == null || u.Strength <= 0 || IsHQUnit(u)) continue;
                ss.units++;
                material += Worth(u);
                if (u.tile != null && axisLength > 0) { along += Along(u.tile); n++; }
            }
            if (n > 0) ss.front = along / n;
            ss.material = Mathf.RoundToInt(material);
            foreach (Building b in Building.all) {
                if (b == null || b.owner != p) continue;
                ss.buildings++;
                ss.income += b.income;
                foreach (Unit u in b.Purchasable()) {
                    string k = KindOf(u);
                    if (!canBuy.Exists(x => x.side == p.id && x.kind == k)) canBuy.Add(new Count { side = p.id, kind = k, n = 1 });
                }
                if (b.type == BuildingType.City) ss.cities++;
            }
            snap.sides.Add(ss);
        }
        snaps.Add(snap);
    }

    /// <summary>The game has ended (it can end mid-turn: an HQ taken or destroyed): close the record, once.</summary>
    public void EndIfOver() {
        GameController gc = GameController.gc;
        if (!over && gc != null && gc.gameOver) Finish();
    }

    void Finish() {
        GameController gc = GameController.gc;
        over = true;
        winner = gc != null && gc.winner != null ? gc.winner.id : -1;
        if (gc != null) lastRound = gc.days;
        Stop();
        // The report beside the game log (when the game is recorded).
        if (HexTactica.Harness.GameLog.Recording && !string.IsNullOrEmpty(HexTactica.Harness.GameLog.Folder)) {
            try {
                File.WriteAllText(Path.Combine(HexTactica.Harness.GameLog.Folder, "post-game.txt"), GameInsights.Report(this, GameInsights.ViewerId()));
            } catch (Exception e) { Debug.LogWarning("GameStats: could not write the post-game report: " + e.Message); }
        }
    }

    static long TurnOf() { GameController gc = GameController.gc; return gc != null && gc.activePlayer != null ? gc.activePlayer.id : -1; }
    static int Round() { GameController gc = GameController.gc; return gc != null ? gc.days : 0; }

    static void OnProduced(Building b, Unit u) {
        GameStats s = Current;
        if (s == null || u == null || u.owner == null) return;
        s.Note(u);
        s.buys.Add(new Buy { round = Round(), side = u.owner.id, kind = KindOf(u), cost = u.cost });
    }

    static void OnDitch(Unit u) { if (Current != null && u != null) Current.ditched.Add(u); }

    static void OnDied(Unit u) {
        GameStats s = Current;
        if (s == null || u == null || u.owner == null || IsHQUnit(u)) return;
        s.Note(u);
        Loss l = new Loss { round = Round(), side = u.owner.id, kind = KindOf(u), cost = u.cost, strength = Mathf.Max(0, u.Strength), turnOf = TurnOf() };
        if (s.ditched.Remove(u)) l.cause = "fuel";
        else if (u.trasnporter != null) l.cause = "aboard";
        else if (Player.InUpkeep) l.cause = "supply";
        s.losses.Add(l);
        s.dying[u] = l;
    }

    static void OnAttack(Unit attacker, Unit defender, HexTile from, HexTile at, CombatOutcome o) {
        GameStats s = Current;
        if (s == null || attacker == null || defender == null || attacker.owner == null || defender.owner == null) return;
        s.Note(attacker); s.Note(defender);
        int dist = from != null && at != null ? Distance(Cube(from), Cube(at)) : 0;
        int before = Mathf.Max(0, defender.Strength) + o.damageToDefender;
        s.attacks.Add(new Attack {
            round = Round(), side = attacker.owner.id, kind = KindOf(attacker), targetSide = defender.owner.id, targetKind = KindOf(defender),
            dealt = o.damageToDefender, taken = o.damageToAttacker, distance = dist, targetStrength = before,
            killed = o.defenderDies, attackerDied = o.attackerDies, turnOf = TurnOf()
        });
        Loss l;
        if (o.defenderDies && s.dying.TryGetValue(defender, out l)) {
            l.cause = "attack"; l.killerSide = attacker.owner.id; l.killerKind = KindOf(attacker); l.strength = before;
            s.dying.Remove(defender);
        }
        if (o.attackerDies && s.dying.TryGetValue(attacker, out l)) {
            l.cause = "return fire"; l.killerSide = defender.owner.id; l.killerKind = KindOf(defender);
            s.dying.Remove(attacker);
        }
        // Cargo that went down with a transport: lost to whoever destroyed the transport.
        foreach (KeyValuePair<Unit, Loss> kv in s.dying)
            if (kv.Value.cause == "aboard" && kv.Value.killerSide < 0 && kv.Value.round == Round()) {
                Unit killer = o.defenderDies ? attacker : o.attackerDies ? defender : null;
                if (killer != null) { kv.Value.killerSide = killer.owner.id; kv.Value.killerKind = KindOf(killer); }
            }
        if (s.dying.Count > 64) s.dying.Clear();
        s.EndIfOver();
    }

    static void OnOwnerChanged(Building b, Player previous) {
        GameStats s = Current;
        if (s == null || b == null || b.owner == null) return;
        s.captures.Add(new Capture { round = Round(), side = b.owner.id, building = b.type.ToString(), from = previous != null ? previous.id : -1 });
        s.EndIfOver();
    }

    /// <summary>The kind's attributes, the first time a unit of it is seen.</summary>
    void Note(Unit u) {
        string k = KindOf(u);
        if (kinds.Exists(x => x.name == k)) return;
        kinds.Add(new Kind {
            name = k, cost = u.cost, range = u.AttackRange, minRange = u.MinRange, maxStrength = Mathf.Max(1, MaxStrength(u)),
            locomotion = u.locomotion, supply = u.Hub != null
        });
    }

    static readonly Dictionary<string, int> fullStrength = new Dictionary<string, int>();

    /// <summary>The strength a unit of its kind has when new (its prefab's).</summary>
    static int MaxStrength(Unit u) {
        string n = GameController.UnitName(u);
        int s;
        if (fullStrength.TryGetValue(n, out s)) return s;
        Unit prefab = GameAssets.Unit(n);
        s = Mathf.Max(1, prefab != null ? prefab.Strength : u.Strength);
        fullStrength[n] = s;
        return s;
    }

    /// <summary>A unit's worth as it stands: its cost times the share of its full strength it has (the duel logs' "material").</summary>
    public static float Worth(Unit u) { return u.cost * Mathf.Max(0, u.Strength) / (float)MaxStrength(u); }

    // ------------------------------------------------------------------ saves

    /// <summary>Where a save's statistics are kept: beside it.</summary>
    public static string BesidePath(string savePath) { return savePath + ".stats.json"; }

    /// <summary>Keep the statistics so far beside a save (the in-game menu's Save).</summary>
    public static void SaveBeside(string savePath) {
        if (Current == null) return;
        foreach (Unit u in UnitController.uc != null ? UnitController.uc.units : new List<Unit>()) if (u != null) Current.Note(u);
        try { File.WriteAllText(BesidePath(savePath), JsonUtility.ToJson(Current)); }
        catch (Exception e) { Debug.LogWarning("GameStats: could not save the statistics beside " + savePath + ": " + e.Message); }
    }

    /// <summary>A save was just loaded: go on with the statistics kept beside it, if there are any (else they start from this board).</summary>
    public static void LoadBeside(string savePath) {
        string p = BesidePath(savePath);
        if (!File.Exists(p)) return;
        GameStats s;
        try { s = JsonUtility.FromJson<GameStats>(File.ReadAllText(p)); }
        catch (Exception e) { Debug.LogWarning("GameStats: could not read " + p + ": " + e.Message); return; }
        if (s == null) return;
        Stop();
        s.over = false;
        s.Setup(false);
        Current = s;
        Subscribe();
    }

    // ------------------------------------------------------------------ helpers

    public Side SideOf(Player p) { return p == null ? null : sides.Find(x => x.id == p.id); }
    public Side SideOf(long id) { return sides.Find(x => x.id == id); }
    public Kind KindNamed(string k) { return kinds.Find(x => x.name == k); }

    static Player PlayerOf(long id) {
        GameController gc = GameController.gc;
        if (gc == null) return null;
        foreach (Player p in gc.players) if (p != null && p.id == id) return p;
        return null;
    }

    /// <summary>An HQ that is a unit (older saves): not counted as a unit (GameController.Remaining does the same).</summary>
    static bool IsHQUnit(Unit u) { SupplyPoint sp = u.Hub; return sp != null && sp.producer; }

    /// <summary>
    /// The kind a unit is counted as: its prefab's name less " Variant"; a faction's version that only looks different
    /// (Faction.Cosmetic) as the base unit it stands in for.
    /// </summary>
    public static string KindOf(Unit u) {
        if (u == null) return "?";
        Unit shown = u;
        foreach (Faction f in GameAssets.AllFactions) {
            if (f == null || f.replacements == null) continue;
            foreach (Faction.Replacement r in f.replacements)
                if (r != null && r.replacement != null && r.original != null && GameController.UnitName(r.replacement) == GameController.UnitName(u) && f.Cosmetic(r)) { shown = r.original; break; }
        }
        string n = GameController.UnitName(shown);
        return n.EndsWith(" Variant") ? n.Substring(0, n.Length - " Variant".Length).Trim() : n;
    }

    static Vector3 Cube(HexTile t) { return new Vector3(t.x, t.y, t.z); }
    static int Distance(Vector3 a, Vector3 b) {
        return Mathf.RoundToInt(Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Max(Mathf.Abs(a.y - b.y), Mathf.Abs(a.z - b.z))));
    }

    /// <summary>How far along the line from the first side's HQ to the second's a tile lies (0 at the first, 1 at the second).</summary>
    float Along(HexTile t) {
        Vector3 ab = hqB - hqA, ap = Cube(t) - hqA;
        float len2 = ab.x * ab.x + ab.y * ab.y + ab.z * ab.z;
        return len2 > 0f ? (ap.x * ab.x + ap.y * ab.y + ap.z * ab.z) / len2 : 0f;
    }
}
