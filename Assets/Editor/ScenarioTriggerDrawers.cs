using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// How a scenario's triggers look in the inspector: each trigger as one line when folded ("Intro 2: when 'Intro 1'
/// has fired, 2 actions"), and unfolded only the fields its condition uses; each action likewise. Name fields are
/// choices: trigger names, sides, unit and building kinds, characters, markers, tracks and sounds.
/// The field tables here say which condition and action uses which field: keep them in step with ScenarioTrigger.
/// </summary>
static class TriggerFields {

    /// <summary>"Area": the centre tile and radius. "InArea": the In Area switch, and the area when it is on.</summary>
    public static string[] For(TriggerCondition c) {
        switch (c) {
            case TriggerCondition.LevelStart: return new string[0];
            case TriggerCondition.UnitEnters:
            case TriggerCondition.UnitsInArea: return new[] { "Side", "UnitKind", "UnitTag", "Area", "Count" };
            case TriggerCondition.UnitKilled:
            case TriggerCondition.UnitBuilt:
            case TriggerCondition.UnitStarving:
            case TriggerCondition.UnitOutOfAmmo:
            case TriggerCondition.UnitOutOfFuel:
            case TriggerCondition.UnitCutOff: return new[] { "Side", "UnitKind", "UnitTag", "InArea", "Count" };
            case TriggerCondition.UnitDamaged:
            case TriggerCondition.UnitHealed:
            case TriggerCondition.UnitLowOnFuel: return new[] { "Side", "UnitKind", "UnitTag", "InArea", "Value", "Count" };
            case TriggerCondition.UnitsAtMost:
            case TriggerCondition.UnitsAtLeast: return new[] { "Side", "UnitKind", "UnitTag", "InArea", "Value" };
            case TriggerCondition.BuildingCaptured:
            case TriggerCondition.BuildingOwned: return new[] { "Side", "BuildingKind", "InArea", "Count" };
            case TriggerCondition.BuildingDestroyed: return new[] { "BuildingKind", "InArea", "Count" };
            case TriggerCondition.DayReached: return new[] { "Value", "Side" };
            case TriggerCondition.MoneyAtLeast:
            case TriggerCondition.MoneyAtMost: return new[] { "Side", "Value" };
            case TriggerCondition.TurnOver:
            case TriggerCondition.TurnStarted: return new[] { "Side", "Count" };
            case TriggerCondition.AfterTrigger: return new[] { "AfterTrigger" };
            case TriggerCondition.AllOf:
            case TriggerCondition.AnyOf:
            case TriggerCondition.NoneOf: return new[] { "Parts" };
        }
        return new string[0];
    }

    /// <summary>What a field is called for this condition (null: its own name).</summary>
    public static string Label(TriggerCondition c, string field) {
        switch (field) {
            case "Side":
                if (c == TriggerCondition.BuildingCaptured) return "Taken By";
                if (c == TriggerCondition.BuildingOwned) return "Held By";
                if (c == TriggerCondition.MoneyAtLeast || c == TriggerCondition.MoneyAtMost) return "Whose";
                if (c == TriggerCondition.TurnOver || c == TriggerCondition.TurnStarted || c == TriggerCondition.DayReached) return "On Whose Turn";
                return "Whose Units";
            case "Value":
                switch (c) {
                    case TriggerCondition.DayReached: return "Day";
                    case TriggerCondition.MoneyAtLeast: return "Gold At Least";
                    case TriggerCondition.MoneyAtMost: return "Gold At Most";
                    case TriggerCondition.UnitDamaged: return "Strength At Most";
                    case TriggerCondition.UnitHealed: return "Strength At Least";
                    case TriggerCondition.UnitLowOnFuel: return "Fuel % At Most";
                    case TriggerCondition.UnitsAtMost: return "Units At Most";
                    case TriggerCondition.UnitsAtLeast: return "Units At Least";
                }
                return null;
            case "Count":
                switch (c) {
                    case TriggerCondition.UnitsInArea: case TriggerCondition.UnitStarving: case TriggerCondition.UnitOutOfAmmo:
                    case TriggerCondition.UnitOutOfFuel: case TriggerCondition.UnitLowOnFuel: case TriggerCondition.UnitCutOff:
                    case TriggerCondition.BuildingOwned: return "At Least";
                    case TriggerCondition.TurnStarted: case TriggerCondition.TurnOver: return "Their Turn Number";
                }
                return "Times";
        }
        return null;
    }

    public static string[] For(TriggerActionType a) {
        string[] units = { "UnitSide", "UnitKind", "UnitTag", "InArea" };
        string[] buildings = { "BuildingSide", "BuildingKind", "InArea" };
        switch (a) {
            case TriggerActionType.Dialogue: return new[] { "Lines" };
            case TriggerActionType.CameraFocus: return new[] { "TileX", "TileY", "Zoom", "Seconds" };
            case TriggerActionType.CameraFrameBoard: return new string[0];
            case TriggerActionType.ShowMarker: return new[] { "Marker", "MarkerId", "Text", "TileX", "TileY" };
            case TriggerActionType.HideMarker: return new[] { "MarkerId" };
            case TriggerActionType.PlayMusic: return new[] { "Track", "Loop" };
            case TriggerActionType.StopMusic: return new string[0];
            case TriggerActionType.Win:
            case TriggerActionType.Lose: return new[] { "Side" };
            case TriggerActionType.SpawnUnit: return new[] { "Side", "UnitKind", "TileX", "TileY", "Count", "Value", "Veterancy", "UnitTag", "ReadyToAct" };
            case TriggerActionType.GiveUnits: return Join(units, "Side");
            case TriggerActionType.GiveBuilding: return Join(buildings, "Side");
            case TriggerActionType.DestroyTile:
            case TriggerActionType.RepairTile: return new[] { "TileX", "TileY", "Radius" };
            case TriggerActionType.DestroyBuilding:
            case TriggerActionType.RepairBuilding: return buildings;
            case TriggerActionType.SetVeterancy: return Join(units, "Mode", "Value");
            case TriggerActionType.SetStat: return Join(units, "Stat", "Mode", "Value");
            case TriggerActionType.DestroyUnits: return units;
            case TriggerActionType.ReadyUnits: return Join(units, "Value");
            case TriggerActionType.GiveMoney: return new[] { "Side", "Mode", "Value" };
            case TriggerActionType.EnableTrigger:
            case TriggerActionType.DisableTrigger:
            case TriggerActionType.ResetTrigger: return new[] { "Trigger" };
            case TriggerActionType.PlaySound: return new[] { "Sound" };
            case TriggerActionType.Wait: return new[] { "Seconds" };
        }
        return new string[0];
    }

