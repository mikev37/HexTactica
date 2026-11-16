using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HexTactica.Harness {

    public static partial class GameLog {

        /// <summary>
        /// The wording of every game event line in game.log, written and read in one place: each line has a method that
        /// writes it and one that reads it back, next to each other, so a replay, a network turn or the post-game
        /// report reads exactly what was written. Change a line's wording here, in both, and nowhere else.
        ///
        /// A side is named by its personal name; a unit by its side's name and its prefab's name ("Blue Grunt"); a tile
        /// by its coordinates "(x,y)", or "(gone)". The AI's trace lines are written elsewhere and are not events.
        /// </summary>
        public static class Lines {
            public const string Indent = "   ";
            const string Tile = @"\((?:-?\d+,-?\d+|gone)\)";
            static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

            public static string D(HexTile t) { return t == null ? "(gone)" : "(" + t.x + "," + t.y + ")"; }
            public static string UnitName(Unit u) { return u == null ? "?" : u.name.Replace("(Clone)", "").Trim(); }
            public static string Who(Unit u) { return u == null ? "?" : (u.owner != null ? u.owner.personal_name + " " : "") + UnitName(u); }
            static string Name(Player p) { return p != null ? p.personal_name : "neutral"; }

            /// <summary>"Blue Grunt" → side "Blue", unit "Grunt": the longest side name it starts with.</summary>
            public static bool SplitWho(string who, IEnumerable<string> sides, out string side, out string unit) {
                side = null; unit = who;
                foreach (string s in sides)
                    if (who.StartsWith(s + " ", StringComparison.Ordinal) && (side == null || s.Length > side.Length)) side = s;
                if (side == null) return false;
                unit = who.Substring(side.Length + 1);
                return true;
            }

            // ---------------------------------------------------------- header

            /// <summary>A side: its id, name, faction ("-" none), and who plays it. Written at the start of a recording and again when a save is resumed (who plays it may have changed).</summary>
            public static string Side(Player p) {
                return "side: " + p.id.ToString(Inv) + " " + p.personal_name + " | faction " + (p.faction != null ? p.faction.name : "-") + " | " + (AIControl.Of(p) != null ? "AI" : "human");
            }
            static readonly Regex side = new Regex(@"^side: (-?\d+) (.+) \| faction (.+) \| (AI|human)$");
            public static bool ReadSide(string line, out long id, out string name, out string faction, out bool ai) {
                Match m = side.Match(line);
                id = 0; name = faction = null; ai = false;
                if (!m.Success) return false;
                id = long.Parse(m.Groups[1].Value, Inv); name = m.Groups[2].Value;
                faction = m.Groups[3].Value == "-" ? "" : m.Groups[3].Value; ai = m.Groups[4].Value == "AI";
                return true;
            }

            /// <summary>How many tiles lie between the first two sides' HQs: the line "front" is measured along (0 the first's HQ, 1 the second's).</summary>
            public static string Axis(int tiles) { return "axis: " + tiles.ToString(Inv); }
            static readonly Regex axis = new Regex(@"^axis: (\d+)$");
            public static bool ReadAxis(string line, out int tiles) { Match m = axis.Match(line); tiles = m.Success ? int.Parse(m.Groups[1].Value, Inv) : 0; return m.Success; }

            /// <summary>The game's fog rule: "fog: on" / "fog: off".</summary>
            public static string Fog(bool on) { return "fog: " + (on ? "on" : "off"); }
            public static bool ReadFog(string line, out bool on) {
                on = line == "fog: on";
                return on || line == "fog: off";
            }

            public static string Source(string from) { return "source: " + from; }
            public static bool ReadSource(string line, out string from) { from = line.StartsWith("source: ", StringComparison.Ordinal) ? line.Substring(8) : null; return from != null; }

            // ---------------------------------------------------------- turns

            public static string Round(int round, string side) { return "== round " + round.ToString(Inv) + ": " + side + " to move"; }
            static readonly Regex roundLine = new Regex(@"^== round (\d+): (.+) to move$");
            public static bool ReadRound(string line, out int round, out string side) {
                Match m = roundLine.Match(line);
                round = 0; side = null;
                if (!m.Success) return false;
                round = int.Parse(m.Groups[1].Value, Inv); side = m.Groups[2].Value;
                return true;
            }

            public static string GameOver(int round, Player winner) { return "== game over after round " + round.ToString(Inv) + ": winner " + (winner != null ? winner.personal_name : "none"); }
            static readonly Regex over = new Regex(@"^== game over after round (\d+): winner (.+)$");
            public static bool ReadGameOver(string line, out int round, out string winner) {
                Match m = over.Match(line);
                round = 0; winner = null;
                if (!m.Success) return false;
                round = int.Parse(m.Groups[1].Value, Inv); winner = m.Groups[2].Value == "none" ? null : m.Groups[2].Value;
                return true;
            }

            /// <summary>A side at the start of a turn: worth (each unit's cost x the share of full strength it has; HQs not counted), units, cities, buildings, income a turn, gold, and where its units stand on the HQ-to-HQ line on average (-1 none).</summary>
            public static string State(string sideName, int worth, int units, int cities, int buildings, int income, int gold, float front) {
                return Indent + "state " + sideName + ": worth " + worth.ToString(Inv) + ", units " + units.ToString(Inv) + ", cities " + cities.ToString(Inv) + ", buildings " + buildings.ToString(Inv)
                     + ", income " + income.ToString(Inv) + ", gold " + gold.ToString(Inv) + ", front " + front.ToString("0.000", Inv);
            }
            static readonly Regex state = new Regex(@"^   state (.+): worth (-?\d+), units (\d+), cities (\d+), buildings (\d+), income (-?\d+), gold (-?\d+), front (-?[\d.]+)$");
            public static bool ReadState(string line, out string sideName, out int worth, out int units, out int cities, out int buildings, out int income, out int gold, out float front) {
                Match m = state.Match(line);
                sideName = null; worth = units = cities = buildings = income = gold = 0; front = -1f;
                if (!m.Success) return false;
                sideName = m.Groups[1].Value;
                worth = int.Parse(m.Groups[2].Value, Inv); units = int.Parse(m.Groups[3].Value, Inv); cities = int.Parse(m.Groups[4].Value, Inv);
                buildings = int.Parse(m.Groups[5].Value, Inv); income = int.Parse(m.Groups[6].Value, Inv); gold = int.Parse(m.Groups[7].Value, Inv);
                front = float.Parse(m.Groups[8].Value, Inv);
                return true;
            }

            /// <summary>A side's units at the start of a turn, by kind: "Blue fields: 10 Grunt, 3 Tank".</summary>
            public static string Fields(string sideName, IEnumerable<KeyValuePair<string, int>> counts) { return Indent + sideName + " fields: " + Counts(counts); }
            public static bool ReadFields(string line, IEnumerable<string> sides, out string sideName, out List<KeyValuePair<string, int>> counts) {
                return ReadCounts(line, " fields: ", sides, out sideName, out counts);
            }

            /// <summary>What a side can buy at the start of a turn (at buildings it holds, money aside): "Blue can buy: Grunt, Tank".</summary>
            public static string CanBuy(string sideName, IEnumerable<string> units) { return Indent + sideName + " can buy: " + string.Join(", ", new List<string>(units).ToArray()); }
            public static bool ReadCanBuy(string line, IEnumerable<string> sides, out string sideName, out List<string> units) {
                units = null;
                string rest;
                if (!After(line, " can buy: ", sides, out sideName, out rest)) return false;
                units = new List<string>();
                foreach (string u in rest.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries)) units.Add(u);
                return true;
            }

            // ---------------------------------------------------------- events

            /// <summary>A unit bought: "Blue buys Grunt at City (3,4) for 2500".</summary>
            public static string Buy(Building at, Unit made) {
                return Indent + Name(made.owner) + " buys " + UnitName(made) + " at " + (at != null ? at.name.Replace("(Clone)", "").Trim() : "?") + " " + D(at != null ? at.tile : null) + " for " + made.cost.ToString(Inv)
                     + (made.tile != null ? ", placed " + D(made.tile) : "");
            }

            /// <summary>Where a bought unit was put, from its buy line ("..., placed (3,5)"); false for a log written before that was recorded.</summary>
            public static bool ReadBuyPlace(string line, out int x, out int y) {
                Match m = placed.Match(line);
                x = y = 0;
                if (!m.Success) m = boughtAt.Match(line);   // (a log from before the place was written: a unit is made on its building's tile)
                if (!m.Success) return false;
                x = int.Parse(m.Groups[1].Value, Inv); y = int.Parse(m.Groups[2].Value, Inv);
                return true;
            }
            /// <summary>The building a unit was bought at, from its buy line ("... at Factory (3,5) for 300").</summary>
            public static bool ReadBuyAt(string line, out int x, out int y) {
                Match m = boughtAtBuilding.Match(line);
                x = y = 0;
                if (!m.Success) return false;
                x = int.Parse(m.Groups[1].Value, Inv); y = int.Parse(m.Groups[2].Value, Inv);
                return true;
            }
            static readonly Regex boughtAtBuilding = new Regex(@" \((-?\d+),(-?\d+)\) for \d+(?:, placed \(-?\d+,-?\d+\))?$");
            static readonly Regex placed = new Regex(@", placed \((-?\d+),(-?\d+)\)$");
            static readonly Regex boughtAt = new Regex(@" \((-?\d+),(-?\d+)\) for \d+$");

            /// <summary>A unit ended its action somewhere else: "Blue Tank moved (1,2) -> (3,2)".</summary>
            /// <summary>
            /// A unit's move: "Blue Tank moved (1,2) -> (3,2)"; stopped short of where it was ordered (it ran into a unit
            /// it could not see), "Blue Tank moved (1,2) -> (3,2), ordered to (5,2), stopped short".
            /// </summary>
            public static string Moved(Unit u, HexTile from, HexTile to, HexTile ordered = null) {
                return Indent + Who(u) + " moved " + D(from) + " -> " + D(to) + (ordered != null && ordered != to ? ", ordered to " + D(ordered) + ", stopped short" : "");
            }
            static readonly Regex moved = new Regex(@"^   (.+) moved \((-?\d+),(-?\d+)\) -> \((-?\d+),(-?\d+)\)(?:, ordered to \((-?\d+),(-?\d+)\), stopped short)?$");

            /// <summary>Where a move line says the unit was ordered to, when it stopped short of it; false when it got where it was ordered (or the log is older).</summary>
            public static bool ReadMoveOrder(string line, out int x, out int y) {
                Match m = moved.Match(line);
                x = y = 0;
                if (!m.Success || !m.Groups[6].Success) return false;
                x = int.Parse(m.Groups[6].Value, Inv); y = int.Parse(m.Groups[7].Value, Inv);
                return true;
            }
            public static bool ReadMoved(string line, IEnumerable<string> sides, out string sideName, out string unit, out int fromX, out int fromY, out int toX, out int toY) {
                Match m = moved.Match(line);
                sideName = unit = null; fromX = fromY = toX = toY = 0;
                if (!m.Success || !SplitWho(m.Groups[1].Value, sides, out sideName, out unit)) return false;
                fromX = int.Parse(m.Groups[2].Value, Inv); fromY = int.Parse(m.Groups[3].Value, Inv);
                toX = int.Parse(m.Groups[4].Value, Inv); toY = int.Parse(m.Groups[5].Value, Inv);
                return true;
            }

            /// <summary>A unit got into a transport: "Blue Grunt (1,2) boards Blue APC (2,2)" (the tile it stood on, the transport's).</summary>
            public static string Boards(Unit transport, Unit cargo, HexTile from) { return Indent + Who(cargo) + " " + D(from) + " boards " + Who(transport) + " " + D(transport.tile); }
            static readonly Regex boards = new Regex(@"^   (.+) \((-?\d+),(-?\d+)\) boards (.+) \((-?\d+),(-?\d+)\)$");
            /// <summary>A unit got out of a transport: "Blue Grunt gets off Blue APC (2,2) onto (3,2)".</summary>
            public static string GetsOff(Unit transport, Unit cargo, HexTile onto) { return Indent + Who(cargo) + " gets off " + Who(transport) + " " + D(transport.tile) + " onto " + D(onto); }
            static readonly Regex getsOff = new Regex(@"^   (.+) gets off (.+) \((-?\d+),(-?\d+)\) onto \((-?\d+),(-?\d+)\)$");

            /// <summary>A boards or gets-off line read back: who, which transport and where it stood, and the cargo's tile (stood on before boarding, or put down on).</summary>
            public class CargoLine { public bool boards; public string side, unit, transportSide, transportUnit; public int x, y, transportX, transportY; }
            public static bool ReadCargo(string line, IEnumerable<string> sides, out CargoLine c) {
                c = null;
                CargoLine r = new CargoLine();
                Match m = boards.Match(line);
                if (m.Success) {
                    r.boards = true;
                    if (!SplitWho(m.Groups[1].Value, sides, out r.side, out r.unit) || !SplitWho(m.Groups[4].Value, sides, out r.transportSide, out r.transportUnit)) return false;
                    r.x = int.Parse(m.Groups[2].Value, Inv); r.y = int.Parse(m.Groups[3].Value, Inv);
                    r.transportX = int.Parse(m.Groups[5].Value, Inv); r.transportY = int.Parse(m.Groups[6].Value, Inv);
                } else {
                    m = getsOff.Match(line);
                    if (!m.Success) return false;
                    if (!SplitWho(m.Groups[1].Value, sides, out r.side, out r.unit) || !SplitWho(m.Groups[2].Value, sides, out r.transportSide, out r.transportUnit)) return false;
                    r.transportX = int.Parse(m.Groups[3].Value, Inv); r.transportY = int.Parse(m.Groups[4].Value, Inv);
                    r.x = int.Parse(m.Groups[5].Value, Inv); r.y = int.Parse(m.Groups[6].Value, Inv);
                }
                c = r;
                return true;
            }

            /// <summary>A side gave the game up: "Blue resigns".</summary>
            public static string Resigns(Player p) { return Indent + Name(p) + " resigns"; }
            public static bool ReadResigns(string line, IEnumerable<string> sides, out string sideName) {
                sideName = null;
                foreach (string s in sides) if (line == Indent + s + " resigns") sideName = s;
                return sideName != null;
            }

            /// <summary>The tile a dies or changes-hands line names (its last "(x,y)" before the colon part).</summary>
            public static bool ReadTile(string line, out int x, out int y) {
                Match m = anyTile.Match(line);
                x = y = 0;
                if (!m.Success) return false;
                x = int.Parse(m.Groups[1].Value, Inv); y = int.Parse(m.Groups[2].Value, Inv);
                return true;
            }
            static readonly Regex anyTile = new Regex(@"\((-?\d+),(-?\d+)\) (?:dies|changes hands):");
            static readonly Regex buy = new Regex(@"^   (.+?) buys (.+) at (.+) " + Tile + @" for (\d+)(?:, placed " + Tile + @")?$");
            public static bool ReadBuy(string line, IEnumerable<string> sides, out string sideName, out string unit, out int cost) {
                Match m = buy.Match(line);
                sideName = unit = null; cost = 0;
                if (!m.Success) return false;
                // The side is the longest side name the line starts with (a side's name may hold spaces).
                string ignored;
                if (!SplitWho(line.Substring(Indent.Length), sides, out sideName, out ignored)) return false;
                string rest = line.Substring(Indent.Length + sideName.Length);
                Match r = new Regex(@"^ buys (.+) at (.+) " + Tile + @" for (\d+)(?:, placed " + Tile + @")?$").Match(rest);
                if (!r.Success) return false;
                unit = r.Groups[1].Value; cost = int.Parse(r.Groups[3].Value, Inv);
                return true;
            }

            /// <summary>The ways a unit is lost.</summary>
            public const string Combat = "combat", Supply = "supply", Fuel = "fuel", Aboard = "aboard", Eliminated = "eliminated";

            /// <summary>A unit lost: "Blue Grunt (3,4) dies: supply" (eliminated: its side lost its HQ and every unit with it). Combat deaths come just before the attack line that caused them (which says who killed it); cargo lost "aboard" went down with a transport killed in the next attack line.</summary>
            public static string Dies(Unit u, string cause) { return Indent + Who(u) + " " + D(u.tile) + " dies: " + cause; }
            static readonly Regex dies = new Regex(@"^   (.+) " + Tile + @" dies: (\w+)$");
            public static bool ReadDies(string line, IEnumerable<string> sides, out string sideName, out string unit, out string cause) {
                Match m = dies.Match(line);
                sideName = unit = cause = null;
                if (!m.Success) return false;
                cause = m.Groups[2].Value;
                return SplitWho(m.Groups[1].Value, sides, out sideName, out unit);
            }

            /// <summary>An attack: "Blue Tank (1,2) attacks Red Grunt (2,2): deals 4, takes 1, Grunt destroyed -> 5 v 0".</summary>
            public static string Attack(Unit attacker, Unit defender, HexTile from, HexTile at, CombatOutcome o) {
                StringBuilder sb = new StringBuilder();
                sb.Append(Indent).Append(Who(attacker)).Append(' ').Append(D(from)).Append(" attacks ").Append(Who(defender)).Append(' ').Append(D(at))
                  .Append(": deals ").Append(o.damageToDefender).Append(", takes ").Append(o.damageToAttacker);
                if (o.defenderFiresFirst) sb.Append(" (ambush: defender fired first)");
                if (o.defenderDies) sb.Append(", ").Append(UnitName(defender)).Append(" destroyed");
                else if (o.defenderSuppressed) sb.Append(", suppressed " + o.suppressionAdded + " (" + defender.suppressed + " of " + defender.Strength + " will not fire back until its turn)");
                else if (o.defenderRetreats) sb.Append(o.defenderPinned ? ", pinned (1 of that for having nowhere to retreat)" : ", retreats" + (o.retreatTile != null ? " to " + D(o.retreatTile) : ""));
                if (o.attackerDies) sb.Append(AttackerDestroyed);
                sb.Append(" -> ").Append(attacker.Strength).Append(" v ").Append(defender.Strength);
                return sb.ToString();
            }
            const string AttackerDestroyed = ", attacker destroyed";
            static readonly Regex retreat = new Regex(@", retreats to \((-?\d+),(-?\d+)\)");
            static readonly Regex attack = new Regex(@"^   (.+?) \((-?\d+),(-?\d+)\) attacks (.+?) \((-?\d+),(-?\d+)\): deals (\d+), takes (\d+)(.*) -> (-?\d+) v (-?\d+)$");

            public class AttackLine {
                public string side, unit, targetSide, targetUnit;
                public int fromX, fromY, atX, atY, dealt, taken, strengthAfter, targetStrengthAfter;
                public bool killed, attackerDied;
                /// <summary>The defender was driven back to this tile (retreated: false for a log written before the tile was recorded).</summary>
                public bool retreated; public int retreatX, retreatY;
                /// <summary>Hex distance between where it fired from and where the target stood.</summary>
                public int Distance { get { int dz = (-fromX - fromY) - (-atX - atY); return Math.Max(Math.Abs(fromX - atX), Math.Max(Math.Abs(fromY - atY), Math.Abs(dz))); } }
            }

            public static bool ReadAttack(string line, IEnumerable<string> sides, out AttackLine a) {
                a = null;
                Match m = attack.Match(line);
                if (!m.Success) return false;
                AttackLine r = new AttackLine();
                if (!SplitWho(m.Groups[1].Value, sides, out r.side, out r.unit)) return false;
                if (!SplitWho(m.Groups[4].Value, sides, out r.targetSide, out r.targetUnit)) return false;
                r.fromX = int.Parse(m.Groups[2].Value, Inv); r.fromY = int.Parse(m.Groups[3].Value, Inv);
                r.atX = int.Parse(m.Groups[5].Value, Inv); r.atY = int.Parse(m.Groups[6].Value, Inv);
                r.dealt = int.Parse(m.Groups[7].Value, Inv); r.taken = int.Parse(m.Groups[8].Value, Inv);
                string rest = m.Groups[9].Value;
                r.attackerDied = rest.Contains(AttackerDestroyed);
                r.killed = rest.Replace(AttackerDestroyed, "").Contains(", " + r.targetUnit + " destroyed");
                Match back = retreat.Match(rest);
                if (back.Success) { r.retreated = true; r.retreatX = int.Parse(back.Groups[1].Value, Inv); r.retreatY = int.Parse(back.Groups[2].Value, Inv); }
                r.strengthAfter = int.Parse(m.Groups[10].Value, Inv); r.targetStrengthAfter = int.Parse(m.Groups[11].Value, Inv);
                a = r;
                return true;
            }

            /// <summary>
            /// A strike ordered (Strikes.Order), an action a replay plays again: "Blue Missile (1,2) launches at (5,5)"
            /// (delayed: it lands at the start of Blue's next turn) or "Blue Rocket Artillery (1,2) strikes (5,5)" (at once).
            /// </summary>
            public static string StrikeFired(Unit u, HexTile from, HexTile target, bool delayed) {
                return Indent + Who(u) + " " + D(from) + (delayed ? " launches at " : " strikes ") + D(target);
            }
            static readonly Regex strikeFired = new Regex(@"^   (.+) \((-?\d+),(-?\d+)\) (launches at|strikes) \((-?\d+),(-?\d+)\)$");

            public class StrikeLine { public string side, unit; public int fromX, fromY, x, y; public bool delayed; }

            public static bool ReadStrikeFired(string line, IEnumerable<string> sides, out StrikeLine s) {
                s = null;
                Match m = strikeFired.Match(line);
                if (!m.Success) return false;
                StrikeLine r = new StrikeLine();
                if (!SplitWho(m.Groups[1].Value, sides, out r.side, out r.unit)) return false;
                r.fromX = int.Parse(m.Groups[2].Value, Inv); r.fromY = int.Parse(m.Groups[3].Value, Inv);
                r.delayed = m.Groups[4].Value == "launches at";
                r.x = int.Parse(m.Groups[5].Value, Inv); r.y = int.Parse(m.Groups[6].Value, Inv);
                s = r;
                return true;
            }

            /// <summary>
            /// A strike landed, with what it did (not an action: the game works it out again as the turn starts or the
            /// strike is ordered): "Blue Missile strike lands on (5,5): Red Tank (5,5) -5 destroyed, Red Grunt (5,6) -4 -> 2;
            /// ground Grassland destroyed; City damaged 5", or "... : hits nothing". The splash of an ordinary shot:
            /// "Blue Bomber splash around (5,5): ...". Units destroyed have their "dies: combat" lines just before it.
            /// </summary>
            public static string StrikeLanded(StrikeResult r) {
                Strike s = r.strike;
                StringBuilder sb = new StringBuilder();
                sb.Append(Indent).Append(Name(s.owner)).Append(' ').Append(s.kind).Append(s.splash ? " splash around " : " strike lands on ").Append(D(s.target)).Append(": ");
                if (r.hits.Count == 0) sb.Append("hits nothing");
                for (int i = 0; i < r.hits.Count; i++) {
                    StrikeHit h = r.hits[i];
                    sb.Append(i > 0 ? ", " : "").Append(Name(h.unit.owner)).Append(' ').Append(UnitName(h.unit)).Append(' ').Append(D(h.tile)).Append(" -").Append(h.damage.ToString(Inv))
                      .Append(h.dies ? " destroyed" : " -> " + (h.before - h.damage).ToString(Inv));
                }
                if (r.groundWas != null) sb.Append("; ground ").Append(r.groundWas.name).Append(" destroyed");
                if (r.building != null) sb.Append("; ").Append(r.building.name.Replace("(Clone)", "").Trim()).Append(" damaged ").Append(r.buildingDamage.ToString(Inv));
                return sb.ToString();
            }
            static readonly Regex strikeLanded = new Regex(@"^   (.+) (strike lands on|splash around) \((-?\d+),(-?\d+)\): (.*)$");
            static readonly Regex strikeHit = new Regex(@"^(.+) \((-?\d+),(-?\d+)\) -(\d+)(?: destroyed| -> (\d+))$");

            public class StrikeHitLine { public string side, unit; public int x, y, damage, after; public bool destroyed; }
            public class StrikeLandedLine { public string side, unit; public int x, y; public bool splash; public readonly List<StrikeHitLine> hits = new List<StrikeHitLine>(); }

            public static bool ReadStrikeLanded(string line, IEnumerable<string> sides, out StrikeLandedLine s) {
                s = null;
                Match m = strikeLanded.Match(line);
                if (!m.Success) return false;
                StrikeLandedLine r = new StrikeLandedLine();
                if (!SplitWho(m.Groups[1].Value, sides, out r.side, out r.unit)) return false;
                r.splash = m.Groups[2].Value == "splash around";
                r.x = int.Parse(m.Groups[3].Value, Inv); r.y = int.Parse(m.Groups[4].Value, Inv);
                string rest = m.Groups[5].Value;
                int semi = rest.IndexOf(';');
                if (semi >= 0) rest = rest.Substring(0, semi);
                foreach (string part in rest.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries)) {
                    Match h = strikeHit.Match(part);
                    if (!h.Success) continue;
                    StrikeHitLine hit = new StrikeHitLine();
                    if (!SplitWho(h.Groups[1].Value, sides, out hit.side, out hit.unit)) continue;
                    hit.x = int.Parse(h.Groups[2].Value, Inv); hit.y = int.Parse(h.Groups[3].Value, Inv);
                    hit.damage = int.Parse(h.Groups[4].Value, Inv);
                    hit.destroyed = !h.Groups[5].Success;
                    hit.after = h.Groups[5].Success ? int.Parse(h.Groups[5].Value, Inv) : 0;
                    r.hits.Add(hit);
                }
                s = r;
                return true;
            }

            /// <summary>A building changed owner: "City (3,4) changes hands: Red -> Blue".</summary>
            public static string ChangesHands(Building b, Player previous) {
                return Indent + b.name.Replace("(Clone)", "").Trim() + " " + D(b.tile) + " changes hands: " + Name(previous) + " -> " + Name(b.owner);
            }
            static readonly Regex hands = new Regex(@"^   (.+) " + Tile + @" changes hands: (.+) -> (.+)$");
            public static bool ReadChangesHands(string line, out string building, out string from, out string to) {
                Match m = hands.Match(line);
                building = from = to = null;
                if (!m.Success) return false;
                building = m.Groups[1].Value; from = m.Groups[2].Value; to = m.Groups[3].Value;
                return true;
            }

            // ---------------------------------------------------------- saves

            /// <summary>The game was saved here (a save goes on from this line: the log is cut here when it is loaded).</summary>
            public static string Saved(string path) { return "-- saved as " + path; }
            /// <summary>A saved game was loaded: the log up to its save line, then the game from there.</summary>
            public static string Resumed(string path) { return "-- resumed from " + path; }

            // ---------------------------------------------------------- helpers

            static string Counts(IEnumerable<KeyValuePair<string, int>> counts) {
                List<string> parts = new List<string>();
                foreach (KeyValuePair<string, int> kv in counts) parts.Add(kv.Value.ToString(Inv) + " " + kv.Key);
                return string.Join(", ", parts.ToArray());
            }

            static bool ReadCounts(string line, string marker, IEnumerable<string> sides, out string sideName, out List<KeyValuePair<string, int>> counts) {
                counts = null;
                string rest;
                if (!After(line, marker, sides, out sideName, out rest)) return false;
                counts = new List<KeyValuePair<string, int>>();
                foreach (string part in rest.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries)) {
                    int sp = part.IndexOf(' ');
                    int n;
                    if (sp > 0 && int.TryParse(part.Substring(0, sp), NumberStyles.Integer, Inv, out n)) counts.Add(new KeyValuePair<string, int>(part.Substring(sp + 1), n));
                }
                return true;
            }

            /// <summary>"&lt;indent&gt;&lt;side&gt;&lt;marker&gt;&lt;rest&gt;" for a known side.</summary>
            static bool After(string line, string marker, IEnumerable<string> sides, out string sideName, out string rest) {
                sideName = null; rest = null;
                if (!line.StartsWith(Indent, StringComparison.Ordinal)) return false;
                foreach (string s in sides)
                    if (line.StartsWith(Indent + s + marker, StringComparison.Ordinal) && (sideName == null || s.Length > sideName.Length)) sideName = s;
                if (sideName == null) return false;
                rest = line.Substring(Indent.Length + sideName.Length + marker.Length);
                return true;
            }
        }
    }
}
