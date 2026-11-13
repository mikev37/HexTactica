using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// What a game's log says (read into a GameRecord), in sentences: how the strength of each side went, where it stalled,
/// which kinds of unit were fed into the other side's answers, which killers went unanswered, which good trades
/// production passed over. Each finding is a heading and a few sentences, as a player would tell it after the game.
///
/// Written from the viewer's side ("you", "your"): the human player at the screen, or the first side when every side
/// is played by an AI. Material is what units are worth as they stand (cost x the share of full strength left), in thousands.
/// Findings only appear where there is enough to say something (the thresholds are in <see cref="Limits"/>).
/// </summary>
public static class GameInsights {

    public class Finding {
        public string title;
        public string body;
    }

    /// <summary>The thresholds a finding needs before it is worth saying (set on the FrontEndSettings asset).</summary>
    [Serializable]
    public class Limits {
        [Tooltip("A stalemate: lasts at least this many rounds, the front moves no more than stallTiles in it, and each side loses at least stallLosses units in it.")]
        public int stallRounds = 6;          // a stalemate lasts at least this many rounds
        public float stallTiles = 2.5f;      // ... and the front moves no more than this many tiles in it
        public int stallLosses = 5;          // ... and each side loses at least this many units in it
        public int fedLosses = 5;            // a kind fed in: at least this many lost
        public float fedShare = 0.6f;        // ... and at least this share of those fielded
        public int badRoundLosses = 3;       // a bad turn: at least this many of one kind lost in one enemy turn
        public int killerKills = 8;          // a killer: at least this many kills
        public int unansweredRounds = 8;     // ... unanswered: not attacked for this many rounds while it kept killing
        public int bestTradeKills = 3;       // a good trade passed over: at least this many kills
        public int bestTradeBought = 3;      // ... from no more than this many bought
        public int supplyLosses = 3;         // losses to supply worth a word
        public float fedByOne = 0.4f;        // "fed into" one kind of the enemy's: it killed at least this share of them
        public float evenTrade = 0.5f;       // a stalemate trades at least this evenly (fewer losses / more losses); below, it is one-sided
        [Tooltip("Unit names (or their endings) said the same in the plural: \"Artillery\", \"Supplies\".")]
        public List<string> samePlural = new List<string> { "Artillery", "Supplies", "Anti Air", "Cache" };
    }

    static Limits limits { get { return FrontEndSettings.Get.insights; } }

    /// <summary>The side the report is written for: the human at the screen, else the first side.</summary>
    public static long ViewerId(GameRecord s) {
        if (s != null) foreach (GameRecord.Side side in s.sides) if (!side.ai) return side.id;
        return s != null && s.sides.Count > 0 ? s.sides[0].id : -1;
    }

    // ------------------------------------------------------------------ the report

    /// <summary>The whole report as text: the headline, every finding, and each side's units table.</summary>
    public static string Report(GameRecord s, long viewer) {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine(Headline(s, viewer));
        sb.AppendLine();
        foreach (Finding f in Findings(s, viewer)) {
            sb.AppendLine(f.title);
            sb.AppendLine(f.body);
            sb.AppendLine();
        }
        sb.Append(UnitTables(s, viewer));
        return sb.ToString();
    }

    /// <summary>
    /// "Blue won in round 31." / "No winner after 30 rounds." <paramref name="side"/>: the players who won together, by id
    /// (the winner and its allies: the game knows who is allied, the log does not); null: the log's winner alone.
    /// </summary>
    public static string Headline(GameRecord s, long viewer, ICollection<long> side = null) {
        if (s == null) return "No statistics were recorded for this game.";
        Ctx c = new Ctx(s, viewer);
        int rounds = s.lastRound + 1;
        string gaveUp = "";
        foreach (long id in s.resigned) gaveUp += " " + Cap(c.Name(id)) + " resigned.";
        if (s.over && s.winner >= 0) {
            bool viewerWon = side != null ? side.Contains(c.Viewer) : s.winner == c.Viewer;
            string who = Cap(c.Name(s.winner));
            if (side != null && side.Count > 1) {
                List<string> names = new List<string>();
                foreach (long id in side) names.Add(Cap(c.Name(id)));
                who = string.Join(", ", names.GetRange(0, names.Count - 1).ToArray()) + " and " + names[names.Count - 1];
            }
            return (c.ViewerHuman && !viewerWon ? "Defeat. " : c.ViewerHuman ? "Victory. " : "") + who + (side != null && side.Count > 1 ? " won together" : " won") + " in round " + s.lastRound + "." + gaveUp;
        }
        if (s.over) return "The game ended without a winner after " + rounds + " rounds.";
        return "Round " + s.lastRound + ": the game is still going.";
    }

    /// <summary>Every finding there is enough to say, most telling first.</summary>
    public static List<Finding> Findings(GameRecord s, long viewer) {
        List<Finding> all = new List<Finding>();
        if (s == null || s.sides.Count < 2) return all;
        Ctx c = new Ctx(s, viewer);
        Add(all, Strength(c));
        Add(all, Stalemate(c));
        Add(all, Cities(c));
        foreach (GameRecord.Side side in c.Sides) Add(all, FedIn(c, side.id));
        foreach (GameRecord.Side side in c.Sides) Add(all, Unanswered(c, side.id));
        foreach (GameRecord.Side side in c.Sides) Add(all, PassedOver(c, side.id));
        foreach (GameRecord.Side side in c.Sides) Add(all, Supply(c, side.id));
        Add(all, Spending(c));
        return all;
    }