    public static string Label(TriggerActionType a, string field) {
        switch (field) {
            case "Side":
                if (a == TriggerActionType.Win) return "Winner";
                if (a == TriggerActionType.Lose) return "Loser";
                if (a == TriggerActionType.SpawnUnit) return "For";
                if (a == TriggerActionType.GiveMoney) return "To";
                return "New Owner";
            case "UnitSide": return "Whose Units";
            case "BuildingSide": return "Whose Buildings";
            case "Value":
                if (a == TriggerActionType.SpawnUnit) return "Strength";
                if (a == TriggerActionType.ReadyUnits) return "Ready (1) / Spent (0)";
                if (a == TriggerActionType.GiveMoney) return "Gold";
                return null;
            case "UnitTag": return a == TriggerActionType.SpawnUnit ? "Name Them" : "Unit Name";
            case "MarkerId": return a == TriggerActionType.ShowMarker ? "Name (To Hide It Later)" : "Marker To Hide";
            case "Marker": return "Looks Like";
            case "Seconds": return a == TriggerActionType.Wait ? "Wait Seconds" : "Glide Seconds";
        }
        return null;
    }

    static string[] Join(string[] a, params string[] b) { List<string> r = new List<string>(a); r.AddRange(b); return r.ToArray(); }

    // ------------------------------------------------------------------ one-line summaries

    public static string Side(long s) { return ScenarioEditing.SideName(s); }

    static string Units(string side, string kind, string tag, bool inArea, int x, int y, int r) {
        string what = (string.IsNullOrEmpty(kind) ? "unit" : kind) + (string.IsNullOrEmpty(tag) ? "" : " '" + tag + "'");
        return side + " " + what + (inArea ? " within " + r + " of (" + x + "," + y + ")" : "");
    }

    public static string Describe(ScenarioTrigger t) {
        string units = Units(Side(t.Side) + "'s", t.UnitKind, t.UnitTag, t.InArea, t.TileX, t.TileY, t.Radius);
        string area = "(" + t.TileX + "," + t.TileY + ")" + (t.Radius > 0 ? " r" + t.Radius : "");
        string times = t.Count > 1 ? " " + t.Count + " times" : "";
        string building = (string.IsNullOrEmpty(t.BuildingKind) ? "a building" : "a " + t.BuildingKind) + (t.InArea ? " within " + t.Radius + " of (" + t.TileX + "," + t.TileY + ")" : "");
        switch (t.Condition) {
            case TriggerCondition.LevelStart: return "the level starts";
            case TriggerCondition.UnitEnters: return Units(Side(t.Side) + "'s", t.UnitKind, t.UnitTag, false, 0, 0, 0) + " enters " + area + times;
            case TriggerCondition.UnitKilled: return (t.Count > 1 ? t.Count + " of " : "") + units + " killed";
            case TriggerCondition.BuildingCaptured: return building + " taken by " + Side(t.Side) + times;
            case TriggerCondition.BuildingDestroyed: return building + " destroyed" + times;
            case TriggerCondition.DayReached: return "day " + t.Value + (t.Side == TriggerSide.Any ? "" : ", on " + Side(t.Side) + "'s turn");
            case TriggerCondition.UnitsInArea: return Mathf.Max(1, t.Count) + "+ " + Units(Side(t.Side) + "'s", t.UnitKind, t.UnitTag, false, 0, 0, 0) + " in " + area;
            case TriggerCondition.MoneyAtLeast: return Side(t.Side) + " has " + t.Value + "+ gold";
            case TriggerCondition.MoneyAtMost: return Side(t.Side) + " has " + t.Value + " gold or less";
            case TriggerCondition.UnitBuilt: return units + " built" + times;
            case TriggerCondition.UnitDamaged: return units + " damaged to " + t.Value + " or less" + times;
            case TriggerCondition.UnitHealed: return units + " healed to " + t.Value + " or more" + times;
            case TriggerCondition.TurnOver: return Side(t.Side) + "'s " + Nth(t.Count) + "turn ends";
            case TriggerCondition.TurnStarted: return Side(t.Side) + "'s " + Nth(t.Count) + "turn starts";
            case TriggerCondition.UnitStarving: return units + " starving";
            case TriggerCondition.UnitOutOfAmmo: return units + " out of ammo";
            case TriggerCondition.UnitOutOfFuel: return units + " out of fuel";
            case TriggerCondition.UnitLowOnFuel: return units + " at " + t.Value + "% fuel or less";
            case TriggerCondition.UnitCutOff: return units + " cut off from supply";
            case TriggerCondition.UnitsAtMost: return t.Value + " or fewer " + units + " left";
            case TriggerCondition.UnitsAtLeast: return t.Value + " or more " + units;
            case TriggerCondition.BuildingOwned: return building + " held by " + Side(t.Side);
            case TriggerCondition.AfterTrigger: return "after '" + t.AfterTrigger + "'";
            case TriggerCondition.AllOf: return "all of " + Names(t.Parts);
            case TriggerCondition.AnyOf: return "any of " + Names(t.Parts);
            case TriggerCondition.NoneOf: return "none of " + Names(t.Parts);
        }
        return t.Condition.ToString();
    }

    static string Nth(int n) { n = Mathf.Max(1, n); return n == 1 ? "" : n + (n % 10 == 2 && n % 100 != 12 ? "nd " : n % 10 == 3 && n % 100 != 13 ? "rd " : n % 10 == 1 && n % 100 != 11 ? "st " : "th "); }

    static string Names(List<string> parts) { return parts == null || parts.Count == 0 ? "(nothing)" : "'" + string.Join("', '", parts.ToArray()) + "'"; }

