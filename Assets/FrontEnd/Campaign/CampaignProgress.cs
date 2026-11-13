using System;
using System.Collections.Generic;
using System.IO;
using Defective.JSON;
using UnityEngine;

/// <summary>
/// How far the player has got in the campaigns: for each mission (by its key, MissionInfo.key: its scene and name), whether its flag has been unlocked, whether it is
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
        public MissionInfo mission;
        public MissionTally tally;
        /// <summary>The stars this attempt earned, and those of them the mission did not have before.</summary>
        public MissionStars earned, gained;
        /// <summary>Its first win.</summary>
        public bool firstWin;
        /// <summary>The campaigns (scene names) this win opened for the first time.</summary>
        public List<string> unlockedCampaigns = new List<string>();
        /// <summary>The keys of the missions this win unlocked for the first time (the campaign screen shows the flags of those not already on the map appearing).</summary>
        public List<string> unlocked = new List<string>();
    }

    public const string FileName = "campaign.dat";

    static Dictionary<string, Entry> entries;
    /// <summary>Campaigns (scene names) opened by a mission won.</summary>
    static HashSet<string> campaigns = new HashSet<string>();

    /// <summary>The file progress is kept in. Set to "" to keep it in memory only (the headless checks).</summary>
    public static string FilePath = null;
    static string Path_ { get { return FilePath ?? Path.Combine(Application.persistentDataPath, FileName); } }

    /// <summary>The campaign (scene name) last looked at or played, for this run: the hub opens looking at it.</summary>
    public static string Focus = "";

    /// <summary>The result of the mission just played, waiting for the campaign screen to show it (null: nothing new).</summary>
    public static Outcome Pending;

    static Dictionary<string, Entry> Entries { get { if (entries == null) Load(); return entries; } }

    static Entry Of(string key, bool make) {
        Entry e;
        if (Entries.TryGetValue(key ?? "", out e)) return e;
        e = new Entry();
        if (make) entries[key ?? ""] = e;
        return e;
    }

    /// <summary>The mission's progress (all false for one never touched).</summary>
    public static Entry Get(MissionInfo m) { return m != null ? Of(m.key, false) : new Entry(); }

    /// <summary>Its flag is on the map: there from the start, unlocked by a win, or already played.</summary>
    public static bool Visible(MissionInfo m) {
        if (m == null) return false;
        Entry e = Get(m);
        return m.availableAtStart || e.unlocked || e.attempted || e.completed;
    }

    /// <summary>Closed off by another mission having been attempted (one already won is never closed).</summary>
    public static bool Blocked(MissionInfo m) {
        Entry e = Get(m);
        return e.blocked && !e.completed;
    }

    /// <summary>Its flag is on the map and it can be started.</summary>
    public static bool Playable(MissionInfo m) { return Visible(m) && !Blocked(m); }

    /// <summary>The mission is being started: it counts as attempted, and the missions it blocks are closed.</summary>
    public static void Attempt(MissionInfo m) {
        if (m == null) return;
        Of(m.key, true).attempted = true;
        if (m.blocks != null) foreach (string b in m.blocks) if (!string.IsNullOrEmpty(b) && b != m.key) Of(b, true).blocked = true;
        Save();
    }

    /// <summary>
    /// The mission has ended with this result: a win is kept with its stars (added to those it had) and unlocks its
    /// missions. Returns what changed, also left in <see cref="Pending"/> for the campaign screen.
    /// </summary>
    public static Outcome Finish(MissionInfo m, MissionTally tally) {
        Outcome o = new Outcome { mission = m, tally = tally };
        if (m == null) return o;
        Entry e = Of(m.key, true);
        e.attempted = true;
        if (tally.won) {
            o.earned = MissionRating.Stars(m, tally);
            o.gained = o.earned & ~e.stars;
            o.firstWin = !e.completed;
            e.completed = true;
            e.stars |= o.earned;
            if (tally.recorded && tally.turns > 0 && (e.bestTurns == 0 || tally.turns < e.bestTurns)) e.bestTurns = tally.turns;
            if (m.unlocks != null) foreach (string u in m.unlocks) {
                if (string.IsNullOrEmpty(u) || u == m.key) continue;
                Entry ue = Of(u, true);
                bool was = ue.unlocked;
                ue.unlocked = true;
                if (!was && !ue.blocked && !o.unlocked.Contains(u)) o.unlocked.Add(u);
            }
            if (m.unlocksCampaigns != null) foreach (string c in m.unlocksCampaigns)
                if (!string.IsNullOrEmpty(c) && campaigns.Add(c)) o.unlockedCampaigns.Add(c);
        }
        Save();
        Pending = o;
        return o;
    }

    /// <summary>A campaign (its scene's name) opened by a mission won.</summary>
    public static bool CampaignUnlocked(string campaign) { if (entries == null) Load(); return !string.IsNullOrEmpty(campaign) && campaigns.Contains(campaign); }

    /// <summary>The pending result, handed over once (the campaign screen plays it out).</summary>
    public static Outcome TakePending() { Outcome o = Pending; Pending = null; return o; }

    /// <summary>Forget everything: the campaign as new. (The file is rewritten empty, not deleted.)</summary>
    public static void Reset() {
        entries = new Dictionary<string, Entry>();
        campaigns = new HashSet<string>();
        Pending = null;
        Save();
    }

    /// <summary>Read the file again (missing or unreadable: no progress).</summary>
    public static void Load() {
        entries = new Dictionary<string, Entry>();
        campaigns = new HashSet<string>();
        string path = Path_;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        try {
            JSONObject obj = new JSONObject(File.ReadAllText(path));
            JSONObject open = obj.GetField("campaigns");
            if (open != null && open.list != null) foreach (JSONObject c in open.list) if (c != null && c.isString && !string.IsNullOrEmpty(c.stringValue)) campaigns.Add(c.stringValue);
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
            JSONObject open = new JSONObject(JSONObject.Type.Array);
            List<string> names = new List<string>(campaigns);
            names.Sort(StringComparer.Ordinal);
            foreach (string c in names) open.Add(JSONObject.CreateStringObject(c));
            obj.AddField("campaigns", open);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, obj.Print(true));
        } catch (Exception e) {
            Debug.LogWarning("Campaign progress could not be saved (" + path + "): " + e.Message);
        }
    }
}