    static void Add(List<Finding> all, Finding f) { if (f != null) all.Add(f); }

    // ------------------------------------------------------------------ findings

    /// <summary>How each side's strength went: where it started, whether the weaker side drew level, and where it ended.</summary>
    static Finding Strength(Ctx c) {
        List<int> rounds = c.Rounds;
        if (rounds.Count < 2) return null;
        long a = c.Viewer, b = c.Other(a);
        int ma0 = c.Material(rounds[0], a), mb0 = c.Material(rounds[0], b);
        if (ma0 <= 0 && mb0 <= 0) return null;
        long weak = ma0 <= mb0 ? a : b, strong = c.Other(weak);
        int w0 = Math.Min(ma0, mb0), s0 = Math.Max(ma0, mb0);
        StringBuilder sb = new StringBuilder();
        if (s0 > 0 && w0 < s0 * 0.9f) {
            sb.Append(Cap(c.Name(weak))).Append(" started at ").Append(Fraction(w0, s0)).Append(' ').Append(c.Poss(strong)).Append(" strength (")
              .Append(K(w0)).Append(" vs ").Append(K(s0)).Append(')');
            int level = -1;
            foreach (int r in rounds.Skip(1)) if (c.Material(r, weak) >= c.Material(r, strong)) { level = r; break; }
            if (level >= 0) sb.Append(" and had drawn level by round ").Append(level).Append(" (").Append(K(c.Material(level, weak))).Append(" vs ").Append(K(c.Material(level, strong))).Append(").");
            else {
                int widest = rounds.OrderByDescending(r => c.Material(r, strong) - c.Material(r, weak)).First();
                sb.Append(" and never drew level; the gap was widest in round ").Append(widest).Append(" (").Append(K(c.Material(widest, weak))).Append(" vs ").Append(K(c.Material(widest, strong))).Append(").");
            }
        } else {
            sb.Append("Both sides started about even (").Append(K(ma0)).Append(" for ").Append(c.Name(a)).Append(", ").Append(K(mb0)).Append(" for ").Append(c.Name(b)).Append(").");
            int lead = -1; long leader = -1;
            foreach (int r in rounds) {
                int x = c.Material(r, a), y = c.Material(r, b);
                if (Math.Max(x, y) > 0 && Math.Abs(x - y) > 0.25f * Math.Max(x, y)) { lead = r; leader = x > y ? a : b; break; }
            }
            if (lead >= 0) sb.Append(' ').Append(Cap(c.Name(leader))).Append(" pulled clearly ahead by round ").Append(lead).Append(" (").Append(K(c.Material(lead, leader))).Append(" vs ").Append(K(c.Material(lead, c.Other(leader)))).Append(").");
        }
        int last = rounds[rounds.Count - 1];
        sb.Append(" At the end: ").Append(K(c.Material(last, a))).Append(" for ").Append(c.Name(a)).Append(", ").Append(K(c.Material(last, b))).Append(" for ").Append(c.Name(b)).Append('.');
        int boughtA = c.s.buys.Where(x => x.side == a).Sum(x => x.cost), boughtB = c.s.buys.Where(x => x.side == b).Sum(x => x.cost);
        if (boughtA + boughtB > 0)
            sb.Append(' ').Append(Cap(c.Name(a))).Append(" spent ").Append(K(boughtA)).Append(" on units, ").Append(c.Name(b)).Append(' ').Append(K(boughtB)).Append('.');
        return new Finding { title = "How the strength went", body = sb.ToString() };
    }

    /// <summary>The longest stretch where both sides kept losing units and the front barely moved.</summary>
    static Finding Stalemate(Ctx c) {
        List<int> rounds = c.Rounds;
        if (c.s.axisLength <= 0 || rounds.Count < limits.stallRounds) return null;
        long a = c.Viewer, b = c.Other(a);
        int bestFrom = -1, bestTo = -1;
        for (int i = 0; i < rounds.Count; i++) {
            float lo = float.MaxValue, hi = float.MinValue;
            for (int j = i; j < rounds.Count; j++) {
                float f = c.Front(rounds[j]);
                if (f < 0) break;
                lo = Mathf.Min(lo, f); hi = Mathf.Max(hi, f);
                if ((hi - lo) * c.s.axisLength > limits.stallTiles) break;
                int from = rounds[i], to = rounds[j];
                if (to - from + 1 < limits.stallRounds) continue;
                if (c.Lost(a, from, to) < limits.stallLosses || c.Lost(b, from, to) < limits.stallLosses) continue;
                if (to - from > bestTo - bestFrom) { bestFrom = from; bestTo = to; }
            }
        }
        if (bestFrom < 0) return null;
        long focus = c.Lost(b, bestFrom, bestTo) >= c.Lost(a, bestFrom, bestTo) ? b : a;
        int lost = c.Lost(focus, bestFrom, bestTo), killed = c.Lost(c.Other(focus), bestFrom, bestTo);
        bool even = lost > 0 && killed >= limits.evenTrade * lost;
        StringBuilder sb = new StringBuilder();
        sb.Append("In rounds ").Append(bestFrom).Append('–').Append(bestTo).Append(' ').Append(c.Name(focus)).Append(" lost ")
          .Append(lost).Append(" units and killed ").Append(killed).Append(even ? ", and the front barely moved" : ", and gained no ground for it");
        float f0 = c.Front(bestFrom), f1 = c.Front(bestTo);
        sb.Append(" (it moved ").Append(Mathf.RoundToInt(Mathf.Abs(f1 - f0) * c.s.axisLength)).Append(Mathf.RoundToInt(Mathf.Abs(f1 - f0) * c.s.axisLength) == 1 ? " tile" : " tiles").Append(" in ").Append(bestTo - bestFrom + 1).Append(" rounds).");
        sb.Append(" Worth lost in that stretch: ").Append(K(c.ValueLost(a, bestFrom, bestTo))).Append(" for ").Append(c.Name(a)).Append(", ").Append(K(c.ValueLost(b, bestFrom, bestTo))).Append(" for ").Append(c.Name(b)).Append('.');
        string title = even ? "A grinding stalemate (rounds " + bestFrom + "–" + bestTo + ")" : Cap(c.Name(focus)) + " bled units for no ground (rounds " + bestFrom + "–" + bestTo + ")";
        return new Finding { title = title, body = sb.ToString() };
    }