    public static string Describe(TriggerAction a) {
        string units = Units(Side(a.UnitSide) + "'s", a.UnitKind, a.UnitTag, a.InArea, a.TileX, a.TileY, a.Radius);
        string buildings = Side(a.BuildingSide) + "'s " + (string.IsNullOrEmpty(a.BuildingKind) ? "buildings" : a.BuildingKind) + (a.InArea ? " within " + a.Radius + " of (" + a.TileX + "," + a.TileY + ")" : "");
        string at = "(" + a.TileX + "," + a.TileY + ")";
        switch (a.Type) {
            case TriggerActionType.Dialogue: {
                int n = a.Lines != null ? a.Lines.Count : 0;
                string first = n > 0 && a.Lines[0] != null ? (string.IsNullOrEmpty(a.Lines[0].Character) ? "" : a.Lines[0].Character + ": ") + Short(a.Lines[0].Text) : "";
                return "Dialogue, " + n + " line" + (n == 1 ? "" : "s") + (first.Length > 0 ? " — " + first : "");
            }
            case TriggerActionType.CameraFocus: return "Camera to " + at + (a.Zoom > 0 ? ", zoom " + a.Zoom : "");
            case TriggerActionType.CameraFrameBoard: return "Camera on the whole board";
            case TriggerActionType.ShowMarker: return "Marker " + a.Marker + (string.IsNullOrEmpty(a.MarkerId) ? "" : " '" + a.MarkerId + "'") + " at " + at + (string.IsNullOrEmpty(a.Text) ? "" : ": " + Short(a.Text));
            case TriggerActionType.HideMarker: return string.IsNullOrEmpty(a.MarkerId) ? "Hide every marker" : "Hide marker '" + a.MarkerId + "'";
            case TriggerActionType.PlayMusic: return "Music: " + (string.IsNullOrEmpty(a.Track) ? "a special track" : a.Track) + (a.Loop ? " (loop)" : "");
            case TriggerActionType.StopMusic: return "Music back to normal";
            case TriggerActionType.Win: return Side(a.Side) + " wins";
            case TriggerActionType.Lose: return Side(a.Side) + " loses";
            case TriggerActionType.SpawnUnit: return "Spawn " + Mathf.Max(1, a.Count) + " " + a.UnitKind + " for " + Side(a.Side) + " at " + at;
            case TriggerActionType.GiveUnits: return units + " go to " + Side(a.Side);
            case TriggerActionType.GiveBuilding: return buildings + " go to " + Side(a.Side);
            case TriggerActionType.DestroyTile: return "Destroy ground within " + a.Radius + " of " + at;
            case TriggerActionType.RepairTile: return "Repair ground within " + a.Radius + " of " + at;
            case TriggerActionType.DestroyBuilding: return "Destroy " + buildings;
            case TriggerActionType.RepairBuilding: return "Repair " + buildings;
            case TriggerActionType.SetVeterancy: return units + ": veterancy " + (a.Mode == ValueMode.Add ? (a.Value >= 0 ? "+" : "") : "= ") + a.Value;
            case TriggerActionType.SetStat: return units + ": " + a.Stat + " " + (a.Mode == ValueMode.Add ? (a.Value >= 0 ? "+" : "") : "= ") + a.Value;
            case TriggerActionType.DestroyUnits: return "Destroy " + units;
            case TriggerActionType.ReadyUnits: return units + (a.Value != 0 ? " may act again" : " have used their turn");
            case TriggerActionType.GiveMoney: return Side(a.Side) + (a.Mode == ValueMode.Set ? " has " + a.Value : " gets " + a.Value) + " gold";
            case TriggerActionType.EnableTrigger: return "Switch on '" + (string.IsNullOrEmpty(a.Trigger) ? "this trigger" : a.Trigger) + "'";
            case TriggerActionType.DisableTrigger: return "Switch off '" + (string.IsNullOrEmpty(a.Trigger) ? "this trigger" : a.Trigger) + "'";
            case TriggerActionType.ResetTrigger: return "Re-arm '" + (string.IsNullOrEmpty(a.Trigger) ? "this trigger" : a.Trigger) + "'";
            case TriggerActionType.PlaySound: return "Sound " + a.Sound;
            case TriggerActionType.Wait: return "Wait " + a.Seconds + " s";
        }
        return a.Type.ToString();
    }

    static string Short(string s) { s = (s ?? "").Replace('\n', ' '); return s.Length > 40 ? s.Substring(0, 40) + "…" : s; }
}

/// <summary>The trigger being drawn's script, and its sides (for the side choices).</summary>
static class ScenarioEditing {
    public static ScenarioScript Script(SerializedProperty p) { return p.serializedObject.targetObject as ScenarioScript; }

    public static ScenarioScript Shown;

    public static string SideName(long s) {
        switch (s) {
            case TriggerSide.Any: return "any side";
            case TriggerSide.Human: return "the player";
            case TriggerSide.AI: return "the AI";
            case TriggerSide.Neutral: return "nobody";
        }
        if (Shown != null) foreach (ScenarioScript.KnownSide k in Shown.sides) if (k.id == s) return k.name;
        return "side " + s;
    }

    /// <summary>The value of a field by its name, from a trigger or action object (for the summaries).</summary>
    public static object Target(SerializedProperty p) {
        object o = p.serializedObject.targetObject;
        string path = p.propertyPath.Replace(".Array.data[", "[");
        foreach (string part in path.Split('.')) {
            if (o == null) return null;
            int b = part.IndexOf('[');
            string name = b >= 0 ? part.Substring(0, b) : part;
            System.Reflection.FieldInfo f = o.GetType().GetField(name);
            if (f == null) return null;
            o = f.GetValue(o);
            if (b >= 0) {
                int i = int.Parse(part.Substring(b + 1, part.Length - b - 2));
                System.Collections.IList list = o as System.Collections.IList;
                o = list != null && i < list.Count ? list[i] : null;
            }
        }
        return o;
    }
}

/// <summary>Draws a list of fields of a trigger or action, the area's fields expanded, with labels for the type.</summary>
static class FieldList {
    public static readonly float Line = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

    static IEnumerable<string> Expand(SerializedProperty p, string[] fields) {
        foreach (string f in fields) {
            if (f == "Area") { yield return "TileX"; yield return "TileY"; yield return "Radius"; continue; }
            if (f == "InArea") {
                yield return "InArea";
                SerializedProperty on = p.FindPropertyRelative("InArea");
                if (on != null && on.boolValue) { yield return "TileX"; yield return "TileY"; yield return "Radius"; }
                continue;
            }
            yield return f;
        }
    }

