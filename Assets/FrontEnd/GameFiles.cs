using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Defective.JSON;

/// <summary>A scenario or saved game on disk.</summary>
public class GameFile {
    public string name;
    public string path;
    public DateTime modified;
    /// <summary>Made by the player (the map editor's folder) rather than shipped with the game.</summary>
    public bool user;
}

/// <summary>
/// Where scenarios and saved games are, and what is in one (for the lists in the menus). Scenarios: the folders in
/// FrontEndSettings.scenarioFolders, then the player's own (userScenarioFolder); saved games: savesFolder — a folder of
/// their own, never mixed with the scenarios.
/// </summary>
public static class GameFiles {

    static FrontEndSettings S { get { return FrontEndSettings.Get; } }

    public static string UserScenarioFolder { get { return Path.Combine(Application.persistentDataPath, S.userScenarioFolder); } }
    public static string SavesFolder { get { return Path.Combine(Application.persistentDataPath, S.savesFolder); } }

    /// <summary>Every scenario, by name (a player's own scenario of the same name as a shipped one comes after it).</summary>
    public static List<GameFile> Scenarios() {
        List<GameFile> list = new List<GameFile>();
        foreach (string rel in S.scenarioFolders) {
            if (string.IsNullOrEmpty(rel)) continue;
            List(Path.Combine(Application.dataPath, rel), false, list);
        }
        List(UserScenarioFolder, true, list);
        list.Sort((a, b) => a.user != b.user ? (a.user ? 1 : -1) : string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    /// <summary>Every saved game, the newest first.</summary>
    public static List<GameFile> Saves() {
        List<GameFile> list = new List<GameFile>();
        List(SavesFolder, true, list);
        list.Sort((a, b) => b.modified.CompareTo(a.modified));
        return list;
    }

    static void List(string folder, bool user, List<GameFile> into) {
        if (!Directory.Exists(folder)) return;
        foreach (string f in Directory.GetFiles(folder, "*" + S.fileExtension)) {
            into.Add(new GameFile { name = Path.GetFileNameWithoutExtension(f), path = f, modified = File.GetLastWriteTime(f), user = user });
        }
    }

    /// <summary>A name made safe for a file: letters, digits, spaces, '-' and '_'.</summary>
    public static string SafeName(string name) {
        StringBuilder sb = new StringBuilder();
        foreach (char c in (name ?? "").Trim()) if (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_') sb.Append(c);
        return sb.ToString().Trim();
    }

    /// <summary>The path a saved game of this name is written to (the saves folder made if need be).</summary>
    public static string SavePath(string name) {
        Directory.CreateDirectory(SavesFolder);
        return Path.Combine(SavesFolder, SafeName(name) + S.fileExtension);
    }

    /// <summary>The path a new scenario of this name is written to by the map editor (its folder made if need be).</summary>
    public static string UserScenarioPath(string name) {
        Directory.CreateDirectory(UserScenarioFolder);
        return Path.Combine(UserScenarioFolder, SafeName(name) + S.fileExtension);
    }

    /// <summary>
    /// What a file holds, in a few lines for the menus: its sides (and factions), the board's size, fog and purchases,
    /// the round it was saved on. Empty if it cannot be read.
    /// </summary>
    public static string Describe(string path) {
        try {
            JSONObject obj = new JSONObject(File.ReadAllText(path));
            StringBuilder sb = new StringBuilder();
            // The scenario's own description first (Scenario Script ▸ Description).
            JSONObject about = obj.GetField("description");
            if (about != null && about.isString && about.stringValue.Length > 0) sb.Append(about.stringValue).Append("\n\n");
            JSONObject players = obj.GetField("players");
            if (players != null && players.list != null) {
                List<string> sides = new List<string>();
                foreach (JSONObject p in players.list) {
                    string n = p.GetField("personal_name") != null ? p.GetField("personal_name").stringValue : "?";
                    string f = p.GetField("factionName") != null ? p.GetField("factionName").stringValue : "";
                    sides.Add(string.IsNullOrEmpty(f) ? n : n + " (" + f + ")");
                }
                sb.Append(string.Join(" v ", sides.ToArray())).Append('\n');
            }
            JSONObject w = obj.GetField("width");
            if (w != null) sb.Append("Board ").Append(w.intValue).Append(" wide");
            sb.Append(", fog ").Append(Value(obj, "fog", "off")).Append(", purchases ").Append(Value(obj, "purchases", "on"));
            JSONObject day = obj.GetField("day");
            if (day != null && day.intValue > 0) sb.Append("\nRound ").Append(day.intValue);
            JSONObject test = obj.GetField("test");
            if (test != null && test.GetField("question") != null) sb.Append('\n').Append(test.GetField("question").stringValue);
            sb.Append("\n").Append(File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm"));
            return sb.ToString();
        } catch (Exception e) {
            return "Could not read this file: " + e.Message;
        }
    }

    static string Value(JSONObject obj, string key, string otherwise) {
        JSONObject f = obj.GetField(key);
        if (f == null) { JSONObject t = obj.GetField("test"); if (t != null) f = t.GetField(key); }
        return f != null && !string.IsNullOrEmpty(f.stringValue) ? f.stringValue : otherwise;
    }
}