    /// <summary>Who held the cities for most of the game.</summary>
    static Finding Cities(Ctx c) {
        List<int> rounds = c.Rounds;
        if (rounds.Count < 3) return null;
        long a = c.Viewer, b = c.Other(a);
        Dictionary<string, int> seen = new Dictionary<string, int>();
        foreach (int r in rounds) {
            string key = c.Cities(r, a) + ":" + c.Cities(r, b);
            int n; seen.TryGetValue(key, out n); seen[key] = n + 1;
        }
        bool anyCities = seen.Keys.Sum(k => int.Parse(k.Split(':')[0]) + int.Parse(k.Split(':')[1])) > 0;
        bool anyIncome = rounds.Any(r => c.Income(r, a) + c.Income(r, b) > 0);
        if (!anyCities && !anyIncome) return null;
        KeyValuePair<string, int> most = seen.OrderByDescending(kv => kv.Value).First();
        int ca = int.Parse(most.Key.Split(':')[0]), cb = int.Parse(most.Key.Split(':')[1]);
        int first = rounds[0], last = rounds[rounds.Count - 1];
        StringBuilder sb = new StringBuilder();
        if (!anyCities) sb.Append("There were no cities to hold.");
        else if (most.Value * 2 >= rounds.Count) {
            if (ca == cb) sb.Append("Both sides held ").Append(ca).Append(" cities each for most of the game.");
            else {
                long more = ca > cb ? a : b;
                sb.Append(Cap(c.Name(more))).Append(' ').Append(c.Verb(more, "held", "held")).Append(' ').Append(Math.Max(ca, cb)).Append(" cities to ")
                  .Append(c.Poss(c.Other(more))).Append(' ').Append(Math.Min(ca, cb)).Append(" for most of the game.");
            }
        } else {
            sb.Append("Cities changed hands through the game: ").Append(c.Cities(first, a)).Append('–').Append(c.Cities(first, b)).Append(" (").Append(c.Name(a)).Append('–').Append(c.Name(b))
              .Append(") at the start, ").Append(c.Cities(last, a)).Append('–').Append(c.Cities(last, b)).Append(" at the end.");
        }
        int takenA = c.s.captures.Count(x => x.side == a), takenB = c.s.captures.Count(x => x.side == b);
        if (takenA + takenB > 0) sb.Append(' ').Append(Cap(c.Name(a))).Append(" took ").Append(takenA).Append(takenA == 1 ? " building, " : " buildings, ").Append(c.Name(b)).Append(' ').Append(takenB).Append('.');
        // Income: where it started, and where it ended (what each side could buy with).
        int ia0 = c.Income(first, a), ib0 = c.Income(first, b), ia1 = c.Income(last, a), ib1 = c.Income(last, b);
        if (ia1 + ib1 > 0 && (ia0 != ia1 || ib0 != ib1))
            sb.Append(" Income went from ").Append(ia0).Append(" vs ").Append(ib0).Append(" to ").Append(ia1).Append(" vs ").Append(ib1)
              .Append(" (").Append(c.Name(a)).Append(" vs ").Append(c.Name(b)).Append(") a turn.");
        return new Finding { title = "Cities and income", body = sb.ToString() };
    }