    public static float Height(SerializedProperty p, string[] fields) {
        float h = 0f;
        foreach (string f in Expand(p, fields)) {
            SerializedProperty c = p.FindPropertyRelative(f);
            if (c != null) h += EditorGUI.GetPropertyHeight(c, true) + EditorGUIUtility.standardVerticalSpacing;
        }
        return h;
    }

    public static float Draw(Rect r, SerializedProperty p, string[] fields, System.Func<string, string> label) {
        float y = r.y;
        foreach (string f in Expand(p, fields)) {
            SerializedProperty c = p.FindPropertyRelative(f);
            if (c == null) continue;
            float h = EditorGUI.GetPropertyHeight(c, true);
            string l = label(f);
            EditorGUI.PropertyField(new Rect(r.x, y, r.width, h), c, l != null ? new GUIContent(l, c.tooltip) : new GUIContent(c.displayName, c.tooltip), true);
            y += h + EditorGUIUtility.standardVerticalSpacing;
        }
        return y;
    }
}

[CustomPropertyDrawer(typeof(ScenarioTrigger))]
class ScenarioTriggerDrawer : PropertyDrawer {
    static readonly string[] Head = { "Name", "Enabled", "Condition" };
    static readonly string[] Timing = { "Delay", "Repeat", "RepeatEvery", "Actions" };
    static string TimingLabel(string f) { return f == "Delay" ? "Delay (seconds)" : f == "RepeatEvery" ? "Repeat Every (seconds)" : null; }

    public override float GetPropertyHeight(SerializedProperty p, GUIContent label) {
        float h = FieldList.Line;
        if (!p.isExpanded) return h;
        TriggerCondition c = (TriggerCondition)p.FindPropertyRelative("Condition").intValue;
        return h + FieldList.Height(p, Head) + FieldList.Height(p, TriggerFields.For(c)) + FieldList.Height(p, Timing);
    }

    public override void OnGUI(Rect r, SerializedProperty p, GUIContent label) {
        EditorGUI.BeginProperty(r, label, p);
        ScenarioEditing.Shown = ScenarioEditing.Script(p);
        ScenarioTrigger t = ScenarioEditing.Target(p) as ScenarioTrigger;
        string summary = t == null ? label.text : t.Name + (t.Enabled ? "" : " (off)") + ": when " + TriggerFields.Describe(t) + ", "
            + (t.Actions != null ? t.Actions.Count : 0) + " action" + (t.Actions != null && t.Actions.Count == 1 ? "" : "s")
            + (Application.isPlaying && t.TimesFired > 0 ? "  [fired " + t.TimesFired + "×]" : "")
            + (Application.isPlaying && t.Events > 0 ? "  [" + t.Events + " so far]" : "");
        p.isExpanded = EditorGUI.Foldout(new Rect(r.x, r.y, r.width, EditorGUIUtility.singleLineHeight), p.isExpanded, new GUIContent(summary, "Fold out to edit."), true);
        if (p.isExpanded) {
            EditorGUI.indentLevel++;
            TriggerCondition c = (TriggerCondition)p.FindPropertyRelative("Condition").intValue;
            Rect rest = new Rect(r.x, r.y + FieldList.Line, r.width, r.height);
            float y = FieldList.Draw(rest, p, Head, f => null);
            rest.y = y;
            y = FieldList.Draw(rest, p, TriggerFields.For(c), f => TriggerFields.Label(c, f));
            rest.y = y;
            FieldList.Draw(rest, p, Timing, TimingLabel);
            EditorGUI.indentLevel--;
        }
        EditorGUI.EndProperty();
    }
}

[CustomPropertyDrawer(typeof(TriggerAction))]
class TriggerActionDrawer : PropertyDrawer {
    static readonly string[] Head = { "Type" };

    public override float GetPropertyHeight(SerializedProperty p, GUIContent label) {
        float h = FieldList.Line;
        if (!p.isExpanded) return h;
        TriggerActionType a = (TriggerActionType)p.FindPropertyRelative("Type").intValue;
        return h + FieldList.Height(p, Head) + FieldList.Height(p, TriggerFields.For(a));
    }

    public override void OnGUI(Rect r, SerializedProperty p, GUIContent label) {
        EditorGUI.BeginProperty(r, label, p);
        TriggerAction a = ScenarioEditing.Target(p) as TriggerAction;
        p.isExpanded = EditorGUI.Foldout(new Rect(r.x, r.y, r.width, EditorGUIUtility.singleLineHeight), p.isExpanded, a != null ? TriggerFields.Describe(a) : label.text, true);
        if (p.isExpanded) {
            EditorGUI.indentLevel++;
            TriggerActionType t = (TriggerActionType)p.FindPropertyRelative("Type").intValue;
            Rect rest = new Rect(r.x, r.y + FieldList.Line, r.width, r.height);
            rest.y = FieldList.Draw(rest, p, Head, f => null);
            FieldList.Draw(rest, p, TriggerFields.For(t), f => TriggerFields.Label(t, f));
            EditorGUI.indentLevel--;
        }
        EditorGUI.EndProperty();
    }
}

[CustomPropertyDrawer(typeof(TriggerNameAttribute))]
class TriggerNameDrawer : PropertyDrawer {
    public override void OnGUI(Rect r, SerializedProperty p, GUIContent label) {
        ScenarioScript s = ScenarioEditing.Script(p);
        if (p.propertyType != SerializedPropertyType.String || s == null) { EditorGUI.PropertyField(r, p, label); return; }
        // (An action's Trigger left empty means the trigger the action is on; elsewhere, empty is nothing.)
        List<string> names = new List<string> { p.name == "Trigger" ? "(this trigger)" : "(none)" };
        foreach (ScenarioTrigger t in s.triggers) if (t != null && !names.Contains(t.Name)) names.Add(t.Name);
        int at = string.IsNullOrEmpty(p.stringValue) ? 0 : names.IndexOf(p.stringValue);
        if (at < 0) { names.Add(p.stringValue + " (missing)"); at = names.Count - 1; }
        EditorGUI.BeginProperty(r, label, p);
        int now = EditorGUI.Popup(r, label.text, at, names.ToArray());
        if (now != at) p.stringValue = now == 0 ? "" : names[now];
        EditorGUI.EndProperty();
    }
}

