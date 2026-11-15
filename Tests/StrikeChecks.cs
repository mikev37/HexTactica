using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using HexTactica.Testing;
using HexTactica.AI;

/// <summary>
/// Strikes (Strikes.cs): the Missile (all four attributes) and each attribute on its own; the aiming preview; the
/// crater and the lit ground; the silo building one at a time; saving with a strike in the air; the game log and a
/// replay of a game with one; the scenario script's Strike Tile, Strike Launched and Strike Landed; and the AI firing
/// one, holding one, and spreading out under one.
/// </summary>
public static partial class Scenarios {

    static int ExportStrikeChecks(Action<string, string, Action<Player, Player>, string> make) {
        int n = 0;
        make("strike-missile", "strikeMissile", (a, o) => {
                 Rig.PlaceBuilding("Missile Silo", a, -4, 0); Put("Missile", a, -4, 0);
                 Put("Tank", o, 3, 0); Put("Grunt", o, 4, 0); Put("Grunt", o, 3, 1); Put("Grunt", o, 5, -1);
                 Put("Artillery", a, 2, 0);
             },
             "The Missile (targets ground, blast radius 1, ballistic, self-destructs): the silo builds one only while its tile is free; aimed at a tile, the preview lists the units the player knows of on the seven tiles with what each would lose (friend and foe: a Blue artillery beside the target); launched, it leaves the board at once (not a loss: no 'dies' line, no Died for scripts), every enemy side is shown the launch, nothing is hit yet and nothing can attack it; at the start of Blue's next turn it lands: each unit on the seven tiles loses what the preview said (its ordinary damage), one two tiles away nothing, the tile becomes a Crater, and Blue sees two tiles around it until its turn ends.");
        n++;
        make("strike-attributes", "strikeAttributes", (a, o) => {
                 Put("Artillery", a, -1, -4); Put("Grunt", o, 2, -4); Put("Grunt", o, 3, -4);
                 Put("Artillery", a, -3, -1); Put("Tank", o, 0, -1); Put("Grunt", o, 1, -1); Put("Grunt", a, -1, 0);
                 Put("Tank", a, -1, 2); Put("Grunt", o, 0, 2);
                 Put("Artillery", a, -3, 5); Put("Grunt", o, 0, 5);
             },
             "Each strike attribute on its own, set on ordinary units: target ground alone (artillery strikes an empty tile, then a tile with a unit: only that unit, at once, ordinary damage); area alone (artillery attacks a tank: the tank takes its ordinary damage and every unit beside it, a Blue grunt too, the splash; no crater); self-destruct alone (a tank attacks and is expended, not killed); ballistic alone (artillery fires at a grunt: nothing now, the artillery stays; the grunt moves away on Red's turn and the strike lands on the empty tile at Blue's next turn).");
        n++;
        make("strike-save", "strikeSave", (a, o) => {
                 Rig.PlaceBuilding("Missile Silo", a, -4, 0); Put("Missile", a, -4, 0);
                 Put("Tank", o, 3, 0); Put("Grunt", o, 4, 0);
             },
             "A game saved with a missile in the air (the test harness's save, the game's own BoardJson and the game log's snapshot all write 'strikes'), loaded again: the strike is back in the air with its target, strength and side, and lands at Blue's next turn as it would have.");
        n++;
        make("strike-log-replay", "strikeLogReplay", (a, o) => {
                 Rig.PlaceBuilding("Missile Silo", a, -4, 0); Put("Missile", a, -4, 0);
                 Put("Tank", o, 3, 0); Put("Tank", o, 4, 0); Put("Tank", o, 3, 1);
             },
             "A game with a missile, recorded in the game log and replayed: the AI launches at the Red tanks (log: 'launches at'), it lands at Blue's next turn ('strike lands on' with each hit; its kills are 'dies: combat' and the post-game record credits them to the Missile; the missile itself is not a loss). The replay plays the launch again from the log and ends with the board as recorded: units, strengths and the crater.");
        n++;
        make("ai-strike-fire", "aiStrikeFire", (a, o) => {
                 Rig.PlaceBuilding("Missile Silo", a, -4, 0); Put("Missile", a, -4, 0);
                 Put("Tank", o, 3, 0); Put("Tank", o, 4, 0); Put("Tank", o, 3, 1); Put("Grunt", o, 4, -3);
             },
             "The AI with a missile and three Red tanks bunched in range (and a lone grunt): it launches at the tile whose blast covers the most of them, not at the grunt. With only the grunt in range it holds the missile (not worth what it spends).");
        n++;
        Action<Player, Player, bool> spreadBoard = (a, o, incoming) => {
            Put("Grunt", o, 2, 0); Put("Grunt", o, 3, 0); Put("Grunt", o, 2, 1); Put("Grunt", o, 3, -1); Put("Grunt", o, 4, -1); Put("Tank", o, 4, 0);
            Put("Grunt", a, -4, 0);
            if (incoming) {
                Unit prefab = PrefabLoader.Load("Missile");
                Strikes.Add(new Strike { owner = a, kind = "Missile", weapon = prefab, strength = 6, from = Rig.At(-4, 0), target = Rig.At(3, 0), day = 1 });
            }
        };
        make("ai-strike-spread", "aiStrikeSpread", (a, o) => spreadBoard(a, o, true),
             "Red's units bunched within reach of a Blue missile in the air (the file carries it; where it is aimed is not read by Red's AI): on its turn Red's AI spreads them out or takes them out of reach, so that no blast catches as many as on the same board with nothing in the air (ai-strike-spread-control).");
        n++;
        make("ai-strike-spread-control", "aiStrikeControl", (a, o) => spreadBoard(a, o, false),
             "The board of ai-strike-spread with nothing in the air (played from that check).");
        n++;
        make("script-strike", "scriptStrike", (a, o) => {
                 Rig.PlaceBuilding("Missile Silo", a, -4, 0); Put("Missile", a, -4, 0);
                 Put("Tank", o, 3, 0);
                 Script(Trig("Launch", TriggerCondition.TurnStarted, t => { t.Side = Blue; t.Count = 1; },
                             Act(TriggerActionType.StrikeTile, x => { x.UnitSide = Blue; x.UnitKind = "Missile"; x.TileX = 3; x.TileY = 0; })),
                        Trig("Launched", TriggerCondition.StrikeLaunched, t => { t.Side = Blue; t.UnitKind = "Missile"; t.InArea = true; t.TileX = 3; t.TileY = 0; t.Radius = 1; }, Money(Blue, 1)),
                        Trig("Landed", TriggerCondition.StrikeLanded, t => { t.Side = Blue; }, Money(Blue, 10)),
                        Trig("Lost one", TriggerCondition.UnitKilled, t => { t.Side = Blue; }, Money(Blue, 1000)));
             },
             "Scripted strikes: a trigger at Blue's first turn makes the Missile strike (3,0) (Strike Tile, as a player orders it: it has used its turn); Strike Launched (Blue's Missile, aimed within 1 of (3,0)) fires then; Strike Landed fires when it lands at Blue's next turn; Unit Killed for Blue never does (the missile was expended, not killed).");
        n++;
        return n;
    }