    /// <summary>
    /// The kind <paramref name="side"/> lost most of, fed into the other side's answers: what killed them, what they
    /// killed in return, the worst turn for them, and — when the other side's main killer of them was a kind this side's
    /// faction trades for a dearer version it hardly bought — that the side never contested it, and what it bought instead.
    /// </summary>
    static Finding FedIn(Ctx c, long side) {
        long enemy = c.Other(side);
        string kind = null; int worst = 0;
        foreach (string k in c.KindsOf(side)) {
            int lost = c.LostOf(side, k), fielded = c.Fielded(side, k);
            if (lost < limits.fedLosses || fielded == 0 || lost < limits.fedShare * fielded) continue;
            int value = c.s.losses.Where(x => x.side == side && x.kind == k).Sum(x => x.cost);
            if (value > worst) { worst = value; kind = k; }
        }
        if (kind == null) return null;
        List<GameRecord.Loss> lost_ = c.s.losses.Where(x => x.side == side && x.kind == kind).ToList();
        StringBuilder sb = new StringBuilder();
        int bought = c.Bought(side, kind), started = c.Started(side, kind);
        sb.Append(Cap(c.Name(side))).Append(' ');
        if (bought > 0) sb.Append(c.Verb(side, "bought", "bought")).Append(' ').Append(bought).Append(' ').Append(Plural(kind, bought)).Append(started > 0 ? " (and started with " + started + ")" : "");
        else sb.Append(c.Verb(side, "started with", "started with")).Append(' ').Append(started).Append(' ').Append(Plural(kind, started));
        sb.Append(" and lost ").Append(lost_.Count);
        List<KeyValuePair<string, int>> killers = lost_.Where(x => x.killerSide == enemy && x.killerKind != "").GroupBy(x => x.killerKind)
            .Select(g => new KeyValuePair<string, int>(g.Key, g.Count())).OrderByDescending(kv => kv.Value).ToList();
        if (killers.Count > 0) {
            sb.Append(": ");
            for (int i = 0; i < Math.Min(3, killers.Count); i++) {
                if (i > 0) sb.Append(i == Math.Min(3, killers.Count) - 1 ? " and " : ", ");
                sb.Append(killers[i].Value).Append(" to ").Append(i == 0 ? c.Poss(enemy) + " " : "").Append(Plural(killers[i].Key, 2));
            }
        }
        int otherWays = lost_.Count(x => x.killerSide != enemy);
        if (otherWays > 0) sb.Append(otherWays == lost_.Count ? ", none in combat" : "; " + otherWays + " outside combat (supply, fuel, aboard a transport, or with their side)");
        sb.Append('.');
        int kills = c.KillsBy(side, kind);
        string topKiller = c.KindsOf(side).OrderByDescending(k => c.KillsBy(side, k)).FirstOrDefault();
        float trade = c.TradeOf(side, kind);
        if (kills > 0) {
            sb.Append(topKiller == kind ? " They were also " + c.PossIts(side) + " best killers (" + kills + " kills)" : " They killed " + kills);
            sb.Append(", ").Append(TradeWords(trade)).Append('.');
        } else sb.Append(" They killed nothing.");

        // The worst turn: most of this kind lost in one enemy turn.
        var turns = lost_.Where(x => x.turnOf == enemy).GroupBy(x => x.round).Select(g => new { round = g.Key, n = g.Count() }).OrderByDescending(x => x.n).ToList();
        if (turns.Count > 0 && turns[0].n >= limits.badRoundLosses) {
            int fieldedThen = c.Fielded(side, kind, turns[0].round);
            sb.Append("\nIn round ").Append(turns[0].round).Append(", ").Append(Number(turns[0].n)).Append(' ').Append(Plural(kind, turns[0].n))
              .Append(fieldedThen > 0 && fieldedThen <= turns[0].n ? " — all " + c.PossIts(side) + " " + Plural(kind, 2) + " on the board — " : " ")
              .Append("died in ").Append(c.Poss(enemy)).Append(" turn.");
        }

        // The other side's main killer of them, and how this side answered it.
        if (killers.Count > 0) {
            string answerTo = killers[0].Key;
            GameRecord.Side me = c.s.SideOf(side);
            Faction f = me != null ? GameAssets.Faction(me.faction) : null;
            Unit baseUnit = GameAssets.Unit(answerTo);
            if (f != null && baseUnit != null && f.replacements != null) {
                foreach (Faction.Replacement r in f.replacements) {
                    if (r == null || r.original == null || r.replacement == null || GameController.UnitName(r.original) != GameController.UnitName(baseUnit)) continue;
                    if (f.Cosmetic(r) || r.replacement.cost <= r.original.cost) continue;
                    string own = GameRecord.KindOf(GameController.UnitName(r.replacement));
                    int boughtOwn = c.Bought(side, own);
                    if (boughtOwn > 2) continue;
                    sb.Append('\n').Append(Cap(c.Name(side))).Append(" never contested ").Append(c.Poss(enemy)).Append(' ').Append(Plural(answerTo, 2)).Append(". ")
                      .Append(f.name).Append(" swaps the ").Append(answerTo).Append(" for the ").Append(r.replacement.cost.ToString("N0", CultureInfo.InvariantCulture)).Append(' ').Append(own)
                      .Append(", and ").Append(c.Name(side)).Append(' ').Append(c.Verb(side, "bought", "bought")).Append(' ').Append(boughtOwn).Append(" all game.");
                    break;
                }
            }
            // Its answer: the kind of its own that killed the most of that killer.
            var answer = c.s.losses.Where(x => x.side == enemy && x.kind == answerTo && x.killerSide == side && x.killerKind != "")
                .GroupBy(x => x.killerKind).Select(g => new { kind = g.Key, n = g.Count() }).OrderByDescending(x => x.n).FirstOrDefault();
            if (answer != null && answer.kind != kind) {
                int ab = c.Bought(side, answer.kind) + c.Started(side, answer.kind);
                var died = c.s.losses.Where(x => x.side == side && x.kind == answer.kind && x.killerSide == enemy && x.killerKind != "")
                    .GroupBy(x => x.killerKind).Select(g => new { kind = g.Key, n = g.Count() }).OrderByDescending(x => x.n).Take(2).ToList();
                sb.Append(' ').Append(Cap(c.Poss(side))).Append(" answer to them was ").Append(ab).Append(' ').Append(Plural(answer.kind, ab))
                  .Append(" (").Append(answer.n).Append(answer.n == 1 ? " kill" : " kills").Append(" on them)");
                if (died.Count > 0) {
                    sb.Append(", which mostly died to ");
                    for (int i = 0; i < died.Count; i++) sb.Append(i > 0 ? " and " : "").Append(Plural(died[i].kind, 2)).Append(" (").Append(died[i].n).Append(')');
                }
                sb.Append('.');
            }
        }
        // "Fed into" one answer when it took a good share of them; else they were simply worn down.
        string title = killers.Count > 0 && killers[0].Value >= limits.fedByOne * lost_.Count
            ? Cap(c.Poss(side)) + " " + Plural(kind, 2) + " fed into " + c.Poss(enemy) + " " + Plural(killers[0].Key, 2)
            : Cap(c.Poss(side)) + " " + Plural(kind, 2) + " were worn down";
        return new Finding { title = title, body = sb.ToString() };
    }