/// <summary>Sibling fields of a trigger or action, and the board, for the drawers that fill several fields from one drag.</summary>
static class Fields {
    public static SerializedProperty Sibling(SerializedProperty p, string name) {
        string path = p.propertyPath;
        int dot = path.LastIndexOf('.');
        return p.serializedObject.FindProperty(dot >= 0 ? path.Substring(0, dot + 1) + name : name);
    }

    public static HexTile TileAt(int x, int y) { return HexGenerator.hg != null ? HexGenerator.hg.get(x, y, -x - y) : null; }

    /// <summary>The tile a dragged object stands for: a tile, or the tile a unit or building is on.</summary>
    public static HexTile TileOf(Object o) {
        HexTile t = o as HexTile;
        if (t != null) return t;
        Unit u = o as Unit;
        if (u != null) return u.tile;
        Building b = o as Building;
        if (b != null) return b.tile;
        GameObject g = o as GameObject;
        if (g != null) { t = g.GetComponentInParent<HexTile>(); if (t != null) return t; u = g.GetComponentInParent<Unit>(); if (u != null) return u.tile; b = g.GetComponentInParent<Building>(); if (b != null) return b.tile; }
        return null;
    }

    public static void SetTile(SerializedProperty anyField, HexTile t) {
        if (t == null) return;
        SerializedProperty x = Sibling(anyField, "TileX"), y = Sibling(anyField, "TileY");
        if (x != null) x.intValue = t.x;
        if (y != null) y.intValue = t.y;
    }

    /// <summary>On the board: a scene object (a unit or building placed in the game), not a prefab asset.</summary>
    public static bool OnBoard(Object o) { return o != null && !EditorUtility.IsPersistent(o); }

    public static readonly float Line = EditorGUIUtility.singleLineHeight;
    public static readonly float Gap = EditorGUIUtility.standardVerticalSpacing;
}

/// <summary>
/// A name field as an object slot: drag the asset in (a clip, a character, a marker prefab, a unit or building prefab,
/// or a unit or building on the board) and its key is written for you (ScenarioAssets.KeyOf). An asset outside a
/// Resources folder cannot be loaded by the game: dropping one offers to move it into its folder.
/// </summary>
[CustomPropertyDrawer(typeof(ResourceNameAttribute))]
class ResourceNameDrawer : PropertyDrawer {

    static System.Type SlotType(ResourceNameAttribute a) {
        switch (a.kind) {
            case ResourceKind.Unit: return typeof(Unit);
            case ResourceKind.Building: return typeof(Building);
            case ResourceKind.Marker: return typeof(ScenarioMarker);
        }
        return a.type;
    }

    static string Folder(ResourceNameAttribute a) { return a.folder.EndsWith("/") ? a.folder : a.folder + "/"; }

    /// <summary>What the key names now, or null.</summary>
    static Object Current(ResourceNameAttribute a, string key) {
        if (string.IsNullOrEmpty(key)) return null;
        switch (a.kind) {
            case ResourceKind.Unit: return GameAssets.Unit(key);
            case ResourceKind.Building: return GameAssets.Building(key);
            case ResourceKind.Marker: { GameObject g = ScenarioAssets.Load<GameObject>(key, Folder(a)); return g != null ? g.GetComponent<ScenarioMarker>() : null; }
        }
        Object o = key.IndexOf('/') < 0 ? Resources.Load(Folder(a) + key, a.type) : null;
        return o != null ? o : Resources.Load(key, a.type);
    }

    public override float GetPropertyHeight(SerializedProperty p, GUIContent label) {
        return Fields.Line + (Missing(p) ? Fields.Gap + 2f * Fields.Line : 0f);
    }

    bool Missing(SerializedProperty p) {
        return p.propertyType == SerializedPropertyType.String && !string.IsNullOrEmpty(p.stringValue) && Current((ResourceNameAttribute)attribute, p.stringValue) == null;
    }

    public override void OnGUI(Rect r, SerializedProperty p, GUIContent label) {
        if (p.propertyType != SerializedPropertyType.String) { EditorGUI.PropertyField(r, p, label); return; }
        ResourceNameAttribute a = (ResourceNameAttribute)attribute;
        EditorGUI.BeginProperty(r, label, p);
        Rect slot = new Rect(r.x, r.y, r.width, Fields.Line);
        Object now = Current(a, p.stringValue);
        bool board = a.kind == ResourceKind.Unit || a.kind == ResourceKind.Building;
        Object picked = EditorGUI.ObjectField(slot, label, now, SlotType(a), board);
        if (picked != now) Take(a, p, picked);
        if (Missing(p))
            EditorGUI.HelpBox(new Rect(r.x, r.y + Fields.Line + Fields.Gap, r.width, 2f * Fields.Line),
                "'" + p.stringValue + "' is not in Resources/" + a.folder + " (or under any Resources folder): drag the asset in again.", MessageType.Warning);
        EditorGUI.EndProperty();
    }

    static void Take(ResourceNameAttribute a, SerializedProperty p, Object picked) {
        if (picked == null) { p.stringValue = ""; return; }
        if (a.kind == ResourceKind.Unit) {
            Unit u = picked as Unit;
            // A unit on the board stands for its kind (a faction's version: the base unit it stands in for).
            string kind = Fields.OnBoard(u) ? GameController.DisplayName(u, u.owner) : u.name;
            if (GameAssets.Unit(kind) == null && GameAssets.Unit(u.name) == null) {
                EditorUtility.DisplayDialog("Not a unit the game knows", u.name + " is not in Assets/Resources/Units, where the game finds units by name. Move the prefab there first.", "OK");
                return;
            }
            p.stringValue = kind;
            return;
        }
        if (a.kind == ResourceKind.Building) {
            Building b = picked as Building;
            p.stringValue = b.name.Replace("(Clone)", "").Trim();
            // A building on the board: that one (its tile, radius 0).
            if (Fields.OnBoard(b) && b.tile != null) {
                SerializedProperty inArea = Fields.Sibling(p, "InArea"), radius = Fields.Sibling(p, "Radius");
                if (inArea != null) inArea.boolValue = true;
                if (radius != null) radius.intValue = 0;
                Fields.SetTile(p, b.tile);
            }
            return;
        }
        Object asset = a.kind == ResourceKind.Marker ? (Object)((ScenarioMarker)picked).gameObject : picked;
        string path = AssetDatabase.GetAssetPath(asset);
        string rel = ResourcesPath(path);
        if (rel == null) {
            string folder = "Assets/Resources/" + a.folder;
            if (!EditorUtility.DisplayDialog("Move into Resources?", asset.name + " is not in a Resources folder, so the game cannot load it. Move it to " + folder + "?", "Move", "Cancel")) return;
            MakeFolders(folder);
            string to = folder + "/" + System.IO.Path.GetFileName(path);
            string error = AssetDatabase.MoveAsset(path, to);
            if (!string.IsNullOrEmpty(error)) { EditorUtility.DisplayDialog("Could not move it", error, "OK"); return; }
            rel = ResourcesPath(to);
        }
        p.stringValue = ScenarioAssets.KeyOf(rel, a.folder);
    }

