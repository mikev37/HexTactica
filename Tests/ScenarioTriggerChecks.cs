using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using HexTactica.Testing;

/// <summary>
/// Scenario triggers (ScenarioScript): rule scenarios whose files carry a script ("script"), as a campaign map would,
/// so each one can be opened in Unity and watched. Checks: timing (once, Delay held without a break, Repeat), facts as
/// they happen (killed, damaged, healed, captured, destroyed, built, entered, turns, day) and states (money, supplies,
/// ammunition, fuel, units left, buildings held), combining (All Of / Any Of / None Of, After Trigger, parts switched
/// off, a loop of parts), every board action, the dialogue (queued, the game held, actions waiting on it) and saving
/// a game with its triggers part way.
/// </summary>
public static partial class Scenarios {

    const long Blue = ScenarioFile.AI_ID, Red = ScenarioFile.OTHER_ID;

    static ScenarioTrigger Trig(string name, TriggerCondition c, Action<ScenarioTrigger> set, params TriggerAction[] actions) {
        ScenarioTrigger t = new ScenarioTrigger { Name = name, Condition = c };
        if (set != null) set(t);
        t.Actions.AddRange(actions);
        if (t.Actions.Count == 0) t.Actions.Add(new TriggerAction { Type = TriggerActionType.Wait, Seconds = 0f });
        return t;
    }

    static TriggerAction Act(TriggerActionType type, Action<TriggerAction> set = null) {
        TriggerAction a = new TriggerAction { Type = type };
        if (set != null) set(a);
        return a;
    }

    static TriggerAction Money(long side, int add) { return Act(TriggerActionType.GiveMoney, a => { a.Side = side; a.Mode = ValueMode.Add; a.Value = add; }); }

    /// <summary>The script the next scenario file is saved with (ScenarioFile.ScriptToSave).</summary>
    static void Script(params ScenarioTrigger[] triggers) {
        ScenarioScript s = ScenarioScript.Current;
        s.triggers = new List<ScenarioTrigger>(triggers);
        ScenarioFile.ScriptToSave = s.ToJson(false);
        s.triggers = new List<ScenarioTrigger>();
    }