    /// <summary>
    /// A kind of <paramref name="side"/>'s that kept killing while the other side stopped attacking it: what it killed,
    /// from how far, and how far the other side could reach.
    /// </summary>
    static Finding Unanswered(Ctx c, long side) {
        long enemy = c.Other(side);
        Finding best = null; int bestKills = 0;
        foreach (string k in c.KindsOf(side)) {
            List<GameRecord.Attack> shots = c.s.attacks.Where(x => x.side == side && x.kind == k && x.targetSide == enemy).ToList();
            int kills = shots.Count(x => x.killed);
            if (kills < limits.killerKills) continue;
            List<GameRecord.Attack> onIt = c.s.attacks.Where(x => x.side == enemy && x.targetKind == k && x.targetSide == side).ToList();
            int lastHit = onIt.Count > 0 ? onIt.Max(x => x.round) : -1;
            int lastRound = c.s.lastRound;
            int from = lastHit + 1;
            if (lastRound - from + 1 < limits.unansweredRounds) continue;
            List<GameRecord.Attack> after = shots.Where(x => x.round >= from).ToList();
            int killsAfter = after.Count(x => x.killed);
            if (killsAfter < limits.killerKills / 2 || killsAfter <= bestKills) continue;
            bestKills = killsAfter;

            StringBuilder sb = new StringBuilder();
            int firstShot = after.Min(x => x.round), lastShot = after.Max(x => x.round);
            int shotRounds = after.Select(x => x.round).Distinct().Count();
            sb.Append(Cap(c.Poss(side))).Append(' ').Append(Plural(k, 2)).Append(" fired ").Append(Rate(after.Count, shotRounds)).Append(" from round ").Append(firstShot);
            int fullKills = after.Count(x => x.killed && c.FullStrength(x.targetKind, x.targetStrength));
            if (killsAfter > 0 && fullKills >= 0.6f * killsAfter) sb.Append(", and most shots that killed took out a full-strength unit");
            sb.Append(". They killed ").Append(killsAfter).Append(' ').Append(c.Name(enemy)).Append(" units in rounds ").Append(firstShot).Append('–').Append(lastShot);
            var victims = after.Where(x => x.killed).GroupBy(x => x.targetKind).Select(g => new { kind = g.Key, n = g.Count() }).OrderByDescending(x => x.n).Take(2).ToList();
            if (victims.Count > 0) {
                sb.Append(": ");
                for (int i = 0; i < victims.Count; i++) sb.Append(i > 0 ? " and " : "").Append(victims[i].n).Append(' ').Append(Plural(victims[i].kind, victims[i].n));
            }
            int allKills = shots.Count(x => x.killed);
            if (allKills > killsAfter) sb.Append(" (").Append(allKills).Append(" over the whole game)");
            sb.Append(".\n");
            if (onIt.Count == 0) sb.Append(Cap(c.Name(enemy))).Append(" never attacked one");
            else sb.Append(Cap(c.Name(enemy))).Append(" didn't attack one once after round ").Append(lastHit);
            sb.Append('.');
            float dist = after.Count > 0 ? (float)after.Average(x => x.distance) : 0f;
            GameRecord.Kind reach = c.KindsOf(enemy).Select(x => c.s.KindNamed(x)).Where(x => x != null && !x.Air).OrderByDescending(x => x.range).FirstOrDefault();
            GameRecord.Kind kk = c.s.KindNamed(k);
            if (dist >= 2f) {
                sb.Append(" They fired from ").Append(dist.ToString("0.#", CultureInfo.InvariantCulture)).Append(" tiles away on average");
                if (reach != null && reach.range < dist) sb.Append("; ").Append(c.Poss(enemy)).Append(" longest ground reach was ").Append(reach.range).Append(" (").Append(Plural(reach.name, 2)).Append(')');
                sb.Append('.');
            }
            List<string> air = c.KindsOf(enemy).Where(x => { GameRecord.Kind q = c.s.KindNamed(x); return q != null && q.Air && c.s.attacks.Any(a => a.side == enemy && a.kind == x); }).ToList();
            if (air.Count > 0 && kk != null && !kk.Air) {
                int airLost = c.s.losses.Count(x => x.side == enemy && air.Contains(x.kind) && x.round >= from);
                if (airLost > 0) sb.Append(' ').Append(Cap(c.Poss(enemy))).Append(" aircraft, which could have reached them, lost ").Append(airLost).Append(" in the same rounds.");
            }
            best = new Finding { title = Cap(Plural(k, 2)) + " had no answer (rounds " + firstShot + "–" + lastShot + ")", body = sb.ToString() };
        }
        return best;
    }

