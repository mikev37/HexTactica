using System;
using System.Collections.Generic;
using Defective.JSON;

/// <summary>
/// What the game needs to know of a campaign mission once its campaign scene is gone: the mission being played, its
/// briefing and result screens, the campaign's progress, and a game saved during it. Made from the scene's Mission
/// (Mission.Info); the missions it unlocks and blocks are kept by key.
/// </summary>
[Serializable]
public class MissionInfo {
    /// <summary>Its campaign scene and its name there ("Campaign/Tutorial 1"): what progress is kept under.</summary>
    public string key = "";
    /// <summary>The campaign scene it belongs to: where the way out of the mission leads.</summary>
    public string campaignScene = "";
    public string title = "";
    public string scenario = "";
    public string factionName = "";
    public string briefing = "";
    public string objectives = "";
    public bool availableAtStart;
    public List<string> unlocks = new List<string>();
    public List<string> blocks = new List<string>();
    public int parTurns, parKills, parLosses;

    [NonSerialized] Faction factionCache;
    [NonSerialized] bool factionLooked;

    public string Title { get { return string.IsNullOrEmpty(title) ? Name : title; } }
    /// <summary>Its name in its scene (the key's last part).</summary>
    public string Name { get { int i = key.LastIndexOf('/'); return i >= 0 ? key.Substring(i + 1) : key; } }
    public string ScenarioName { get { return string.IsNullOrEmpty(scenario) ? Name : scenario; } }

    /// <summary>Whose point of view it is told from (the faction asset of that name), or null.</summary>
    public Faction faction {
        get {
            if (!factionLooked) { factionLooked = true; factionCache = string.IsNullOrEmpty(factionName) ? null : GameAssets.Faction(factionName); }
            return factionCache;
        }
        set { factionCache = value; factionLooked = true; factionName = value != null ? value.name : ""; }
    }

    /// <summary>The key a mission named <paramref name="name"/> in <paramref name="scene"/> is kept under.</summary>
    public static string KeyOf(string scene, string name) { return (scene ?? "") + "/" + (name ?? ""); }

    public JSONObject ToJson() {
        JSONObject j = new JSONObject(JSONObject.Type.Object);
        j.AddField("key", Str(key)); j.AddField("campaignScene", Str(campaignScene)); j.AddField("title", Str(title));
        j.AddField("scenario", Str(scenario)); j.AddField("faction", Str(factionName));
        j.AddField("briefing", Str(briefing)); j.AddField("objectives", Str(objectives));
        j.AddField("availableAtStart", availableAtStart);
        j.AddField("unlocks", List(unlocks)); j.AddField("blocks", List(blocks));
        j.AddField("parTurns", parTurns); j.AddField("parKills", parKills); j.AddField("parLosses", parLosses);
        return j;
    }

    /// <summary>A mission read back from ToJson's form, or null if <paramref name="j"/> is not one.</summary>
    public static MissionInfo FromJson(JSONObject j) {
        if (j == null || !j.isObject) return null;
        MissionInfo m = new MissionInfo {
            key = Text(j, "key"), campaignScene = Text(j, "campaignScene"), title = Text(j, "title"), scenario = Text(j, "scenario"),
            factionName = Text(j, "faction"), briefing = Text(j, "briefing"), objectives = Text(j, "objectives"),
            availableAtStart = j.GetField("availableAtStart") != null && j.GetField("availableAtStart").boolValue,
            unlocks = Texts(j, "unlocks"), blocks = Texts(j, "blocks"),
            parTurns = Number(j, "parTurns"), parKills = Number(j, "parKills"), parLosses = Number(j, "parLosses")
        };
        return string.IsNullOrEmpty(m.key) ? null : m;
    }

    static JSONObject Str(string s) { return JSONObject.CreateStringObject(s ?? ""); }
    static JSONObject List(List<string> l) {
        JSONObject a = new JSONObject(JSONObject.Type.Array);
        if (l != null) foreach (string s in l) a.Add(Str(s));
        return a;
    }
    static string Text(JSONObject j, string k) { JSONObject f = j.GetField(k); return f != null && f.isString ? f.stringValue : ""; }
    static int Number(JSONObject j, string k) { JSONObject f = j.GetField(k); return f != null ? f.intValue : 0; }
    static List<string> Texts(JSONObject j, string k) {
        List<string> r = new List<string>();
        JSONObject a = j.GetField(k);
        if (a != null && a.list != null) foreach (JSONObject s in a.list) if (s != null && s.isString) r.Add(s.stringValue);
        return r;
    }
}