    /// <summary>The path under the (last) Resources folder, without the extension; null if not under one.</summary>
    static string ResourcesPath(string assetPath) {
        if (string.IsNullOrEmpty(assetPath)) return null;
        int i = assetPath.LastIndexOf("/Resources/");
        if (i < 0) return null;
        string rel = assetPath.Substring(i + "/Resources/".Length);
        int dot = rel.LastIndexOf('.');
        return dot > 0 ? rel.Substring(0, dot) : rel;
    }

    static void MakeFolders(string folder) {
        string[] parts = folder.Split('/');
        string at = parts[0];
        for (int i = 1; i < parts.Length; i++) {
            string next = at + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(at, parts[i]);
            at = next;
        }
    }
}

/// <summary>
/// A tile (Tile X, with Tile Y after it): a slot to drag a tile, a unit or a building of the board onto (its tile is
/// taken), and Last Clicked (the tile last clicked in the game). Tile X and Tile Y are below, to fine-tune.
/// </summary>
[CustomPropertyDrawer(typeof(TileFieldAttribute))]
class TileFieldDrawer : PropertyDrawer {
    public override float GetPropertyHeight(SerializedProperty p, GUIContent label) { return 2f * Fields.Line + Fields.Gap; }

    public override void OnGUI(Rect r, SerializedProperty p, GUIContent label) {
        SerializedProperty y = Fields.Sibling(p, "TileY");
        HexTile now = Fields.TileAt(p.intValue, y != null ? y.intValue : 0);
        Rect slot = new Rect(r.x, r.y, r.width - 96f, Fields.Line);
        Rect last = new Rect(r.xMax - 92f, r.y, 92f, Fields.Line);
        Object picked = EditorGUI.ObjectField(slot, new GUIContent("Tile", "Drag a tile, a unit or a building from the board (Hierarchy or Scene view) here: its tile is used."), now, typeof(MonoBehaviour), true);
        if (picked != now && picked != null) Fields.SetTile(p, Fields.TileOf(picked));
        GUI.enabled = HexTile.lastClicked != null;
        if (GUI.Button(last, new GUIContent("Last clicked", "The tile last clicked on the board, in play or in the map editor."))) Fields.SetTile(p, HexTile.lastClicked);
        GUI.enabled = true;
        EditorGUI.PropertyField(new Rect(r.x, r.y + Fields.Line + Fields.Gap, r.width, Fields.Line), p, label);
    }
}

/// <summary>
/// A unit filter's name: drag a unit of the board onto the slot to mean that unit (it is given a name of its own if it
/// has none, kept with it in the scenario file), or choose a name already in use (units on the board, and the names
/// this script's Spawn Unit actions give). Spawn Unit's own field is the name it gives, typed.
/// </summary>
[CustomPropertyDrawer(typeof(UnitNameAttribute))]
class UnitNameDrawer : PropertyDrawer {
    public override void OnGUI(Rect r, SerializedProperty p, GUIContent label) {
        if (p.propertyType != SerializedPropertyType.String) { EditorGUI.PropertyField(r, p, label); return; }
        SerializedProperty type = Fields.Sibling(p, "Type");
        bool naming = p.propertyPath.Contains("Actions") && type != null && type.intValue == (int)TriggerActionType.SpawnUnit;
        EditorGUI.BeginProperty(r, label, p);
        if (naming) { p.stringValue = EditorGUI.TextField(r, new GUIContent(label.text, "The name the new units go by: later triggers pick them out by it (their Unit Name)."), p.stringValue); EditorGUI.EndProperty(); return; }

        Unit now = Named(p.stringValue);
        Rect slot = new Rect(r.x, r.y, r.width - 24f, r.height);
        Rect pick = new Rect(r.xMax - 22f, r.y, 22f, r.height);
        Object picked = EditorGUI.ObjectField(slot, new GUIContent(label.text, "Drag a unit from the board to mean that unit. Empty: any unit." + (now == null && !string.IsNullOrEmpty(p.stringValue) ? " Now: units named '" + p.stringValue + "' (none on the board yet)." : "")), now, typeof(Unit), true);
        if (picked != now) {
            Unit u = picked as Unit;
            if (u == null) p.stringValue = "";
            else if (!Fields.OnBoard(u)) EditorUtility.DisplayDialog("Drag a unit from the board", "This field means one unit on the board (or units a Spawn Unit names). Drag the unit itself, from the Hierarchy or the Scene view, while the scenario is open in play or in the map editor.", "OK");
            else p.stringValue = NameOf(u);
        }
        if (EditorGUI.DropdownButton(pick, new GUIContent("▾", "Names in use"), FocusType.Passive)) {
            GenericMenu menu = new GenericMenu();
            SerializedObject so = p.serializedObject;
            string path = p.propertyPath;
            menu.AddItem(new GUIContent("(any unit)"), string.IsNullOrEmpty(p.stringValue), () => Set(so, path, ""));
            foreach (string n in NamesInUse(ScenarioEditing.Script(p))) { string v = n; menu.AddItem(new GUIContent(v), v == p.stringValue, () => Set(so, path, v)); }
            menu.DropDown(pick);
        }
        EditorGUI.EndProperty();
    }