    static int ExportScriptChecks(Action<string, string, Action<Player, Player>, string> make) {
        int n = 0;
        make("script-timing", "scriptTiming", (a, o) => {
                 Put("Grunt", a, -2, 0);
                 Script(Trig("Start", TriggerCondition.LevelStart, null, Money(Blue, 100)),
                        Trig("Hold", TriggerCondition.UnitsInArea, t => { t.Side = Blue; t.TileX = 0; t.TileY = 0; t.Radius = 0; t.Delay = 2; t.TurnsOf = Blue; }, Money(Blue, 1)),
                        Trig("Every Blue turn", TriggerCondition.TurnOver, t => { t.Side = Blue; t.Repeat = true; }, Money(Red, 10)),
                        Trig("Second Blue turn", TriggerCondition.TurnOver, t => { t.Side = Blue; t.Count = 2; }, Money(Red, 1000)));
             },
             "Scenario trigger timing (ScenarioScript): Level Start fires once however long the game runs; a trigger with Delay 2 (turns of Blue) fires only once its condition (a Blue unit on (0,0)) has held through two of Blue's turns starting, without a break, and the count starts again when it breaks; Turn Over with Repeat fires at the end of every Blue turn; with Count 2 (and no Repeat), once, at the end of the second.");
        n++;
        make("script-events", "scriptEvents", (a, o) => {
                 Rig.PlaceBuilding("City", null, 0, 0); Put("Grunt", a, 0, 0);
                 Put("Tank", a, 1, 1); Put("Grunt", o, 2, 0);
                 Rig.PlaceBuilding("City", null, 3, 3);
                 Script(Trig("Killed", TriggerCondition.UnitKilled, t => { t.Side = Red; t.UnitKind = "Grunt"; }),
                        Trig("Hurt", TriggerCondition.UnitDamaged, t => { t.Side = Red; t.Value = 4; }),
                        Trig("Healed", TriggerCondition.UnitHealed, t => { t.Side = Blue; t.UnitKind = "Tank"; t.Value = 6; }),
                        Trig("Captured", TriggerCondition.BuildingCaptured, t => { t.Side = Blue; t.BuildingKind = "City"; }),
                        Trig("Entered", TriggerCondition.UnitEnters, t => { t.Side = Blue; t.UnitKind = "Tank"; t.TileX = 3; t.TileY = -3; t.Radius = 1; }),
                        Trig("Day 2", TriggerCondition.DayReached, t => { t.Value = 2; }),
                        Trig("Day 2 on Blue's turn", TriggerCondition.DayReached, t => { t.Value = 2; t.Side = Blue; }),
                        Trig("Red turn starts", TriggerCondition.TurnStarted, t => { t.Side = Red; }),
                        Trig("Built", TriggerCondition.UnitBuilt, t => { t.Side = Blue; }),
                        Trig("Destroyed", TriggerCondition.BuildingDestroyed, t => { t.BuildingKind = "City"; t.InArea = true; t.TileX = 3; t.TileY = 3; }),
                        Trig("Starving", TriggerCondition.UnitStarving, t => { t.Side = Blue; t.UnitKind = "Grunt"; }),
                        Trig("No ammo", TriggerCondition.UnitOutOfAmmo, t => { t.Side = Blue; }),
                        Trig("Low fuel", TriggerCondition.UnitLowOnFuel, t => { t.Side = Blue; t.Value = 25; }),
                        Trig("No fuel", TriggerCondition.UnitOutOfFuel, t => { t.Side = Blue; }),
                        Trig("Rich", TriggerCondition.MoneyAtLeast, t => { t.Side = Blue; t.Value = 5000; }),
                        Trig("Broke", TriggerCondition.MoneyAtMost, t => { t.Side = Red; t.Value = 0; }),
                        Trig("No Red grunts", TriggerCondition.UnitsAtMost, t => { t.Side = Red; t.UnitKind = "Grunt"; t.Value = 0; }),
                        Trig("City held", TriggerCondition.BuildingOwned, t => { t.Side = Blue; t.BuildingKind = "City"; }),
                        Trig("Human turn over", TriggerCondition.TurnOver, t => { t.Side = TriggerSide.Human; }),
                        Trig("Three Blue units", TriggerCondition.UnitsAtLeast, t => { t.Side = Blue; t.Value = 3; }));
             },
             "What happens, as it happens, and states: a Red grunt shot to 4 or less (Unit Damaged) and killed (Unit Killed, then Units At Most 0); a Blue tank brought back to 6 (Unit Healed) and driven within 1 of (3,-3) (Unit Enters); a neutral city taken by a Blue grunt over two turns (Building Captured, then Building Owned); another destroyed (Building Destroyed); a unit bought (Unit Built); the day, Red's turn starting, the human side's turn over; Blue out of supplies, ammunition and fuel, low on fuel; money at least and at most. None fires before what it waits for.");
        n++;
        make("script-composite", "scriptComposite", (a, o) => {
                 Put("Grunt", a, 0, 0); Put("Grunt", o, 4, 0);
                 Script(Trig("Blue in middle", TriggerCondition.UnitsInArea, t => { t.Enabled = false; t.Side = Blue; t.Radius = 1; }),
                        Trig("Red in middle", TriggerCondition.UnitsInArea, t => { t.Enabled = false; t.Side = Red; t.Radius = 1; }),
                        Trig("Both", TriggerCondition.AllOf, t => { t.Parts = new List<string> { "Blue in middle", "Red in middle" }; }),
                        Trig("Either", TriggerCondition.AnyOf, t => { t.Parts = new List<string> { "Blue in middle", "Red in middle" }; }),
                        Trig("Neither", TriggerCondition.NoneOf, t => { t.Parts = new List<string> { "Blue in middle", "Red in middle" }; }),
                        Trig("After either", TriggerCondition.AfterTrigger, t => { t.AfterTrigger = "Either"; }),
                        Trig("Loop A", TriggerCondition.AllOf, t => { t.Parts = new List<string> { "Loop B" }; }),
                        Trig("Loop B", TriggerCondition.AnyOf, t => { t.Parts = new List<string> { "Loop A" }; }),
                        Trig("Arm", TriggerCondition.AfterTrigger, t => { t.AfterTrigger = "Both"; }, Act(TriggerActionType.EnableTrigger, x => x.Trigger = "Late")),
                        Trig("Late", TriggerCondition.LevelStart, t => { t.Enabled = false; }));
             },
             "Combining conditions: two triggers switched off are named conditions (a Blue / a Red unit within 1 of (0,0)) and never fire themselves; All Of fires when both hold, Any Of when either does, None Of when neither does; After Trigger follows a trigger that fired; Enable Trigger switches on a trigger that was off; two triggers whose parts name each other are never met (and do not overflow).");
        n++;
        make("script-actions", "scriptActions", (a, o) => {
                 Rig.At(2, 2).terrain = T("Forest");
                 Put("Grunt", a, 0, 0); Put("Tank", o, 3, -1);
                 Rig.PlaceBuilding("City", null, -2, 3); Rig.PlaceBuilding("City", o, 4, 2);
                 Script(Trig("Do it", TriggerCondition.LevelStart, null,
                            Act(TriggerActionType.SpawnUnit, x => { x.Side = Blue; x.UnitKind = "Grunt"; x.TileX = 0; x.TileY = 2; x.Count = 2; x.Value = 4; x.Veterancy = 1; x.UnitTag = "Reinforcement"; x.ReadyToAct = false; }),
                            Act(TriggerActionType.GiveUnits, x => { x.UnitSide = Red; x.UnitKind = "Tank"; x.Side = Blue; }),
                            Act(TriggerActionType.GiveBuilding, x => { x.BuildingSide = TriggerSide.Neutral; x.BuildingKind = "City"; x.Side = Blue; }),
                            Act(TriggerActionType.DestroyTile, x => { x.TileX = 2; x.TileY = 2; x.Radius = 0; }),
                            Act(TriggerActionType.DestroyBuilding, x => { x.BuildingSide = Red; x.BuildingKind = "City"; }),
                            Act(TriggerActionType.SetVeterancy, x => { x.UnitSide = Blue; x.UnitTag = "Reinforcement"; x.Mode = ValueMode.Add; x.Value = 1; }),
                            Act(TriggerActionType.SetStat, x => { x.UnitSide = Blue; x.UnitTag = "Reinforcement"; x.Stat = UnitStat.Health; x.Mode = ValueMode.Add; x.Value = -1; }),
                            Act(TriggerActionType.SetStat, x => { x.UnitSide = Blue; x.UnitKind = "Tank"; x.Stat = UnitStat.Ammo; x.Mode = ValueMode.Set; x.Value = 0; }),
                            Act(TriggerActionType.ShowMarker, x => { x.Marker = "Marker"; x.MarkerId = "goal"; x.Text = "Take it"; x.TileX = 5; x.TileY = 0; })),
                        Trig("Then", TriggerCondition.AfterTrigger, t => { t.AfterTrigger = "Do it"; },
                            Act(TriggerActionType.Wait, x => x.Seconds = 1.5f),
                            Act(TriggerActionType.RepairTile, x => { x.TileX = 2; x.TileY = 2; }),
                            Act(TriggerActionType.RepairBuilding, x => { x.BuildingSide = TriggerSide.Any; x.BuildingKind = "City"; }),
                            Act(TriggerActionType.DestroyUnits, x => { x.UnitSide = Blue; x.UnitKind = "Grunt"; x.InArea = true; x.TileX = 0; x.TileY = 0; x.Radius = 0; }),
                            Act(TriggerActionType.HideMarker, x => { x.MarkerId = "goal"; })),
                        Trig("Victory", TriggerCondition.AfterTrigger, t => { t.AfterTrigger = "Then"; },
                            Act(TriggerActionType.Wait, x => x.Seconds = 2.5f),
                            Act(TriggerActionType.Win, x => x.Side = Blue)));
             },
             "Every board action, in order: two tagged Blue grunts spawned (strength 4, veterancy 1, spent this turn) beside a taken tile; Red's tank given to Blue; the neutral city given to Blue; a forest destroyed; Red's city destroyed; the spawned grunts' veterancy raised by 1 and strength lowered by 1; the tank's ammunition set to 0; a marker shown. After a 1.5 s Wait (pacing, as in a cutscene) the forest and the city are repaired, the grunt on (0,0) destroyed and the marker hidden; at 2.5 s Blue wins.");
        n++;
        make("script-dialogue", "scriptDialogue", (a, o) => {
                 Put("Grunt", a, 0, 0);
                 Script(Trig("Talk", TriggerCondition.LevelStart, null,
                            Act(TriggerActionType.Dialogue, x => x.Lines = new List<DialogueLine> {
                                new DialogueLine { Character = "Placeholder Officer", Text = "One." },
                                new DialogueLine { Character = "Placeholder Scout", Text = "Two.", Portrait = PortraitSide.Right } }),
                            Money(Red, 5)),
                        Trig("Also", TriggerCondition.LevelStart, null,
                            Act(TriggerActionType.Dialogue, x => x.Lines = new List<DialogueLine> { new DialogueLine { Character = "Placeholder Commander", Text = "Three." } }),
                            Money(Red, 7)));
             },
             "Dialogue: two triggers speak at once; their lines are queued and shown one at a time (never over each other), the game held (GameController.Paused) while one is shown; each trigger's next action waits until its own lines are closed. With no dialogue box (headless, or no prefab), lines pass at once and nothing waits.");
        n++;
        make("script-save", "scriptSave", (a, o) => {
                 Put("Grunt", a, 0, 0); Put("Grunt", o, 3, 0); Put("Grunt", o, 4, 0);
                 Script(Trig("Two kills", TriggerCondition.UnitKilled, t => { t.Side = Red; t.Count = 2; }, Money(Blue, 1)),
                        Trig("Mark", TriggerCondition.LevelStart, null, Act(TriggerActionType.ShowMarker, x => { x.Marker = "Marker"; x.MarkerId = "m"; x.TileX = 3; x.TileY = 0; })),
                        Trig("Talk", TriggerCondition.LevelStart, null,
                            Act(TriggerActionType.Dialogue, x => x.Lines = new List<DialogueLine> { new DialogueLine { Character = "Placeholder Officer", Text = "Saved mid-sentence." } }),
                            Money(Blue, 10)),
                        Trig("In area", TriggerCondition.UnitsInArea, t => { t.Side = Blue; t.Radius = 2; t.Enabled = false; }));
             },
             "Saving a game with its triggers part way (ScenarioScript.ToJson with progress, as LevelEditor.BoardJson writes it, and Load): a kill counted toward Two kills, a trigger fired, a marker shown, a line on screen with its trigger's next action waiting on it, the level clock — all come back; the conversation is shown again, and closing it lets the waiting action run. The scenario as authored (no progress) is what the file keeps; conditions are saved by number (Units In Area is 6).");
        n++;
        make("script-ai-given", "scriptAIGiven", (a, o) => {
                 Put("Grunt", a, -4, 0); Put("Grunt", a, -4, 1);
                 Put("Grunt", o, -3, -2); Put("Tank", o, 5, -2);
                 Script(Trig("Defect", TriggerCondition.MoneyAtLeast, t => { t.Side = Blue; t.Value = 999999; },
                            Act(TriggerActionType.GiveUnits, x => { x.UnitSide = Red; x.UnitKind = "Grunt"; x.Side = Blue; }),
                            Act(TriggerActionType.SpawnUnit, x => { x.Side = Blue; x.UnitKind = "Grunt"; x.TileX = -5; x.TileY = 2; x.UnitTag = "Arrived"; x.ReadyToAct = true; })));
             },
             "Units an AI gets in the middle of its turn play that turn: after Blue's AI (this AI) has made its first move, a trigger gives it a Red grunt and spawns a grunt for it, ready to act; both are taken into its turn (each decides, as every unit does).");
        n++;
        // The tutorial mission itself (Assets/Resources/Scenarios/Tutorial.dat), with a test block: played through below.
        string tutorial = Path.Combine(PrefabLoader.Root("Scenarios"), "Tutorial.dat");
        if (File.Exists(tutorial)) {
            string text = File.ReadAllText(tutorial).TrimEnd();
            text = text.Substring(0, text.LastIndexOf('}')) + ",\n\"test\": {\"name\": \"tutorial-playthrough\", \"kind\": \"rules\", \"opponent\": \"Human\", \"rounds\": \"8\", \"rules\": \"sandbox\", \"check\": \"scriptTutorial\", "
                 + "\"question\": \"The tutorial mission (Resources/Scenarios/Tutorial.dat), played with the player's clicks: select the grunt, move it to the mark, end the turn; the raider spawned in the fog is ordered at it and hits it; back into supply; the HQ hint on turn 3; a grunt and a supply truck bought; the attack hint; the raider killed: the player wins. Each step's lines are said once, in order.\"}\n}\n";
            File.WriteAllText(Path.Combine(Dir, "tutorial-playthrough.dat"), text);
            n++;
        }
        return n;
    }

