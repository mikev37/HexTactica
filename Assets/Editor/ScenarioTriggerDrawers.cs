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
            case TriggerCondition.DayReached: return new[] { "Value" };
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
                if (c == TriggerCondition.MoneyAtLeast || c == TriggerCondition.MoneyAtMost || c == TriggerCondition.TurnOver || c == TriggerCondition.TurnStarted) return "Whose";
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
            case TriggerCondition.DayReached: return "day " + t.Value;
            case TriggerCondition.UnitsInArea: return Mathf.Max(1, t.Count) + "+ " + Units(Side(t.Side) + "'s", t.UnitKind, t.UnitTag, false, 0, 0, 0) + " in " + area;
            case TriggerCondition.MoneyAtLeast: return Side(t.Side) + " has " + t.Value + "+ gold";
            case TriggerCondition.MoneyAtMost: return Side(t.Side) + " has " + t.Value + " gold or less";
            case TriggerCondition.UnitBuilt: return units + " built" + times;
            case TriggerCondition.UnitDamaged: return units + " damaged to " + t.Value + " or less" + times;
            case TriggerCondition.UnitHealed: return units + " healed to " + t.Value + " or more" + times;
            case TriggerCondition.TurnOver: return Side(t.Side) + "'s turn ends" + times;
            case TriggerCondition.TurnStarted: return Side(t.Side) + "'s turn starts" + times;
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
            FieldList.Draw(rest, p, Timing, f => null);
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

[CustomPropertyDrawer(typeof(ResourceNameAttribute))]
class ResourceNameDrawer : PropertyDrawer {
    static readonly Dictionary<string, string[]> cache = new Dictionary<string, string[]>();

    static string[] Names(ResourceNameAttribute a) {
        string key = a.folder + "|" + a.kind;
        string[] n;
        if (cache.TryGetValue(key, out n)) return n;
        SortedSet<string> found = new SortedSet<string>();
        foreach (Object o in Resources.LoadAll(a.folder)) {
            if (o == null) continue;
            GameObject g = o as GameObject;
            if (a.kind == ResourceKind.Unit && (g == null || g.GetComponent<Unit>() == null)) continue;
            if (a.kind == ResourceKind.Building && (g == null || g.GetComponent<Building>() == null)) continue;
            if (a.kind == ResourceKind.Any && !(o is GameObject || o is ScriptableObject || o is AudioClip)) continue;
            found.Add(o.name);
        }
        n = new List<string>(found).ToArray();
        cache[key] = n;
        return n;
    }

    public override void OnGUI(Rect r, SerializedProperty p, GUIContent label) {
        if (p.propertyType != SerializedPropertyType.String) { EditorGUI.PropertyField(r, p, label); return; }
        ResourceNameAttribute a = (ResourceNameAttribute)attribute;
        List<string> names = new List<string> { "(none)" };
        names.AddRange(Names(a));
        int at = string.IsNullOrEmpty(p.stringValue) ? 0 : names.IndexOf(p.stringValue);
        if (at < 0) { names.Add(p.stringValue + " (not in Resources/" + a.folder + ")"); at = names.Count - 1; }
        EditorGUI.BeginProperty(r, label, p);
        int now = EditorGUI.Popup(r, label.text, at, names.ToArray());
        if (now != at) p.stringValue = now == 0 ? "" : names[now];
        EditorGUI.EndProperty();
    }
}

