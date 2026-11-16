using System;
using System.Collections.Generic;
using System.IO;
using HexTactica.Harness;

/// <summary>
/// A game as its game log tells it (game.log, read with GameLog.Lines, which also wrote it): the sides, each
/// side's state at the start of every turn, and every purchase, attack, unit lost and building taken. The
/// post-game report (GameInsights) is read from this; nothing else records statistics.
///
/// Unit kinds go by the prefab's name less a trailing " Variant"; a faction's version that differs from the base
/// unit only in looks (Faction.Cosmetic) is counted as the base unit.
/// </summary>
public class GameRecord {

    public class Side {
        public long id; public string name; public string faction = ""; public bool ai;
        /// <summary>Sides with the same team (not 0) were allies in this game (GameLog.Lines.Side). 0: on nobody's side but its own.</summary>
        public int team;
    }

    public class Kind {
        public string name; public int cost; public int range; public int maxStrength = 1;
        /// <summary>Its prefab's move (movement points) and whether it can move and fire in one turn.</summary>
        public int move; public bool moveAndFire = true;
        /// <summary>Tiles it could strike at next turn from where it stands, terrain aside: its move and range, or its range alone if it cannot move and fire.</summary>
        public int Reach { get { return (moveAndFire ? move : 0) + range; } }
        public Locomotion locomotion;
        public bool Air { get { return LocomotionRules.IsAirborne(locomotion); } }
    }

    /// <summary>One side at the start of a turn.</summary>
    public class SideSnap {
        public long id;
        /// <summary>What its units are worth as they stand: each unit's cost times the share of its full strength it has (HQs not counted).</summary>
        public int material;
        public int units, cities, buildings, money, income;
        /// <summary>Where its units stand on the line from the first side's HQ (0) to its first enemy's (1), on average; -1 if none.</summary>
        public float front = -1f;
        /// <summary>Its units by kind.</summary>
        public Dictionary<string, int> fielded = new Dictionary<string, int>();
    }

    public class Snap { public int round; public long mover; public List<SideSnap> sides = new List<SideSnap>(); }
    public class Count { public long side; public string kind; public int n; }
    public class Buy { public int round; public long side; public string kind; public int cost; }

    /// <summary>
    /// One unit, followed through the log by where it stands (the log names units by side and kind only; every move,
    /// boarding, purchase, attack and loss says where). A unit that has not moved yet is known from the first line that
    /// names its tile. Its kills, and who killed it.
    /// </summary>
    public class Track {
        public int id; public long side; public string kind;
        public int x, y;
        public bool alive = true;
        /// <summary>Aboard this transport (null: on its tile).</summary>
        public Track aboard;
        public readonly List<Track> cargo = new List<Track>();
        public readonly List<Track> kills = new List<Track>();
        public int diedRound = -1;
        public Track killedBy;
        /// <summary>When it was destroyed: each side's units (as far as the log had placed them) within its reach (Kind.Reach) of where it stood.</summary>
        public Dictionary<long, int> inReachAtDeath;
    }

    /// <summary>A unit lost: to whom, how, and when.</summary>
    public class Loss {
        public int round; public long side; public string kind; public int cost;
        /// <summary>The unit lost and the one that killed it, if the log placed them (Track).</summary>
        public Track unit, killer;
        /// <summary>Its strength when it was lost (what was destroyed).</summary>
        public int strength;
        /// <summary>GameLog.Lines.Combat (destroyed attacking or attacked: the killer is named), Supply (starved), Fuel (ditched), Aboard (went down with its transport).</summary>
        public string cause;
        public long killerSide = -1; public string killerKind = ""; public long turnOf = -1;
    }

    public class Attack {
        public int round; public long side; public string kind; public long targetSide; public string targetKind;
        public Track unit, target;
        public int dealt, taken, distance, targetStrength; public bool killed, attackerDied; public long turnOf;
    }

    public class Capture { public int round; public long side; public string building; public long from = -1; }