    /// <summary>The kind that traded best for <paramref name="side"/> while production hardly bought it.</summary>
    static Finding PassedOver(Ctx c, long side) {
        int spent = c.s.buys.Where(x => x.side == side).Sum(x => x.cost);
        if (spent <= 0) return null;
        string best = null; float bestRate = 0f;
        foreach (string k in c.KindsOf(side)) {
            int bought = c.Bought(side, k);
            int fielded = c.Fielded(side, k);
            int kills = c.KillsBy(side, k);
            if (bought > limits.bestTradeBought || fielded == 0 || kills < limits.bestTradeKills) continue;
            // Production can only be blamed for what it could buy, and only a kind that paid for itself is a good trade.
            if (bought == 0 && !c.s.canBuy.Exists(x => x.side == side && x.kind == k)) continue;
            if (!(c.TradeOf(side, k) >= 1f)) continue;
            float rate = kills / (float)fielded;
            if (rate > bestRate && rate >= c.KillRate(side) * 1.5f) { bestRate = rate; best = k; }
        }
        if (best == null) return null;
        int b = c.Bought(side, best), st = c.Started(side, best);
        StringBuilder sb = new StringBuilder();
        sb.Append(Cap(Plural(best, 2))).Append(" were ").Append(c.Poss(side)).Append(" best trade (")
          .Append(b > 0 ? b + " bought" : st + " at the start").Append(b > 0 && st > 0 ? ", " + st + " at the start" : "").Append(", ").Append(c.KillsBy(side, best)).Append(" kills, ")
          .Append(TradeWords(c.TradeOf(side, best))).Append("), and production ").Append(b == 0 ? "never chose them." : "almost never chose them.");
        sb.Append(" ").Append(Cap(c.Poss(side))).Append(" units overall killed about ").Append((c.KillRate(side)).ToString("0.0", CultureInfo.InvariantCulture)).Append(" each.");
        return new Finding { title = Cap(Plural(best, 2)) + ": " + c.Poss(side) + " best trade, hardly bought", body = sb.ToString() };
    }

    /// <summary>Units lost to supply (starved) and to fuel (aircraft ditched).</summary>
    static Finding Supply(Ctx c, long side) {
        int starved = c.s.losses.Count(x => x.side == side && x.cause == HexTactica.Harness.GameLog.Lines.Supply);
        int ditched = c.s.losses.Count(x => x.side == side && x.cause == HexTactica.Harness.GameLog.Lines.Fuel);
        if (starved + ditched < limits.supplyLosses) return null;
        StringBuilder sb = new StringBuilder(Cap(c.Name(side)) + " lost ");
        if (starved > 0) sb.Append(starved).Append(starved == 1 ? " unit" : " units").Append(" to starvation out of supply");
        if (starved > 0 && ditched > 0) sb.Append(" and ");
        if (ditched > 0) sb.Append(ditched).Append(ditched == 1 ? " aircraft" : " aircraft").Append(" that ran out of fuel");
        sb.Append('.');
        var kinds = c.s.losses.Where(x => x.side == side && (x.cause == HexTactica.Harness.GameLog.Lines.Supply || x.cause == HexTactica.Harness.GameLog.Lines.Fuel)).GroupBy(x => x.kind).OrderByDescending(g => g.Count()).Take(3).ToList();
        if (kinds.Count > 0) sb.Append(" They were ").Append(string.Join(", ", kinds.Select(g => g.Count() + " " + Plural(g.Key, g.Count())).ToArray())).Append(kinds.Sum(g => g.Count()) < starved + ditched ? " and others." : ".");
        return new Finding { title = Cap(c.Poss(side)) + " supply and fuel cost " + c.Verb(side, "you", "it") + " units", body = sb.ToString() };
    }

    /// <summary>What each side spent its money on.</summary>
    static Finding Spending(Ctx c) {
        StringBuilder sb = new StringBuilder();
        foreach (GameRecord.Side side in c.Sides) {
            List<GameRecord.Buy> buys = c.s.buys.Where(x => x.side == side.id).ToList();
            if (buys.Count == 0) continue;
            int total = buys.Sum(x => x.cost);
            var top = buys.GroupBy(x => x.kind).Select(g => new { kind = g.Key, n = g.Count(), cost = g.Sum(x => x.cost) }).OrderByDescending(x => x.cost).Take(4).ToList();
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(Cap(c.Name(side.id))).Append(' ').Append(c.Verb(side.id, "bought", "bought")).Append(' ').Append(buys.Count).Append(" units for ").Append(K(total)).Append(": ");
            sb.Append(string.Join(", ", top.Select(x => x.n + " " + Plural(x.kind, x.n) + " (" + Mathf.RoundToInt(100f * x.cost / total) + "%)").ToArray())).Append('.');
        }
        return sb.Length == 0 ? null : new Finding { title = "Production", body = sb.ToString() };
    }

    // ------------------------------------------------------------------ the units table

    /// <summary>One side's units table: its title, and a row per kind (unit, start, bought, lost, kills, trade).</summary>
    public class UnitTable {
        public string title;
        public List<string[]> rows = new List<string[]>();
    }

    /// <summary>What the trade column means (the post-game screen shows it under the tables).</summary>
    public const string TradeNote = "Trade: the worth a kind destroyed (damage x the target's cost per strength point) for the worth it lost; above 1 it paid for itself.";

