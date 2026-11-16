using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One attack that is worked out over an area or later, not as a duel between two units: a missile on its way, a
/// barrage on a tile, the splash around a shot. It carries everything its landing needs, so it lands the same whether
/// the unit that fired it is still there or not, and a saved game keeps it (StrikeData).
/// </summary>
public class Strike {
    public Player owner;
    /// <summary>The prefab name of the unit that fired it (a save keeps this; the weapon is found again by it).</summary>
    public string kind = "";
    /// <summary>What it hits with (firepower, piercing, targeting, blast, crater ...): the firing unit for an attack that
    /// lands at once, its prefab for one in the air (the unit may be gone by then).</summary>
    public Unit weapon;
    /// <summary>The firing unit's strength, veterancy and garrison bonus as it fired.</summary>
    public int strength, veterancy, attackBonus;
    public HexTile from, target;
    /// <summary>The splash around an ordinary shot: the tile fired on and the unit firing are not hit again, and the ground is only worn.</summary>
    public bool splash;
    /// <summary>Not hit by it (the splash of a shot: the unit that fired, and the one it fired on).</summary>
    public Unit spareA, spareB;
    /// <summary>The round it was fired in.</summary>
    public int day;
    /// <summary>The other sides that saw it launched: they saw the unit that fired it as it fired (FogOfWar.Sees), or had a
    /// unit within its launchSeenWithin. Filled before it goes in the air, never changed after. Only the hostile ones among
    /// them know it is coming (Strikes.KnownTo); every one is warned on screen (LaunchWarning).</summary>
    public readonly List<Player> seenBy = new List<Player>();

    public int Radius { get { return weapon != null ? Mathf.Max(0, weapon.blastRadius) : 0; } }
}

/// <summary>A unit hit by a strike: where it stood, its strength before, what it lost, whether it was destroyed.</summary>
public struct StrikeHit {
    public Unit unit;
    public HexTile tile;
    public int before, damage;
    public bool dies;
}

/// <summary>What came of a strike landing.</summary>
public class StrikeResult {
    public Strike strike;
    public readonly List<StrikeHit> hits = new List<StrikeHit>();
    /// <summary>The terrain the target tile was, if the strike destroyed it (a crater, a fallen bridge); null: not destroyed.</summary>
    public TerrainData groundWas;
    /// <summary>The building on the target tile that took damage, and how much.</summary>
    public Building building;
    public int buildingDamage;
}

/// <summary>
/// A strike in the air as one side knows it (Strikes.KnownTo): the only way the AI learns of strikes. For its own
/// strikes a side knows everything, the target included; for an enemy's it knows only what it saw at the launch (who,
/// from where, with what, at what strength) and never where it is aimed: <see cref="target"/> is then null.
/// </summary>
public struct StrikeSighting {
    public Player owner;
    public HexTile from;
    /// <summary>Its weapon (the prefab of the unit that fired it): range, blast, damage.</summary>
    public Unit weapon;
    public int strength, veterancy, attackBonus;
    /// <summary>Where it lands: known for one's own strikes only; null for another side's.</summary>
    public HexTile target;
}

/// <summary>A strike in the air, as a save keeps it (LevelEditor.BoardJson, GameLog.Snapshot: "strikes").</summary>
[Serializable]
public struct StrikeData {
    public long owner_id;
    public string kind;
    public int strength, veterancy, attackBonus;
    public int fromX, fromY, x, y;
    public int day;
    /// <summary>The ids of the enemy sides that saw it launched, comma separated ("" none: a file from before this was kept).</summary>
    public string seenBy;