    static void Set(SerializedObject so, string path, string v) { so.Update(); so.FindProperty(path).stringValue = v; so.ApplyModifiedProperties(); }

    static IEnumerable<Unit> Board() {
        if (UnitController.uc == null || UnitController.uc.units == null) yield break;
        foreach (Unit u in UnitController.uc.units) if (u != null) yield return u;
    }

    static Unit Named(string name) {
        if (string.IsNullOrEmpty(name)) return null;
        foreach (Unit u in Board()) if (string.Equals((u.personal_name ?? "").Trim(), name.Trim(), System.StringComparison.OrdinalIgnoreCase)) return u;
        return null;
    }

    /// <summary>The unit's own name; one is made for it (its kind and a number) if it has none, or shares it with another unit.</summary>
    static string NameOf(Unit u) {
        string mine = (u.personal_name ?? "").Trim();
        bool shared = false;
        if (mine.Length > 0) foreach (Unit o in Board()) if (o != u && string.Equals((o.personal_name ?? "").Trim(), mine, System.StringComparison.OrdinalIgnoreCase)) shared = true;
        if (mine.Length > 0 && !shared) return mine;
        string kind = GameController.DisplayName(u, u.owner);
        HashSet<string> used = new HashSet<string>();
        foreach (Unit o in Board()) if (!string.IsNullOrEmpty(o.personal_name)) used.Add(o.personal_name.Trim().ToLowerInvariant());
        int n = 1;
        while (used.Contains((kind + " " + n).ToLowerInvariant())) n++;
        Undo.RecordObject(u, "Name unit");
        u.personal_name = kind + " " + n;
        Debug.Log("Named the " + kind + " at (" + (u.tile != null ? u.tile.x + "," + u.tile.y : "?") + ") '" + u.personal_name + "' so triggers can pick it out: save the scenario to keep the name.");
        return u.personal_name;
    }

    static List<string> NamesInUse(ScenarioScript s) {
        SortedSet<string> names = new SortedSet<string>();
        foreach (Unit u in Board()) if (!string.IsNullOrEmpty(u.personal_name)) names.Add(u.personal_name.Trim());
        if (s != null) foreach (ScenarioTrigger t in s.triggers) if (t != null && t.Actions != null)
            foreach (TriggerAction a in t.Actions) if (a != null && a.Type == TriggerActionType.SpawnUnit && !string.IsNullOrEmpty(a.UnitTag)) names.Add(a.UnitTag.Trim());
        return new List<string>(names);
    }
}

/// <summary>
/// A side: a choice of Any side / the player (human) / the AI / nobody and the scenario's players by name, and a slot to
/// drag a player (its Player component, from the Hierarchy) onto. Kept as the player's id.
/// </summary>
[CustomPropertyDrawer(typeof(SideChoiceAttribute))]
class SideChoiceDrawer : PropertyDrawer {
    static IEnumerable<Player> Live() {
        if (GameController.gc == null || GameController.gc.players == null) yield break;
        foreach (Player p in GameController.gc.players) if (p != null) yield return p;
    }

    public override void OnGUI(Rect r, SerializedProperty p, GUIContent label) {
        if (p.propertyType != SerializedPropertyType.Integer) { EditorGUI.PropertyField(r, p, label); return; }
        bool neutral = ((SideChoiceAttribute)attribute).neutral;
        ScenarioScript s = ScenarioEditing.Script(p);
        List<long> ids = new List<long> { TriggerSide.Any, TriggerSide.Human, TriggerSide.AI };
        List<string> names = new List<string> { "Any side", "The player (any human side)", "The AI (any AI side)" };
        if (neutral) { ids.Add(TriggerSide.Neutral); names.Add("Nobody (neutral)"); }
        foreach (Player pl in Live()) if (!ids.Contains(pl.id)) { ids.Add(pl.id); names.Add(pl.personal_name); }
        if (s != null) foreach (ScenarioScript.KnownSide k in s.sides) if (!ids.Contains(k.id)) { ids.Add(k.id); names.Add(k.name); }
        long v = p.longValue;
        int at = ids.IndexOf(v);
        if (at < 0) { ids.Add(v); names.Add("a side not in this scenario"); at = ids.Count - 1; }
        Player now = null;
        foreach (Player pl in Live()) if (pl.id == v) now = pl;

        EditorGUI.BeginProperty(r, label, p);
        Rect pop = new Rect(r.x, r.y, r.width - 124f, r.height);
        Rect slot = new Rect(r.xMax - 120f, r.y, 120f, r.height);
        int chosen = EditorGUI.Popup(pop, label.text, at, names.ToArray());
        if (chosen != at) p.longValue = ids[chosen];
        int indent = EditorGUI.indentLevel; EditorGUI.indentLevel = 0;
        Object dropped = EditorGUI.ObjectField(slot, new GUIContent("", "Drag a player (its Player object in the Hierarchy) here."), now, typeof(Player), true);
        if (dropped != now && dropped != null) p.longValue = ((Player)dropped).id;
        EditorGUI.indentLevel = indent;
        EditorGUI.EndProperty();
    }
}

/// <summary>A marker's name: typed on Show Marker (it names the marker), chosen on Hide Marker from the names Show Marker actions give.</summary>
[CustomPropertyDrawer(typeof(MarkerNameAttribute))]
class MarkerNameDrawer : PropertyDrawer {
    public override void OnGUI(Rect r, SerializedProperty p, GUIContent label) {
        SerializedProperty type = Fields.Sibling(p, "Type");
        EditorGUI.BeginProperty(r, label, p);
        if (type == null || type.intValue != (int)TriggerActionType.HideMarker) {
            p.stringValue = EditorGUI.TextField(r, new GUIContent(label.text, "Optional: a name a Hide Marker action can pick, to remove this marker later."), p.stringValue);
            EditorGUI.EndProperty();
            return;
        }
        List<string> names = new List<string> { "(every marker)" };
        ScenarioScript s = ScenarioEditing.Script(p);
        if (s != null) foreach (ScenarioTrigger t in s.triggers) if (t != null && t.Actions != null)
            foreach (TriggerAction a in t.Actions) if (a != null && a.Type == TriggerActionType.ShowMarker && !string.IsNullOrEmpty(a.MarkerId) && !names.Contains(a.MarkerId)) names.Add(a.MarkerId);
        int at = string.IsNullOrEmpty(p.stringValue) ? 0 : names.IndexOf(p.stringValue);
        if (at < 0) { names.Add(p.stringValue + " (no Show Marker names it)"); at = names.Count - 1; }
        int chosen = EditorGUI.Popup(r, label.text, at, names.ToArray());
        if (chosen != at) p.stringValue = chosen == 0 ? "" : names[chosen];
        EditorGUI.EndProperty();
    }
}