    /// <summary>Each side's kinds of unit: started, bought, lost, kills, and the worth they destroyed for the worth they lost.</summary>
    public static List<UnitTable> UnitTablesOf(GameRecord s, long viewer) {
        List<UnitTable> tables = new List<UnitTable>();
        if (s == null) return tables;
        Ctx c = new Ctx(s, viewer);
        foreach (GameRecord.Side side in c.Sides) {
            UnitTable t = new UnitTable { title = Cap(c.Name(side.id)) + (side.faction.Length > 0 ? " (" + side.faction + ")" : "") };
            foreach (string k in c.KindsOf(side.id).OrderByDescending(k => c.Bought(side.id, k) + c.Started(side.id, k))) {
                float tr = c.TradeOf(side.id, k);
                t.rows.Add(new[] {
                    k, c.Started(side.id, k).ToString(CultureInfo.InvariantCulture), c.Bought(side.id, k).ToString(CultureInfo.InvariantCulture),
                    c.LostOf(side.id, k).ToString(CultureInfo.InvariantCulture), c.KillsBy(side.id, k).ToString(CultureInfo.InvariantCulture),
                    float.IsNaN(tr) ? "-" : float.IsInfinity(tr) ? "no losses" : tr.ToString("0.00", CultureInfo.InvariantCulture)
                });
            }
            tables.Add(t);
        }
        return tables;
    }

    /// <summary>The units tables as plain text (the headless report).</summary>
    public static string UnitTables(GameRecord s, long viewer) {
        StringBuilder sb = new StringBuilder();
        foreach (UnitTable t in UnitTablesOf(s, viewer)) {
            sb.Append(t.title).Append('\n');
            sb.Append(string.Format(CultureInfo.InvariantCulture, "{0,-24}{1,6}{2,7}{3,6}{4,7}{5,10}\n", "unit", "start", "bought", "lost", "kills", "trade"));
            foreach (string[] r in t.rows)
                sb.Append(string.Format(CultureInfo.InvariantCulture, "{0,-24}{1,6}{2,7}{3,6}{4,7}{5,10}\n", Trim(r[0], 23), r[1], r[2], r[3], r[4], r[5]));
            sb.Append('\n');
        }
        sb.Append(TradeNote).Append('\n');
        return sb.ToString();
    }

    // ------------------------------------------------------------------ words

    static string Trim(string s, int n) { return s.Length <= n ? s : s.Substring(0, n - 1) + "…"; }

    /// <summary>Material in thousands: 153k.</summary>
    public static string K(int material) { return Mathf.RoundToInt(material / 1000f).ToString(CultureInfo.InvariantCulture) + "k"; }

    static string Fraction(int w, int s) {
        float f = s > 0 ? w / (float)s : 1f;
        if (f < 0.4f) return "a third of";
        if (f < 0.6f) return "half";
        if (f < 0.72f) return "two thirds of";
        if (f < 0.85f) return "three quarters of";
        return Mathf.RoundToInt(f * 100f) + "% of";
    }

    static string TradeWords(float t) {
        if (float.IsNaN(t)) return "neither dealing nor taking damage";
        if (float.IsInfinity(t)) return "losing none";
        if (t < 0.5f) return "trading far below cost (" + t.ToString("0.00", CultureInfo.InvariantCulture) + ")";
        if (t < 0.9f) return "trading below cost (" + t.ToString("0.00", CultureInfo.InvariantCulture) + ")";
        if (t < 1f) return "trading slightly below cost (" + t.ToString("0.00", CultureInfo.InvariantCulture) + ")";
        if (t < 1.15f) return "about paying for themselves (" + t.ToString("0.00", CultureInfo.InvariantCulture) + ")";
        return "paying for themselves " + t.ToString("0.0", CultureInfo.InvariantCulture) + " times over";
    }

    static string Rate(int shots, int rounds) {
        if (rounds <= 0) return shots + " shots";
        float r = shots / (float)rounds;
        if (r >= 1.5f) return Mathf.FloorToInt(r) + "–" + Mathf.CeilToInt(r) + " shots a turn";
        return shots + " shots in " + rounds + " turns";
    }

    static readonly string[] small = { "no", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten" };
    static string Number(int n) { return n >= 0 && n < small.Length ? small[n] : n.ToString(CultureInfo.InvariantCulture); }

    /// <summary>"Gunship" → "Gunships", "Bus" → "Buses", one → itself.</summary>
    public static string Plural(string kind, int n) {
        if (n == 1 || string.IsNullOrEmpty(kind)) return kind;
        foreach (string same in limits.samePlural) if (!string.IsNullOrEmpty(same) && kind.EndsWith(same)) return kind;
        if (kind.EndsWith("s")) return kind;
        if (kind.EndsWith("x") || kind.EndsWith("ch") || kind.EndsWith("sh")) return kind + "es";
        if (kind.EndsWith("y") && kind.Length > 1 && "aeiou".IndexOf(kind[kind.Length - 2]) < 0) return kind.Substring(0, kind.Length - 1) + "ies";
        return kind + "s";
    }

    static string Cap(string s) { return string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1); }

    // ------------------------------------------------------------------ reading the statistics

    /// <summary>The statistics, read from the viewer's side.</summary>
    class Ctx {
        public readonly GameRecord s;
        public readonly long Viewer;
        public readonly bool ViewerHuman;
        public readonly List<GameRecord.Side> Sides;
        public readonly List<int> Rounds;
        readonly Dictionary<int, GameRecord.Snap> byRound = new Dictionary<int, GameRecord.Snap>();

