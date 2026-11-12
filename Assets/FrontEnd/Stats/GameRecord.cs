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

    public class Side { public long id; public string name; public string faction = ""; public bool ai; }

    public class Kind {
        public string name; public int cost; public int range; public int maxStrength = 1;
        public Locomotion locomotion;
        public bool Air { get { return LocomotionRules.IsAirborne(locomotion); } }
    }

    /// <summary>One side at the start of a turn.</summary>
    public class SideSnap {
        public long id;
        /// <summary>What its units are worth as they stand: each unit's cost times the share of its full strength it has (HQs not counted).</summary>
        public int material;
        public int units, cities, buildings, money, income;
        /// <summary>Where its units stand on the line from the first side's HQ (0) to the second's (1), on average; -1 if none.</summary>
        public float front = -1f;
        /// <summary>Its units by kind.</summary>
        public Dictionary<string, int> fielded = new Dictionary<string, int>();
    }

    public class Snap { public int round; public long mover; public List<SideSnap> sides = new List<SideSnap>(); }
    public class Count { public long side; public string kind; public int n; }
    public class Buy { public int round; public long side; public string kind; public int cost; }

    /// <summary>A unit lost: to whom, how, and when.</summary>
    public class Loss {
        public int round; public long side; public string kind; public int cost;
        /// <summary>Its strength when it was lost (what was destroyed).</summary>
        public int strength;
        /// <summary>GameLog.Lines.Combat (destroyed attacking or attacked: the killer is named), Supply (starved), Fuel (ditched), Aboard (went down with its transport).</summary>
        public string cause;
        public long killerSide = -1; public string killerKind = ""; public long turnOf = -1;
    }

    public class Attack {
        public int round; public long side; public string kind; public long targetSide; public string targetKind;
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
    /// <summary>The sides that gave the game up.</summary>
    public List<long> resigned = new List<long>();
    public bool over;
    public long winner = -1;
    public int lastRound;

    public Side SideOf(long id) { return sides.Find(x => x.id == id); }
    public Side SideNamed(string name) { return sides.Find(x => x.name == name); }
    public Kind KindNamed(string k) { return kinds.Find(x => x.name == k); }

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

        foreach (string raw in lines) {
            string line = raw.TrimEnd('\r');
            long id; string name, faction, sideName, unit, cause, from, to; bool ai; int n;

            if (GameLog.Lines.ReadSide(line, out id, out name, out faction, out ai)) {
                Side s = g.SideOf(id);
                if (s == null) { s = new Side { id = id }; g.sides.Add(s); }
                s.name = name; s.faction = faction; s.ai = ai;
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
                if (s != null) g.buys.Add(new Buy { round = round, side = s.id, kind = g.Note(unit), cost = cost });
                continue;
            }
            if (GameLog.Lines.ReadDies(line, names, out sideName, out unit, out cause)) {
                Side s = g.SideNamed(sideName);
                if (s == null) continue;
                string k = g.Note(unit);
                Kind kind = g.KindNamed(k);
                Loss l = new Loss { round = round, side = s.id, kind = k, cost = kind != null ? kind.cost : 0, cause = cause, turnOf = mover };
                g.losses.Add(l);
                if (cause == GameLog.Lines.Combat || cause == GameLog.Lines.Aboard) pending.Add(l);
                continue;
            }
            GameLog.Lines.AttackLine a;
            if (GameLog.Lines.ReadAttack(line, names, out a)) {
                Side att = g.SideNamed(a.side), def = g.SideNamed(a.targetSide);
                if (att == null || def == null) { pending.Clear(); continue; }
                string ak = g.Note(a.unit), dk = g.Note(a.targetUnit);
                int before = Math.Max(0, a.targetStrengthAfter) + a.dealt;
                g.attacks.Add(new Attack {
                    round = round, side = att.id, kind = ak, targetSide = def.id, targetKind = dk, dealt = a.dealt, taken = a.taken,
                    distance = a.Distance, targetStrength = before, killed = a.killed, attackerDied = a.attackerDied, turnOf = mover
                });
                // The losses just before this line were its doing: the defender (killed by the attacker), the attacker
                // (killed by return fire), and cargo aboard either.
                Loss defLoss = a.killed ? pending.FindLast(x => x.side == def.id && x.kind == dk && x.cause == GameLog.Lines.Combat) : null;
                Loss attLoss = a.attackerDied ? pending.FindLast(x => x.side == att.id && x.kind == ak && x.cause == GameLog.Lines.Combat) : null;
                if (defLoss != null) { defLoss.killerSide = att.id; defLoss.killerKind = ak; defLoss.strength = before; }
                if (attLoss != null) { attLoss.killerSide = def.id; attLoss.killerKind = dk; attLoss.strength = Math.Max(0, a.strengthAfter) + a.taken; }
                foreach (Loss l in pending)
                    if (l.cause == GameLog.Lines.Aboard && l.killerSide < 0) {
                        if (l.side == def.id && a.killed) { l.killerSide = att.id; l.killerKind = ak; }
                        else if (l.side == att.id && a.attackerDied) { l.killerSide = def.id; l.killerKind = dk; }
                    }
                pending.Clear();
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
            maxStrength = prefab != null ? Math.Max(1, prefab.Strength) : 1, locomotion = prefab != null ? prefab.locomotion : default(Locomotion)
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