    public string source = "";
    public int axisLength;
    public List<Side> sides = new List<Side>();
    public List<Kind> kinds = new List<Kind>();
    /// <summary>Each side's units by kind at the start of the game.</summary>
    public List<Count> start = new List<Count>();
    /// <summary>The kinds each side could buy at some point (at a building it held, money aside).</summary>
    public List<Count> canBuy = new List<Count>();
    public List<Snap> snaps = new List<Snap>();
    public List<Buy> buys = new List<Buy>();
    public List<Loss> losses = new List<Loss>();
    public List<Attack> attacks = new List<Attack>();
    public List<Capture> captures = new List<Capture>();
    /// <summary>Every unit the log placed (see Track), in the order it first came up.</summary>
    public List<Track> tracks = new List<Track>();
    /// <summary>The sides that gave the game up.</summary>
    public List<long> resigned = new List<long>();
    public bool over;
    public long winner = -1;
    public int lastRound;

    public Side SideOf(long id) { return sides.Find(x => x.id == id); }

    /// <summary>Two different sides on one team (not 0): allies in this game.</summary>
    public bool Allied(long a, long b) {
        if (a == b) return false;
        Side x = SideOf(a), y = SideOf(b);
        return x != null && y != null && x.team != 0 && x.team == y.team;
    }
    /// <summary>Different sides and not allies: who fought whom.</summary>
    public bool Hostile(long a, long b) { return a != b && !Allied(a, b); }
    /// <summary>The side and its allies, in the log's order.</summary>
    public List<long> SideAndAllies(long id) { List<long> l = new List<long>(); foreach (Side x in sides) if (x.id == id || Allied(x.id, id)) l.Add(x.id); return l; }
    public Side SideNamed(string name) { return sides.Find(x => x.name == name); }
    public Kind KindNamed(string k) { return kinds.Find(x => x.name == k); }

    /// <summary>Follows units through the log by tile (Track).</summary>
    class Tracker {
        readonly GameRecord g;
        readonly Dictionary<long, Track> at = new Dictionary<long, Track>();
        /// <summary>Strikes in flight: the unit that fired, by its side and kind and the tile aimed at.</summary>
        readonly List<KeyValuePair<string, Track>> strikes = new List<KeyValuePair<string, Track>>();
        public Tracker(GameRecord g) { this.g = g; }
        static long Key(int x, int y) { return ((long)x << 32) ^ (uint)y; }

