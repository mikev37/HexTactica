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
             "Scenario trigger timing (ScenarioScript): triggers wait until no unit is animating (Wait For Animations); Level Start fires once however long the game runs; a trigger with Delay 2 (turns of Blue) fires only once its condition (a Blue unit on (0,0)) has held through two of Blue's turns starting, without a break, and the count starts again when it breaks; Turn Over with Repeat fires at the end of every Blue turn; with Count 2 (and no Repeat), once, at the end of the second.");
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
        make("script-not-eliminable", "scriptEliminable", (a, o) => { Put("Grunt", o, 0, 0); },
             "A side that is not eliminable (Player.eliminable off, as a scenario's enemy that starts with nothing) is not knocked out by losing its HQ and every unit, and the game goes on; the same side eliminable is knocked out, and the game ends.");
        n++;
        // The campaign's tutorial missions themselves (Assets/Resources/Campaign/Scenarios), each with a test block: played through below.
        n += ExportMission("Tutorial", "tutorial-playthrough", "scriptTutorial", 8,
            "The tutorial mission (Resources/Campaign/Scenarios/Tutorial.dat), played with the player's clicks: select the grunt; a move elsewhere does not go on; move it to the mark, end the turn; the raiders' side (not eliminable) is still in; the raider spawned in the fog is ordered at it, is spotted, and hits it, then holds its ground (fed, so it never starves); back into supply (told to end the turn); the HQ hint on turn 3; a grunt and a supply truck bought; the attack hint; the raider killed: the player wins. Each step's lines are said once, in order.");
        n += ExportMission("Tutorial 2", "tutorial2-playthrough", "scriptTutorial2", 10,
            "The second tutorial mission (Resources/Campaign/Scenarios/Tutorial 2.dat), played with the player's orders and recorded as a game is: the grunt boards the truck (told to load the supply unit), the supply unit boards (told to drive); the marked tile is outside the HQ's supply until the truck with supply aboard stands there; the grunt is put down in supply; the lookout (held, fed) is spotted; on turn 2 a recon car and a squad arrive, the squad boards the car; the lookout is destroyed, a grunt takes the village: the player wins. Then the campaign, on the mission assets as they are (Resources/Campaign/Missions): the result read from the game log earns its stars against Tutorial 2's pars; only Tutorial 1's flag is on the map at first, winning it unlocks Tutorial 2, stars once earned are kept, a mission blocked by an attempt is closed, and progress survives being saved and read back.");
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
    /// The mission assets as they are on disk (Assets/Resources/Campaign/Missions/*.asset): their plain fields, and
    /// their unlocks and blocks followed through the assets' GUIDs (their .meta files), as Unity links them.
    /// </summary>
    static List<Mission> MissionAssets() {
        string dir = PrefabLoader.Root(Mission.Folder);
        List<Mission> list = new List<Mission>();
        if (!Directory.Exists(dir)) return list;
        Dictionary<string, Mission> byGuid = new Dictionary<string, Mission>();
        Dictionary<Mission, string> text = new Dictionary<Mission, string>();
        string[] files = Directory.GetFiles(dir, "*.asset");
        Array.Sort(files, StringComparer.Ordinal);
        foreach (string f in files) {
            string yaml = File.ReadAllText(f);
            Mission m = ScriptableObject.CreateInstance<Mission>();
            m.name = Path.GetFileNameWithoutExtension(f);
            m.title = Yaml(yaml, "title").Trim('\'', '"');
            m.scenario = Yaml(yaml, "scenario");
            m.availableAtStart = Yaml(yaml, "availableAtStart") == "1";
            int.TryParse(Yaml(yaml, "parTurns"), out m.parTurns);
            int.TryParse(Yaml(yaml, "parKills"), out m.parKills);
            int.TryParse(Yaml(yaml, "parLosses"), out m.parLosses);
            System.Text.RegularExpressions.Match g = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(f + ".meta"), @"^guid: (\w+)", System.Text.RegularExpressions.RegexOptions.Multiline);
            if (g.Success) byGuid[g.Groups[1].Value] = m;
            text[m] = yaml;
            list.Add(m);
        }
        foreach (Mission m in list) {
            m.unlocks = Linked(text[m], "unlocks", byGuid);
            m.blocks = Linked(text[m], "blocks", byGuid);
        }
        return list;
    }

    static string Yaml(string yaml, string key) {
        System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(yaml, @"^  " + key + @": ?(.*)$", System.Text.RegularExpressions.RegexOptions.Multiline);
        return m.Success ? m.Groups[1].Value.Trim() : "";
    }

    /// <summary>The assets a list field links ("  key:" followed by "  - {fileID: ..., guid: ..., type: 2}" lines).</summary>
    static List<Mission> Linked(string yaml, string key, Dictionary<string, Mission> byGuid) {
        List<Mission> r = new List<Mission>();
        System.Text.RegularExpressions.Match block = System.Text.RegularExpressions.Regex.Match(yaml, @"^  " + key + @":\n((?:  - \{.*\}\n?)*)", System.Text.RegularExpressions.RegexOptions.Multiline);
        if (!block.Success) return r;
        foreach (System.Text.RegularExpressions.Match g in System.Text.RegularExpressions.Regex.Matches(block.Groups[1].Value, @"guid: (\w+)")) {
            Mission m;
            r.Add(byGuid.TryGetValue(g.Groups[1].Value, out m) ? m : null);   // (a link to nothing shows as null, and fails the check)
        }
        return r;
    }

    /// <summary>
    /// The campaign's rules on the real mission assets, with <paramref name="tally"/> (a won game of Tutorial 2) as
    /// the result: what is on the map at first, unlocking, stars against the pars, stars kept, a fork (a fixture: no
    /// real mission blocks another yet), and progress saved and read back.
    /// </summary>
    static bool CampaignCheck(MissionTally tally, out string detail) {
        List<Mission> missions = MissionAssets();
        Mission t1 = missions.Find(m => m.name == "Tutorial 1"), t2 = missions.Find(m => m.name == "Tutorial 2");
        if (t1 == null || t2 == null) { detail = "the mission assets Tutorial 1 and Tutorial 2 were not found (" + missions.Count + " in " + PrefabLoader.Root(Mission.Folder) + ")"; return false; }
        List<Mission> was = null;
        string file = Path.Combine(Path.GetTempPath(), "hextactica-campaign-" + Guid.NewGuid().ToString("N") + ".dat");
        string fileWas = CampaignProgress.FilePath;
        try {
            Mission.All = missions;
            CampaignProgress.FilePath = file;
            CampaignProgress.Load();
            string scenarios = PrefabLoader.Root("Campaign/Scenarios");
            bool files = File.Exists(Path.Combine(scenarios, t1.ScenarioName + ".dat")) && File.Exists(Path.Combine(scenarios, t2.ScenarioName + ".dat"))
                      && !File.Exists(Path.Combine(PrefabLoader.Root("Scenarios"), t1.ScenarioName + ".dat")) && !File.Exists(Path.Combine(PrefabLoader.Root("Scenarios"), t2.ScenarioName + ".dat"));
            bool linked = t1.unlocks.Count == 1 && t1.unlocks[0] == t2 && t1.availableAtStart && !t2.availableAtStart;
            bool atFirst = CampaignProgress.Playable(t1) && !CampaignProgress.Visible(t2);
            // Tutorial 1 lost: attempted, nothing unlocked, no stars.
            CampaignProgress.Attempt(t1);
            CampaignProgress.Outcome lost = CampaignProgress.Finish(t1, new MissionTally { recorded = true, won = false, turns = 3, kills = 5, losses = 0 });
            bool lostNothing = lost.earned == MissionStars.None && lost.unlocked.Count == 0 && !CampaignProgress.Get(t1).completed && !CampaignProgress.Visible(t2);
            // Tutorial 1 won slowly, with a loss, its raider destroyed: one star (aggression), and Tutorial 2 appears.
            CampaignProgress.Outcome slow = CampaignProgress.Finish(t1, new MissionTally { recorded = true, won = true, turns = t1.parTurns + 1, kills = t1.parKills, losses = t1.parLosses + 1 });
            bool unlocked = slow.firstWin && slow.unlocked.Count == 1 && slow.unlocked[0] == t2 && CampaignProgress.Playable(t2) && slow.earned == MissionStars.Aggression && slow.gained == MissionStars.Aggression
                         && CampaignProgress.Pending == slow && CampaignProgress.TakePending() == slow && CampaignProgress.Pending == null;
            // Won again within every par: the two new stars are gained, the old one kept, nothing unlocked twice.
            CampaignProgress.Outcome fast = CampaignProgress.Finish(t1, new MissionTally { recorded = true, won = true, turns = t1.parTurns, kills = t1.parKills, losses = t1.parLosses });
            bool kept = !fast.firstWin && fast.unlocked.Count == 0 && MissionRating.Count(fast.earned) == 3 && fast.gained == (MissionStars.Speed | MissionStars.Defense)
                     && MissionRating.Count(CampaignProgress.Get(t1).stars) == 3 && CampaignProgress.Get(t1).bestTurns == t1.parTurns;
            // A worse win later takes nothing away; a win that was not recorded earns no stars.
            CampaignProgress.Finish(t1, new MissionTally { recorded = false, won = true });
            bool notLost = MissionRating.Count(CampaignProgress.Get(t1).stars) == 3;
            // Tutorial 2, with the result of the game just played.
            CampaignProgress.Attempt(t2);
            CampaignProgress.Outcome played = CampaignProgress.Finish(t2, tally);
            MissionStars expected = MissionStars.None;
            if (tally.won && t2.parTurns > 0 && tally.turns <= t2.parTurns) expected |= MissionStars.Speed;
            if (tally.won && tally.kills >= t2.parKills) expected |= MissionStars.Aggression;
            if (tally.won && tally.losses <= t2.parLosses) expected |= MissionStars.Defense;
            bool rated = played.earned == expected && MissionRating.Count(played.earned) == 3 && CampaignProgress.Get(t2).completed;
            // A fork (fixture missions: the campaign has none yet): attempting one closes the other, for good unless it was already won.
            Mission left = ScriptableObject.CreateInstance<Mission>(), right = ScriptableObject.CreateInstance<Mission>(), done = ScriptableObject.CreateInstance<Mission>();
            left.name = "Fixture Left"; right.name = "Fixture Right"; done.name = "Fixture Done";
            left.availableAtStart = right.availableAtStart = done.availableAtStart = true;
            left.blocks.Add(right); left.blocks.Add(done);
            CampaignProgress.Finish(done, new MissionTally { recorded = true, won = true });
            bool open = CampaignProgress.Playable(left) && CampaignProgress.Playable(right);
            CampaignProgress.Attempt(left);
            bool fork = open && CampaignProgress.Blocked(right) && !CampaignProgress.Playable(right) && CampaignProgress.Visible(right) && !CampaignProgress.Blocked(done) && CampaignProgress.Playable(left);
            // Read back from its file.
            CampaignProgress.Load();
            bool saved = File.Exists(file) && CampaignProgress.Get(t1).completed && MissionRating.Count(CampaignProgress.Get(t1).stars) == 3 && CampaignProgress.Get(t1).bestTurns == t1.parTurns
                      && CampaignProgress.Playable(t2) && CampaignProgress.Get(t2).stars == expected && CampaignProgress.Blocked(right) && CampaignProgress.Get(left).attempted;
            CampaignProgress.Reset();
            bool reset = !CampaignProgress.Visible(t2) && !CampaignProgress.Get(t1).completed && CampaignProgress.Playable(t1);
            detail = "mission files in the campaign folder, not among the scenarios " + files + "; Tutorial 1 unlocks Tutorial 2 (the assets' links) " + linked + "; only Tutorial 1 on the map at first " + atFirst
                   + "; lost: nothing " + lostNothing + "; won: one star, Tutorial 2 unlocked, handed to the campaign screen once " + unlocked + "; won again: stars added, kept " + kept + ", not lost by a worse win " + notLost
                   + "; this game against Tutorial 2's pars (" + t2.parTurns + " turns, " + t2.parKills + " killed, " + t2.parLosses + " lost): " + MissionRating.Count(played.earned) + " stars " + rated
                   + "; a fork closes the other mission, not one already won " + fork + "; saved and read back " + saved + "; reset " + reset;
            return files && linked && atFirst && lostNothing && unlocked && kept && notLost && rated && fork && saved && reset;
        } finally {
            Mission.All = was;
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
                s.Tick(0.1f); s.Tick(0.1f);
                gc.headless = true;
                bool waitsForAnimation = Fired(s, "Start") == 0;
                g.path.Clear();
                for (int i = 0; i < 5; i++) s.Tick(0.1f);
                bool once = a.money == 100 && Fired(s, "Start") == 1 && waitsForAnimation;
                MoveTo(g, 0, 0); s.Tick(0.1f);                                    // held from Blue's turn 0
                gc.EndTurn(); s.Tick(0.1f); gc.EndTurn(); s.Tick(0.1f);           // Blue's turn 1 starts: held 1 turn
                bool notYet = Fired(s, "Hold") == 0;
                MoveTo(g, -1, 0); s.Tick(0.1f); MoveTo(g, 0, 0); s.Tick(0.1f);   // broken, held again from turn 1
                gc.EndTurn(); s.Tick(0.1f); gc.EndTurn(); s.Tick(0.1f);           // turn 2: 1 turn since
                bool restarted = Fired(s, "Hold") == 0;
                gc.EndTurn(); s.Tick(0.1f); gc.EndTurn(); s.Tick(0.1f);           // turn 3: 2 turns since
                bool held = Fired(s, "Hold") == 1 && a.money == 101;
                bool repeat = Fired(s, "Every Blue turn") == 3 && Fired(s, "Second Blue turn") == 1 && o.money == 1030;
                detail = "waits while a unit animates " + waitsForAnimation + "; Level Start once " + once + " (Blue " + a.money + "); Delay 2 turns: fired after 1 turn " + !notYet + ", count started over after a break " + restarted + ", fired once held 2 of Blue's turns " + held
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
            case "scriptTutorial": {
                Player human = o, raiders = a;
                bool keptIn = !raiders.eliminable && human.eliminable;
                human.money = 9000;
                FakeBox box = new FakeBox();
                ScenarioDialogue.Presenter = box;
                foreach (MonoBehaviour m in raiders.GetComponents<MonoBehaviour>()) if (m is IAIController) m.enabled = true;
                AIPlayer ai = raiders.GetComponent<AIPlayer>();
                List<string> log = new List<string>();
                List<string> transcript = new List<string>();
                Func<string> said = () => { string r = string.Join(" | ", box.shown.ToArray()); transcript.AddRange(box.shown); box.shown.Clear(); return r; };
                Func<string, bool> everSaid = text => { said(); return transcript.Exists(x => x.Contains(text)); };
                Action settle = () => { for (int i = 0; i < 20; i++) { s.Tick(0.2f); while (ScenarioDialogue.Showing) { ScenarioDialogue.Next(); s.Tick(0.2f); } } };
                Action aiTurn = () => { int guard = 0; bool more = true; while (more && gc.activePlayer == raiders && guard++ < 100) { more = ai.StepOnce(true); settle(); } if (gc.activePlayer == raiders) gc.EndTurn(); settle(); };
                Unit recruit = ScenarioGame.Named("Recruit");
                bool ok = recruit != null && human.units.Contains(recruit);

                settle();
                string welcome = said(); bool step1 = welcome.Contains("Click on him") && s.markers.Exists(m => m.Id == "recruit");
                // (The board's click flow needs the tiles' overlays, which the headless board lacks: the clicks' effects are
                // made as ClickBoard makes them: selection notified, the move walked as CompleteMove walks it.)
                HexTile.NotifyUnitSelected(recruit); settle();
                // Parked somewhere else first (a step off the path, by hand): the lesson waits for the marked tile.
                ScenarioGame.Order(recruit, null, Rig.At(-3, 2)); settle();
                bool waits = Fired(s, "Recruit spent") == 0 && s.markers.Exists(m => m.Id == "goal");
                gc.EndTurn(); settle(); aiTurn();
                bool noAmbush = ScenarioGame.Named("Raider") == null && !gc.gameOver && !raiders.eliminated;
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
                string hurt = said(); bool step4 = spawned && hit && hurt.Contains("Enemy spotted") && hurt.Contains("We took damage") && hurt.IndexOf("Enemy spotted") < hurt.IndexOf("We took damage");
                aiTurn();
                // Turn 2: back into supply beside the HQ.
                HexTile.NotifyUnitSelected(recruit); ScenarioGame.Order(recruit, null, Rig.At(-3, 1)); settle(); said();
                bool back = recruit.tile == Rig.At(-3, 1) && everSaid("Good, now end your turn") && !s.markers.Exists(m => m.Id == "supply");
                HexTile raiderAt = raider != null ? raider.tile : null;
                int before = recruit.Strength;
                gc.EndTurn(); settle(); aiTurn();
                // Turn 3: healed in supply, and the HQ hint (it was never opened).
                string third = said();
                raider = ScenarioGame.Named("Raider");
                bool holds = raider != null && raider.tile == raiderAt && raider.supplies > 0;   // the raiders' turn 2: it stayed, fed
                bool healed = recruit.Strength > before || recruit.Strength == 6;
                bool step5 = back && everSaid("click the HQ") && Fired(s, "HQ hint") == 1 && s.markers.Exists(m => m.Id == "hq");
                Building hq = Rig.At(-4, 2).building;
                HexTile.NotifyBuildingOpened(hq); settle();
                string opened = said(); bool step6 = opened.Contains("build list") && !s.markers.Exists(m => m.Id == "hq");
                Func<string, Unit> buy = kind => { Unit pick = hq.buildable.Find(x => x != null && GameController.UnitName(x) == kind); return pick != null ? Rig.bc.Produce(hq, pick) : null; };
                string offered = string.Join(",", hq.buildable.ConvertAll(x => x != null ? GameController.UnitName(x) : "?").ToArray());
                Unit grunt = buy("Grunt"); settle();
                string g1 = said(); bool step7 = grunt != null && g1.Contains("A new grunt") && g1.Contains("capture") && s.markers.Exists(m => m.Id == "city");
                gc.EndTurn(); settle(); aiTurn();
                // Turn 4: the new grunt steps off the HQ, a supply truck is bought; the attack hint (turn 4, and the truck).
                if (grunt != null && grunt.Strength > 0) ScenarioGame.Order(grunt, null, Rig.At(-5, 2));
                settle();
                Unit truck = buy("Supplies"); settle();
                string fourth = said();
                bool step8 = truck != null && everSaid("supply truck") && everSaid("To attack") && Fired(s, "Fight back") == 1;
                // The raider dies (the player's attacks, as many as it takes: ordered here as the clicks would).
                raider = ScenarioGame.Named("Raider");
                int rounds = 0;
                while (raider != null && raider.Strength > 0 && !gc.gameOver && rounds++ < 10) {
                    foreach (Unit u in new List<Unit>(human.units)) if (u != null && u.Strength > 0 && u.active && raider != null && raider.Strength > 0 && u.CanAttack(raider)) ScenarioGame.Order(u, raider, null);
                    settle();
                    raider = ScenarioGame.Named("Raider");
                    if (raider == null || gc.gameOver) break;
                    gc.EndTurn(); settle(); aiTurn();
                }
                string end = said();
                bool won = gc.gameOver && gc.winner == human && end.Contains("Raider down");
                bool words = s.description.StartsWith("Tutorial") && ScenarioScript.EndText(true).StartsWith("Tutorial complete") && ScenarioScript.EndText(false).Length > 0;
                detail = "start " + ok + ", raiders kept in, player eliminable " + keptIn + "; welcome and marker " + step1 + "; selected: move hint, marker moved " + step2
                       + "; parked elsewhere: no end-turn hint, marker stays " + waits + ", no ambush, raiders still in " + noAmbush + "; moved to the mark: Good job, end-turn hint " + step3
                       + "; ambush: raider spawned in the fog " + spawned + ", next to the grunt and hit it (to " + hitTo + ") " + hit + ", lines " + step4
                       + "; back into supply, told to end the turn " + back + ", raider held its ground, fed " + holds + ", healed " + healed + ", HQ hint on turn 3 " + step5 + "; HQ opened " + step6 + "; grunt bought " + step7 + (step7 ? "" : " (HQ offers " + offered + "; gold " + human.money + ")") + "; truck bought and attack hint " + step8
                       + "; raider killed in " + rounds + " round(s), player wins " + won + "; description, win and loss text " + words + (won ? "" : " [last lines: " + end + "]");
                ScenarioDialogue.Presenter = null;
                return ok && words && holds && keptIn && waits && noAmbush && step1 && step2 && step3 && step4 && back && healed && step5 && step6 && step7 && step8 && won;
            }
            case "scriptTutorial2": {
                Player human = o, raiders = a;
                FakeBox box = new FakeBox();
                ScenarioDialogue.Presenter = box;
                foreach (MonoBehaviour m in raiders.GetComponents<MonoBehaviour>()) if (m is IAIController) m.enabled = true;
                AIPlayer ai = raiders.GetComponent<AIPlayer>();
                List<string> transcript = new List<string>();
                Func<string> said = () => { string r = string.Join(" | ", box.shown.ToArray()); transcript.AddRange(box.shown); box.shown.Clear(); return r; };
                Func<string, bool> everSaid = text => { said(); return transcript.Exists(x => x.Contains(text)); };
                Action settle = () => { for (int i = 0; i < 20; i++) { s.Tick(0.2f); while (ScenarioDialogue.Showing) { ScenarioDialogue.Next(); s.Tick(0.2f); } } };
                Action aiTurn = () => { int guard = 0; bool more = true; while (more && gc.activePlayer == raiders && guard++ < 100) { more = ai.StepOnce(true); settle(); } if (gc.activePlayer == raiders) gc.EndTurn(); settle(); };
                // Boarding as the board's click does it (HexTile.ClickBoard): the walk in, then aboard, the unit's turn over.
                Func<Unit, Unit, bool> board = (cargo, transport) => {
                    if (cargo == null || transport == null) return false;
                    List<HexTile> steps = HexTile.BoardingPath(cargo, transport);
                    if (steps == null || !cargo.WalkAndBoard(steps, transport, true)) return false;
                    cargo.FinishActive();
                    return true;
                };
                Func<Unit, bool> supplied = u => { SupplyNetwork.Solve(human, false); return u != null && !ScenarioGame.CutOff(u); };
                // Recorded as a game is (the game log): the mission's result is read from it at the end.
                string logRoot = Path.Combine(Path.GetTempPath(), "hextactica-tutorial2-" + Guid.NewGuid().ToString("N"));
                HexTactica.Harness.GameLog.Start(l.path, "Human", logRoot);

                Unit truck = ScenarioGame.Named("Truck"), rifles = ScenarioGame.Named("Rifles"), supply = ScenarioGame.Named("Supply"), lookout = ScenarioGame.Named("Lookout");
                HexTile front = Rig.At(2, 2), village = Rig.At(4, 2);
                bool ok = truck != null && rifles != null && supply != null && lookout != null && human.units.Contains(truck) && lookout.owner == raiders
                       && village.building != null && village.building.owner == raiders && !raiders.eliminable && !gc.purchasesAllowed;
                settle();
                string welcome = said(); bool step1 = welcome.Contains("climbs aboard") && s.markers.Exists(m => m.Id == "rifles");
                // Turn 1: the grunt boards, then the supply unit; only then the drive is asked for.
                bool aboard1 = board(rifles, truck); settle();
                string first = said(); bool step2 = aboard1 && rifles.trasnporter == truck && first.Contains("Boarding used his turn") && s.markers.Exists(m => m.Id == "supply") && Fired(s, "Loaded") == 0;
                bool aboard2 = board(supply, truck); settle();
                string second = said(); bool step3 = aboard2 && supply.trasnporter == truck && second.Contains("Supply aboard") && second.Contains("Both aboard") && s.markers.Exists(m => m.Id == "front");
                // The marked tile is beyond the HQ's own supply: what supplies it is the supply unit aboard the truck.
                bool beyond = !SupplyNetwork.Solve(human, false).IsSupplied(front);
                bool drove = ScenarioGame.Order(truck, null, front); settle();
                string arrived = said();
                bool carried = SupplyNetwork.Solve(human, false).IsSupplied(front) && SupplyNetwork.Solve(human, false).IsSupplied(Rig.At(3, 2));
                bool step4 = drove && truck.tile == front && arrived.Contains("put the grunt down") && !s.markers.Exists(m => m.Id == "front");
                bool put = Transporting.Unload(truck, rifles, Rig.At(3, 2)); settle();
                string down = said(); bool step5 = put && rifles.tile == Rig.At(3, 2) && down.Contains("On foot again") && supplied(rifles) && !rifles.active;
                gc.EndTurn(); settle(); aiTurn();
                // Turn 2: the lookout has held (fed, its turn used), reinforcements at the HQ.
                bool held = lookout.Strength > 0 && lookout.tile == village && lookout.supplies > 0;
                bool spotted = everSaid("Enemy spotted");
                Unit recon = ScenarioGame.Named("Recon"), scouts = ScenarioGame.Named("Scouts");
                bool step6 = recon != null && scouts != null && recon.owner == human && everSaid("Reinforcements") && s.markers.Exists(m => m.Id == "recon") && Fired(s, "Reinforcements") == 1;
                bool aboard3 = board(scouts, recon); settle();
                bool step7 = aboard3 && scouts != null && scouts.trasnporter == recon && everSaid("Deal with that lookout") && s.markers.Exists(m => m.Id == "lookout");
                // The lookout is destroyed (the recon car and the grunt, as many attacks as it takes), nobody cut off meanwhile.
                int rounds = 0;
                while (lookout != null && lookout.Strength > 0 && !gc.gameOver && rounds++ < 6) {
                    foreach (Unit u in new List<Unit> { recon, rifles }) if (u != null && u.Strength > 0 && u.active && u.trasnporter == null && lookout.Strength > 0 && u.CanAttack(lookout)) { ScenarioGame.Order(u, lookout, null); settle(); }
                    if (lookout.Strength <= 0 || gc.gameOver) break;
                    gc.EndTurn(); settle(); aiTurn();
                }
                bool down2 = lookout.Strength <= 0 && everSaid("Lookout down") && s.markers.Exists(m => m.Id == "village") && !s.markers.Exists(m => m.Id == "lookout");
                // A grunt takes the village and holds it.
                int holdTurns = 0;
                while (!gc.gameOver && holdTurns++ < 8) {
                    if (rifles.Strength > 0 && rifles.active && rifles.tile != village) { ScenarioGame.Order(rifles, null, village); settle(); }
                    if (gc.gameOver) break;
                    gc.EndTurn(); settle(); aiTurn();
                }
                bool won = gc.gameOver && gc.winner == human && everSaid("The village is ours") && village.building.owner == human;
                bool neverCutOff = Fired(s, "Out of supply") == 0;
                bool words = s.description.StartsWith("Tutorial 2") && ScenarioScript.EndText(true).Length > 0 && ScenarioScript.EndText(false).Length > 0;
                int humanTurns = s.TurnsOf(human.id);

                // The result, as the mission's result screen reads it: from the game log.
                HexTactica.Harness.GameLog.EndIfOver();
                string folder = HexTactica.Harness.GameLog.Folder;
                HexTactica.Harness.GameLog.Stop();
                GameRecord record = GameRecord.Read(folder);
                MissionTally tally = MissionRating.Tally(record, human.id, won);
                try { Directory.Delete(logRoot, true); } catch (Exception) { }
                bool tallied = tally.recorded && tally.turns == humanTurns && tally.kills == 1 && tally.losses == 0;

                string campaign;
                bool campaignOk = CampaignCheck(tally, out campaign);
                detail = "start " + ok + "; welcome and marker " + step1 + "; grunt aboard, told to load supply, no drive yet " + step2 + "; supply aboard, told to drive " + step3
                       + "; the mark is beyond the HQ's supply " + beyond + "; drove there, told to unload " + step4 + ", supply reaches round the truck " + carried + "; grunt put down in supply, its turn used " + step5
                       + "; lookout held, fed " + held + ", spotted " + spotted + "; reinforcements on turn 2 " + step6 + "; squad aboard the recon car, attack hint " + step7
                       + "; lookout destroyed in " + rounds + " round(s) " + down2 + "; village taken, player wins " + won + " (player turns " + humanTurns + "), nobody ever out of supply " + neverCutOff + "; words " + words
                       + "; from the game log: " + tally.turns + " turns, " + tally.kills + " killed, " + tally.losses + " lost " + tallied + "; campaign: " + campaign
                       + (won ? "" : " [last lines: " + string.Join(" | ", transcript.GetRange(Math.Max(0, transcript.Count - 4), Math.Min(4, transcript.Count)).ToArray()) + "]");
                ScenarioDialogue.Presenter = null;
                return ok && words && step1 && step2 && step3 && beyond && step4 && carried && step5 && held && spotted && step6 && step7 && down2 && won && neverCutOff && tallied && campaignOk;
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