[CustomPropertyDrawer(typeof(SideChoiceAttribute))]
class SideChoiceDrawer : PropertyDrawer {
    public override void OnGUI(Rect r, SerializedProperty p, GUIContent label) {
        if (p.propertyType != SerializedPropertyType.Integer) { EditorGUI.PropertyField(r, p, label); return; }
        bool neutral = ((SideChoiceAttribute)attribute).neutral;
        ScenarioScript s = ScenarioEditing.Script(p);
        List<long> ids = new List<long> { TriggerSide.Any, TriggerSide.Human, TriggerSide.AI };
        List<string> names = new List<string> { "Any side", "The player (human)", "The AI" };
        if (neutral) { ids.Add(TriggerSide.Neutral); names.Add("Nobody (neutral)"); }
        if (s != null) foreach (ScenarioScript.KnownSide k in s.sides) { ids.Add(k.id); names.Add(k.name + " (id " + k.id + ")"); }
        long v = p.longValue;
        int at = ids.IndexOf(v);
        if (at < 0) { ids.Add(v); names.Add("side id " + v); at = ids.Count - 1; }
        EditorGUI.BeginProperty(r, label, p);
        Rect pop = new Rect(r.x, r.y, r.width - 70f, r.height);
        Rect num = new Rect(r.xMax - 66f, r.y, 66f, r.height);
        int now = EditorGUI.Popup(pop, label.text, at, names.ToArray());
        if (now != at) p.longValue = ids[now];
        int indent = EditorGUI.indentLevel; EditorGUI.indentLevel = 0;
        long typed = EditorGUI.LongField(num, p.longValue);
        if (typed != p.longValue) p.longValue = typed;
        EditorGUI.indentLevel = indent;
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
        EditorGUILayout.LabelField("Scenario file", string.IsNullOrEmpty(s.scenarioPath) ? "(none: this scene's own)" : s.scenarioPath);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Open scenario file...")) ScenarioTriggerFiles.Open();
        GUI.enabled = !string.IsNullOrEmpty(s.scenarioPath);
        if (GUILayout.Button("Save triggers into it")) ScenarioTriggerFiles.Save(s, s.scenarioPath);
        GUI.enabled = true;
        if (GUILayout.Button("Save as...")) {
            string path = EditorUtility.OpenFilePanel("Save the triggers into a scenario file", ScenarioTriggerFiles.Folder, "dat");
            if (!string.IsNullOrEmpty(path)) ScenarioTriggerFiles.Save(s, path);
        }
        EditorGUILayout.EndHorizontal();
        if (Application.isPlaying)
            EditorGUILayout.HelpBox("Playing: changes here apply at once, and are lost when play stops unless you save them into the file. Saved without the game's progress. Level clock " + s.Clock.ToString("0.0") + " s.", MessageType.Info);
        if (HexTile.lastClicked != null)
            EditorGUILayout.LabelField("Last tile clicked", "(" + HexTile.lastClicked.x + "," + HexTile.lastClicked.y + ")  — use it as a trigger's Tile X, Tile Y");
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

/// <summary>
/// Scenario triggers in their files, from the editor: HexTactica ▸ Scenario Triggers ▸ Open Scenario File… puts a
/// file's triggers on a Scenario Script in the open scene (not saved with the scene) to edit in the inspector; its
/// Save button writes them back into the file's "script", touching nothing else in it.
/// </summary>
static class ScenarioTriggerFiles {
    public static string Folder { get { return Path.Combine(Application.dataPath, "Resources/Scenarios"); } }

    [MenuItem("HexTactica/Scenario Triggers/Open Scenario File...")]
    public static void Open() {
        string path = EditorUtility.OpenFilePanel("Edit the triggers of a scenario file", Folder, "dat");
        if (string.IsNullOrEmpty(path)) return;
        ScenarioScript s = Application.isPlaying ? ScenarioScript.Current : Editing();
        Defective.JSON.JSONObject file = new Defective.JSON.JSONObject(File.ReadAllText(path));
        if (Application.isPlaying) {
            // In play the board is the game's: only the triggers are taken from the file (load the scenario itself to play it).
            s.Load(file.GetField(ScenarioScript.FileKey), path);
        } else {
            s.Load(file.GetField(ScenarioScript.FileKey), path);
            s.sides.Clear();
            Defective.JSON.JSONObject players = file.GetField("players");
            if (players != null && players.list != null)
                foreach (Defective.JSON.JSONObject p in players.list) {
                    Defective.JSON.JSONObject id = p.GetField("id"), name = p.GetField("personal_name");
                    if (id != null) s.sides.Add(new ScenarioScript.KnownSide { id = id.longValue, name = name != null ? name.stringValue : "side " + id.longValue });
                }
        }
        Selection.activeObject = s.gameObject;
        Debug.Log("Editing the triggers of " + path + " (" + s.triggers.Count + "): select '" + s.gameObject.name + "' and use Save in its inspector.");
    }

    /// <summary>The Scenario Script to edit files on outside play: made in the open scene, never saved with it.</summary>
    static ScenarioScript Editing() {
        foreach (ScenarioScript s in Object.FindObjectsByType<ScenarioScript>(FindObjectsSortMode.None)) if (s != null) return s;
        GameObject g = new GameObject("Scenario Script (editing a file)");
        g.hideFlags = HideFlags.DontSave;
        return g.AddComponent<ScenarioScript>();
    }

    /// <summary>Write <paramref name="s"/>'s triggers (as authored: no game progress) into the scenario file at <paramref name="path"/>.</summary>
    public static void Save(ScenarioScript s, string path) {
        if (s == null || string.IsNullOrEmpty(path) || !File.Exists(path)) { Debug.LogError("No scenario file " + path + " to save the triggers into."); return; }
        Defective.JSON.JSONObject file = new Defective.JSON.JSONObject(File.ReadAllText(path));
        if (s.triggers.Count > 0) file.SetField(ScenarioScript.FileKey, s.ToJson(false));
        else file.RemoveField(ScenarioScript.FileKey);
        File.WriteAllText(path, file.Print(true));
        s.scenarioPath = path;
        AssetDatabase.Refresh();
        Debug.Log("Saved " + s.triggers.Count + " trigger(s) into " + path);
        foreach (string p in s.Validate(false)) Debug.LogWarning("Scenario script (" + path + "): " + p);
    }
}