    public StrikeData(Strike s) {
        owner_id = s.owner != null ? s.owner.id : -1;
        kind = s.kind ?? "";
        strength = s.strength; veterancy = s.veterancy; attackBonus = s.attackBonus;
        fromX = s.from != null ? s.from.x : 0; fromY = s.from != null ? s.from.y : 0;
        x = s.target != null ? s.target.x : 0; y = s.target != null ? s.target.y : 0;
        day = s.day;
        List<string> ids = new List<string>();
        foreach (Player p in s.seenBy) if (p != null) ids.Add(p.id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        seenBy = string.Join(",", ids.ToArray());
    }

    /// <summary>The strike back in the air. Null (said so) if its side, its target tile or its unit's prefab is not in this game.</summary>
    public Strike Load() {
        Player p = null;
        StrikeData that = this;
        if (GameController.gc != null) GameController.gc.players.ForEach(pl => { if (pl != null && pl.id == that.owner_id) p = pl; });
        HexTile to = HexGenerator.hg != null ? HexGenerator.hg.get(x, y, -x - y) : null;
        Unit prefab = Strikes.Prefab(p, kind);
        if (p == null || to == null || prefab == null) {
            Debug.LogError("A strike in the air ('" + kind + "' at " + x + "," + y + ") was left out: " + (p == null ? "no side with id " + owner_id : to == null ? "no such tile" : "no unit prefab of that name"));
            return null;
        }
        Strike s = new Strike { owner = p, kind = kind, weapon = prefab, strength = strength, veterancy = veterancy, attackBonus = attackBonus,
                                from = HexGenerator.hg.get(fromX, fromY, -fromX - fromY), target = to, day = day };
        foreach (string id in (seenBy ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) {
            long n;
            if (!long.TryParse(id.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out n)) continue;
            GameController.gc.players.ForEach(pl => { if (pl != null && pl.id == n && !s.seenBy.Contains(pl)) s.seenBy.Add(pl); });
        }
        Strikes.Add(s);
        return s;
    }
}

/// <summary>
/// The rules of attacks on ground, over an area, delayed, and of units expended by their own attack. Each is one
/// attribute of a unit, on or off by itself (Unit: targetsGround, blastRadius, ballistic, selfDestructs), so a unit can
/// have any mix of them: a missile has all four; rocket artillery could have the first two; a glide bomb the last two.
///
///   - Target ground: the order names a tile (Order). What is hit is whatever stands on the tiles when it lands.
///   - Area of effect: every unit within blastRadius of the target is hit, friend and foe, with the unit's ordinary
///     damage against each (Unit.DamageFrom: the one formula), no answer and no retreat. On a unit that aims at units
///     and lands at once, it is the splash around its ordinary shot (Splash, from Unit.OrderAttack).
///   - Ballistic delay: the attack is in the air (InFlight) until its owner's next turn starts (LandFor, from
///     Player.StartTurn), then lands on the tile it was aimed at. Launching gives the unit away to every enemy side.
///   - Self-destruct: the unit is expended by its attack (Unit.Expend): at once, or, with a delay, as it is launched,
///     after which there is nothing on the board to attack.
///
/// Rules only, on the real board: the AI's thinking copies never come here (it weighs strikes with Preview).
/// </summary>
public static class Strikes {

    // Replaced whole, never changed in place: an AI thought on another thread keeps reading the one it had.
    static Strike[] inFlight = new Strike[0];

    /// <summary>
    /// Every attack in the air, in the order launched, targets and all: for the rules, the screen, saves and tests.
    /// Never for the AI (it would know what no player can): the AI reads KnownTo, and a test (ai-strike-blind) fails if
    /// any AI code names this.
    /// </summary>
    public static IList<Strike> InFlight { get { return inFlight; } }

    /// <summary>An order to strike was given (before it lands or leaves): the unit, the strike, and whether it is delayed. For logs and scripts.</summary>
    public static event Action<Unit, Strike, bool> Fired;
    /// <summary>A strike landed and its results are on the board (the splash of a shot too). For logs, scripts and tests.</summary>
    public static event Action<StrikeResult> Landed;

    /// <summary>A new board: nothing in the air.</summary>
    public static void Clear() { inFlight = new Strike[0]; performing.Clear(); }

    internal static void Add(Strike s) {
        if (s == null) return;
        Strike[] next = new Strike[inFlight.Length + 1];
        Array.Copy(inFlight, next, inFlight.Length);
        next[inFlight.Length] = s;
        inFlight = next;
    }

    /// <summary>Has <paramref name="p"/> a unit on the board within <paramref name="range"/> tiles of <paramref name="at"/>? (0: none counts.)</summary>
    static bool WithinOf(Player p, HexTile at, int range) {
        if (range <= 0 || at == null || p == null || p.units == null) return false;
        foreach (Unit o in p.units) if (o != null && o.Strength > 0 && o.tile != null && o.tile.Distance(at) <= range) return true;
        return false;
    }

    /// <summary>A side is out of the game: what it had in the air is gone with it.</summary>
    public static void Drop(Player p) {
        List<Strike> keep = new List<Strike>();
        foreach (Strike s in inFlight) if (s.owner != p) keep.Add(s);
        if (keep.Count != inFlight.Length) inFlight = keep.ToArray();
    }

    /// <summary>
    /// The strikes in the air <paramref name="p"/> knows of: its own, target and all; an enemy's only if it saw the
    /// launch (Strike.seenBy), without the target. An ally's are left out. Safe from any thread (it reads only what was
    /// fixed at launch).
    /// </summary>
    public static List<StrikeSighting> KnownTo(Player p) {
        List<StrikeSighting> known = new List<StrikeSighting>();
        Strike[] now = inFlight;
        for (int i = 0; i < now.Length; i++) {
            Strike s = now[i];
            if (s == null || p == null) continue;
            bool own = s.owner == p;
            if (!own && !(Player.Hostile(s.owner, p) && s.seenBy.Contains(p))) continue;
            known.Add(new StrikeSighting { owner = s.owner, from = s.from, weapon = s.weapon, strength = s.strength, veterancy = s.veterancy,
                                           attackBonus = s.attackBonus, target = own ? s.target : null });
        }
        return known;
    }

    /// <summary>
    /// Should the player at the screen be warned of this launch (LaunchWarning)? Under fog, the side shown on screen
    /// (FogOfWar.Viewer) is, if it saw the launch and did not make it. With no viewer (no fog, or a spectator), it is if any
    /// side played by a person saw it.
    /// </summary>
    public static bool WarnsScreen(Strike s) {
        if (s == null || s.seenBy.Count == 0) return false;
        Player v = FogOfWar.Viewer;
        if (v != null) return v != s.owner && s.seenBy.Contains(v);
        foreach (Player p in s.seenBy) if (p != null && AIControl.Of(p) == null) return true;
        return false;
    }

    /// <summary>The unit prefab <paramref name="p"/> fields under <paramref name="kind"/> (as a loaded save finds its units).</summary>
    public static Unit Prefab(Player p, string kind) {
        if (string.IsNullOrEmpty(kind)) return null;
        return LevelEditor.UnitFor(p, kind);
    }

    // ------------------------------------------------------------------ saves

    /// <summary>The keys a save keeps the strikes in the air and the ground lit up under.</summary>
    public const string FileKey = "strikes", LitKey = "lit";

    /// <summary>
    /// What a save keeps of strikes: those in the air ("strikes") and the ground lit up by one that landed this turn
    /// ("lit"), as JSON members to go inside the file's object, each led by a comma; "" when there is neither (a file
    /// without strikes is as it was before there were any). Written by hand, the same in Unity and headless.
    /// </summary>
    public static string SaveJson() {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
        Strike[] now = inFlight;
        if (now.Length > 0) {
            sb.Append(",\n\"").Append(FileKey).Append("\":[");
            for (int i = 0; i < now.Length; i++) {
                StrikeData d = new StrikeData(now[i]);
                sb.Append(i > 0 ? "," : "").Append(string.Format(inv,
                    "\n{{\"owner_id\":{0},\"kind\":\"{1}\",\"strength\":{2},\"veterancy\":{3},\"attackBonus\":{4},\"fromX\":{5},\"fromY\":{6},\"x\":{7},\"y\":{8},\"day\":{9},\"seenBy\":\"{10}\"}}",
                    d.owner_id, (d.kind ?? "").Replace("\\", "/").Replace("\"", "'"), d.strength, d.veterancy, d.attackBonus, d.fromX, d.fromY, d.x, d.y, d.day, d.seenBy));
            }
            sb.Append("\n]");
        }
        IList<FogOfWar.Lit> lit = FogOfWar.Flares;
        if (lit.Count > 0) {
            sb.Append(",\n\"").Append(LitKey).Append("\":[");
            for (int i = 0; i < lit.Count; i++)
                sb.Append(i > 0 ? "," : "").Append(string.Format(inv, "\n{{\"owner_id\":{0},\"x\":{1},\"y\":{2},\"radius\":{3}}}",
                    lit[i].to != null ? lit[i].to.id : -1, lit[i].at != null ? lit[i].at.x : 0, lit[i].at != null ? lit[i].at.y : 0, lit[i].radius));
            sb.Append("\n]");
        }
        return sb.ToString();
    }

    /// <summary>Ground lit up for a side, from a save (LitKey).</summary>
    public static void LoadLit(long ownerId, int x, int y, int radius) {
        Player p = null;
        if (GameController.gc != null) GameController.gc.players.ForEach(pl => { if (pl != null && pl.id == ownerId) p = pl; });
        HexTile at = HexGenerator.hg != null ? HexGenerator.hg.get(x, y, -x - y) : null;
        if (p != null && at != null) FogOfWar.Flare(p, at, radius);
    }

    // ------------------------------------------------------------------ what can be ordered

    /// <summary>The tiles <paramref name="u"/> could strike from <paramref name="from"/>: every tile in its range and line of fire, whatever is on it.</summary>
    public static List<HexTile> Targets(Unit u, HexTile from) {
        if (u == null || from == null || !u.targetsGround) return new List<HexTile>();
        return from.AllInAttackRange(u);
    }

    /// <summary>Could <paramref name="u"/>, standing on <paramref name="from"/>, be ordered to strike <paramref name="target"/>: able to fire, and the tile in its range and line of fire?</summary>
    public static bool CanOrder(Unit u, HexTile from, HexTile target) {
        if (u == null || from == null || target == null || !u.StrikesArea || !u.CanAttack(from)) return false;
        return CombatMath.InFireRange(u, from, target, target.occupier);
    }

    /// <summary>The tiles a strike on <paramref name="target"/> with this blast radius hits, the target first, the rest in the board's order.</summary>
    public static List<HexTile> Blast(HexTile target, int radius) {
        List<HexTile> tiles = new List<HexTile>();
        if (target == null) return tiles;
        tiles.Add(target);
        if (radius > 0) foreach (HexTile t in HexTactica.AI.AIPath.TilesInRangeSorted(target, -1, radius)) if (t != null && t != target) tiles.Add(t);
        return tiles;
    }

    /// <summary>Can a strike hurt <paramref name="o"/> at all under the rules (Attrition: an HQ unit cannot be attacked)?</summary>
    static bool Protected(Unit o) {
        if (GameController.gc == null || GameController.gc.rules != RuleSet.Attrition) return false;
        SupplyPoint sp = o.Hub;
        return sp != null && sp.producer;
    }

    /// <summary>
    /// What a strike would do to the units on its tiles, with <paramref name="who"/> saying which unit stands on a tile
    /// (the real one when it lands; the one a side knows of for a preview or the AI). Nothing is changed.
    /// </summary>
    public static List<StrikeHit> Hits(Strike s, Func<HexTile, Unit> who) {
        List<StrikeHit> hits = new List<StrikeHit>();
        if (s == null || s.weapon == null || s.target == null) return hits;
        foreach (HexTile t in Blast(s.target, s.Radius)) {
            if (s.splash && t == s.target) continue;
            Unit o = who(t);
            if (o == null || o.Strength <= 0 || o == s.spareA || o == s.spareB || Protected(o)) continue;
            int damage = Mathf.Min(o.Strength, Unit.DamageFrom(s.weapon, s.strength, s.veterancy, s.attackBonus, o, t));
            if (damage <= 0) continue;
            hits.Add(new StrikeHit { unit = o, tile = t, before = o.Strength, damage = damage, dies = damage >= o.Strength });
        }
        return hits;
    }

    /// <summary>The strike <paramref name="u"/> would make on <paramref name="target"/> from <paramref name="from"/> (nothing is ordered).</summary>
    public static Strike Plan(Unit u, HexTile from, HexTile target) {
        Building atk = Building.GarrisonFor(u, from);
        return new Strike {
            owner = u.owner, kind = GameController.UnitName(u), weapon = u, strength = u.Strength, veterancy = u.Veterancy,
            attackBonus = atk != null ? atk.attackBonus : 0, from = from, target = target,
            day = GameController.gc != null ? GameController.gc.days : 0
        };
    }

    /// <summary>
    /// The preview of an order: what <paramref name="u"/>'s strike on <paramref name="target"/> would do to the units
    /// <paramref name="knower"/> knows to be on its tiles as they stand now (under fog of war, one it does not see is
    /// not shown). A delayed strike hits what is there when it lands: this is what it would do if nothing moved.
    /// </summary>
    public static List<StrikeHit> Preview(Unit u, HexTile from, HexTile target, Player knower) {
        Strike s = Plan(u, from, target);
        // A unit that leaves the board as it fires is not under its own strike.
        if (u.ballistic && u.selfDestructs) s.spareA = u;
        return Hits(s, t => FogOfWar.ApparentOccupier(t, knower));
    }

    // ------------------------------------------------------------------ orders

    /// <summary>
    /// <paramref name="u"/> strikes <paramref name="target"/> from where it stands: at once, or launched to land at the
    /// start of its owner's next turn (ballistic). It has then attacked this turn; a unit that self-destructs is
    /// expended. False if it cannot (not a unit that strikes, unable to fire, the tile out of its range).
    /// </summary>
    public static bool Order(Unit u, HexTile target) {
        if (u == null || u.tile == null || BoardView.current != null) return false;
        if (!CanOrder(u, u.tile, target)) return false;
        Strike s = Plan(u, u.tile, target);
        u.attackedThisTurn = true;
        if (u.ammoMax > 0) u.ammo = Mathf.Max(0, u.ammo - Mathf.Max(1, u.ammoPerShot));
        if (Fired != null) Fired(u, s, u.ballistic);
        if (u.ballistic) {
            // In the air it is its prefab's weapon at the strength it left with: the unit may be gone when it lands.
            Unit prefab = Prefab(u.owner, s.kind);
            if (prefab != null) s.weapon = prefab;
            // Who saw it go: the sides that see the unit as it fires (no fog: all of them), and those with a unit within its
            // launchSeenWithin, whatever their sight: they also see the ground round the launch tile until their turn ends.
            GameController gc = GameController.gc;
            if (gc != null && gc.players != null)
                foreach (Player p in gc.players) {
                    if (p == null || p == u.owner || p.eliminated) continue;
                    bool near = WithinOf(p, u.tile, u.launchSeenWithin);
                    if (near || FogOfWar.Sees(p, u)) s.seenBy.Add(p);
                    if (near) FogOfWar.Flare(p, u.tile, Mathf.Max(0, u.launchRevealRadius));
                }
            Add(s);
            if (u.selfDestructs) u.Expend(true);
            else u.ShowStrikeFired(null);
            FogOfWar.Changed();
            return true;
        }
        StrikeResult r = Resolve(s);
        u.ShowStrikeFired(r);
        if (u.selfDestructs) u.Expend(false);
        return true;
    }

    /// <summary>
    /// The start of <paramref name="p"/>'s turn (Player.StartTurn, before its upkeep): everything it has in the air
    /// lands, in the order it was launched.
    /// </summary>
    public static void LandFor(Player p) {
        if (p == null || inFlight.Length == 0 || BoardView.current != null) return;
        List<Strike> mine = new List<Strike>(), rest = new List<Strike>();
        foreach (Strike s in inFlight) (s.owner == p ? mine : rest).Add(s);
        if (mine.Count == 0) return;
        inFlight = rest.ToArray();
        foreach (Strike s in mine) {
            StrikeResult r = Resolve(s);
            ShowArrival(r);
        }
    }

    /// <summary>
    /// The splash of an ordinary shot by <paramref name="attacker"/> (blastRadius over 0) on <paramref name="center"/>,
    /// fired at <paramref name="strength"/>: every unit around the tile fired on is hit, but for the two that fought.
    /// Null: no splash.
    /// </summary>
    internal static StrikeResult Splash(Unit attacker, int strength, Unit defender, HexTile from, HexTile center) {
        if (attacker == null || center == null || attacker.blastRadius <= 0 || strength <= 0 || BoardView.current != null) return null;
        Strike s = Plan(attacker, from, center);
        s.strength = strength; s.splash = true; s.spareA = attacker; s.spareB = defender;
        return Resolve(s);
    }

    /// <summary>The strike lands on the real board: its hits, the ground, the building, what its owner sees. Tells Landed.</summary>
    static StrikeResult Resolve(Strike s) {
        StrikeResult r = new StrikeResult { strike = s };
        if (s == null || s.weapon == null || s.target == null) return r;
        r.hits.AddRange(Hits(s, t => t.RealOccupier));
        foreach (StrikeHit h in r.hits) {
            h.unit.TakeStrike(h.damage);
            // A fight on a tile wears its ground (or the building on it), as any attack does.
            h.tile.RegisterCombat();
        }
        if (!s.splash) {
            Building b = s.target.building;
            if (b != null) {
                if (s.weapon.buildingDamage > 0 && b.health > 0 && !b.Destroyed) {
                    int was = b.damage;
                    b.Damage(s.weapon.buildingDamage);
                    r.building = b; r.buildingDamage = b.damage - was;
                }
            } else {
                TerrainData was = s.target.terrain;
                if (s.target.Crater(s.weapon.craterTerrain)) r.groundWas = was;
            }
            if (s.weapon.strikeRevealRadius > 0) FogOfWar.Flare(s.owner, s.target, s.weapon.strikeRevealRadius);
        }
        FogOfWar.Changed();
        if (Landed != null) Landed(r);
        return r;
    }

    // ------------------------------------------------------------------ on screen (nothing here changes the rules)

    // The units playing a launch or an arrival (removed when their animation ends: StrikeStage).
    static readonly List<Unit> performing = new List<Unit>();

    /// <summary>A launch or an arrival is still playing: the AI and a turn's end wait for it, as for any animation (Unit.AnyAnimating).</summary>
    public static bool Showing {
        get {
            if (GameController.gc == null || GameController.gc.headless) return false;
            performing.RemoveAll(u => u == null || u.ShownAt == null);
            return performing.Count > 0;
        }
    }

    internal static void Performing(Unit u) { if (u != null && !performing.Contains(u)) performing.Add(u); }

    /// <summary>
    /// A strike from the air has landed: a unit that was expended as it launched is shown arriving (its prefab, playing
    /// its animator's Land for landSeconds), then the explosion and the hits; anything else shows them at once.
    /// </summary>
    static void ShowArrival(StrikeResult r) {
        if (GameController.gc == null || GameController.gc.headless || r.strike.target == null) return;
        Unit w = r.strike.weapon;
        if (w != null && w.selfDestructs && w.ballistic) {
            Unit body = GameObject.Instantiate(w.gameObject, PositionOf(r.strike.target), Quaternion.identity).GetComponent<Unit>();
            if (body != null) { body.owner = r.strike.owner; body.BeginArrival(r); return; }
        }
        ShowImpact(r);
    }

    static Vector3 PositionOf(HexTile t) { return t.unitPlacement != null ? t.unitPlacement.position : t.transform.position; }

    /// <summary>The explosion where it landed and each unit's hit (its strength as it now is, its death), for whoever can see the tile.</summary>
    internal static void ShowImpact(StrikeResult r) {
        if (r == null || GameController.gc == null || GameController.gc.headless) return;
        Strike s = r.strike;
        if (!s.splash && s.target != null && s.weapon != null) {
            Player v = FogOfWar.Viewer;
            bool seen = v == null || FogOfWar.TileVisible(v, s.target);
            GameObject boom = s.weapon.strikeExplosion != null ? s.weapon.strikeExplosion : s.weapon.weaponImpact;
            if (seen && boom != null) GameObject.Instantiate(boom, PositionOf(s.target), Quaternion.identity);
            if (seen && s.weapon.strikeSound != null && GameSounds.On) GameSounds.Play(s.weapon.strikeSound);
        }
        foreach (StrikeHit h in r.hits) if (h.unit != null) h.unit.ShowHit();
    }
}