    // ------------------------------------------------------------------ checks

    static Unit At(int x, int y) { return Rig.At(x, y).occupier; }

    /// <summary>Units, strengths and terrain that is not grass, for comparing boards.</summary>
    static string StrikeBoard() {
        List<string> parts = new List<string>();
        foreach (Unit u in Rig.uc.units) if (u != null && u.tile != null) parts.Add(u.name.Replace("(Clone)", "").Trim() + "@" + u.tile.x + "," + u.tile.y + ":" + u.Strength);
        foreach (HexTile t in Rig.hg.tiles) if (t != null && t.terrain != null && t.terrain.name == "Crater") parts.Add("crater@" + t.x + "," + t.y);
        parts.Sort(StringComparer.Ordinal);
        return string.Join(" ", parts.ToArray());
    }

    /// <summary>The most of <paramref name="p"/>'s units one blast of radius 1 could catch.</summary>
    static int MostUnderOneBlast(Player p) {
        int best = 0;
        foreach (HexTile c in Rig.hg.tiles) {
            if (c == null) continue;
            int k = 0;
            foreach (HexTile t in Strikes.Blast(c, 1)) if (t.occupier != null && t.occupier.owner == p && t.occupier.Strength > 0) k++;
            best = Math.Max(best, k);
        }
        return best;
    }