        public Ctx(GameRecord s, long viewer) {
            this.s = s;
            Sides = s.sides.Take(2).ToList();
            Viewer = Sides.Exists(x => x.id == viewer) ? viewer : (Sides.Count > 0 ? Sides[0].id : -1);
            GameRecord.Side v = s.SideOf(Viewer);
            ViewerHuman = v != null && !v.ai;
            // The last snapshot of each round: the board as the round ended.
            foreach (GameRecord.Snap sn in s.snaps) byRound[sn.round] = sn;
            Rounds = byRound.Keys.OrderBy(r => r).ToList();
        }

        public long Other(long id) { foreach (GameRecord.Side x in Sides) if (x.id != id) return x.id; return -1; }
        bool You(long id) { return ViewerHuman && id == Viewer; }
        public string Name(long id) { if (You(id)) return "you"; GameRecord.Side x = s.SideOf(id); return x != null ? x.name : "?"; }
        public string Poss(long id) { if (You(id)) return "your"; return Name(id) + "'s"; }
        /// <summary>"its" for a side, "your" for the viewer.</summary>
        public string PossIts(long id) { return You(id) ? "your" : "its"; }
        public string Verb(long id, string you, string it) { return You(id) ? you : it; }

        GameRecord.SideSnap At(int round, long id) {
            GameRecord.Snap sn;
            if (!byRound.TryGetValue(round, out sn)) return null;
            return sn.sides.Find(x => x.id == id);
        }
        public int Material(int round, long id) { GameRecord.SideSnap x = At(round, id); return x != null ? x.material : 0; }
        public int Cities(int round, long id) { GameRecord.SideSnap x = At(round, id); return x != null ? x.cities : 0; }
        public int Income(int round, long id) { GameRecord.SideSnap x = At(round, id); return x != null ? x.income : 0; }
        /// <summary>The front in a round: midway between the two sides' average positions on the HQ-to-HQ line; -1 if unknown.</summary>
        public float Front(int round) {
            GameRecord.SideSnap a = At(round, Viewer), b = At(round, Other(Viewer));
            if (a == null || b == null || a.front < 0 || b.front < 0) return -1f;
            return (a.front + b.front) * 0.5f;
        }
        public int Lost(long side, int from, int to) { return s.losses.Count(x => x.side == side && x.round >= from && x.round <= to); }
        /// <summary>What the units it lost in those rounds cost.</summary>
        public int ValueLost(long side, int from, int to) { return s.losses.Where(x => x.side == side && x.round >= from && x.round <= to).Sum(x => x.cost); }

        public IEnumerable<string> KindsOf(long side) {
            return s.start.Where(x => x.side == side).Select(x => x.kind)
                .Concat(s.buys.Where(x => x.side == side).Select(x => x.kind))
                .Concat(s.losses.Where(x => x.side == side).Select(x => x.kind))
                .Concat(s.attacks.Where(x => x.side == side).Select(x => x.kind)).Distinct();
        }
        public int Started(long side, string k) { GameRecord.Count c = s.start.Find(x => x.side == side && x.kind == k); return c != null ? c.n : 0; }
        public int Bought(long side, string k) { return s.buys.Count(x => x.side == side && x.kind == k); }
        public int Fielded(long side, string k) { return Started(side, k) + Bought(side, k); }
        /// <summary>How many of the kind it had as the round began (the log's "fields" line for the round).</summary>
        public int Fielded(long side, string k, int round) {
            GameRecord.Snap first = s.snaps.Find(x => x.round == round);
            GameRecord.SideSnap ss = first != null ? first.sides.Find(x => x.id == side) : null;
            int n;
            return ss != null && ss.fielded.TryGetValue(k, out n) ? n : 0;
        }
        public int LostOf(long side, string k) { return s.losses.Count(x => x.side == side && x.kind == k); }
        public int KillsBy(long side, string k) { return s.losses.Count(x => x.killerSide == side && x.killerKind == k); }
        /// <summary>Kills per unit fielded, over every kind of the side's.</summary>
        public float KillRate(long side) {
            int fielded = KindsOf(side).Sum(k => Fielded(side, k));
            int kills = s.losses.Count(x => x.killerSide == side);
            return fielded > 0 ? kills / (float)fielded : 0f;
        }
        /// <summary>The worth it destroyed (damage x the target's cost per strength point) for the worth it lost (strength lost x its own).</summary>
        public float TradeOf(long side, string k) {
            float point = PointCost(k);
            float dealt = 0f, lost = 0f;
            foreach (GameRecord.Attack a in s.attacks) {
                if (a.side == side && a.kind == k) { dealt += a.dealt * PointCost(a.targetKind); lost += a.taken * point; }
                if (a.targetSide == side && a.targetKind == k) { dealt += a.taken * PointCost(a.kind); lost += a.dealt * point; }
            }
            foreach (GameRecord.Loss l in s.losses) if (l.side == side && l.kind == k && l.cause != HexTactica.Harness.GameLog.Lines.Combat) lost += l.strength * point;
            return lost > 0f ? dealt / lost : (dealt > 0f ? float.PositiveInfinity : float.NaN);
        }
        /// <summary>What one point of strength of the kind is worth: its cost over its full strength.</summary>
        float PointCost(string kind) { GameRecord.Kind k = s.KindNamed(kind); return k != null ? k.cost / (float)Math.Max(1, k.maxStrength) : 0f; }
        public bool FullStrength(string kind, int strength) { GameRecord.Kind k = s.KindNamed(kind); return k != null && strength >= k.maxStrength; }
    }
}