        /// <summary>The unit of this side and kind on (x,y): the one the log put there, or a new one (not yet named by any line).</summary>
        public Track Find(long side, string kind, int x, int y) {
            Track t;
            if (at.TryGetValue(Key(x, y), out t) && t.alive && t.side == side && t.kind == kind) return t;
            t = new Track { id = g.tracks.Count, side = side, kind = kind, x = x, y = y };
            g.tracks.Add(t);
            at[Key(x, y)] = t;
            return t;
        }
        public void Place(Track t, int x, int y) {
            Track was;
            if (t.aboard == null && at.TryGetValue(Key(t.x, t.y), out was) && was == t) at.Remove(Key(t.x, t.y));
            if (t.aboard != null) { t.aboard.cargo.Remove(t); t.aboard = null; }
            t.x = x; t.y = y;
            at[Key(x, y)] = t;
        }
        public void Board(Track cargo, Track transport) {
            Track was;
            if (cargo.aboard == null && at.TryGetValue(Key(cargo.x, cargo.y), out was) && was == cargo) at.Remove(Key(cargo.x, cargo.y));
            cargo.aboard = transport; cargo.x = transport.x; cargo.y = transport.y;
            if (!transport.cargo.Contains(cargo)) transport.cargo.Add(cargo);
        }
        /// <summary>The unit of this side and kind aboard <paramref name="transport"/> (a new one if the log never saw it board).</summary>
        public Track Aboard(Track transport, long side, string kind) {
            Track c = transport.cargo.Find(x => x.alive && x.side == side && x.kind == kind);
            if (c != null) return c;
            c = new Track { id = g.tracks.Count, side = side, kind = kind, x = transport.x, y = transport.y, aboard = transport };
            g.tracks.Add(c); transport.cargo.Add(c);
            return c;
        }
        /// <summary>Gone: off the board with its cargo; who killed it (null: lost some other way), and who stood within its reach.</summary>
        public void Kill(Track t, Track by, int round) {
            if (t == null || !t.alive) return;
            if (t.aboard == null) {
                GameRecord.Kind k = g.KindNamed(t.kind);
                int reach = k != null ? k.Reach : 0;
                t.inReachAtDeath = new Dictionary<long, int>();
                foreach (Track o in at.Values) {
                    if (o == t || !o.alive || Distance(o.x, o.y, t.x, t.y) > reach) continue;
                    int n; t.inReachAtDeath.TryGetValue(o.side, out n); t.inReachAtDeath[o.side] = n + 1;
                }
            }
            t.alive = false; t.diedRound = round; t.killedBy = by;
            if (by != null) by.kills.Add(t);
            Track was;
            if (t.aboard == null && at.TryGetValue(Key(t.x, t.y), out was) && was == t) at.Remove(Key(t.x, t.y));
            if (t.aboard != null) t.aboard.cargo.Remove(t);
            foreach (Track c in t.cargo.ToArray()) { c.aboard = null; Kill(c, by, round); }
        }
        public static int Distance(int ax, int ay, int bx, int by) { int dz = (-ax - ay) - (-bx - by); return Math.Max(Math.Abs(ax - bx), Math.Max(Math.Abs(ay - by), Math.Abs(dz))); }
        public void Fired(long side, string kind, int x, int y, Track unit) { strikes.Add(new KeyValuePair<string, Track>(side + "|" + kind + "|" + x + "," + y, unit)); }
        /// <summary>The unit that fired the strike landing on (x,y), or null.</summary>
        public Track Landed(long side, string kind, int x, int y) {
            string key = side + "|" + kind + "|" + x + "," + y;
            int i = strikes.FindIndex(s => s.Key == key);
            if (i < 0) return null;
            Track t = strikes[i].Value; strikes.RemoveAt(i);
            return t;
        }
    }

    // ------------------------------------------------------------------ reading