    static bool StrikeCheck(ScenarioFile.Loaded l, out string detail) {
        GameController gc = Rig.gc;
        Player a = l.ai, o = l.other;
        foreach (Building b in Building.all) if (b != null) b.income = 0;
        switch (l.meta.Get("check", "")) {
            case "strikeMissile": {
                gc.fogOfWar = true; FogOfWar.Reset(); FogOfWar.Changed(); FogOfWar.Refresh();
                Unit missile = At(-4, 0);
                Building silo = Rig.At(-4, 0).building;
                HexTile aim = Rig.At(3, 0);
                Unit farGrunt = At(5, -1), blueGrunt = At(2, 0);
                bool shape = missile != null && missile.targetsGround && missile.ballistic && missile.selfDestructs && missile.blastRadius == 1 && missile.cost == 30000
                             && missile.moveRange == 0 && missile.craterTerrain != null && missile.craterTerrain.name == "Crater" && silo != null && silo.buildable.Exists(u => u.name.StartsWith("Missile"));
                a.money = 100000;
                Unit offered = silo != null ? silo.buildable.Find(u => u.name.StartsWith("Missile")) : null;
                bool oneAtATime = offered != null && Rig.bc.Produce(silo, offered) == null && a.money == 100000;
                // What the player is shown (as they know the board) and the order.
                List<StrikeHit> preview = Strikes.Preview(missile, missile.tile, aim, a);
                Dictionary<Unit, int> before = new Dictionary<Unit, int>();
                foreach (Unit u in Rig.uc.units) if (u != null) before[u] = u.Strength;
                int deaths = 0, expendedSeen = 0; Action<Unit> died = u => { deaths++; if (Unit.Expending) expendedSeen++; };
                Unit.Died += died;
                // The ground two tiles round the target Blue does not see before it lands.
                List<HexTile> dark = Strikes.Blast(aim, 2).FindAll(t => !FogOfWar.TileVisible(a, t));
                bool canOrder = Strikes.CanOrder(missile, missile.tile, aim) && !Strikes.CanOrder(missile, missile.tile, Rig.At(-3, 0));
                bool ordered = Strikes.Order(missile, aim);
                Unit.Died -= died;
                bool gone = missile.tile == null && !a.units.Contains(missile) && !Rig.uc.units.Contains(missile) && Strikes.InFlight.Count == 1 && expendedSeen == 1;
                bool nothingYet = Rig.uc.units.All(u => u == null || !before.ContainsKey(u) || u.Strength == before[u]);
                bool freeAgain = offered != null && Rig.bc.Produce(silo, offered) != null && a.money == 70000;
                gc.EndTurn();
                bool stillUp = Strikes.InFlight.Count == 1 && Rig.uc.units.All(u => u == null || !before.ContainsKey(u) || u.Strength == before[u]);
                // Blue's next turn: it lands.
                Unit.Died += died;
                gc.EndTurn();
                Unit.Died -= died;
                List<string> hits = new List<string>(); bool asPreviewed = preview.Count > 0;
                foreach (StrikeHit h in preview) {
                    int now = h.unit.Strength;
                    hits.Add(h.unit.name.Replace("(Clone)", "") + Pos(h.tile) + " " + h.before + "->" + now + " (preview -" + h.damage + ")");
                    if (now != h.before - h.damage) asPreviewed = false;
                }
                bool friendlyHit = preview.Exists(h => h.unit == blueGrunt);
                bool farSafe = farGrunt.Strength == before[farGrunt];
                bool crater = aim.terrain != null && aim.terrain.name == "Crater";
                bool litNow = dark.Count > 0 && dark.TrueForAll(t => FogOfWar.TileVisible(a, t));
                bool landed = Strikes.InFlight.Count == 0;
                gc.EndTurn();
                bool litEnds = dark.Exists(t => !FogOfWar.TileVisible(a, t));
                detail = "missile as made " + shape + "; silo full: no second " + oneAtATime + ", built again once launched " + freeAgain + "; may aim at (3,0), not at (-3,0) (too close) " + canOrder
                       + "; preview " + preview.Count + " units (the Blue unit among them " + friendlyHit + "); launched " + ordered + ", gone from the board (Died told as expended) " + gone + ", nothing hit yet " + nothingYet + ", still in the air on Red's turn " + stillUp
                       + "; landed " + landed + ": " + string.Join(", ", hits.ToArray()) + "; as previewed " + asPreviewed + "; two tiles away untouched " + farSafe + "; crater " + crater
                       + "; " + dark.Count + " tiles within 2 Blue did not see: all seen after landing " + litNow + ", dark again after Blue's turn " + litEnds;
                return shape && oneAtATime && freeAgain && canOrder && ordered && gone && nothingYet && stillUp && landed && asPreviewed && friendlyHit && farSafe && crater && litNow && litEnds;
            }
            case "strikeAttributes": {
                Unit groundArty = At(-1, -4), areaArty = At(-3, -1), selfTank = At(-1, 2), lateArty = At(-3, 5);
                Unit g1 = At(2, -4), g1b = At(3, -4), tank2 = At(0, -1), g2 = At(1, -1), blue2 = At(-1, 0), g3 = At(0, 2), g4 = At(0, 5);
                groundArty.targetsGround = true;
                areaArty.blastRadius = 1;
                selfTank.selfDestructs = true;
                lateArty.ballistic = true;
                // Target ground alone.
                int ammo = groundArty.ammo;
                bool empty = Strikes.Order(groundArty, Rig.At(2, -5)) && groundArty.attackedThisTurn && groundArty.ammo == ammo - 1 && Strikes.InFlight.Count == 0;
                groundArty.attackedThisTurn = false; groundArty.active = true;
                int want1 = Mathf.Min(g1.Strength, Unit.calculateDamage(groundArty, g1, g1.tile, 0)), b1 = g1.Strength, b1b = g1b.Strength;
                bool ground = Strikes.Order(groundArty, g1.tile) && g1.Strength == b1 - want1 && g1b.Strength == b1b && want1 > 0;
                // Area alone: an ordinary attack, and the splash.
                int want2 = Mathf.Min(tank2.Strength, Unit.calculateDamage(areaArty, tank2, tank2.tile, 0));
                int bt = tank2.Strength, bg2 = g2.Strength, bb2 = blue2.Strength, ba = areaArty.Strength;
                CombatOutcome oc = areaArty.OrderAttack(tank2);
                bool area = tank2.Strength == bt - oc.damageToDefender && oc.damageToDefender == want2 && g2.Strength < bg2 && blue2.Strength < bb2 && areaArty.Strength == ba - oc.damageToAttacker
                            && tank2.tile.terrain.name != "Crater";
                // Self-destruct alone.
                int deaths = 0; Action<Unit> died = u => { if (!Unit.Expending) deaths++; };
                Unit.Died += died;
                int bg3 = g3.Strength;
                selfTank.OrderAttack(g3);
                Unit.Died -= died;
                bool self = g3.Strength < bg3 && selfTank.tile == null && !a.units.Contains(selfTank) && deaths == (g3.Strength <= 0 ? 1 : 0);
                // Ballistic alone: nothing now; lands on the tile next Blue turn, on whatever is there then.
                int bg4 = g4.Strength;
                lateArty.OrderAttack(g4);
                bool later = g4.Strength == bg4 && lateArty.tile != null && lateArty.attackedThisTurn && Strikes.InFlight.Count == 1 && Strikes.InFlight[0].target == Rig.At(0, 5);
                gc.EndTurn();
                MoveTo(g4, 2, 5);
                gc.EndTurn();
                bool missed = g4.Strength == bg4 && Strikes.InFlight.Count == 0;
                detail = "ground alone: empty tile struck " + empty + ", the unit on a tile only, at once " + ground + " (-" + want1 + "); area alone: tank -" + oc.damageToDefender + " (ordinary " + want2 + "), Red grunt beside " + bg2 + "->" + g2.Strength + ", Blue grunt beside " + bb2 + "->" + blue2.Strength + ", no crater " + area
                       + "; self-destruct alone: grunt " + bg3 + "->" + g3.Strength + ", tank expended, not counted killed " + self + "; ballistic alone: nothing at once, artillery stays " + later + ", lands on the empty tile the grunt left " + missed;
                return empty && ground && area && self && later && missed;
            }
            case "strikeSave": {
                Unit missile = At(-4, 0);
                HexTile aim = Rig.At(3, 0);
                int expectT = Strikes.Preview(missile, missile.tile, aim, a).Where(h => h.unit.name.StartsWith("Tank")).Select(h => h.damage).FirstOrDefault();
                Strikes.Order(missile, aim);
                string board = "";
                try { board = LevelEditor.BoardJson(); } catch (Exception e) { board = "(BoardJson: " + e.GetType().Name + ")"; }
                bool inBoard = board.Contains("\"" + Strikes.FileKey + "\"") || board.StartsWith("(BoardJson");
                string snapshot = HexTactica.Harness.GameLog.Snapshot(a, "check");
                bool inSnapshot = snapshot.Contains("\"" + Strikes.FileKey + "\"") && snapshot.Contains("\"kind\":\"Missile\"");
                string path = Path.Combine(Path.GetTempPath(), "hextactica-strike-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".dat");
                ScenarioFile.Save(path, Test("strike-save-copy", "rules", "Human", 4, "strikeSave", "copy"), a, o, a);
                ScenarioFile.Loaded back = ScenarioFile.Load(path, "Human", null);
                try { File.Delete(path); } catch (Exception) { }
                Strike s = Strikes.InFlight.Count == 1 ? Strikes.InFlight[0] : null;
                bool restored = s != null && s.owner == back.ai && s.target == Rig.At(3, 0) && s.strength == 6 && s.weapon != null && s.weapon.ballistic && Rig.At(-4, 0).occupier == null;
                Unit tank = At(3, 0); int bt = tank != null ? tank.Strength : -1;
                Rig.gc.EndTurn(); Rig.gc.EndTurn();
                bool landed = tank != null && tank.Strength == Math.Max(0, bt - expectT) && Strikes.InFlight.Count == 0 && Rig.At(3, 0).terrain.name == "Crater";
                detail = "BoardJson has strikes " + inBoard + "; log snapshot has it " + inSnapshot + "; loaded back in the air (side, target, strength, weapon) " + restored + "; landed as it would have (tank " + bt + " -" + expectT + " -> " + (tank != null ? tank.Strength : -1) + ", crater) " + landed;
                return inBoard && inSnapshot && restored && landed;
            }
            case "strikeLogReplay": {
                string root = Path.Combine(Path.GetTempPath(), "hextactica-strike-replay-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                AIPlayer blueAI = a.GetComponent<AIPlayer>();
                HexTactica.Harness.GameLog.Start(l.path, "Human", root);
                blueAI.PlayWholeTurn();
                bool launched = Strikes.InFlight.Count == 1;
                gc.EndTurn();                 // Red does nothing
                blueAI.PlayWholeTurn();       // it lands as Blue's turn starts
                string recorded = StrikeBoard();
                string folder = HexTactica.Harness.GameLog.Folder;
                HexTactica.Harness.GameLog.EndIfOver();
                HexTactica.Harness.GameLog.Stop();
                string log = File.ReadAllText(Path.Combine(folder, "game.log"));
                bool logged = log.Contains(" launches at (") && log.Contains("strike lands on (") && !log.Contains("Missile (-4,0) dies");
                GameRecord record = GameRecord.Read(folder);
                int credited = record.losses.Count(x => x.killerKind.StartsWith("Missile"));
                int killed = record.losses.Count(x => x.cause == HexTactica.Harness.GameLog.Lines.Combat);
                bool missileNotLost = !record.losses.Exists(x => x.kind.StartsWith("Missile"));
                // Replay.
                HexTactica.Harness.Replay.Loader = f => { ScenarioFile.Load(f, "Human", null); return true; };
                bool opened = HexTactica.Harness.Replay.Open(folder);
                int guard = 0;
                HexTactica.Harness.Replay.Show(0);
                while (HexTactica.Harness.Replay.NextAction() && guard++ < 200) { }
                string replayed = StrikeBoard();
                HexTactica.Harness.Replay.Close();
                HexTactica.Harness.Replay.Loader = HexTactica.Harness.ScenarioLoader.LoadBoardFromPath;
                try { Directory.Delete(root, true); } catch (Exception) { }
                bool same = replayed == recorded && recorded.Contains("crater@");
                string excerpt = string.Join(" / ", log.Split('\n').Where(x => x.Contains("launches at") || x.Contains("strike lands on")).Select(x => x.Trim()).ToArray());
                detail = "launched by the AI " + launched + "; log: " + excerpt + "; kills credited to the Missile " + credited + "/" + killed + ", missile not a loss " + missileNotLost
                       + "; replay opened " + opened + ", ends as recorded " + same + (same ? "" : " [recorded " + recorded + " | replayed " + replayed + "]");
                return launched && logged && credited == killed && killed > 0 && missileNotLost && opened && same;
            }
            case "aiStrikeFire": {
                Unit missile = At(-4, 0);
                AIMap map = new AIMap(a, a.GetComponent<AIPlayer>().Profile);
                AIAction? first = AIStrike.Decide(missile, map);
                HexTile aim = first.HasValue ? first.Value.destination : null;
                int covered = aim == null ? 0 : Strikes.Blast(aim, 1).Count(t => t.occupier != null && t.occupier.name.StartsWith("Tank"));
                // Only the grunt in range: it holds.
                foreach (Unit t in new List<Unit> { At(3, 0), At(4, 0), At(3, 1) }) t.Perish();
                map = new AIMap(a, a.GetComponent<AIPlayer>().Profile);
                AIAction? second = AIStrike.Decide(missile, map);
                // A whole turn of the AI's: the missile stays in its silo.
                a.GetComponent<AIPlayer>().PlayWholeTurn();
                bool kept = missile.tile == Rig.At(-4, 0) && Strikes.InFlight.Count == 0;
                detail = "three tanks bunched: " + (first.HasValue ? first.Value.reason : "holds") + " (" + covered + " tanks under the blast); only a grunt: " + (second.HasValue ? second.Value.reason : "holds") + "; after a whole AI turn the missile is still in its silo " + kept;
                return covered == 3 && !second.HasValue && kept;
            }
            case "aiStrikeSpread": {
                Func<string, int[]> play = name => {
                    ScenarioFile.Loaded g = ScenarioFile.Load(Path.Combine(Dir, name + ".dat"), "Full", null);
                    foreach (MonoBehaviour m in g.ai.GetComponents<MonoBehaviour>()) if (m is IAIController) m.enabled = false;
                    int air = Strikes.InFlight.Count;
                    int before = MostUnderOneBlast(g.other);
                    Rig.gc.EndTurn();   // Blue's turn passes: Red's AI plays
                    g.other.GetComponent<AIPlayer>().PlayWholeTurn();
                    return new[] { air, before, MostUnderOneBlast(g.other) };
                };
                int[] withStrike = play("ai-strike-spread"), without = play("ai-strike-spread-control");
                detail = "Red's units under one blast at most: with a Blue missile in the air (" + withStrike[0] + " in the file) " + withStrike[1] + " -> " + withStrike[2] + "; with nothing in the air " + without[1] + " -> " + without[2];
                return withStrike[0] == 1 && without[0] == 0 && withStrike[2] < without[2] && withStrike[2] < withStrike[1];
            }
            case "aiStrikeControl": {
                detail = "the control board of ai-strike-spread (played there)";
                return true;
            }
            case "scriptStrike": {
                ScenarioScript s = ScenarioScript.Current;
                foreach (MonoBehaviour m in a.GetComponents<MonoBehaviour>()) if (m is IAIController) m.enabled = false;
                a.money = 0;
                Unit missile = At(-4, 0), tank = At(3, 0);
                int bt = tank.Strength;
                for (int i = 0; i < 4; i++) s.Tick(0.1f);   // Blue's first turn: the trigger
                bool launched = Strikes.InFlight.Count == 1 && missile.tile == null && Fired(s, "Launch") == 1 && Fired(s, "Launched") == 1 && Fired(s, "Landed") == 0;
                gc.EndTurn(); s.Tick(0.1f); gc.EndTurn();
                for (int i = 0; i < 4; i++) s.Tick(0.1f);
                bool landed = Strikes.InFlight.Count == 0 && tank.Strength < bt && Fired(s, "Landed") == 1 && Fired(s, "Lost one") == 0;
                detail = "Strike Tile at Blue's first turn: launched " + launched + "; Strike Launched " + Fired(s, "Launched") + "x; landed (tank " + bt + "->" + tank.Strength + ") " + landed + "; Strike Landed " + Fired(s, "Landed") + "x; Unit Killed (Blue) " + Fired(s, "Lost one") + "x; Blue gold " + a.money;
                return launched && landed && a.money == 11;
            }
        }
        detail = "no such strike check";
        return false;
    }
}