    /// <summary>Stand <paramref name="u"/> on (x, y), as a move leaves it.</summary>
    static void MoveTo(Unit u, int x, int y) {
        HexTile to = Rig.At(x, y);
        if (u.tile != null && u.tile.occupier == u) u.tile.occupier = null;
        u.tile = to;
        to.occupier = u;
    }

    class FakeBox : IDialoguePresenter {
        public readonly List<string> shown = new List<string>();
        public int closed;
        public void ShowLine(DialogueLine line) { shown.Add(line.Text); }
        public void Close() { closed++; }
    }

    static int Fired(ScenarioScript s, string name) { ScenarioTrigger t = s.Find(name); return t != null ? t.TimesFired : -1; }

    static bool ScriptCheck(ScenarioFile.Loaded l, out string detail) {
        GameController gc = Rig.gc;
        Player a = l.ai, o = l.other;
        ScenarioScript s = ScenarioScript.Current;
        foreach (Building b in Building.all) if (b != null) b.income = 0;
        // Blue played by hand (the harness gives it an AI): Red is the human side.
        foreach (MonoBehaviour m in a.GetComponents<MonoBehaviour>()) if (m is IAIController) m.enabled = false;
        ScenarioDialogue.Presenter = null;
        switch (l.meta.Get("check", "")) {
            case "scriptTiming": {
                a.money = 0; o.money = 0;
                Unit g = Rig.At(-2, 0).occupier;
                for (int i = 0; i < 5; i++) s.Tick(0.1f);
                bool once = a.money == 100 && Fired(s, "Start") == 1;
                MoveTo(g, 0, 0); s.Tick(0.1f);                                    // held from Blue's turn 0
                gc.EndTurn(); s.Tick(0.1f); gc.EndTurn(); s.Tick(0.1f);           // Blue's turn 1 starts: held 1 turn
                bool notYet = Fired(s, "Hold") == 0;
                MoveTo(g, -1, 0); s.Tick(0.1f); MoveTo(g, 0, 0); s.Tick(0.1f);   // broken, held again from turn 1
                gc.EndTurn(); s.Tick(0.1f); gc.EndTurn(); s.Tick(0.1f);           // turn 2: 1 turn since
                bool restarted = Fired(s, "Hold") == 0;
                gc.EndTurn(); s.Tick(0.1f); gc.EndTurn(); s.Tick(0.1f);           // turn 3: 2 turns since
                bool held = Fired(s, "Hold") == 1 && a.money == 101;
                bool repeat = Fired(s, "Every Blue turn") == 3 && Fired(s, "Second Blue turn") == 1 && o.money == 1030;
                detail = "Level Start once " + once + " (Blue " + a.money + "); Delay 2 turns: fired after 1 turn " + !notYet + ", count started over after a break " + restarted + ", fired once held 2 of Blue's turns " + held
                       + "; three Blue turn ends: every Blue turn " + Fired(s, "Every Blue turn") + "x, second Blue turn " + Fired(s, "Second Blue turn") + "x (Red " + o.money + ")";
                return once && notYet && restarted && held && repeat;
            }
            case "scriptEvents": {
                Unit tank = Rig.At(1, 1).occupier, redGrunt = Rig.At(2, 0).occupier, blueGrunt = Rig.At(0, 0).occupier;
                Building city = Rig.At(0, 0).building, other = Rig.At(3, 3).building;
                o.money = 50;
                s.Tick(0.1f);
                List<string> early = new List<string>();
                foreach (ScenarioTrigger t in s.triggers) if (t.TimesFired > 0) early.Add(t.Name);
                bool quiet = early.Count == 0;   // (Blue has 2 units: Three Blue units waits for the third)
                // Shot until it dies.
                int shots = 0;
                string hurtAt = "";
                while (redGrunt != null && redGrunt.Strength > 0 && shots < 6) {
                    tank.active = true; tank.ammo = Mathf.Max(1, tank.ammoMax); tank.Strength = 6;
                    tank.OrderAttack(redGrunt); shots++;
                    if (redGrunt.Strength > 0 && redGrunt.Strength <= 4 && hurtAt == "") hurtAt = "strength " + redGrunt.Strength;
                    if (redGrunt.Strength <= 0) redGrunt.Perish();
                    s.Tick(0.1f);
                    if (Fired(s, "Hurt") == 1 && hurtAt == "") hurtAt = "the shot that killed it";
                }
                bool killed = Fired(s, "Killed") == 1 && Fired(s, "Hurt") == 1 && Fired(s, "No Red grunts") == 1;
                tank.Strength = 3; s.Tick(0.1f); tank.Strength = 6; s.Tick(0.1f);
                bool healed = Fired(s, "Healed") == 1;
                bool notEntered = Fired(s, "Entered") == 0;
                MoveTo(tank, 3, -2); s.Tick(0.1f);
                bool entered = Fired(s, "Entered") == 1;
                bool day1 = Fired(s, "Day 2") == 0;
                bool dayOnBlue = true;
                for (int i = 0; i < 3; i++) {   // Blue, Red, Blue: the grunt on the city takes it
                    gc.EndTurn(); s.Tick(0.1f);
                    if (i == 0) dayOnBlue &= Fired(s, "Day 2") == 1 && gc.activePlayer == o && Fired(s, "Day 2 on Blue's turn") == 0;
                }
                dayOnBlue &= Fired(s, "Day 2 on Blue's turn") == 1;
                bool captured = city.owner == a && Fired(s, "Captured") == 1 && Fired(s, "City held") == 1;
                bool turns = Fired(s, "Day 2") == 1 && Fired(s, "Red turn starts") == 1 && Fired(s, "Human turn over") == 1;
                a.money = 100000; s.Tick(0.1f);
                bool rich = Fired(s, "Rich") == 1;
                Building hq = null; foreach (Building b in Building.all) if (b != null && b.IsHQ && b.owner == a) hq = b;
                Unit bought = hq != null && hq.buildable != null && hq.buildable.Count > 0 ? Rig.bc.Produce(hq, hq.buildable[0]) : null;
                s.Tick(0.1f);
                bool built = bought != null && Fired(s, "Built") == 1 && Fired(s, "Three Blue units") == 1;
                bool threeNotBefore = true;
                other.Damage(other.health); s.Tick(0.1f);
                bool destroyed = Fired(s, "Destroyed") == 1;
                bool preStates = Fired(s, "Starving") == 0 && Fired(s, "No ammo") == 0 && Fired(s, "Low fuel") == 0 && Fired(s, "No fuel") == 0;
                blueGrunt.supplies = 0; tank.ammo = 0; tank.fuel = Mathf.Max(1, tank.fuelMax / 5); s.Tick(0.1f);
                bool low = Fired(s, "Starving") == (blueGrunt.suppliesMax > 0 ? 1 : 0) && Fired(s, "No ammo") == (tank.ammoMax > 0 ? 1 : 0) && Fired(s, "Low fuel") == (tank.fuelMax > 0 ? 1 : 0);
                tank.fuel = 0; s.Tick(0.1f);
                bool empty = Fired(s, "No fuel") == (tank.fuelMax > 0 ? 1 : 0);
                bool broke = Fired(s, "Broke") == 0;
                o.money = 0; s.Tick(0.1f);
                broke &= Fired(s, "Broke") == 1;
                detail = "nothing fired early " + quiet + (quiet ? "" : " (" + string.Join(", ", early.ToArray()) + ")") + "; Red grunt hurt (" + hurtAt + ") and killed in " + shots + " shots " + killed
                       + "; tank healed to 6 " + healed + "; entered (not before " + notEntered + ") " + entered + "; city captured and held " + captured + " (day 2 not before " + day1 + "; turns and day " + turns + ", day 2 waits for Blue's turn " + dayOnBlue
                       + "); rich " + rich + "; built, then 3 Blue units " + built + "; destroyed " + destroyed + "; states not before " + preStates + ", starving / no ammo / low fuel " + low + ", no fuel " + empty + "; broke at 0, not at 50 " + broke;
                return quiet && killed && healed && notEntered && entered && day1 && captured && turns && dayOnBlue && rich && built && threeNotBefore && destroyed && preStates && low && empty && broke;
            }
            case "scriptComposite": {
                Unit red = Rig.At(4, 0).occupier, blue = Rig.At(0, 0).occupier;
                s.Tick(0.1f);
                bool first = Fired(s, "Either") == 1 && Fired(s, "After either") == 1 && Fired(s, "Both") == 0 && Fired(s, "Neither") == 0 && Fired(s, "Late") == 0;
                MoveTo(red, 1, 0); s.Tick(0.1f);
                bool both = Fired(s, "Both") == 1 && Fired(s, "Arm") == 1 && Fired(s, "Late") == 1;
                MoveTo(red, 5, 0); MoveTo(blue, -4, 0); s.Tick(0.1f);
                bool neither = Fired(s, "Neither") == 1;
                bool parts = Fired(s, "Blue in middle") == 0 && Fired(s, "Red in middle") == 0;
                bool loop = Fired(s, "Loop A") == 0 && Fired(s, "Loop B") == 0;
                detail = "Blue in the middle: Any Of " + Fired(s, "Either") + ", After it " + Fired(s, "After either") + ", All Of " + Fired(s, "Both") + ", None Of " + Fired(s, "Neither") + " (" + first + ")"
                       + "; both: All Of, its After Trigger, and the trigger it switched on " + both + "; neither: None Of " + neither + "; parts switched off never fired " + parts + "; a loop of parts never met " + loop;
                return first && both && neither && parts && loop;
            }
            case "scriptActions": {
                Unit grunt = Rig.At(0, 0).occupier, tank = Rig.At(3, -1).occupier;
                Building neutral = Rig.At(-2, 3).building, redCity = Rig.At(4, 2).building;
                s.Tick(0.1f);
                List<Unit> spawned = ScenarioGame.Units(new UnitFilter { side = Blue, tag = "Reinforcement" });
                bool spawn = spawned.Count == 2;
                foreach (Unit u in spawned) spawn &= u.Strength == 3 && u.Veterancy == 2 && !u.active && u.tile != null && u.tile != Rig.At(0, 0);
                bool given = tank.owner == a && a.units.Contains(tank) && !o.units.Contains(tank) && tank.ammo == 0 && neutral.owner == a;
                bool ground = Rig.At(2, 2).terrain != null && Rig.At(2, 2).terrain.name != "Forest";
                bool cityDown = redCity.Destroyed;
                bool marker = s.markers.Count == 1 && s.markers[0].Id == "goal";
                s.Tick(1f); s.Tick(0.6f);   // "Then": After "Do it", held 1.5 s
                bool repaired = Rig.At(2, 2).terrain.name == "Forest" && !redCity.Destroyed && grunt.Strength <= 0 && Rig.At(0, 0).occupier == null && s.markers.Count == 0;
                bool notYet = !gc.gameOver;
                s.Tick(0.5f); bool stillWaiting = !gc.gameOver;
                s.Tick(0.6f);
                bool won = gc.gameOver && gc.winner == a;
                detail = "spawned " + spawned.Count + " tagged grunts (strength 3, veterancy 2, spent, beside the taken tile) " + spawn + "; tank and neutral city given, tank out of ammunition " + given
                       + "; forest destroyed " + ground + " (now " + Rig.At(2, 2).terrain.name + "); Red's city destroyed " + cityDown + "; marker shown " + marker + "; after 1.5 s repaired, grunt destroyed, marker hidden " + repaired
                       + "; Win waits its Wait " + (notYet && stillWaiting) + ", then Blue wins " + won;
                return spawn && given && ground && cityDown && marker && repaired && notYet && stillWaiting && won;
            }
            case "scriptDialogue": {
                FakeBox box = new FakeBox();
                ScenarioDialogue.Presenter = box;
                o.money = 0;
                s.Tick(0.1f);
                bool held = GameController.Paused && GameController.ScriptHold && box.shown.Count == 1 && box.shown[0] == "One." && o.money == 0;
                ScenarioDialogue.Next(); s.Tick(0.1f);
                bool second = box.shown.Count == 2 && box.shown[1] == "Two." && o.money == 0;
                ScenarioDialogue.Next(); s.Tick(0.1f);
                bool third = box.shown.Count == 3 && box.shown[2] == "Three." && o.money == 5 && GameController.Paused;
                ScenarioDialogue.Next(); s.Tick(0.1f);
                bool done = !GameController.Paused && !GameController.ScriptHold && o.money == 12 && box.closed == 1;
                // No box: lines pass at once.
                ScenarioDialogue.Presenter = null;
                int last = ScenarioDialogue.Enqueue(new List<DialogueLine> { new DialogueLine { Text = "nobody sees this" } });
                bool passes = ScenarioDialogue.Closed(last) && !GameController.Paused;
                detail = "first line shown, game held, next action waiting " + held + "; second " + second + "; the other trigger's line after them, the first trigger's action run " + third
                       + "; closed: game goes on, both actions run (Red " + o.money + "), box closed once " + done + "; with no box lines pass " + passes;
                return held && second && third && done && passes;
            }
            case "scriptAIGiven": {
                foreach (MonoBehaviour m in a.GetComponents<MonoBehaviour>()) if (m is IAIController) m.enabled = true;
                AIPlayer ai = a.GetComponent<AIPlayer>();
                Unit defector = Rig.At(-3, -2).occupier;
                gc.activePlayer = a;
                bool more = ai.StepOnce(true);   // the turn is prepared, and its first unit acts
                a.money = 999999; s.Tick(0.1f); s.Tick(0.1f);
                Unit arrived = null; foreach (Unit u in a.units) if (u != null && u.personal_name == "Arrived") arrived = u;
                bool given = defector.owner == a && arrived != null && defector.active && arrived.active;
                // (At the turn's end every unit is ready again: who decided is what tells.)
                HashSet<Unit> decided = new HashSet<Unit>();
                ai.onDecision = (u, act, did) => decided.Add(u);
                int guard = 0;
                while (more && gc.activePlayer == a && guard++ < 200) more = ai.StepOnce(true);
                ai.onDecision = null;
                bool played = decided.Contains(defector) && arrived != null && decided.Contains(arrived);
                detail = "given and spawned mid-turn, still to act " + given + "; decided in Blue's turn (taken into it): defector " + decided.Contains(defector) + ", spawned " + (arrived != null && decided.Contains(arrived));
                return given && played;
            }
            case "scriptTutorial": {
                Rig.gc.rules = RuleSet.Sandbox;
                Player human = o, raiders = a;
                human.money = 9000;
                FakeBox box = new FakeBox();
                ScenarioDialogue.Presenter = box;
                foreach (MonoBehaviour m in raiders.GetComponents<MonoBehaviour>()) if (m is IAIController) m.enabled = true;
                AIPlayer ai = raiders.GetComponent<AIPlayer>();
                List<string> log = new List<string>();
                Func<string> said = () => { string r = string.Join(" | ", box.shown.ToArray()); box.shown.Clear(); return r; };
                Action settle = () => { for (int i = 0; i < 20; i++) { s.Tick(0.2f); while (ScenarioDialogue.Showing) { ScenarioDialogue.Next(); s.Tick(0.2f); } } };
                Action aiTurn = () => { int guard = 0; bool more = true; while (more && gc.activePlayer == raiders && guard++ < 100) { more = ai.StepOnce(true); settle(); } if (gc.activePlayer == raiders) gc.EndTurn(); settle(); };
                Unit recruit = ScenarioGame.Named("Recruit");
                bool ok = recruit != null && human.units.Contains(recruit);

                settle();
                string welcome = said(); bool step1 = welcome.Contains("Click on him") && s.markers.Exists(m => m.Id == "recruit");
                // (The board's click flow needs the tiles' overlays, which the headless board lacks: the clicks' effects are
                // made as ClickBoard makes them: selection notified, the move walked as CompleteMove walks it.)
                HexTile.NotifyUnitSelected(recruit); settle();
                string selected = said(); bool step2 = selected.Contains("propose a move") && s.markers.Exists(m => m.Id == "goal") && !s.markers.Exists(m => m.Id == "recruit");
                ScenarioGame.Order(recruit, null, Rig.At(0, 0)); settle();
                string moved = said(); bool step3 = recruit.tile == Rig.At(0, 0) && moved.StartsWith("Good job!") && moved.Contains("End your turn") && !s.markers.Exists(m => m.Id == "goal");
                FogOfWar.Changed();
                bool fogged = gc.fogOfWar && !FogOfWar.TileVisible(human, Rig.At(4, -1));   // where the raider will appear: out of sight
                gc.EndTurn(); settle();   // the raiders' turn: the ambush
                Unit raider = ScenarioGame.Named("Raider");
                bool spawned = raider != null && raider.owner == raiders && fogged;
                int hitTo = recruit.Strength;
                bool hit = recruit.Strength < 6 && raider != null && raider.tile != null && raider.tile.Distance(recruit.tile) == 1;
                string hurt = said(); bool step4 = spawned && hit && hurt.Contains("We took damage");
                aiTurn();
                // Turn 2: back into supply beside the HQ.
                HexTile.NotifyUnitSelected(recruit); ScenarioGame.Order(recruit, null, Rig.At(-3, 1)); settle(); said();
                bool back = recruit.tile == Rig.At(-3, 1);
                int before = recruit.Strength;
                gc.EndTurn(); settle(); aiTurn();
                // Turn 3: healed in supply, and the HQ hint (it was never opened).
                string third = said();
                bool healed = recruit.Strength > before || recruit.Strength == 6;
                bool step5 = back && third.Contains("click the HQ") && s.markers.Exists(m => m.Id == "hq");
                Building hq = Rig.At(-4, 2).building;
                HexTile.NotifyBuildingOpened(hq); settle();
                string opened = said(); bool step6 = opened.Contains("build list") && !s.markers.Exists(m => m.Id == "hq");
                Func<string, Unit> buy = kind => { Unit pick = hq.buildable.Find(x => x != null && GameController.UnitName(x) == kind); return pick != null ? Rig.bc.Produce(hq, pick) : null; };
                string offered = string.Join(",", hq.buildable.ConvertAll(x => x != null ? GameController.UnitName(x) : "?").ToArray());
                Unit grunt = buy("Grunt"); settle();
                string g1 = said(); bool step7 = grunt != null && g1.Contains("A new grunt");
                gc.EndTurn(); settle(); aiTurn();
                // Turn 4: the new grunt steps off the HQ, a supply truck is bought; the attack hint (turn 4, and the truck).
                if (grunt != null && grunt.Strength > 0) ScenarioGame.Order(grunt, null, Rig.At(-5, 2));
                settle();
                Unit truck = buy("Supplies"); settle();
                string fourth = said();
                bool step8 = truck != null && fourth.Contains("supply truck") && fourth.Contains("To attack") && Fired(s, "Fight back") == 1;
                // The raider dies (the player's attacks, as many as it takes: ordered here as the clicks would).
                raider = ScenarioGame.Named("Raider");
                int rounds = 0;
                while (raider != null && raider.Strength > 0 && !gc.gameOver && rounds++ < 6) {
                    foreach (Unit u in new List<Unit>(human.units)) if (u != null && u.Strength > 0 && u.active && raider != null && raider.Strength > 0 && u.CanAttack(raider)) ScenarioGame.Order(u, raider, null);
                    settle();
                    raider = ScenarioGame.Named("Raider");
                    if (raider == null || gc.gameOver) break;
                    gc.EndTurn(); settle(); aiTurn();
                }
                string end = said();
                bool won = gc.gameOver && gc.winner == human && end.Contains("Raider down");
                detail = "start " + ok + "; welcome and marker " + step1 + "; selected: move hint, marker moved " + step2 + "; moved to the mark: Good job, end-turn hint " + step3
                       + "; ambush: raider spawned in the fog " + spawned + ", next to the grunt and hit it (to " + hitTo + ") " + hit + ", lines " + step4
                       + "; back into supply " + back + ", healed " + healed + ", HQ hint on turn 3 " + step5 + "; HQ opened " + step6 + "; grunt bought " + step7 + (step7 ? "" : " (HQ offers " + offered + "; gold " + human.money + ")") + "; truck bought and attack hint " + step8
                       + "; raider killed in " + rounds + " round(s), player wins " + won + (won ? "" : " [last lines: " + end + "]");
                ScenarioDialogue.Presenter = null;
                return ok && step1 && step2 && step3 && step4 && back && healed && step5 && step6 && step7 && step8 && won;
            }
            case "scriptSave": {
                bool fromFile = s.triggers.Count == 4 && s.Find("Two kills") != null && s.Find("Two kills").Count == 2 && !s.Find("In area").Enabled;
                FakeBox box = new FakeBox();
                ScenarioDialogue.Presenter = box;
                a.money = 0;
                s.Tick(0.5f);
                Rig.At(3, 0).occupier.Perish();
                s.Tick(0.5f);
                s.turnStarts.Clear(); s.turnStarts.Add(new SideTurns { Id = Blue, Count = 3 });   // (as three Blue turns would leave it)
                string json = s.ToJson(true).Print(true);
                string authored = s.ToJson(false).Print(true);
                bool partWay = s.Find("Two kills").Events == 1 && Fired(s, "Talk") == 1 && a.money == 0 && box.shown.Count == 1;
                // A fresh load from the saved text (as ScenarioLoader does from a save).
                FakeBox box2 = new FakeBox();
                ScenarioDialogue.Presenter = box2;
                s.Load(new Defective.JSON.JSONObject(json), l.path);
                bool back = s.Find("Two kills").Events == 1 && Fired(s, "Talk") == 1 && Fired(s, "Mark") == 1 && s.markers.Count == 1 && s.running.Count == 1
                         && Math.Abs(s.Clock - 0.5f) < 0.01f && s.TurnsOf(Blue) == 3 && s.TurnsOf(TriggerSide.Any) == 3 && box2.shown.Count == 1 && box2.shown[0] == "Saved mid-sentence." && GameController.Paused;
                s.Tick(0.1f);
                bool waits = a.money == 0;
                ScenarioDialogue.Next(); s.Tick(0.1f);
                bool ran = a.money == 10 && !GameController.Paused;
                Rig.At(4, 0).occupier.Perish(); s.Tick(0.1f);
                bool second = Fired(s, "Two kills") == 1 && a.money == 11;
                bool noState = !authored.Contains("TimesFired") && !authored.Contains("\"Events\"") && !authored.Contains("Clock") && json.Contains("TimesFired");
                bool byNumber = System.Text.RegularExpressions.Regex.IsMatch(authored, "\"Condition\"\\s*:\\s*6\\b");
                string board = "";
                try { board = LevelEditor.BoardJson(); } catch (Exception e) { board = "(BoardJson: " + e.GetType().Name + ")"; }
                bool inBoard = board.Contains("\"" + ScenarioScript.FileKey + "\"");
                // Keys for dragged assets: a name in the kind's folder, else the path under Resources.
                bool keys = ScenarioAssets.KeyOf("Music/Special/Theme", "Music/Special") == "Theme" && ScenarioAssets.KeyOf("Campaign/Theme", "Music/Special") == "Campaign/Theme"
                         && ScenarioAssets.KeyOf("Music/Special/Act1/Theme", "Music/Special") == "Music/Special/Act1/Theme";
                ScenarioDialogue.Presenter = null;
                detail = "loaded from the file " + fromFile + "; part way (1 kill of 2, a line up, its action waiting) " + partWay + "; back from the save " + back + "; still waiting " + waits
                       + "; closed, the action runs " + ran + "; second kill fires " + second + "; the authored script has no progress " + noState + ", conditions by number " + byNumber + "; in the game's save " + inBoard + "; asset keys " + keys;
                return fromFile && partWay && back && waits && ran && second && noState && byNumber && inBoard && keys;
            }
        }
        detail = "no such check";
        return false;
    }
}