    /// <summary>The game recorded in <paramref name="folder"/> (its game.log), or null if there is none.</summary>
    public static GameRecord Read(string folder) {
        if (string.IsNullOrEmpty(folder)) return null;
        string log = Path.Combine(folder, "game.log");
        if (!File.Exists(log)) return null;
        // Read while the game may still be writing it.
        List<string> lines = new List<string>();
        using (FileStream fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (StreamReader r = new StreamReader(fs)) {
            string line;
            while ((line = r.ReadLine()) != null) lines.Add(line);
        }
        return FromLines(lines);
    }

    /// <summary>A game from the lines of its game.log.</summary>
    public static GameRecord FromLines(IEnumerable<string> lines) {
        GameRecord g = new GameRecord();
        List<string> names = new List<string>();
        int round = 0;
        long mover = -1;
        Snap snap = null;
        // Losses in combat and aboard, waiting for the attack line that says who did it.
        List<Loss> pending = new List<Loss>();
        Tracker tr = new Tracker(g);

        foreach (string raw in lines) {
            string line = raw.TrimEnd('\r');
            long id; string name, faction, sideName, unit, cause, from, to; bool ai; int n, team;

            if (GameLog.Lines.ReadSide(line, out id, out name, out faction, out ai, out team)) {
                Side s = g.SideOf(id);
                if (s == null) { s = new Side { id = id }; g.sides.Add(s); }
                s.name = name; s.faction = faction; s.ai = ai; s.team = team;
                if (!names.Contains(name)) names.Add(name);
                continue;
            }
            if (GameLog.Lines.ReadAxis(line, out n)) { g.axisLength = n; continue; }
            if (GameLog.Lines.ReadResigns(line, names, out sideName)) { Side q = g.SideNamed(sideName); if (q != null) g.resigned.Add(q.id); continue; }
            if (g.source.Length == 0 && GameLog.Lines.ReadSource(line, out from)) { g.source = from; continue; }
            int newRound;
            if (GameLog.Lines.ReadRound(line, out newRound, out sideName)) {
                round = newRound;
                Side s = g.SideNamed(sideName);
                mover = s != null ? s.id : -1;
                snap = new Snap { round = round, mover = mover };
                g.snaps.Add(snap);
                g.lastRound = Math.Max(g.lastRound, round);
                pending.Clear();
                continue;
            }
            int winnerRound; string winnerName;
            if (GameLog.Lines.ReadGameOver(line, out winnerRound, out winnerName)) {
                g.over = true; g.lastRound = Math.Max(g.lastRound, winnerRound);
                Side w = winnerName != null ? g.SideNamed(winnerName) : null;
                g.winner = w != null ? w.id : -1;
                continue;
            }
            int worth, units, cities, buildings, income, gold; float front;
            if (GameLog.Lines.ReadState(line, out sideName, out worth, out units, out cities, out buildings, out income, out gold, out front)) {
                Side s = g.SideNamed(sideName);
                if (s != null && snap != null)
                    snap.sides.Add(new SideSnap { id = s.id, material = worth, units = units, cities = cities, buildings = buildings, income = income, money = gold, front = front });
                continue;
            }
            List<KeyValuePair<string, int>> counts;
            if (GameLog.Lines.ReadFields(line, names, out sideName, out counts)) {
                Side s = g.SideNamed(sideName);
                if (s == null || snap == null) continue;
                SideSnap ss = snap.sides.Find(x => x.id == s.id);
                foreach (KeyValuePair<string, int> kv in counts) {
                    string k = g.Note(kv.Key);
                    int have; if (ss != null) { ss.fielded.TryGetValue(k, out have); ss.fielded[k] = have + kv.Value; }
                }
                continue;
            }
            List<string> buyable;
            if (GameLog.Lines.ReadCanBuy(line, names, out sideName, out buyable)) {
                Side s = g.SideNamed(sideName);
                if (s == null) continue;
                foreach (string u in buyable) {
                    string k = g.Note(u);
                    if (!g.canBuy.Exists(x => x.side == s.id && x.kind == k)) g.canBuy.Add(new Count { side = s.id, kind = k, n = 1 });
                }
                continue;
            }
            int cost;
            if (GameLog.Lines.ReadBuy(line, names, out sideName, out unit, out cost)) {
                Side s = g.SideNamed(sideName);
                if (s != null) {
                    string k = g.Note(unit);
                    g.buys.Add(new Buy { round = round, side = s.id, kind = k, cost = cost });
                    int bx, by;
                    if (GameLog.Lines.ReadBuyPlace(line, out bx, out by)) tr.Place(tr.Find(s.id, k, bx, by), bx, by);
                }
                continue;
            }
            if (GameLog.Lines.ReadDies(line, names, out sideName, out unit, out cause)) {
                Side s = g.SideNamed(sideName);
                if (s == null) continue;
                string k = g.Note(unit);
                Kind kind = g.KindNamed(k);
                Loss l = new Loss { round = round, side = s.id, kind = k, cost = kind != null ? kind.cost : 0, cause = cause, turnOf = mover };
                g.losses.Add(l);
                // Lost in combat or aboard: placed by the attack or strike line that follows (a unit that moved and died
                // attacking has its dies line before its move line, so its tile here is not yet where the log has it).
                int dx, dy;
                if (cause != GameLog.Lines.Aboard && cause != GameLog.Lines.Combat && GameLog.Lines.ReadTile(line, out dx, out dy)) {
                    l.unit = tr.Find(s.id, k, dx, dy);
                    tr.Kill(l.unit, null, round);
                }
                if (cause == GameLog.Lines.Combat || cause == GameLog.Lines.Aboard) pending.Add(l);
                continue;
            }
            GameLog.Lines.AttackLine a;
            if (GameLog.Lines.ReadAttack(line, names, out a)) {
                Side att = g.SideNamed(a.side), def = g.SideNamed(a.targetSide);
                if (att == null || def == null) { pending.Clear(); continue; }
                string ak = g.Note(a.unit), dk = g.Note(a.targetUnit);
                int before = Math.Max(0, a.targetStrengthAfter) + a.dealt;
                Track at_ = tr.Find(att.id, ak, a.fromX, a.fromY), dt = tr.Find(def.id, dk, a.atX, a.atY);
                g.attacks.Add(new Attack {
                    round = round, side = att.id, kind = ak, targetSide = def.id, targetKind = dk, dealt = a.dealt, taken = a.taken,
                    distance = a.Distance, targetStrength = before, killed = a.killed, attackerDied = a.attackerDied, turnOf = mover,
                    unit = at_, target = dt
                });
                // The losses just before this line were its doing: the defender (killed by the attacker), the attacker
                // (killed by return fire), and cargo aboard either.
                Loss defLoss = a.killed ? pending.FindLast(x => x.side == def.id && x.kind == dk && x.cause == GameLog.Lines.Combat) : null;
                Loss attLoss = a.attackerDied ? pending.FindLast(x => x.side == att.id && x.kind == ak && x.cause == GameLog.Lines.Combat) : null;
                if (defLoss != null) { defLoss.killerSide = att.id; defLoss.killerKind = ak; defLoss.strength = before; defLoss.unit = dt; defLoss.killer = at_; }
                if (attLoss != null) { attLoss.killerSide = def.id; attLoss.killerKind = dk; attLoss.strength = Math.Max(0, a.strengthAfter) + a.taken; attLoss.unit = at_; attLoss.killer = dt; }
                if (a.killed) tr.Kill(dt, at_, round);
                else if (a.retreated) tr.Place(dt, a.retreatX, a.retreatY);
                if (a.attackerDied) tr.Kill(at_, dt, round);
                foreach (Loss l in pending)
                    if (l.cause == GameLog.Lines.Aboard && l.killerSide < 0) {
                        if (l.side == def.id && a.killed) { l.killerSide = att.id; l.killerKind = ak; }
                        else if (l.side == att.id && a.attackerDied) { l.killerSide = def.id; l.killerKind = dk; }
                    }
                pending.Clear();
                continue;
            }
            GameLog.Lines.StrikeLandedLine landed;
            if (GameLog.Lines.ReadStrikeLanded(line, names, out landed)) {
                // The combat losses just before this line were the strike's doing (and cargo aboard a transport it destroyed).
                Side by = g.SideNamed(landed.side);
                string bk = g.Note(landed.unit);
                Track striker = by != null ? tr.Landed(by.id, bk, landed.x, landed.y) : null;
                foreach (GameLog.Lines.StrikeHitLine h in landed.hits) {
                    if (!h.destroyed) continue;
                    Side hs = g.SideNamed(h.side);
                    string hk = g.Note(h.unit);
                    Loss l = hs != null ? pending.Find(x => x.side == hs.id && x.kind == hk && x.cause == GameLog.Lines.Combat && x.killerSide < 0) : null;
                    if (l != null && by != null) { l.killerSide = by.id; l.killerKind = bk; l.strength = h.damage; l.killer = striker; }
                    if (hs != null) { Track victim = tr.Find(hs.id, hk, h.x, h.y); if (l != null) l.unit = victim; tr.Kill(victim, striker, round); }
                }
                if (by != null) foreach (Loss l in pending) if (l.cause == GameLog.Lines.Aboard && l.killerSide < 0) { l.killerSide = by.id; l.killerKind = bk; }
                pending.Clear();
                continue;
            }
            int mx0, my0, mx1, my1;
            if (GameLog.Lines.ReadMoved(line, names, out sideName, out unit, out mx0, out my0, out mx1, out my1)) {
                Side s = g.SideNamed(sideName);
                if (s != null) tr.Place(tr.Find(s.id, g.Note(unit), mx0, my0), mx1, my1);
                continue;
            }
            GameLog.Lines.CargoLine cl;
            if (GameLog.Lines.ReadCargo(line, names, out cl)) {
                Side s = g.SideNamed(cl.side), ts = g.SideNamed(cl.transportSide);
                if (s == null || ts == null) continue;
                Track transport = tr.Find(ts.id, g.Note(cl.transportUnit), cl.transportX, cl.transportY);
                if (cl.boards) tr.Board(tr.Find(s.id, g.Note(cl.unit), cl.x, cl.y), transport);
                else tr.Place(tr.Aboard(transport, s.id, g.Note(cl.unit)), cl.x, cl.y);
                continue;
            }
            GameLog.Lines.StrikeLine sl;
            if (GameLog.Lines.ReadStrikeFired(line, names, out sl)) {
                Side s = g.SideNamed(sl.side);
                if (s != null) { string k = g.Note(sl.unit); tr.Fired(s.id, k, sl.x, sl.y, tr.Find(s.id, k, sl.fromX, sl.fromY)); }
                continue;
            }
            if (GameLog.Lines.ReadChangesHands(line, out name, out from, out to)) {
                Side s = g.SideNamed(to), was = g.SideNamed(from);
                if (s != null) g.captures.Add(new Capture { round = round, side = s.id, building = name, from = was != null ? was.id : -1 });
                continue;
            }
        }
        // The units each side starts with: those it fielded in the first turn of the log.
        foreach (Side side in g.sides) {
            foreach (Snap sn in g.snaps) {
                SideSnap ss = sn.sides.Find(x => x.id == side.id);
                if (ss == null) continue;
                foreach (KeyValuePair<string, int> kv in ss.fielded) g.start.Add(new Count { side = side.id, kind = kv.Key, n = kv.Value });
                break;
            }
        }
        // A loss out of supply or fuel destroyed whatever strength it had left: the strength in the turn's state is not
        // known per unit, so it counts as a whole unit.
        foreach (Loss l in g.losses) if (l.strength == 0 && l.cause != GameLog.Lines.Combat) { Kind k = g.KindNamed(l.kind); l.strength = k != null ? k.maxStrength : 1; }
        return g;
    }

    /// <summary>The kind a unit of this prefab name is counted as (and its attributes noted, from the prefab).</summary>
    string Note(string prefabName) {
        string k = KindOf(prefabName);
        if (kinds.Exists(x => x.name == k)) return k;
        Unit prefab = GameAssets.Unit(prefabName);
        if (prefab == null) prefab = GameAssets.Unit(k);
        kinds.Add(new Kind {
            name = k, cost = prefab != null ? prefab.cost : 0, range = prefab != null ? prefab.AttackRange : 0,
            maxStrength = prefab != null ? Math.Max(1, prefab.Strength) : 1, locomotion = prefab != null ? prefab.locomotion : default(Locomotion),
            move = prefab != null ? prefab.moveRange : 0, moveAndFire = prefab == null || prefab.moveAndAttack
        });
        return k;
    }

    /// <summary>
    /// The kind a prefab name is counted as: less " Variant"; a faction's version that only looks different
    /// (Faction.Cosmetic) as the base unit it stands in for.
    /// </summary>
    public static string KindOf(string prefabName) {
        string shown = prefabName;
        foreach (Faction f in GameAssets.AllFactions) {
            if (f == null || f.replacements == null) continue;
            foreach (Faction.Replacement r in f.replacements)
                if (r != null && r.replacement != null && r.original != null && GameController.UnitName(r.replacement) == prefabName && f.Cosmetic(r)) {
                    shown = GameController.UnitName(r.original);
                    break;
                }
        }
        return shown.EndsWith(" Variant") ? shown.Substring(0, shown.Length - " Variant".Length).Trim() : shown;
    }
}
