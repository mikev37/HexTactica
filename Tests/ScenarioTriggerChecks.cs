using System.Text.RegularExpressions;
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
                        Trig("Board", TriggerCondition.UnitsAtLeast, t => { t.Side = Blue; t.Value = 1; }, Money(Blue, 7)),
                        Trig("Clicked", TriggerCondition.UnitSelected, t => { t.Side = Blue; }, Money(Red, 3)),
                        Trig("Hold", TriggerCondition.UnitsInArea, t => { t.Side = Blue; t.TileX = 0; t.TileY = 0; t.Radius = 0; t.Delay = 2; t.TurnsOf = Blue; }, Money(Blue, 1)),
                        Trig("Every Blue turn", TriggerCondition.TurnOver, t => { t.Side = Blue; t.Repeat = true; }, Money(Red, 10)),
                        Trig("Second Blue turn", TriggerCondition.TurnOver, t => { t.Side = Blue; t.Count = 2; }, Money(Red, 1000)));
             },
             "Scenario trigger timing (ScenarioScript): a trigger about the board (Units At Least) waits until no unit is animating (Wait For Animations); one about the clock or a click (Level Start, Unit Selected) fires at once while a unit is still walking; Level Start fires once however long the game runs; a trigger with Delay 2 (turns of Blue) fires only once its condition (a Blue unit on (0,0)) has held through two of Blue's turns starting, without a break, and the count starts again when it breaks; Turn Over with Repeat fires at the end of every Blue turn; with Count 2 (and no Repeat), once, at the end of the second.");
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
                            Act(TriggerActionType.ShowMarker, x => { x.Marker = "Marker"; x.MarkerId = "goal"; x.Text = "Take it"; x.TileX = 5; x.TileY = 0; }),
                            Act(TriggerActionType.SetPurchases, x => x.On = false),
                            Act(TriggerActionType.SetFog, x => x.On = true)),
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
             "Every board action, in order: two tagged Blue grunts spawned (strength 4, veterancy 1, spent this turn) beside a taken tile; Red's tank given to Blue; the neutral city given to Blue; a forest destroyed; Red's city destroyed; the spawned grunts' veterancy raised by 1 and strength lowered by 1; the tank's ammunition set to 0; a marker shown; purchases switched off and fog of war on. After a 1.5 s Wait (pacing, as in a cutscene) the forest and the city are repaired, the grunt on (0,0) destroyed and the marker hidden; at 2.5 s Blue wins.");
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
             "Dialogue: two triggers speak at once; their lines are shown one at a time (never over each other), the second trigger's once the first's scene is over, the game held (GameController.Paused) while one is shown; each trigger's next action waits until its own lines are closed. With no dialogue box (headless, or no prefab), lines pass at once and nothing waits.");
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
        make("script-not-eliminable", "scriptEliminable", (a, o) => { Put("Grunt", o, 0, 0); },
             "A side that is not eliminable (Player.eliminable off, as a scenario's enemy that starts with nothing) is not knocked out by losing its HQ and every unit, and the game goes on; the same side eliminable is knocked out, and the game ends.");
        n++;
        make("script-move-proposed", "scriptMoveProposed", (a, o) => {
                 Put("Grunt", a, 0, 0); Put("Grunt", o, 3, 0);
                 Script(Trig("Next to Red", TriggerCondition.MoveProposed, t => { t.Side = Blue; t.InArea = true; t.TileX = 2; t.TileY = 0; t.Radius = 0; }),
                        Trig("Any two", TriggerCondition.MoveProposed, t => { t.Side = Blue; t.Count = 2; }),
                        Trig("Red proposes", TriggerCondition.MoveProposed, t => { t.Side = Red; }),
                        Trig("Arrived", TriggerCondition.UnitEnters, t => { t.Side = Blue; t.TileX = 2; t.TileY = 0; t.Radius = 0; }));
             },
             "Move Proposed (a tutorial's attack step 2): the player proposing a move for a Blue grunt (the first click on a reachable tile: nothing moves) counts toward Move Proposed; In Area counts only a move proposed to a tile within the area (here the tile next to the Red grunt), Count 2 needs two proposals, a side filter ignores the other side's units, and none of it is Unit Enters, which waits for the move itself.");
        n++;
        make("script-place-inspect", "scriptPlaceInspect", (a, o) => {
                 Put("Grunt", a, 0, 0); Put("Grunt", o, 3, 0);
                 Script(Trig("Red inspected", TriggerCondition.EnemyInspected, t => { t.Side = Red; },
                            Act(TriggerActionType.PlaceUnits, x => { x.UnitSide = Blue; x.UnitKind = "Grunt"; x.TileX = -2; x.TileY = 0; x.ReadyToAct = true; })),
                        Trig("Blue inspected", TriggerCondition.EnemyInspected, t => { t.Side = Blue; }),
                        Trig("Crowd", TriggerCondition.EnemyInspected, t => { t.Side = Red; t.Count = 2; },
                            Act(TriggerActionType.PlaceUnits, x => { x.UnitSide = Blue; x.UnitKind = "Grunt"; x.TileX = 3; x.TileY = 0; x.ReadyToAct = false; })));
             },
             "Enemy Inspected and Place Units: the player clicking a Red unit to see its reach counts for a Red filter, not a Blue one; Place Units lifts the Blue grunt onto (-2,0) at once, ready to act, its old tile left empty, nobody killed; placed onto a tile taken (the Red grunt's), it stands on the nearest free tile instead, spent as asked.");
        n++;
        make("script-subject-ambush", "scriptSubjectAmbush", (a, o) => {
                 Put("Grunt", o, -2, 0); Put("Grunt", o, 3, 0); Put("Tank", a, 4, -1);
                 Rig.At(3, 0).terrain = T("Forest"); Rig.At(-3, 2).terrain = T("Forest"); Rig.At(-6, 2).terrain = T("Forest");
                 Script(Trig("Came near", TriggerCondition.UnitEnters, t => { t.Side = Red; t.TileX = 0; t.TileY = 0; t.Radius = 1; },
                            Act(TriggerActionType.OrderUnits, x => { x.UnitSide = Blue; x.UnitKind = "Tank"; x.Target = "{unit}"; x.TargetSide = Red; })),
                        Trig("Hunt", TriggerCondition.UnitDamaged, t => { t.Side = Red; t.Value = 5; },
                            Act(TriggerActionType.ReadyUnits, x => { x.UnitSide = Blue; x.Value = 1; }),
                            Act(TriggerActionType.OrderUnits, x => { x.UnitSide = Blue; x.UnitKind = "Tank"; x.TargetSide = Red; x.Value = 2; })),
                        Trig("Red ambush", TriggerCondition.Ambush, t => { t.Side = Red; }),
                        Trig("Blue ambush", TriggerCondition.Ambush, t => { t.Side = Blue; }),
                        Trig("Reach", TriggerCondition.UnitSelected, t => { t.Side = Red; t.Repeat = true; },
                            Act(TriggerActionType.HideMarker, x => { x.MarkerId = "reach*"; }),
                            Act(TriggerActionType.MarkReach, x => { x.UnitSide = Red; x.UnitTag = "{unit}"; x.Value = 2; x.Marker = "Highlight"; x.MarkerId = "reach"; })),
                        Trig("Any preview", TriggerCondition.AttackProposed, t => { t.Side = Red; }),
                        Trig("Ambush preview", TriggerCondition.AttackProposed, t => { t.Side = Red; t.Value = 1; }),
                        Trig("Spawn hidden", TriggerCondition.AfterTrigger, t => { t.Enabled = false; t.AfterTrigger = "Came near"; },
                            Act(TriggerActionType.SpawnUnit, x => { x.Side = Blue; x.UnitKind = "Grunt"; x.TileX = -2; x.TileY = 1; x.UnitTag = "Hidden"; x.OutOfSight = true; })));
             },
             "{unit}, Order Units in cover, Ambush, Mark Reach, Attack Proposed, Spawn out of sight: a Red grunt steps within 1 of (0,0), and the Blue tank ordered at {unit} goes for that grunt, not the nearer one; ordered at the nearest Red unit in cover 2 or more, it goes for the grunt in the forest, which fires first (Ambush for Red, its subject that grunt; none for Blue); selecting a Red grunt marks the forests it can reach this turn (and not one beyond), and selecting it again marks them afresh, 'reach*' hiding them all; Attack Proposed with Value 1 counts only a preview where the defender would fire first; with fog on, a grunt spawned out of sight stands on a tile Red cannot see, not on the tile asked for, which Red sees.");
        n++;
        make("script-order-targets", "scriptOrderTargets", (a, o) => {
                 Rig.PlaceBuilding("City", null, 0, 0);
                 Put("Grunt", o, 0, 0); Put("Grunt", o, -2, 2); Put("Grunt", o, 2, -2); Put("Supplies", o, 3, -3); Put("Tank", a, 3, 0); Put("Tank", a, 4, -1);
                 Rig.At(-2, 2).terrain = T("Forest"); Rig.At(2, -2).terrain = T("Grassland"); Rig.At(3, -3).terrain = T("Grassland");
                 Script(Trig("Exposed", TriggerCondition.UnitsExposed, t => { t.Side = Red; t.UnitKind = "Grunt"; t.Value = 2; t.Count = 2; }),
                        Trig("On the city", TriggerCondition.LevelStart, null,
                            Act(TriggerActionType.OrderUnits, x => { x.UnitSide = Blue; x.UnitKind = "Tank"; x.TargetSide = Red; x.TargetOnTile = true; x.TileX = 0; x.TileY = 0; })));
             },
             "Order Units' targets and Units Exposed: a Blue tank ordered at whoever holds the city (Target On Tile) attacks the Red grunt on it, not a nearer one; Units Exposed (grunts on cover below 2) counts the grunt on grass and the one on the city's ground (cover 2 is not below 2: not that one), not the one in the forest; Target Exposed picks the grunt on the grass, never the supply unit (it cannot use cover).");
        n++;
        make("script-terrain-moved-fresh", "scriptTerrainMovedFresh", (a, o) => {
                 Put("Grunt", o, -3, 0); Put("Grunt", o, 2, -1); Put("Grunt", o, 3, -2); Put("Tank", a, 5, -2);
                 Rig.At(-2, 0).terrain = T("Hills"); Rig.At(-3, 1).terrain = T("Road"); Rig.At(3, -2).terrain = T("Forest");
                 Script(Trig("Onto hills", TriggerCondition.UnitMoved, t => { t.Side = Red; t.OnTerrain = "Hills"; }),
                        Trig("Onto road or forest", TriggerCondition.UnitMoved, t => { t.Side = Red; t.OnTerrain = "Road, Forest"; t.Count = 2; }),
                        Trig("Moved", TriggerCondition.UnitMoved, t => { t.Side = Red; t.Count = 2; }),
                        Trig("Hit in the open", TriggerCondition.UnitDamaged, t => { t.Side = Red; t.Value = 6; t.CoverBelow = 2; }),
                        Trig("Hit in cover", TriggerCondition.UnitDamaged, t => { t.Side = Red; t.Value = 6; t.OnTerrain = "Forest"; }),
                        Trig("Fresh", TriggerCondition.UnitMoved, t => { t.Side = Red; t.InArea = true; t.TileX = -3; t.TileY = 1; t.Radius = 0; },
                            Act(TriggerActionType.OrderUnits, x => { x.UnitSide = Blue; x.UnitKind = "Tank"; x.TargetSide = Red; x.TargetFresh = true; x.Value = 2; })),
                        Trig("Salute", TriggerCondition.LevelStart, null, Act(TriggerActionType.Salute, x => { x.UnitSide = Blue; x.Value = 3; })));
             },
             "Unit Moved, On Terrain, Cover Below, Order Units' Target Fresh, Salute: a Red grunt moved onto hills counts for On Terrain Hills, then onto a road for 'Road, Forest' (one of two); every move counts for Unit Moved; the Blue tank, ordered at the strongest Red unit it can reach, passes over the hurt grunt nearer it for the full-strength one (ties: the one in the forest), whose hit counts for On Terrain Forest and not Cover Below 2; a hurt grunt hit on grass counts for Cover Below 2; a salute (headless: nothing shown) hits nobody.");
        n++;
        make("script-new-functions", "scriptNewFunctions", (a, o) => {
                 Put("Grunt", o, 0, 0); Put("Grunt", o, 3, 0); Put("Grunt", o, -6, 6); Put("Tank", a, 1, 0); Put("Grunt", a, 4, 2); Put("Truck", a, -2, 2); Put("Grunt", a, -2, 3);
                 Rig.PlaceBuilding("City", null, -3, 0);
                 Script(Trig("Shot", TriggerCondition.UnitKilled, t => { t.Side = Red; t.Value = 1; }, Act(TriggerActionType.CameraFocus, x => { x.AtUnit = "{unit}"; x.Zoom = 17; })),
                        Trig("Starved", TriggerCondition.UnitKilled, t => { t.Side = Red; t.Value = 2; }),
                        Trig("Either", TriggerCondition.UnitKilled, t => { t.Side = Red; t.Count = 2; }),
                        Trig("Off the mark", TriggerCondition.UnitMoved, t => { t.Side = Blue; t.UnitTag = "Walker"; t.InArea = true; t.OutsideArea = true; t.TileX = -5; t.TileY = 5; t.Radius = 0; }),
                        Trig("On the mark", TriggerCondition.UnitMoved, t => { t.Side = Blue; t.UnitTag = "Walker"; t.InArea = true; t.TileX = -5; t.TileY = 5; t.Radius = 0; }),
                        Trig("None in sight", TriggerCondition.UnitsAtMost, t => { t.Side = Red; t.Value = 0; t.OnlySeen = true; t.SeenBy = Blue; }),
                        Trig("None left", TriggerCondition.UnitsAtMost, t => { t.Side = Red; t.Value = 0; }),
                        Trig("Half wrecked", TriggerCondition.BuildingDamaged, t => { t.InArea = true; t.TileX = -3; t.TileY = 0; t.Radius = 0; t.Value = 50; }),
                        Trig("Picked", TriggerCondition.UnloadProposed, t => { t.Side = Blue; t.UnitKind = "Grunt"; }),
                        Trig("Stars", TriggerCondition.LevelStart, null,
                            Act(TriggerActionType.BonusStars, x => { x.Value = -1; x.Text = "The city was destroyed."; }),
                            Act(TriggerActionType.BonusStars, x => { x.Value = 2; x.Text = "Nobody left behind."; }),
                            Act(TriggerActionType.RevealArea, x => { x.Side = Blue; x.TileX = -6; x.TileY = 6; x.Radius = 1; x.MarkerId = "poi"; }),
                            Act(TriggerActionType.HighlightUI, x => { x.Text = "TransportPanel"; })),
                        Trig("Hide", TriggerCondition.TurnOver, t => { t.Side = Blue; t.Count = 2; }, Act(TriggerActionType.RevealArea, x => { x.On = false; x.MarkerId = "poi"; })),
                        Trig("Caught up", TriggerCondition.UnitSelected, t => { t.Enabled = false; t.Side = Blue; t.UnitTag = "Walker"; }),
                        Trig("Missed", TriggerCondition.UnitSelected, t => { t.Side = Blue; t.UnitTag = "Walker"; }),
                        Trig("Enable it", TriggerCondition.LevelStart, null, Act(TriggerActionType.EnableTrigger, x => { x.Trigger = "Caught up"; })),
                        Trig("Scene", TriggerCondition.LevelStart, null,
                            Act(TriggerActionType.Dialogue, x => { x.Lines.Add(new DialogueLine { Character = "Anna", Text = "First." }); }),
                            Act(TriggerActionType.Wait, x => { x.Seconds = 0.5f; }),
                            Act(TriggerActionType.Dialogue, x => { x.Lines.Add(new DialogueLine { Character = "Anna", Text = "Last." }); })),
                        Trig("Brief", TriggerCondition.Briefing, null,
                            Act(TriggerActionType.ShowMarker, x => { x.MarkerId = "brief"; x.TileX = 2; x.TileY = 2; }),
                            Act(TriggerActionType.Dialogue, x => { x.Lines.Add(new DialogueLine { Character = "Anna", Text = "Never under the briefing." }); })),
                        Trig("Walker cut", TriggerCondition.UnitCutOff, t => { t.Enabled = false; t.Side = Blue; t.UnitTag = "Walker"; }),
                        Trig("Rider moved", TriggerCondition.UnitMoved, t => { t.Side = Blue; t.UnitTag = "Rider"; }),
                        Trig("Walker moves", TriggerCondition.UnitMoved, t => { t.Side = Blue; t.UnitTag = "Walker"; t.Count = 5; }));
             },
             "The scenario functions added for Act 1's playtest: Unit Killed's How (in action, starved); Unit Moved outside an area; Units At Most counting only units in sight; Building Damaged (half its health gone); Unload Proposed (a unit picked in its transport's panel); Unit Moved not counting boarding or riding; Bonus Stars (-1 and +2: one more star in the rating, a star taken from a perfect result); Reveal Area (the ground shown through the fog past the end of the turn, then hidden by name); Highlight UI (the hook told the panel's name); a trigger switched on after its click was made counts the click (the unit already selected); the dialogue box's Next while a later Dialogue of the same trigger is to come, Close at the last; the script holds an AI while its actions run; the camera on a unit that has died goes where it fell; Briefing: its marker shown under the briefing, its line not said, never fired in play; Unit Cut Off keeps its real reading while a move's preview is on the board.");
        n++;
        make("script-new-functions-2", "scriptNewFunctions2", (a, o) => {
                 Put("Grunt", o, 0, 0); Put("Tank", a, 2, 0); Put("Grunt", a, -3, 3); Put("Truck", a, -4, 3); Put("Grunt", o, 4, -2); Put("Tank", a, 6, -2);
                 Put("Grunt", a, 6, 1); Put("Grunt", a, 7, 0); Put("Grunt", o, 4, 3); Put("Grunt", a, -6, 0); Put("Supplies", o, -2, 5); Put("Artillery", a, 0, -4); Put("Supplies", a, -1, 6);
                 Script(Trig("Killed by Blue", TriggerCondition.UnitKilled, t => { t.Side = Red; t.KilledBy = Blue; }),
                        Trig("Killed by Red", TriggerCondition.UnitKilled, t => { t.Side = Red; t.KilledBy = Red; }),
                        Trig("Start", TriggerCondition.LevelStart, null,
                            Act(TriggerActionType.ProtectGround, x => { x.TileX = -2; x.TileY = 0; x.Radius = 0; }),
                            Act(TriggerActionType.OrderUnits, x => { x.UnitSide = Blue; x.UnitTag = "Rider"; x.Target = "Lorry"; }),
                            Act(TriggerActionType.OrderUnits, x => { x.UnitSide = Blue; x.UnitTag = "Ambusher"; x.TargetSide = Red; x.FromTile = true; x.TileX = 5; x.TileY = -3; })),
                        Trig("From the fog", TriggerCondition.FiredFromFog, t => { t.Side = Blue; t.UnitKind = "Artillery"; }),
                        Trig("Two armed", TriggerCondition.UnitsAtMost, t => { t.Side = Red; t.Value = 2; t.ArmedOnly = true; }),
                        Trig("Forced", TriggerCondition.AfterTrigger, t => { t.Enabled = false; t.AfterTrigger = "Start"; },
                            Act(TriggerActionType.OrderUnits, x => { x.UnitSide = Blue; x.UnitTag = "Hiker"; x.TileX = 6; x.TileY = -6; x.Force = true; }),
                            Act(TriggerActionType.OrderUnits, x => { x.UnitSide = Blue; x.UnitTag = "Far"; x.Target = "Lorry"; x.Force = true; }),
                            Act(TriggerActionType.OrderUnits, x => { x.UnitSide = Blue; x.UnitTag = "Pair"; x.TargetSide = Red; x.TargetOnTile = true; x.TileX = 4; x.TileY = 3; }),
                            Act(TriggerActionType.PlaceBuilding, x => { x.BuildingKind = "City"; x.Side = TriggerSide.Neutral; x.TileX = 0; x.TileY = 4; }),
                            Act(TriggerActionType.DestroyUnits, x => { x.UnitSide = Red; x.UnitKind = "Supplies"; x.Withdraw = true; })),
                        Trig("Supply gone", TriggerCondition.UnitKilled, t => { t.Side = Red; t.UnitKind = "Supplies"; }),
                        Trig("Unplaced", TriggerCondition.LevelStart, t => { t.Enabled = false; }, Act(TriggerActionType.PlaceBuilding, x => { x.BuildingKind = "None"; x.TileX = 0; x.TileY = 4; })),
                        Trig("Allies", TriggerCondition.TurnStarted, t => { t.Side = Red; }, Act(TriggerActionType.SetTeam, x => { x.Side = Red; x.Value = 4; }), Act(TriggerActionType.SetTeam, x => { x.Side = Blue; x.Value = 4; })));
             },
             "More scenario functions (Act 1, mission 5): Unit Killed By (a Red grunt killed by Blue's tank counts for By Blue, not By Red); Protect Ground (a protected tile takes no damage); Order Units boarding a transport named as its target; Order Units attacking from a set tile (From Tile); Set Team (with alliances on, two sides put on one team are allies, no longer hostile); Order Units Force (the whole way, however far; boarding a transport too); two units ordered at one target take different tiles; Place Building, and Place Building None taking it away; Destroy Units Withdraw (gone, nobody's kill); Units At Most Armed Only; Fired From Fog (artillery the target's side did not see).");
        n++;
        make("script-ai-objective", "scriptAIObjective", (a, o) => {
                 Rig.PlaceBuilding("City", o, -3, 0); Put("Grunt", o, -3, 0);
                 Put("Grunt", a, 3, 0); Put("Grunt", a, 3, 1); Put("Grunt", a, 4, -1);
                 Script(Trig("Go for the city", TriggerCondition.LevelStart, null, Act(TriggerActionType.SetAIObjective, x => { x.Side = Blue; x.TileX = -3; x.TileY = 0; })));
             },
             "Set AI Objective: Blue's AI (this AI), told to go for the city Red holds, has it as its mission objective (ScenarioAIGoal) and its three grunts close on it over two turns, each on a tile of its own (the AI decides who stands where).");
        n++;
        make("rule-bystander", "scriptBystander", (a, o) => {
                 Put("Grunt", o, 0, 0); Put("Grunt", a, 2, 0);
                 Script(Trig("Red turns", TriggerCondition.TurnStarted, t => { t.Side = Red; t.Repeat = true; }),
                        Trig("Humans", TriggerCondition.UnitsAtLeast, t => { t.Side = TriggerSide.Human; t.UnitKind = "Artillery"; t.Value = 1; }));
             },
             "A bystander side (Player.bystander: the range's guns at an exercise): nobody can attack its units and they attack nobody, it is nobody's enemy; its turn is skipped (Red's turn follows Blue's); it is not a Human (or AI) side for triggers; with its units still on the board, the side left standing wins as usual; given to a side (Give Units) its units fight again.");
        n++;
        make("script-one-scene", "scriptOneScene", (a, o) => {
                 Put("Grunt", a, 0, 0); Put("Grunt", o, 4, 0);
                 Script(Trig("Big hit", TriggerCondition.UnitDamaged, t => { t.Side = Red; t.Value = 6; t.LostAtLeast = 2; }, Money(Red, 100)),
                        Trig("First", TriggerCondition.LevelStart, null,
                            Act(TriggerActionType.Dialogue, x => x.Lines = new List<DialogueLine> { new DialogueLine { Character = "Anna", Text = "A1." } }),
                            Money(Blue, 1),
                            Act(TriggerActionType.Dialogue, x => x.Lines = new List<DialogueLine> { new DialogueLine { Character = "Anna", Text = "A2." } })),
                        Trig("Second", TriggerCondition.LevelStart, null,
                            Money(Red, 5),
                            Act(TriggerActionType.Dialogue, x => x.Lines = new List<DialogueLine> { new DialogueLine { Character = "Jori", Text = "B1." } })));
             },
             "One scene at a time (ScenarioScript): two triggers fire together, the first with two lines and an action between them, the second with one line: the second's line waits until the first's scene is over (A1, A2, then B1, never B1 between them); the second's action before its line goes ahead at once. Unit Damaged with Lost At Least 2: a 1-strength scratch does not count, a 2-strength hit does.");
        n++;
        make("mission-tally", "scriptMissionTally", (a, o) => { Put("Grunt", o, 0, 0); },
             "The mission rating's tally (MissionRating.Tally, from the game log): turns are the days the player moved on, each once (a turn logged twice, at a load, is one day); an enemy unit killed by the player, starved out of supply or ditched out of fuel is the player's kill; an ally's is nobody's; the player's own losses whatever the cause.");
        n++;
        make("campaign-progress", "scriptCampaignProgress", (a, o) => { Put("Grunt", o, 0, 0); },
             "The campaign (Campaign scene, campaign.dat): Act 1's flags linked 1.1 > 1.2 > 1.3 > 1.4 > 1.5, each scenario in the campaign folder; only 1.1 on the map at first; a loss unlocks nothing; a win unlocks the next and is kept on file (read back: still won, the next still there), on down the chain; stars by the pars, added on a better win and never taken away; a fork closes the other mission; no Act 1 mission opens Campaign 2; the map's pan and zoom.");
        n++;
        return n;
    }

    /// <summary>A campaign mission's scenario file (Resources/Campaign/Scenarios) as a test scenario: the file as it is, with a test block.</summary>
    static int ExportMission(string scenario, string name, string check, int rounds, string question) {
        string file = Path.Combine(PrefabLoader.Root("Campaign/Scenarios"), scenario + ".dat");
        if (!File.Exists(file)) return 0;
        string text = File.ReadAllText(file).TrimEnd();
        text = text.Substring(0, text.LastIndexOf('}')).TrimEnd() + ",\n\"test\": {\"name\": \"" + name + "\", \"kind\": \"rules\", \"opponent\": \"Human\", \"rounds\": \"" + rounds + "\", \"rules\": \"standard\", \"check\": \"" + check + "\", "
             + "\"question\": \"" + question + "\"}\n}\n";
        File.WriteAllText(Path.Combine(Dir, name + ".dat"), text);
        return 1;
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

    // ------------------------------------------------------------------ the campaign (Assets/FrontEnd/Campaign)

    /// <summary>
    /// The missions placed in a campaign scene (Assets/Scenes/&lt;scene&gt;.unity), read from the scene file as Unity
    /// wrote it: every instance of the Mission Flag prefab, its name and its Mission's fields from the instance's
    /// overrides (the prefab's own values otherwise: all empty), and its unlocks and blocks followed through the
    /// scene's references to other flags' Missions. What the campaign screen would make of each (Mission.Info).
    /// </summary>
    static List<MissionInfo> SceneMissions(string scene, out Dictionary<string, Vector2> anchors) {
        anchors = new Dictionary<string, Vector2>();
        List<MissionInfo> list = new List<MissionInfo>();
        string assets = PrefabLoader.Root("..");
        string path = Path.Combine(Path.Combine(assets, "Scenes"), scene + ".unity");
        string prefabMeta = Path.Combine(Path.Combine(Path.Combine(assets, "FrontEnd"), "Prefabs"), "Mission Flag.prefab.meta");
        string missionMeta = Path.Combine(Path.Combine(Path.Combine(assets, "FrontEnd"), "Campaign"), "Mission.cs.meta");
        if (!File.Exists(path) || !File.Exists(prefabMeta) || !File.Exists(missionMeta)) return list;
        string prefabGuid = Regex.Match(File.ReadAllText(prefabMeta), @"^guid: (\w+)", RegexOptions.Multiline).Groups[1].Value;
        string missionGuid = Regex.Match(File.ReadAllText(missionMeta), @"^guid: (\w+)", RegexOptions.Multiline).Groups[1].Value;
        // The prefab's Mission component (its fileID), found by the script it runs.
        string prefab = File.ReadAllText(prefabMeta.Substring(0, prefabMeta.Length - 5));
        Match mc = Regex.Match(prefab, @"--- !u!114 &(\d+)\nMonoBehaviour:\n(?:  .*\n)*?  m_Script: \{fileID: 11500000, guid: " + missionGuid);
        if (!mc.Success) return list;
        string missionId = mc.Groups[1].Value;
        // The prefab's root RectTransform (the one with no parent): where a flag's anchors are.
        Match rr = Regex.Match(prefab, @"--- !u!224 &(\d+)\nRectTransform:\n(?:  .*\n)*?  m_Father: \{fileID: 0\}");
        string rootRect = rr.Success ? rr.Groups[1].Value : "";
        string text = File.ReadAllText(path).Replace("\r\n", "\n");
        // A reference to another flag's Mission is to a stripped MonoBehaviour standing for it: which instance.
        Dictionary<string, string> strippedOf = new Dictionary<string, string>();
        foreach (Match st in Regex.Matches(text, @"--- !u!114 &(\d+) stripped\nMonoBehaviour:\n  m_CorrespondingSourceObject: \{fileID: " + missionId + @", guid: " + prefabGuid + @", type: 3\}\n  m_PrefabInstance: \{fileID: (\d+)\}"))
            strippedOf[st.Groups[1].Value] = st.Groups[2].Value;
        Dictionary<string, MissionInfo> byInstance = new Dictionary<string, MissionInfo>();
        Dictionary<MissionInfo, List<string>[]> refs = new Dictionary<MissionInfo, List<string>[]>();
        // Each prefab instance of the flag, read line by line (Unity may quote a property path or a value).
        foreach (string block in text.Split(new[] { "\n--- " }, StringSplitOptions.None)) {
            Match head = Regex.Match(block, @"^(?:--- )?!u!1001 &(\d+)\n");
            if (!head.Success || !block.Contains("m_SourcePrefab: {fileID: 100100000, guid: " + prefabGuid + ",")) continue;
            Dictionary<string, string> value = new Dictionary<string, string>(), reference = new Dictionary<string, string>();
            string[] lines = block.Split('\n');
            for (int li = 0; li + 3 < lines.Length; li++) {
                Match t = Regex.Match(lines[li], @"^    - target: \{fileID: (\d+),");
                if (!t.Success || !lines[li + 1].StartsWith("      propertyPath: ") || !lines[li + 2].StartsWith("      value:")) continue;
                // A value may run over several lines (Unity folds long or multi-line text): up to the objectReference line.
                int end = li + 3;
                while (end < lines.Length && !lines[end].StartsWith("      objectReference: ")) end++;
                if (end >= lines.Length) continue;
                List<string> raw = new List<string> { lines[li + 2].Substring("      value:".Length) };
                for (int k = li + 3; k < end; k++) raw.Add(lines[k]);
                string property = Scalar(lines[li + 1].Substring("      propertyPath: ".Length));
                string target = t.Groups[1].Value;
                // The flag's own Mission is keyed by its property alone, its root RectTransform as "root:", other parts by their file ID.
                string key = (target == missionId ? "" : target == rootRect ? "root:" : target + ":") + property;
                value[key] = Scalar(Fold(raw));
                reference[key] = Regex.Match(lines[end], @"fileID: (-?\d+)").Groups[1].Value;
                li = end - 1;
            }
            Match inst = head;
            string name = "";
            foreach (KeyValuePair<string, string> kv in value) if (kv.Key.EndsWith(":m_Name")) name = kv.Value;
            Func<string, string> v = k => { string x; return value.TryGetValue(k, out x) ? x : ""; };
            Func<string, int> n = k => { int x; int.TryParse(v(k), out x); return x; };
            MissionInfo info = new MissionInfo {
                key = MissionInfo.KeyOf(scene, name), campaignScene = scene, title = v("title"), scenario = v("scenario"),
                briefing = v("briefing"), objectives = v("objectives"), availableAtStart = v("availableAtStart") == "1",
                parTurns = n("parTurns"), parKills = n("parKills"), parLosses = n("parLosses"), factionName = reference.ContainsKey("faction") && reference["faction"] != "0" ? "(set)" : ""
            };
            List<string>[] links = { new List<string>(), new List<string>() };
            string[] fields = { "unlocks", "blocks" };
            for (int f = 0; f < 2; f++) for (int k = 0; k < n(fields[f] + ".Array.size"); k++) {
                string r; links[f].Add(reference.TryGetValue(fields[f] + ".Array.data[" + k + "]", out r) ? r : "0");
            }
            for (int k = 0; k < n("unlocksCampaigns.Array.size"); k++) info.unlocksCampaigns.Add(v("unlocksCampaigns.Array.data[" + k + "]"));
            refs[info] = links;
            byInstance[inst.Groups[1].Value] = info;
            float ax, ay;
            foreach (KeyValuePair<string, string> kv in value) if (kv.Key == "root:m_AnchorMin.x") { float.TryParse(kv.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out ax); anchors[info.key] = new Vector2(ax, anchors.ContainsKey(info.key) ? anchors[info.key].y : 0f); }
            foreach (KeyValuePair<string, string> kv in value) if (kv.Key == "root:m_AnchorMin.y") { float.TryParse(kv.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out ay); anchors[info.key] = new Vector2(anchors.ContainsKey(info.key) ? anchors[info.key].x : 0f, ay); }
            list.Add(info);
        }
        foreach (MissionInfo info in list) {
            List<string>[] links = refs[info];
            List<string>[] into = { info.unlocks, info.blocks };
            for (int f = 0; f < 2; f++) foreach (string r in links[f]) {
                string instance; MissionInfo to;
                // (a link to nothing, or to something not a flag of this scene, shows as "?" and fails the check)
                into[f].Add(strippedOf.TryGetValue(r, out instance) && byInstance.TryGetValue(instance, out to) ? to.key : "?");
            }
        }
        list.Sort((a, b) => string.CompareOrdinal(a.key, b.key));
        return list;
    }

    static bool Near(Vector2 a, Vector2 b) { return Math.Abs(a.x - b.x) < 0.01f && Math.Abs(a.y - b.y) < 0.01f; }

    /// <summary>
    /// A YAML scalar written over several lines, as one line again: a quoted scalar's line breaks fold to spaces, an
    /// empty line in it is a line break.
    /// </summary>
    static string Fold(List<string> raw) {
        if (raw.Count == 1) return raw[0];
        System.Text.StringBuilder b = new System.Text.StringBuilder(raw[0].Trim());
        bool broke = false;
        for (int i = 1; i < raw.Count; i++) {
            string l = raw[i].Trim();
            if (l.Length == 0) { b.Append("\n"); broke = true; continue; }
            if (!broke) b.Append(' ');
            b.Append(l);
            broke = false;
        }
        return b.ToString();
    }

    /// <summary>A YAML scalar as Unity writes one in an override: plain, or double-quoted with escapes.</summary>
    static string Scalar(string raw) {
        raw = raw.Trim();
        if (raw.Length >= 2 && raw[0] == '"' && raw[raw.Length - 1] == '"') {
            string body = raw.Substring(1, raw.Length - 2);
            System.Text.StringBuilder b = new System.Text.StringBuilder();
            for (int i = 0; i < body.Length; i++) {
                if (body[i] == '\\' && i + 1 < body.Length) { char c = body[++i]; b.Append(c == 'n' ? '\n' : c == 't' ? '\t' : c); }
                else b.Append(body[i]);
            }
            return b.ToString();
        }
        if (raw.Length >= 2 && raw[0] == '\'' && raw[raw.Length - 1] == '\'') return raw.Substring(1, raw.Length - 2).Replace("''", "'");
        return raw;
    }

    /// <summary>
    /// The campaign's rules on the real missions (the flags placed in the Campaign scene), with <paramref name="tally"/> (a won game of Tutorial 2) as
    /// the result: what is on the map at first, unlocking, stars against the pars, stars kept, a fork (a fixture: no
    /// real mission blocks another yet), and progress saved and read back.
    /// </summary>
    static bool CampaignCheck(MissionTally tally, out string detail) {
        Dictionary<string, Vector2> anchors;
        List<MissionInfo> missions = SceneMissions("Campaign", out anchors);
        MissionInfo t1 = missions.Find(m => m.Name == "Tutorial 1"), t2 = missions.Find(m => m.Name == "Tutorial 2");
        if (t1 == null || t2 == null) { detail = "the flags Tutorial 1 and Tutorial 2 were not found in the Campaign scene (" + missions.Count + " missions read)"; return false; }
        string file = Path.Combine(Path.GetTempPath(), "hextactica-campaign-" + Guid.NewGuid().ToString("N") + ".dat");
        string fileWas = CampaignProgress.FilePath;
        try {
            CampaignProgress.FilePath = file;
            CampaignProgress.Load();
            string scenarios = PrefabLoader.Root("Campaign/Scenarios");
            bool files = File.Exists(Path.Combine(scenarios, t1.ScenarioName + ".dat")) && File.Exists(Path.Combine(scenarios, t2.ScenarioName + ".dat"))
                      && !File.Exists(Path.Combine(PrefabLoader.Root("Scenarios"), t1.ScenarioName + ".dat")) && !File.Exists(Path.Combine(PrefabLoader.Root("Scenarios"), t2.ScenarioName + ".dat"));
            bool linked = t1.unlocks.Count == 1 && t1.unlocks[0] == t2.key && t2.unlocks.Count == 1 && t1.availableAtStart && !t2.availableAtStart
                       && t1.key == "Campaign/Tutorial 1" && t1.factionName != "" && t2.factionName != "" && t2.objectives.StartsWith("Take the middle city") && t2.scenario == "1.2 Combat Trials" && t1.parTurns == 8 && t2.parTurns == 10;
            // Act 1 down the chain: 1.1 (Tutorial 1) > 1.2 (Tutorial 2) > 1.3 > 1.4, each with its scenario in the campaign folder.
            MissionInfo m3 = missions.Find(m => m.Name == "1.3 Transit Trouble"), m4 = missions.Find(m => m.Name == "1.4 Live Fire"), m5 = missions.Find(m => m.Name == "1.5 Border Brawl");
            bool chain = m3 != null && m4 != null && t2.unlocks.Count == 1 && t2.unlocks[0] == m3.key && m3.unlocks.Count == 1 && m3.unlocks[0] == m4.key
                      && !m3.availableAtStart && !m4.availableAtStart && File.Exists(Path.Combine(scenarios, m3.ScenarioName + ".dat")) && File.Exists(Path.Combine(scenarios, m4.ScenarioName + ".dat"))
                      && m5 != null && m4.unlocks.Count == 1 && m4.unlocks[0] == m5.key && !m5.availableAtStart && File.Exists(Path.Combine(scenarios, m5.ScenarioName + ".dat"));
            bool noCampaign2 = missions.TrueForAll(m => m.unlocksCampaigns.Count == 0);
            // Each flag pinned to its spot on the picture (its anchors), apart from the other.
            bool placed = anchors.ContainsKey(t1.key) && anchors.ContainsKey(t2.key) && !Near(anchors[t1.key], anchors[t2.key])
                       && anchors[t1.key].x > 0f && anchors[t1.key].x < 1f && anchors[t2.key].y > 0f && anchors[t2.key].y < 1f;
            // The mission as a saved game keeps it (MatchSetup.StampMission), read back whole.
            MissionInfo back = MissionInfo.FromJson(new Defective.JSON.JSONObject(t2.ToJson().Print()));
            bool stamped = back != null && back.key == t2.key && back.campaignScene == "Campaign" && back.objectives == t2.objectives && back.briefing == t2.briefing
                        && back.parTurns == t2.parTurns && back.unlocks.Count == t2.unlocks.Count && back.availableAtStart == t2.availableAtStart;
            bool atFirst = CampaignProgress.Playable(t1) && !CampaignProgress.Visible(t2);
            // Tutorial 1 lost: attempted, nothing unlocked, no stars.
            CampaignProgress.Attempt(t1);
            CampaignProgress.Outcome lost = CampaignProgress.Finish(t1, new MissionTally { recorded = true, won = false, turns = 3, kills = 5, losses = 0 });
            bool lostNothing = lost.earned == MissionStars.None && lost.unlocked.Count == 0 && !CampaignProgress.Get(t1).completed && !CampaignProgress.Visible(t2);
            // Tutorial 1 won slowly, with a loss, its raider destroyed: one star (aggression), and Tutorial 2 appears.
            CampaignProgress.Outcome slow = CampaignProgress.Finish(t1, new MissionTally { recorded = true, won = true, turns = t1.parTurns + 1, kills = t1.parKills, losses = t1.parLosses + 1 });
            bool unlocked = slow.firstWin && slow.unlocked.Count == 1 && slow.unlocked[0] == t2.key && CampaignProgress.Playable(t2) && slow.earned == MissionStars.Aggression && slow.gained == MissionStars.Aggression
                         && CampaignProgress.Pending == slow && CampaignProgress.TakePending() == slow && CampaignProgress.Pending == null;
            // Won again within every par: the two new stars are gained, the old one kept, nothing unlocked twice.
            CampaignProgress.Outcome fast = CampaignProgress.Finish(t1, new MissionTally { recorded = true, won = true, turns = t1.parTurns, kills = t1.parKills, losses = t1.parLosses });
            bool kept = !fast.firstWin && fast.unlocked.Count == 0 && MissionRating.Count(fast.earned) == 3 && fast.gained == (MissionStars.Speed | MissionStars.Defense)
                     && MissionRating.Count(CampaignProgress.Get(t1).stars) == 3 && CampaignProgress.Get(t1).bestTurns == t1.parTurns;
            // A worse win later takes nothing away; a win that was not recorded earns no stars.
            CampaignProgress.Finish(t1, new MissionTally { recorded = false, won = true });
            bool notLost = MissionRating.Count(CampaignProgress.Get(t1).stars) == 3;
            // Tutorial 2, with the result of the game just played.
            bool closedFirst = !CampaignProgress.CampaignUnlocked("Campaign 2");
            // No Act 1 mission opens Campaign 2 yet (owner: not mission 2): the opening is checked on a copy of Tutorial 2's flag that does.
            bool noneInScene = t2.unlocksCampaigns.Count == 0 && t1.unlocksCampaigns.Count == 0;
            t2.unlocksCampaigns.Add("Campaign 2");
            CampaignProgress.Attempt(t2);
            CampaignProgress.Outcome played = CampaignProgress.Finish(t2, tally);
            MissionStars expected = MissionStars.None;
            if (tally.won && t2.parTurns > 0 && tally.turns <= t2.parTurns) expected |= MissionStars.Speed;
            if (tally.won && tally.kills >= t2.parKills) expected |= MissionStars.Aggression;
            if (tally.won && tally.losses <= t2.parLosses) expected |= MissionStars.Defense;
            bool rated = played.earned == expected && MissionRating.Count(played.earned) == 3 && CampaignProgress.Get(t2).completed;
            // A fork (fixture missions: the campaign has none yet): attempting one closes the other, for good unless it was already won.
            MissionInfo left = new MissionInfo { key = "Fixture/Left" }, right = new MissionInfo { key = "Fixture/Right" }, done = new MissionInfo { key = "Fixture/Done" };
            left.availableAtStart = right.availableAtStart = done.availableAtStart = true;
            left.blocks.Add(right.key); left.blocks.Add(done.key);
            CampaignProgress.Finish(done, new MissionTally { recorded = true, won = true });
            bool open = CampaignProgress.Playable(left) && CampaignProgress.Playable(right);
            CampaignProgress.Attempt(left);
            bool fork = open && CampaignProgress.Blocked(right) && !CampaignProgress.Playable(right) && CampaignProgress.Visible(right) && !CampaignProgress.Blocked(done) && CampaignProgress.Playable(left);
            // Down the chain, each win read back from the file before the next.
            bool down = true;
            foreach (MissionInfo[] step in new[] { new[] { t2, m3 }, new[] { m3, m4 }, new[] { m4, m5 } }) {
                if (step[0] == null || step[1] == null) { down = false; break; }
                CampaignProgress.Attempt(step[0]);
                CampaignProgress.Finish(step[0], new MissionTally { recorded = true, won = true, turns = 1 });
                CampaignProgress.Load();
                down &= CampaignProgress.Get(step[0]).completed && CampaignProgress.Playable(step[1]);
            }
            // Read back from its file.
            CampaignProgress.Load();
            bool savedCampaigns = CampaignProgress.CampaignUnlocked("Campaign 2");
            bool saved = savedCampaigns && File.Exists(file) && CampaignProgress.Get(t1).completed && MissionRating.Count(CampaignProgress.Get(t1).stars) == 3 && CampaignProgress.Get(t1).bestTurns == t1.parTurns
                      && CampaignProgress.Playable(t2) && CampaignProgress.Get(t2).stars == expected && CampaignProgress.Blocked(right) && CampaignProgress.Get(left).attempted;
            // Several campaigns: Tutorial 2 opens Campaign 2, a scene of its own the hub lists and loads (the scenes as they are).
            string scenes = Path.Combine(PrefabLoader.Root(".."), "Scenes");
            string hub = File.ReadAllText(Path.Combine(scenes, "Campaign.unity")).Replace("\r\n", "\n");
            string second = File.Exists(Path.Combine(scenes, "Campaign 2.unity")) ? File.ReadAllText(Path.Combine(scenes, "Campaign 2.unity")).Replace("\r\n", "\n") : "";
            string build = File.ReadAllText(Path.Combine(Path.Combine(Path.Combine(PrefabLoader.Root(".."), ".."), "ProjectSettings"), "EditorBuildSettings.asset"));
            string mapGuid = Regex.Match(File.ReadAllText(Path.Combine(Path.Combine(Path.Combine(PrefabLoader.Root(".."), "FrontEnd"), "Campaign"), "CampaignMap.cs.meta")), @"^guid: (\w+)", RegexOptions.Multiline).Groups[1].Value;
            Func<string, string> mapOf = scene => { Match cm = Regex.Match(scene, @"  m_Script: \{fileID: 11500000, guid: " + mapGuid + @", type: 3\}\n(?:  .*\n)*?  availableAtStart: (\d)\n  flags: \{fileID: (\d+)\}"); return cm.Success ? cm.Groups[1].Value + "/" + cm.Groups[2].Value : ""; };
            bool scenesWired = noneInScene
                            && Regex.IsMatch(hub, @"  campaigns:\n  - scene: Campaign 2\n    at: \{x: 1, y: 0\}") && mapOf(hub) == "1/733062130"
                            && mapOf(second).StartsWith("0/") && second.Contains("--- !u!224 &" + mapOf(second).Substring(2) + "\n")
                            && build.Contains("path: Assets/Scenes/Campaign 2.unity") && build.IndexOf("Scenes/Campaign.unity") < build.IndexOf("Scenes/Campaign 2.unity");
            bool opened = played.unlockedCampaigns.Count == 1 && played.unlockedCampaigns[0] == "Campaign 2" && CampaignProgress.CampaignUnlocked("Campaign 2");
            CampaignProgress.Outcome again = CampaignProgress.Finish(t2, tally);
            bool openedOnce = again.unlockedCampaigns.Count == 0 && CampaignProgress.CampaignUnlocked("Campaign 2");
            MissionInfo t2back = MissionInfo.FromJson(new Defective.JSON.JSONObject(t2.ToJson().Print()));
            bool campaignsKept = t2back != null && t2back.unlocksCampaigns.Count == 1 && t2back.unlocksCampaigns[0] == "Campaign 2";
            // The view over several maps (MapView.ClampBounds): two 1920x1080 maps side by side, the second at x = 2073.6 (gap 0.08).
            Vector2 vw = new Vector2(1920f, 1080f);
            Vector2 lo = new Vector2(-960f, -540f), hi = new Vector2(2073.6f + 960f, 540f);
            bool reachSecond = Near(MapView.ClampBounds(MapView.CentreOn(new Vector2(0f, 0f), new Vector2(2073.6f, 0f), 1f), lo, hi, 1f, vw), new Vector2(-2073.6f, 0f));   // the second map's middle can be centred
            bool stopsAtEdges = Near(MapView.ClampBounds(new Vector2(5000f, 0f), lo, hi, 1f, vw), new Vector2(0f, 0f)) && Near(MapView.ClampBounds(new Vector2(-9000f, 0f), lo, hi, 1f, vw), new Vector2(-2073.6f, 0f));
            bool oneMapPinned = Near(MapView.ClampBounds(new Vector2(300f, -200f), new Vector2(-960f, -540f), new Vector2(960f, 540f), 1f, vw), new Vector2(0f, 0f));   // one map, unzoomed: no room
            bool campaignsOk = scenesWired && closedFirst && opened && openedOnce && campaignsKept && reachSecond && stopsAtEdges && oneMapPinned;
            // The map's pan and zoom (MapView): a 1920x1080 map in a view its size.
            Vector2 view = new Vector2(1920f, 1080f);
            bool pinned = Near(MapView.Clamp(new Vector2(300f, -200f), new Vector2(0f, 0f), view, 1f, view), new Vector2(0f, 0f));              // unzoomed: no room to pan
            Vector2 edge = MapView.Clamp(new Vector2(5000f, -5000f), new Vector2(0f, 0f), view, 2f, view);                         // at 2x: half a view each way
            bool panRoom = Near(edge, new Vector2(960f, -540f));
            Vector2 cursor = new Vector2(400f, 100f);                                                                         // a point on the map, from its middle
            Vector2 zoomed = MapView.ZoomAbout(new Vector2(0f, 0f), cursor, 1f, 2f);
            bool aboutCursor = Near(zoomed + cursor * 2f, cursor);                                                                 // still under the cursor
            Vector2 centred = MapView.CentreOn(new Vector2(0f, 0f), MapView.Local(new Vector2(0.75f, 0.25f), new Vector2(0.5f, 0.5f), view), 2f);
            bool centres = Near(centred + MapView.Local(new Vector2(0.75f, 0.25f), new Vector2(0.5f, 0.5f), view) * 2f, new Vector2(0f, 0f));
            bool mapView = pinned && panRoom && aboutCursor && centres;
            CampaignProgress.Reset();
            bool reset = !CampaignProgress.Visible(t2) && !CampaignProgress.Get(t1).completed && CampaignProgress.Playable(t1) && !CampaignProgress.CampaignUnlocked("Campaign 2");
            detail = "mission files in the campaign folder, not among the scenarios " + files + "; the flags in the Campaign scene, Tutorial 1 unlocking Tutorial 2 " + linked + ", each pinned to its spot " + placed + ", kept whole by a save " + stamped + "; only Tutorial 1 on the map at first " + atFirst
                   + "; lost: nothing " + lostNothing + "; won: one star, Tutorial 2 unlocked, handed to the campaign screen once " + unlocked + "; won again: stars added, kept " + kept + ", not lost by a worse win " + notLost
                   + "; this game against Tutorial 2's pars (" + t2.parTurns + " turns, " + t2.parKills + " killed, " + t2.parLosses + " lost): " + MissionRating.Count(played.earned) + " stars " + rated
                   + "; a fork closes the other mission, not one already won " + fork + "; saved and read back " + saved + "; reset " + reset
                   + "; campaigns: Tutorial 2 opens Campaign 2, listed by the hub, its map closed at first, in the build " + scenesWired + ", closed until won " + closedFirst + ", opened by the win " + opened + ", once " + openedOnce + ", kept by a save " + campaignsKept
                   + ", the view reaches the second map " + reachSecond + ", stops at the outer edges " + stopsAtEdges + ", one map pinned " + oneMapPinned
                   + "; map pan and zoom: pinned unzoomed " + pinned + ", room at 2x " + panRoom + ", zooms about the cursor " + aboutCursor + ", centres a mission " + centres;
            detail += "; Act 1 linked 1.1 > 1.2 > 1.3 > 1.4 > 1.5 " + chain + ", won down the chain, each read back from the file " + down + ", no Act 1 mission opens Campaign 2 " + noCampaign2;
            return files && linked && placed && stamped && campaignsOk && atFirst && lostNothing && unlocked && kept && notLost && rated && fork && saved && reset && mapView && chain && down && noCampaign2;
        } finally {
            CampaignProgress.FilePath = fileWas;
            CampaignProgress.Pending = null;
            CampaignProgress.Load();
            try { File.Delete(file); } catch (Exception) { }
        }
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
                // A unit still walking (an animation): triggers wait until the board is still (Wait For Animations).
                // (Headless boards are not waited on, as nothing plays there: played as the game would be, for these two frames.)
                g.path.Add(g.tile);
                gc.headless = false;
                s.Tick(0.1f);
                HexTile.NotifyUnitSelected(g);   // (clicked while it walks)
                s.Tick(0.1f);
                gc.headless = true;
                int boardEarly = Fired(s, "Board"), startEarly = Fired(s, "Start"), clickedEarly = Fired(s, "Clicked");
                bool waitsForAnimation = boardEarly == 0 && startEarly == 1 && clickedEarly == 1;
                g.path.Clear();
                for (int i = 0; i < 5; i++) s.Tick(0.1f);
                bool once = a.money == 107 && Fired(s, "Start") == 1 && Fired(s, "Board") == 1 && waitsForAnimation;
                o.money = 0;
                MoveTo(g, 0, 0); s.Tick(0.1f);                                    // held from Blue's turn 0
                gc.EndTurn(); s.Tick(0.1f); gc.EndTurn(); s.Tick(0.1f);           // Blue's turn 1 starts: held 1 turn
                bool notYet = Fired(s, "Hold") == 0;
                MoveTo(g, -1, 0); s.Tick(0.1f); MoveTo(g, 0, 0); s.Tick(0.1f);   // broken, held again from turn 1
                gc.EndTurn(); s.Tick(0.1f); gc.EndTurn(); s.Tick(0.1f);           // turn 2: 1 turn since
                bool restarted = Fired(s, "Hold") == 0;
                gc.EndTurn(); s.Tick(0.1f); gc.EndTurn(); s.Tick(0.1f);           // turn 3: 2 turns since
                bool held = Fired(s, "Hold") == 1 && a.money == 108;
                bool repeat = Fired(s, "Every Blue turn") == 3 && Fired(s, "Second Blue turn") == 1 && o.money == 1030;
                detail = "waits while a unit animates " + waitsForAnimation + " (board " + boardEarly + ", start " + startEarly + ", clicked " + clickedEarly + "); Level Start once " + once + " (Blue " + a.money + "); Delay 2 turns: fired after 1 turn " + !notYet + ", count started over after a break " + restarted + ", fired once held 2 of Blue's turns " + held
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
                bool rules = !gc.purchasesAllowed && gc.fogOfWar;
                s.Tick(1f); s.Tick(0.6f);   // "Then": After "Do it", held 1.5 s
                bool repaired = Rig.At(2, 2).terrain.name == "Forest" && !redCity.Destroyed && grunt.Strength <= 0 && Rig.At(0, 0).occupier == null && s.markers.Count == 0;
                bool notYet = !gc.gameOver;
                s.Tick(0.5f); bool stillWaiting = !gc.gameOver;
                s.Tick(0.6f);
                bool won = gc.gameOver && gc.winner == a;
                detail = "spawned " + spawned.Count + " tagged grunts (strength 3, veterancy 2, spent, beside the taken tile) " + spawn + "; tank and neutral city given, tank out of ammunition " + given
                       + "; forest destroyed " + ground + " (now " + Rig.At(2, 2).terrain.name + "); Red's city destroyed " + cityDown + "; marker shown " + marker + "; purchases off, fog on " + rules + "; after 1.5 s repaired, grunt destroyed, marker hidden " + repaired
                       + "; Win waits its Wait " + (notYet && stillWaiting) + ", then Blue wins " + won;
                return spawn && given && ground && cityDown && marker && rules && repaired && notYet && stillWaiting && won;
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
                bool done = !GameController.Paused && !GameController.ScriptHold && o.money == 12 && box.closed == 2;
                // No box: lines pass at once.
                ScenarioDialogue.Presenter = null;
                int last = ScenarioDialogue.Enqueue(new List<DialogueLine> { new DialogueLine { Text = "nobody sees this" } });
                bool passes = ScenarioDialogue.Closed(last) && !GameController.Paused;
                detail = "first line shown, game held, next action waiting " + held + "; second " + second + "; the other trigger's line after them, the first trigger's action run " + third
                       + "; closed: game goes on, both actions run (Red " + o.money + "), box closed after each trigger's scene " + done + "; with no box lines pass " + passes;
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
            case "scriptEliminable": {
                a.eliminable = false;
                Building hq = null; foreach (Building b in Building.all) if (b != null && b.IsHQ && b.owner == a) hq = b;
                hq.ChangeOwner(o);   // Blue's HQ taken
                gc.CheckVictory();
                bool kept = !a.eliminated && !gc.gameOver && a.units.Count == 0 && !a.HasHQ;
                a.eliminable = true;
                gc.CheckVictory();
                bool out_ = a.eliminated && gc.gameOver && gc.winner == o;
                detail = "not eliminable, without HQ or units: still in, game on " + kept + "; eliminable: knocked out, Red wins " + out_;
                return kept && out_;
            }
            case "scriptMoveProposed": {
                Unit blue = Rig.At(0, 0).occupier, red = Rig.At(3, 0).occupier;
                s.Tick(0.1f);
                bool quiet = Fired(s, "Next to Red") == 0 && Fired(s, "Any two") == 0;
                // (The board's click flow needs the tiles' overlays, which the headless board lacks: the proposal is
                // notified as ClickBoard notifies it, on the first click on a tile the selected unit can reach.)
                HexTile.NotifyMoveProposed(blue, Rig.At(0, 1)); s.Tick(0.1f);
                bool elsewhere = Fired(s, "Next to Red") == 0 && Fired(s, "Any two") == 0;
                HexTile.NotifyMoveProposed(blue, Rig.At(2, 0)); s.Tick(0.1f);
                bool nextTo = Fired(s, "Next to Red") == 1 && Fired(s, "Any two") == 1;
                bool notMoved = blue.tile == Rig.At(0, 0) && Fired(s, "Arrived") == 0;
                HexTile.NotifyMoveProposed(red, Rig.At(2, 0)); s.Tick(0.1f);
                bool sideKept = Fired(s, "Red proposes") == 1 && Fired(s, "Next to Red") == 1;
                MoveTo(blue, 2, 0); s.Tick(0.1f);
                bool arrived = Fired(s, "Arrived") == 1 && Fired(s, "Next to Red") == 1;
                detail = "nothing at start " + quiet + "; a proposal elsewhere: not the area, 1 of 2 " + elsewhere + "; proposed next to Red: area and second proposal " + nextTo
                       + "; nothing moved, not entered " + notMoved + "; Red's proposal counts only for Red " + sideKept + "; the move made: entered, area not again " + arrived;
                return quiet && elsewhere && nextTo && notMoved && sideKept && arrived;
            }
            case "scriptPlaceInspect": {
                Unit blue = Rig.At(0, 0).occupier, red = Rig.At(3, 0).occupier;
                int blueCount = a.units.Count;
                blue.active = false;
                s.Tick(0.1f);
                bool quiet = Fired(s, "Red inspected") == 0 && Fired(s, "Blue inspected") == 0;
                HexTile.NotifyEnemyInspected(red); s.Tick(0.1f);
                bool inspected = Fired(s, "Red inspected") == 1 && Fired(s, "Blue inspected") == 0;
                bool placed = blue.tile == Rig.At(-2, 0) && Rig.At(-2, 0).occupier == blue && Rig.At(0, 0).occupier == null && blue.active && a.units.Count == blueCount && blue.Strength > 0;
                HexTile.NotifyEnemyInspected(red); s.Tick(0.1f);
                bool crowded = Fired(s, "Crowd") == 1 && blue.tile != Rig.At(3, 0) && red.tile == Rig.At(3, 0) && blue.tile.Distance(Rig.At(3, 0)) == 1 && blue.tile.occupier == blue && !blue.active;
                detail = "nothing at start " + quiet + "; Red inspected, not Blue " + inspected + "; placed on (-2,0), ready, old tile empty, nobody lost " + placed + "; placed onto a taken tile: the nearest free one, spent " + crowded;
                return quiet && inspected && placed && crowded;
            }
            case "scriptSubjectAmbush": {
                Unit near = Rig.At(-2, 0).occupier, tree = Rig.At(3, 0).occupier, tank = Rig.At(4, -1).occupier;
                s.Tick(0.1f);
                bool quiet = Fired(s, "Came near") == 0 && tank != null && near != null && tree != null;
                ScenarioGame.Order(near, null, Rig.At(-1, 0));
                for (int i = 0; i < 5; i++) s.Tick(0.1f);
                bool subject = Fired(s, "Came near") == 1 && s.Find("Came near").Subject == near && near.Strength < 6;
                bool hunted = Fired(s, "Hunt") == 1 && tree.Strength < 6 && tank.Strength < 6;
                bool ambush = Fired(s, "Red ambush") == 1 && Fired(s, "Blue ambush") == 0 && s.Find("Red ambush").Subject == tree;
                // Mark Reach: the forests the selected grunt reaches this turn (it may move again: made ready).
                near.active = true;
                HexTile.NotifyUnitSelected(near); s.Tick(0.1f);
                List<ShownMarker> marks = s.markers.FindAll(m => m.Id.StartsWith("reach:"));
                bool allForest = marks.Count > 0 && marks.TrueForAll(m => Rig.At(m.TileX, m.TileY).terrain.cover >= 2);
                bool inReach = marks.Exists(m => m.TileX == -3 && m.TileY == 2) && !marks.Exists(m => m.TileX == -6 && m.TileY == 2);
                HexTile.NotifyUnitSelected(near); s.Tick(0.1f);
                bool afresh = s.markers.FindAll(m => m.Id.StartsWith("reach:")).Count == marks.Count && Fired(s, "Reach") == 2;
                s.Find("Reach").Actions.RemoveAt(1); s.Find("Reach").Repeat = false;   // (only the hiding left)
                s.Find("Reach").TimesFired = 0;
                HexTile.NotifyUnitSelected(near); s.Tick(0.1f);
                bool hidden = !s.markers.Exists(m => m.Id.StartsWith("reach:"));
                // Attack Proposed: every preview, or only those the defender would answer first.
                HexTile.NotifyAttackProposed(near, tank, false); s.Tick(0.1f);
                bool plain = Fired(s, "Any preview") == 1 && Fired(s, "Ambush preview") == 0;
                HexTile.NotifyAttackProposed(near, tank, true); s.Tick(0.1f);
                bool flagged = Fired(s, "Ambush preview") == 1;
                // Out of sight: with fog on, not on (-2,1) (Red sees it) but on a tile Red cannot see.
                gc.fogOfWar = true; FogOfWar.Reset(); FogOfWar.Changed(); FogOfWar.Refresh();
                bool askedSeen = FogOfWar.TileVisible(o, Rig.At(-2, 1));
                ScenarioTrigger sh = s.Find("Spawn hidden"); sh.Enabled = true;
                s.Tick(0.1f);
                Unit hid = ScenarioGame.Named("Hidden");
                FogOfWar.Changed(); FogOfWar.Refresh();
                bool outOfSight = hid != null && askedSeen && hid.tile != Rig.At(-2, 1) && !FogOfWar.TileVisible(o, hid.tile);
                gc.fogOfWar = false; FogOfWar.Reset(); FogOfWar.Refresh();
                detail = "start " + quiet + "; the tank went for {unit} (the grunt that came near, to " + near.Strength + ") " + subject + "; then for the grunt in the forest " + hunted + " (it " + tree.Strength + ", tank " + tank.Strength + "), ambush counted for Red with that grunt " + ambush
                       + "; marks: " + marks.Count + " forest tiles " + allForest + ", the near forest in, the far one out " + inReach + ", again afresh " + afresh + ", 'reach*' hides them " + hidden
                       + "; previews: any " + plain + ", ambush only " + flagged + "; spawned out of sight " + outOfSight + (hid != null ? " at " + hid.tile.x + "," + hid.tile.y : "");
                return quiet && subject && hunted && ambush && allForest && inReach && afresh && hidden && plain && flagged && outOfSight;
            }
            case "scriptOrderTargets": {
                Unit onCity = Rig.At(0, 0).occupier, inTrees = Rig.At(-2, 2).occupier, onGrass = Rig.At(2, -2).occupier, sup = Rig.At(3, -3).occupier, tank2 = Rig.At(4, -1).occupier;
                s.Tick(0.1f); s.Tick(0.1f);
                bool city = onCity.Strength < 6 && onGrass.Strength == 6;
                bool exposedCount = ScenarioGame.CountUnits(new UnitFilter { side = Red, kind = "Grunt" }, u => u.RealTile.terrain.cover < 2) >= 1;
                bool exposedMet = s.IsMet(s.Find("Exposed"), 0) == (ScenarioGame.CountUnits(new UnitFilter { side = Red, kind = "Grunt" }, u => u.RealTile.terrain.cover < 2) >= 2);
                Unit pick = ScenarioGame.NearestExposed(tank2, Red, 2);
                bool exposed = pick == onGrass && pick != sup;
                detail = "the tank went for the grunt on the city " + city + " (city " + onCity.Strength + ", grass " + onGrass.Strength + "); Units Exposed as counted " + exposedMet + " (" + exposedCount + "); Target Exposed picks the grunt on the grass, not the supply unit " + exposed + " (" + (pick != null ? pick.name : "none") + ")";
                return city && exposedMet && exposed;
            }
            case "scriptTerrainMovedFresh": {
                Unit walker = Rig.At(-3, 0).occupier, hurt = Rig.At(2, -1).occupier, fresh = Rig.At(3, -2).occupier, tank = Rig.At(5, -2).occupier;
                hurt.Strength = 3;
                s.Tick(0.1f);
                bool salute = Fired(s, "Salute") == 1 && hurt.Strength == 3 && fresh.Strength == 6 && walker.Strength == 6;
                ScenarioGame.Order(walker, null, Rig.At(-2, 0)); walker.active = true; s.Tick(0.1f);
                bool hills = Fired(s, "Onto hills") == 1 && Fired(s, "Moved") == 0 && s.Find("Moved").Events == 1 && s.Find("Onto road or forest").Events == 0;
                ScenarioGame.Order(walker, null, Rig.At(-3, 1)); s.Tick(0.1f); s.Tick(0.1f);
                bool road = s.Find("Onto road or forest").Events == 1 && Fired(s, "Moved") == 1 && Fired(s, "Fresh") == 1;
                bool freshHit = fresh.Strength < 6 && hurt.Strength == 3 && Fired(s, "Hit in cover") == 1 && Fired(s, "Hit in the open") == 0;
                hurt.Strength = 2; s.Tick(0.1f);
                bool openHit = Fired(s, "Hit in the open") == 1;
                detail = "salute hits nobody " + salute + "; onto hills: On Terrain Hills, one move counted " + hills + "; onto the road: 'Road, Forest' one of two, two moves, the fresh order " + road
                       + "; the tank went for the full-strength grunt in the forest, not the hurt one nearer " + freshHit + " (fresh " + fresh.Strength + ", hurt " + hurt.Strength + "); hit on grass: Cover Below 2 " + openHit;
                return salute && hills && road && freshHit && openHit;
            }
            case "scriptNewFunctions": {
                FakeBox box = new FakeBox();
                ScenarioDialogue.Presenter = box;
                Unit victim = Rig.At(0, 0).occupier, starver = Rig.At(3, 0).occupier, far = Rig.At(-6, 6).occupier, tank = Rig.At(1, 0).occupier;
                Unit walker = Rig.At(4, 2).occupier, truck = Rig.At(-2, 2).occupier, rider = Rig.At(-2, 3).occupier;
                walker.personal_name = "Walker"; rider.personal_name = "Rider";
                Action settle = () => { for (int i = 0; i < 10; i++) s.Tick(0.2f); };
                List<string> r = new List<string>();
                gc.fogOfWar = true; FogOfWar.Reset(); FogOfWar.Changed(); FogOfWar.Refresh();
                // Briefing: the marker, not the line; never in play.
                s.Brief();
                bool brief = s.markers.Exists(m => m.Id == "brief") && box.shown.Count == 0;
                s.markers.Clear();
                gc.activeUnit = walker;   // selected before the trigger waiting for it is switched on
                Vector3 camAt = new Vector3(-999f, 0f, 0f); float camZoom = 0f;
                ScenarioGame.CameraFocusHook = (at, zoom, sec) => { camAt = at; camZoom = zoom; };
                string lit = "";
                ScenarioGame.HighlightUIHook = (what, id, on) => { if (on) lit = what; };
                s.Tick(0.1f);
                bool first = box.shown.Count == 1 && box.shown[0] == "First." && ScenarioDialogue.More && ScenarioScript.HoldsPlay;
                ScenarioDialogue.Next(); s.Tick(0.1f);
                bool waiting = ScenarioScript.HoldsPlay;
                s.Tick(0.6f); s.Tick(0.1f);
                bool last = box.shown.Count == 2 && box.shown[1] == "Last." && !ScenarioDialogue.More;
                ScenarioDialogue.Next(); settle();
                bool released = !ScenarioScript.HoldsPlay;
                r.Add("Next then Close " + first + "/" + last + "; the AI held while the scene runs " + waiting + ", let go after " + released);
                bool caught = Fired(s, "Caught up") == 1 && Fired(s, "Missed") == 0;
                r.Add("a click made before the trigger was on counted " + caught);
                bool stars = s.BonusStars == 1 && s.BonusNotes.Contains("destroyed") && s.BonusNotes.Contains("behind");
                MissionInfo mi = new MissionInfo { parTurns = 5, parKills = 1, parLosses = 0 };
                MissionStars all = MissionRating.Stars(mi, new MissionTally { recorded = true, won = true, turns = 3, kills = 2, losses = 0, bonus = -1 });
                MissionStars two = MissionRating.Stars(mi, new MissionTally { recorded = true, won = true, turns = 9, kills = 0, losses = 0, bonus = 1 });
                bool rating = MissionRating.Count(all) == 2 && !MissionRating.Has(all, MissionStars.Defense) && MissionRating.Count(two) == 2 && MissionRating.Has(two, MissionStars.Speed);
                r.Add("bonus stars " + s.BonusStars + " (" + s.BonusNotes + ") " + stars + "; -1 from 3 leaves 2, +1 fills the first missing " + rating);
                FogOfWar.Changed(); FogOfWar.Refresh();
                HexTile poi = Rig.At(-6, 6), beside = Rig.At(-5, 6);
                bool shown = FogOfWar.TileVisible(a, poi) && FogOfWar.TileVisible(a, beside) && FogOfWar.Sees(a, far);
                gc.EndTurn(); settle(); FogOfWar.Changed(); FogOfWar.Refresh();
                bool kept = FogOfWar.TileVisible(a, poi);
                r.Add("the highlight asked for '" + lit + "'; revealed through the fog " + shown + ", still after the turn " + kept);
                // Only units in sight: the far grunt is no longer revealed once hidden (Blue's second turn over).
                Unit.Perishing = false;
                while (victim.Strength > 0) tank.AttackActual(victim, false);
                settle();
                bool cam = Fired(s, "Shot") == 1 && Mathf.Abs(camAt.x - ScenarioGame.TilePosition(0, 0).x) < 0.01f && Mathf.Abs(camAt.y - ScenarioGame.TilePosition(0, 0).y) < 0.01f && camZoom == 17f;
                starver.Perish(); settle();
                bool how = Fired(s, "Shot") == 1 && Fired(s, "Starved") == 1 && Fired(s, "Either") == 1;
                r.Add("killed in action / starved told apart " + how + "; the camera on the dead unit's tile " + cam);
                gc.EndTurn(); settle(); gc.EndTurn(); settle(); FogOfWar.Changed(); FogOfWar.Refresh(); settle();
                bool hidden = !FogOfWar.TileVisible(a, poi) && Fired(s, "Hide") == 1;
                bool seen = Fired(s, "None in sight") == 1 && Fired(s, "None left") == 0 && !FogOfWar.Sees(a, far);
                r.Add("revealed ground hidden by name " + hidden + "; none in sight (one left, unseen) " + seen);
                // Moves: anywhere but the mark, then onto it.
                ScenarioGame.Order(walker, null, Rig.At(4, 1)); settle();
                bool off = Fired(s, "Off the mark") == 1 && Fired(s, "On the mark") == 0;
                int walked = s.Find("Walker moves").Events;
                ScenarioGame.Place(new List<Unit> { walker }, 2, 3, true); settle();
                bool lifted = walked == 1 && s.Find("Walker moves").Events == 1 && walker.tile == Rig.At(2, 3);
                r.Add("lifted by Place Units is not a move " + lifted);
                r.Add("moved off the mark counted, not on it " + off);
                // Building Damaged at half.
                Building city = Rig.At(-3, 0).building;
                city.Damage(city.health / 2 - 1); settle();
                bool notYet = Fired(s, "Half wrecked") == 0;
                city.Damage(1); settle();
                bool wrecked = notYet && Fired(s, "Half wrecked") == 1;
                r.Add("half wrecked at " + city.damage + "/" + city.health + " " + wrecked);
                // Unload Proposed: the rider boards, is picked in the panel.
                List<HexTile> steps = HexTile.BoardingPath(rider, truck);
                bool aboard = steps != null && rider.WalkAndBoard(steps, truck, true);
                rider.FinishActive(); settle();
                TransportMenu.Open(truck); TransportMenu.NotifyCargoPicked(rider); settle();   // (PickCargo marks tiles: not in the rig)
                bool picked = aboard && Fired(s, "Picked") == 1;
                // Riding is not moving: the truck drives with it aboard.
                truck.active = true; ScenarioGame.Order(truck, null, Rig.At(-4, 2)); settle();
                bool rode = truck.tile == Rig.At(-4, 2) && Fired(s, "Rider moved") == 0;
                r.Add("boarding and riding are not Unit Moved " + rode);
                TransportMenu.Close();
                r.Add("picked in the panel " + picked);
                // Unit Cut Off under a move's preview: the real reading kept.
                ScenarioTrigger cut = s.Find("Walker cut"); cut.Enabled = true;
                bool real = s.IsMet(cut, 0);
                SupplyNetwork.Previewing = true;
                MoveTo(walker, 6, -6);
                bool keptReading = s.IsMet(cut, 0) == real;
                SupplyNetwork.Previewing = false;
                r.Add("cut off read as it really is under a preview " + keptReading);
                bool never = Fired(s, "Brief") == 0;
                r.Add("briefing: marker shown, line not said " + brief + ", never fired in play " + never);
                ScenarioGame.CameraFocusHook = null; ScenarioGame.HighlightUIHook = null;
                detail = string.Join("; ", r.ToArray());
                return brief && first && last && waiting && released && caught && stars && rating && lit == "TransportPanel" && shown && kept && how && cam && hidden && seen && off && wrecked && picked && rode && lifted && keptReading && never;
            }
            case "scriptNewFunctions2": {
                Unit victim = Rig.At(0, 0).occupier, tank = Rig.At(2, 0).occupier, rider = Rig.At(-3, 3).occupier, lorry = Rig.At(-4, 3).occupier;
                Unit other = Rig.At(4, -2).occupier, ambusher = Rig.At(6, -2).occupier;
                rider.personal_name = "Rider"; lorry.personal_name = "Lorry"; ambusher.personal_name = "Ambusher";
                Unit hiker = Rig.At(-6, 0).occupier, p1 = Rig.At(6, 1).occupier, p2 = Rig.At(7, 0).occupier, sup = Rig.At(-2, 5).occupier;
                Unit far = Rig.At(-1, 6).occupier; far.personal_name = "Far";
                hiker.personal_name = "Hiker"; p1.personal_name = "Pair"; p2.personal_name = "Pair";
                HexTile safe = Rig.At(-2, 0); safe.terrain = T("Forest");
                Action settle = () => { for (int i = 0; i < 6; i++) s.Tick(0.2f); };
                settle();
                List<string> r = new List<string>();
                safe.Damage(99);
                bool protectedOk = safe.terrain == T("Forest") && safe.damage == 0;
                bool boarded = rider.trasnporter == lorry;
                bool fromTile = ambusher.tile == Rig.At(5, -3) && other.Strength < 6;
                s.Find("Forced").Enabled = true; settle();
                bool forced = hiker.tile == Rig.At(6, -6);
                bool farBoarded = far.trasnporter == lorry;
                bool apart = p1.tile != p2.tile && p1.tile != null && p2.tile != null;
                bool placed = Rig.At(0, 4).building != null;
                s.Find("Unplaced").Enabled = true; settle();
                bool unplaced = Rig.At(0, 4).building == null && !Building.all.Exists(b => b != null && b.tile == Rig.At(0, 4));
                bool gone = sup == null || sup.RealStrength <= 0;
                bool notKilled = Fired(s, "Supply gone") == 0;
                bool armed = Fired(s, "Two armed") == 0;   // (Red: three armed grunts, one supply unit)
                r.Add("forced the whole way (12 tiles) " + forced + "; boarded a truck 6 tiles off (Force) " + farBoarded + "; two ordered at one target on different tiles " + apart + " (" + (p1.tile != null ? p1.tile.x + "," + p1.tile.y : "-") + " / " + (p2.tile != null ? p2.tile.x + "," + p2.tile.y : "-") + "); building placed " + placed + ", taken away again (Place Building None) " + unplaced + "; withdrawn, not killed " + (gone && notKilled) + "; armed only " + armed);
                r.Add("protected ground undamaged " + protectedOk + "; boarded the named truck " + boarded + "; attacked from the set tile " + fromTile + " (at " + (ambusher.tile != null ? ambusher.tile.x + "," + ambusher.tile.y : "-") + ", target " + other.Strength + ")");
                Unit gun = Rig.At(0, -4).occupier;
                gc.fogOfWar = true; FogOfWar.Reset(); FogOfWar.Changed(); FogOfWar.Refresh(); settle();
                bool unseenFirst = !FogOfWar.Sees(o, gun);
                gun.active = true; ScenarioGame.Order(gun, victim, null); settle();
                bool fog = unseenFirst && Fired(s, "From the fog") == 1;
                r.Add("artillery firing from the fog counted " + fog);
                gc.fogOfWar = false; FogOfWar.Changed(); FogOfWar.Refresh();
                int guard = 0;
                while (victim.RealStrength > 0 && guard++ < 10) { tank.active = true; ScenarioGame.Order(tank, victim, null); settle(); }
                bool by = Fired(s, "Killed by Blue") == 1 && Fired(s, "Killed by Red") == 0;
                r.Add("killed by Blue told " + by + " (" + guard + " attacks)");
                gc.alliances = true;
                bool hostileBefore = Player.Hostile(a, o);
                gc.EndTurn(); settle(); gc.EndTurn(); settle();
                bool allied = hostileBefore && !Player.Hostile(a, o) && a.team == 4 && o.team == 4;
                r.Add("set team: hostile before " + hostileBefore + ", allies after " + allied);
                detail = string.Join("; ", r.ToArray());
                return protectedOk && boarded && fromTile && by && allied && forced && farBoarded && apart && placed && unplaced && gone && notKilled && armed && fog;
            }
            case "scriptAIObjective": {
                foreach (MonoBehaviour m in a.GetComponents<MonoBehaviour>()) if (m is IAIController) m.enabled = true;
                AIPlayer ai = a.GetComponent<AIPlayer>();
                s.Tick(0.1f); s.Tick(0.1f);
                bool set = ai.objective is ScenarioAIGoal && ((ScenarioAIGoal)ai.objective).target == Rig.At(-3, 0);
                HexTile city = Rig.At(-3, 0);
                for (int turn = 0; turn < 2; turn++) {
                    gc.activePlayer = a;
                    int guard = 0; bool more = true;
                    while (more && gc.activePlayer == a && guard++ < 200) more = ai.StepOnce(true);
                    foreach (Unit u in a.units) if (u != null) u.active = true;
                    s.Tick(0.1f);
                }
                List<string> at = new List<string>(); HashSet<HexTile> tiles = new HashSet<HexTile>(); int near = 0;
                foreach (Unit u in a.units) if (u != null && u.tile != null) { at.Add(u.tile.x + "," + u.tile.y); tiles.Add(u.tile); if (u.tile.Distance(city) <= 2) near++; }
                detail = "objective set on the AI " + set + "; Blue's grunts at " + string.Join(" ", at.ToArray()) + ", within 2 of the city " + near + " of " + at.Count;
                return set && near >= 2 && tiles.Count == at.Count;
            }
            case "scriptBystander": {
                Player range = Rig.AddPlayer("Range", 200);
                range.bystander = true;
                Unit gun = Put("Artillery", range, -3, 0), red = Rig.At(0, 0).occupier, blue = Rig.At(2, 0).occupier;
                gun.active = true;
                bool fights = !red.CanAttack(gun) && !gun.CanAttack(red) && !blue.CanAttack(gun) && red.CanAttack(blue)
                              && !Player.Hostile(range, o) && !Player.Hostile(a, range) && Player.Hostile(a, o) && !FogOfWar.IsHuman(range)
                              && !ScenarioGame.SideIs(range, TriggerSide.Human) && !ScenarioGame.SideIs(range, TriggerSide.AI);
                s.Tick(0.1f);
                bool notHuman = !s.IsMet(s.Find("Humans"), 0);
                // Turn order: whoever comes after Blue, the range is skipped.
                List<string> order = new List<string>();
                for (int i = 0; i < 5; i++) { gc.EndTurn(); s.Tick(0.1f); order.Add(gc.activePlayer.personal_name); }
                bool skipped = !order.Contains("Range");
                // Blue knocked out with the range's guns still standing: Red wins as usual.
                a.eliminated = true;   // (Blue out, however: the range's guns are still on the board)
                gc.CheckVictory();
                bool won = gc.gameOver && gc.winner == o;
                detail = "";
                // Given to Red, the gun fights again.
                ScenarioGame.GiveUnits(new List<Unit> { gun }, o);
                bool given = gun.owner == o && !gun.owner.bystander;
                detail += "nobody fights its units, they fight nobody, nobody's enemy, not Human or AI " + fights + "; a Human filter leaves it out " + notHuman + "; turns: " + string.Join(", ", order.ToArray()) + ", never the range " + skipped
                       + "; Red wins with the range's guns on the board " + won + "; given to Red: Red's " + given;
                return fights && notHuman && skipped && won && given;
            }
            case "scriptOneScene": {
                FakeBox box = new FakeBox();
                ScenarioDialogue.Presenter = box;
                a.money = 0; o.money = 0;
                s.Tick(0.1f);
                bool early = o.money == 5 && box.shown.Count == 1 && box.shown[0] == "A1.";
                ScenarioDialogue.Next(); s.Tick(0.1f); s.Tick(0.1f);
                bool second = box.shown.Count == 2 && box.shown[1] == "A2.";
                ScenarioDialogue.Next(); s.Tick(0.1f); s.Tick(0.1f);
                bool third = box.shown.Count == 3 && box.shown[2] == "B1.";
                ScenarioDialogue.Next(); s.Tick(0.1f);
                ScenarioDialogue.Presenter = null;
                Unit red = Rig.At(4, 0).occupier;
                o.money = 0;
                red.Strength = 5; s.Tick(0.1f);
                bool scratch = Fired(s, "Big hit") == 0;
                red.Strength = 3; s.Tick(0.1f);
                bool hit = Fired(s, "Big hit") == 1 && o.money == 100;
                detail = "lines in order: " + string.Join(", ", box.shown.ToArray()) + "; the second's money before its line " + early + "; a scratch not counted " + scratch + ", a 2-strength hit counted " + hit;
                return early && second && third && a.money == 1 && scratch && hit;
            }
            case "scriptMissionTally": {
                GameRecord rec = new GameRecord();
                rec.sides.Add(new GameRecord.Side { id = o.id }); rec.sides.Add(new GameRecord.Side { id = a.id }); rec.sides.Add(new GameRecord.Side { id = 7, team = 1 });
                foreach (int[] t in new[] { new[] { 0, (int)o.id }, new[] { 1, (int)a.id }, new[] { 1, (int)o.id }, new[] { 1, (int)o.id }, new[] { 2, (int)a.id }, new[] { 2, (int)o.id } })
                    rec.snaps.Add(new GameRecord.Snap { round = t[0], mover = t[1] });
                rec.losses.Add(new GameRecord.Loss { side = a.id, cause = HexTactica.Harness.GameLog.Lines.Combat, killerSide = o.id });
                rec.losses.Add(new GameRecord.Loss { side = a.id, cause = HexTactica.Harness.GameLog.Lines.Supply });
                rec.losses.Add(new GameRecord.Loss { side = a.id, cause = HexTactica.Harness.GameLog.Lines.Fuel });
                rec.losses.Add(new GameRecord.Loss { side = a.id, cause = HexTactica.Harness.GameLog.Lines.Combat, killerSide = 7 });
                rec.losses.Add(new GameRecord.Loss { side = o.id, cause = HexTactica.Harness.GameLog.Lines.Supply });
                rec.losses.Add(new GameRecord.Loss { side = o.id, cause = HexTactica.Harness.GameLog.Lines.Combat, killerSide = a.id });
                MissionTally tl = MissionRating.Tally(rec, o.id, true);
                detail = "turns " + tl.turns + " (3 days, one logged twice), kills " + tl.kills + " (ours, starved, ditched; not one an ally's side made), losses " + tl.losses;
                return tl.recorded && tl.won && tl.turns == 3 && tl.kills == 3 && tl.losses == 2;
            }
            case "scriptCampaignProgress": {
                Dictionary<string, Vector2> anchors;
                List<MissionInfo> missions = SceneMissions("Campaign", out anchors);
                MissionInfo t2 = missions.Find(m => m.Name == "Tutorial 2");
                MissionTally tally = new MissionTally { recorded = true, won = true, turns = t2 != null ? t2.parTurns : 1, kills = t2 != null ? t2.parKills : 0, losses = 0 };
                return CampaignCheck(tally, out detail);
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
