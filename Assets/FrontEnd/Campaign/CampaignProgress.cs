using System;
using System.Collections.Generic;
using System.IO;
using Defective.JSON;
using UnityEngine;

/// <summary>
/// How far the player has got in the campaign: for each mission, whether its flag has been unlocked, whether it is
/// closed off, attempted, won, and the stars earned so far (the best of every attempt: a star once earned is kept).
/// Kept in one file in the player's data folder (campaign.dat), written whenever it changes.
/// </summary>
public static class CampaignProgress {

    public class Entry {
        public bool unlocked, blocked, attempted, completed;
        public MissionStars stars;
        /// <summary>The fewest turns it was won in (0: never won, or not recorded).</summary>
        public int bestTurns;
    }

    /// <summary>What finishing a mission changed: shown on the result screen, then played out on the campaign screen.</summary>
    public class Outcome {
        public Mission mission;
        public MissionTally tally;
        /// <summary>The stars this attempt earned, and those of them the mission did not have before.</summary>
        public MissionStars earned, gained;
        /// <summary>Its first win.</summary>
        public bool firstWin;
        /// <summary>The missions whose flags this win put on the map.</summary>
        public List<Mission> unlocked = new List<Mission>();
    }

    public const string FileName = "campaign.dat";

    static Dictionary<string, Entry> entries;

    /// <summary>The file progress is kept in. Set to "" to keep it in memory only (the headless checks).</summary>
    public static string FilePath = null;
    static string Path_ { get { return FilePath ?? Path.Combine(Application.persistentDataPath, FileName); } }

    /// <summary>The result of the mission just played, waiting for the campaign screen to show it (null: nothing new).</summary>
    public static Outcome Pending;

    static Dictionary<string, Entry> Entries { get { if (entries == null) Load(); return entries; } }

    static Entry Of(Mission m, bool make) {
        Entry e;
        if (Entries.TryGetValue(m.Key, out e)) return e;
        e = new Entry();
        if (make) entries[m.Key] = e;
        return e;
    }

    /// <summary>The mission's progress (all false for one never touched).</summary>
    public static Entry Get(Mission m) { return m != null ? Of(m, false) : new Entry(); }

    /// <summary>Its flag is on the map: there from the start, unlocked by a win, or already played.</summary>
    public static bool Visible(Mission m) {
        if (m == null) return false;
        Entry e = Get(m);
        return m.availableAtStart || e.unlocked || e.attempted || e.completed;
    }

    /// <summary>Closed off by another mission having been attempted (one already won is never closed).</summary>
    public static bool Blocked(Mission m) {
        Entry e = Get(m);
        return e.blocked && !e.completed;
    }

    /// <summary>Its flag is on the map and it can be started.</summary>
    public static bool Playable(Mission m) { return Visible(m) && !Blocked(m); }

    /// <summary>The mission is being started: it counts as attempted, and the missions it blocks are closed.</summary>
    public static void Attempt(Mission m) {
        if (m == null) return;
        Of(m, true).attempted = true;
        if (m.blocks != null) foreach (Mission b in m.blocks) if (b != null && b != m) Of(b, true).blocked = true;
        Save();
    }

    /// <summary>
    /// The mission has ended with this result: a win is kept with its stars (added to those it had) and unlocks its
    /// missions. Returns what changed, also left in <see cref="Pending"/> for the campaign screen.
    /// </summary>
    public static Outcome Finish(Mission m, MissionTally tally) {
        Outcome o = new Outcome { mission = m, tally = tally };
        if (m == null) return o;
        Entry e = Of(m, true);
        e.attempted = true;
        if (tally.won) {
            o.earned = MissionRating.Stars(m, tally);
            o.gained = o.earned & ~e.stars;
            o.firstWin = !e.completed;
            e.completed = true;
            e.stars |= o.earned;
            if (tally.recorded && tally.turns > 0 && (e.bestTurns == 0 || tally.turns < e.bestTurns)) e.bestTurns = tally.turns;
            if (m.unlocks != null) foreach (Mission u in m.unlocks) {
                if (u == null || u == m) continue;
                bool was = Playable(u);
                Of(u, true).unlocked = true;
                if (!was && Playable(u) && !o.unlocked.Contains(u)) o.unlocked.Add(u);
            }
        }
        Save();
        Pending = o;
        return o;
    }

    /// <summary>The pending result, handed over once (the campaign screen plays it out).</summary>
    public static Outcome TakePending() { Outcome o = Pending; Pending = null; return o; }

    /// <summary>Forget everything: the campaign as new. (The file is rewritten empty, not deleted.)</summary>
    public static void Reset() {
        entries = new Dictionary<string, Entry>();
        Pending = null;
        Save();
    }

    /// <summary>Read the file again (missing or unreadable: no progress).</summary>
    public static void Load() {
        entries = new Dictionary<string, Entry>();
        string path = Path_;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        try {
            JSONObject obj = new JSONObject(File.ReadAllText(path));
            JSONObject list = obj.GetField("missions");
            if (list == null || list.list == null) return;
            foreach (JSONObject j in list.list) {
                JSONObject key = j.GetField("mission");
                if (key == null || string.IsNullOrEmpty(key.stringValue)) continue;
                entries[key.stringValue] = new Entry {
                    unlocked = Flag(j, "unlocked"), blocked = Flag(j, "blocked"), attempted = Flag(j, "attempted"), completed = Flag(j, "completed"),
                    stars = (MissionStars)Number(j, "stars"), bestTurns = Number(j, "bestTurns")
                };
            }
        } catch (Exception e) {
            Debug.LogWarning("Campaign progress could not be read (" + path + "): " + e.Message + ". The campaign starts as new.");
        }
    }

    static bool Flag(JSONObject j, string key) { JSONObject f = j.GetField(key); return f != null && f.boolValue; }
    static int Number(JSONObject j, string key) { JSONObject f = j.GetField(key); return f != null ? f.intValue : 0; }

    public static void Save() {
        string path = Path_;
        if (string.IsNullOrEmpty(path)) return;
        try {
            JSONObject list = new JSONObject(JSONObject.Type.Array);
            List<string> keys = new List<string>(Entries.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string k in keys) {
                Entry e = entries[k];
                JSONObject j = new JSONObject(JSONObject.Type.Object);
                j.AddField("mission", JSONObject.CreateStringObject(k));
                j.AddField("unlocked", e.unlocked); j.AddField("blocked", e.blocked);
                j.AddField("attempted", e.attempted); j.AddField("completed", e.completed);
                j.AddField("stars", (int)e.stars); j.AddField("bestTurns", e.bestTurns);
                list.Add(j);
            }
            JSONObject obj = new JSONObject(JSONObject.Type.Object);
            obj.AddField("version", 1);
            obj.AddField("missions", list);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, obj.Print(true));
        } catch (Exception e) {
            Debug.LogWarning("Campaign progress could not be saved (" + path + "): " + e.Message);
        }
    }
}