/// <summary>
/// The Scenario Script's inspector: its triggers (drawn by the drawers above), what is wrong with them, and the file
/// they belong to (Save writes them into it; changes made while playing are lost when play stops unless saved).
/// In the Scene view, every area a trigger or action uses is drawn on the board (the tiles it covers, its name), and
/// the selected trigger's area can be dragged to another tile.
/// </summary>
[CustomEditor(typeof(ScenarioScript))]
class ScenarioScriptEditor : Editor {

    public override void OnInspectorGUI() {
        ScenarioScript s = (ScenarioScript)target;
        ScenarioEditing.Shown = s;
        EditorGUILayout.HelpBox(Application.isPlaying
            ? "This scenario's triggers. Save the scenario as usual (the map editor's Save) to keep changes: they are lost when play stops otherwise. Drag players, units, buildings and tiles from the Hierarchy or Scene view into the fields."
            : "Triggers belong to a scenario file: open the scenario in the map editor (in play) to edit them, and save it there.", MessageType.Info);
        foreach (string problem in s.Validate(false)) EditorGUILayout.HelpBox(problem, MessageType.Warning);
        serializedObject.Update();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("triggers"), true);
        serializedObject.ApplyModifiedProperties();
    }

    void OnSceneGUI() {
        ScenarioScript s = (ScenarioScript)target;
        ScenarioAreas.Draw(s, true);
    }

    [DrawGizmo(GizmoType.NonSelected)]
    static void DrawAreas(ScenarioScript s, GizmoType type) { ScenarioAreas.Draw(s, false); }
}

/// <summary>The areas and tiles of a script's triggers and actions on the board, in the Scene view.</summary>
static class ScenarioAreas {
    static readonly Color TriggerColour = new Color(1f, 0.85f, 0.25f, 1f);
    static readonly Color ActionColour = new Color(0.36f, 0.75f, 1f, 1f);
    const float HexX = 3.55f, HexY = 3.75f;   // a tile's corner, from its centre (HexGenerator's spacing)

    public static void Draw(ScenarioScript s, bool editable) {
        if (s == null || s.triggers == null) return;
        foreach (ScenarioTrigger t in s.triggers) {
            if (t == null) continue;
            bool area = t.Condition == TriggerCondition.UnitEnters || t.Condition == TriggerCondition.UnitsInArea
                     || (t.InArea && System.Array.IndexOf(TriggerFields.For(t.Condition), "InArea") >= 0);
            if (area) Area(t.Name + (t.Enabled ? "" : " (off)"), ref t.TileX, ref t.TileY, ref t.Radius, TriggerColour, editable, s);
            if (t.Actions == null) continue;
            foreach (TriggerAction a in t.Actions) {
                if (a == null) continue;
                string[] f = TriggerFields.For(a.Type);
                bool tile = System.Array.IndexOf(f, "TileX") >= 0;
                bool radius = System.Array.IndexOf(f, "Radius") >= 0 || (a.InArea && System.Array.IndexOf(f, "InArea") >= 0);
                if (!tile && !radius) continue;
                int r = radius ? a.Radius : 0;
                Area(t.Name + ": " + a.Type, ref a.TileX, ref a.TileY, ref r, ActionColour, editable, s);
                if (radius) a.Radius = r;
            }
        }
    }

    static void Area(string name, ref int x, ref int y, ref int radius, Color c, bool editable, ScenarioScript s) {
        Handles.color = new Color(c.r, c.g, c.b, editable ? 0.9f : 0.45f);
        int rr = Mathf.Clamp(radius, 0, 30);
        for (int dx = -rr; dx <= rr; dx++)
            for (int dy = Mathf.Max(-rr, -dx - rr); dy <= Mathf.Min(rr, -dx + rr); dy++) Hex(ScenarioGame.TilePosition(x + dx, y + dy));
        Vector3 centre = ScenarioGame.TilePosition(x, y);
        Handles.Label(centre + new Vector3(-HexX, HexY + 1.2f, 0f), name + " (" + x + "," + y + ")" + (radius > 0 ? " r" + radius : ""));
        if (!editable) return;
        float size = HandleUtility.GetHandleSize(centre) * 0.12f;
        Vector3 moved = Handles.FreeMoveHandle(centre, size, Vector3.zero, Handles.DotHandleCap);
        if (moved != centre) {
            int nx, ny;
            Nearest(moved, out nx, out ny);
            if (nx != x || ny != y) { Undo.RecordObject(s, "Move scenario area"); x = nx; y = ny; EditorUtility.SetDirty(s); }
        }
    }

    static void Hex(Vector3 c) {
        Vector3[] pts = new Vector3[7];
        for (int i = 0; i < 7; i++) {
            float a = Mathf.PI / 3f * i;
            pts[i] = c + new Vector3(Mathf.Cos(a) * HexX, Mathf.Sin(a) * HexY, 0f);
        }
        Handles.DrawPolyLine(pts);
    }

    /// <summary>The tile nearest a world point (the board's tiles in play; else by the board's layout).</summary>
    static void Nearest(Vector3 p, out int x, out int y) {
        x = 0; y = 0;
        float best = float.MaxValue;
        int w = HexGenerator.hg != null ? HexGenerator.hg.width : 0;
        HexGenerator g = HexGenerator.hg != null ? HexGenerator.hg : Object.FindAnyObjectByType<HexGenerator>();
        if (g != null) w = Mathf.Max(w, g.width);
        int half = Mathf.Max(8, w / 2 + 1);
        for (int i = -half; i <= half; i++)
            for (int j = -half; j <= half; j++) {
                Vector3 q = ScenarioGame.TilePosition(i, j);
                float d = (q.x - p.x) * (q.x - p.x) + (q.y - p.y) * (q.y - p.y);
                if (d < best) { best = d; x = i; y = j; }
            }
    }
}
