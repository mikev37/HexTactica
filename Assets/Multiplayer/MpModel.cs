using System;
using System.Collections.Generic;
using System.Globalization;
using Defective.JSON;

namespace HexTactica.Multiplayer {

    /// <summary>Who plays a side of a networked game.</summary>
    public enum MpSlotKind {
        /// <summary>Nobody yet: a joined player can claim it (in a game under way: its player left, and the host decides).</summary>
        Open = 0,
        /// <summary>The AI (run on the device of the next player to move after it).</summary>
        AI = 1,
        /// <summary>A player (MpSlot.player): the host's own device plays every side the host holds ("Local").</summary>
        Player = 2
    }

    public enum MpState { Lobby = 0, Playing = 1, Over = 2, Terminated = 3 }

    /// <summary>One side of the game and who plays it.</summary>
    public class MpSlot {
        public long side;
        public string sideName = "";
        public MpSlotKind kind = MpSlotKind.Open;
        /// <summary>The player's id (the store's), "" none.</summary>
        public string player = "";
        public string playerName = "";
        /// <summary>The AI profile's name, "" the default.</summary>
        public string profile = "";
        public string faction = "";
        public bool factionLocked;
        public int team;

        public JSONObject ToJson() {
            JSONObject j = new JSONObject(JSONObject.Type.Object);
            j.AddField("side", side); j.AddField("sideName", sideName); j.AddField("kind", (int)kind);
            j.AddField("player", player); j.AddField("playerName", playerName); j.AddField("profile", profile);
            j.AddField("faction", faction); j.AddField("factionLocked", factionLocked); j.AddField("team", team);
            return j;
        }
        public static MpSlot FromJson(JSONObject j) {
            return new MpSlot {
                side = MpJson.Long(j, "side"), sideName = MpJson.Str(j, "sideName"), kind = (MpSlotKind)MpJson.Int(j, "kind"),
                player = MpJson.Str(j, "player"), playerName = MpJson.Str(j, "playerName"), profile = MpJson.Str(j, "profile"),
                faction = MpJson.Str(j, "faction"), factionLocked = MpJson.Bool(j, "factionLocked"), team = MpJson.Int(j, "team")
            };
        }
    }

    /// <summary>Someone in the game: a player (holding sides or waiting for one) or a spectator.</summary>
    public class MpMember {
        public string id = "";
        public string name = "";
        public bool spectator;

        public JSONObject ToJson() {
            JSONObject j = new JSONObject(JSONObject.Type.Object);
            j.AddField("id", id); j.AddField("name", name); j.AddField("spectator", spectator);
            return j;
        }
        public static MpMember FromJson(JSONObject j) {
            return new MpMember { id = MpJson.Str(j, "id"), name = MpJson.Str(j, "name"), spectator = MpJson.Bool(j, "spectator") };
        }
    }

    /// <summary>
    /// A networked game as the store keeps it (its "meta" key): the lobby (name, host, sides, members, rules), and once
    /// it is under way where it stands (how many log entries, whose turn, who must act, the turn clock, a desync
    /// waiting for the host). Small: it is what every device polls. The scenario, the starting board and the log are
    /// under their own keys (Mp.KeyScenario, KeyBase, log chunks).
    /// </summary>
    public class MpGame {
        /// <summary>The join code: unique among the games in the store.</summary>
        public string code = "";
        /// <summary>Which game under this code this is (a code is used again once its game is over): its log and boards are kept under keys that carry it.</summary>
        public string epoch = "";
        /// <summary>The lobby's name: the scenario's at first, the host's to change until the game starts.</summary>
        public string name = "";
        public string host = "";
        public string hostName = "";
        public MpState state = MpState.Lobby;
        /// <summary>The game's version on the host's device (Application.version): a device with another does not join.</summary>
        public string appVersion = "";
        public List<MpSlot> slots = new List<MpSlot>();
        public List<MpMember> members = new List<MpMember>();
        /// <summary>Players the host removed: they cannot come back.</summary>
        public List<string> banned = new List<string>();
        /// <summary>Seconds a player has for a turn; 0 no limit.</summary>
        public int turnSeconds;
        public bool fog, purchases = true, specialUnits = true, allies = true;

        /// <summary>Log entries written so far (a hint: the log's chunks are what count).</summary>
        public int entries;
        /// <summary>The base board (Mp.KeyBase) is the game after this many entries: 0 the start, more after the host put a desync right.</summary>
        public int baseEntry;
        /// <summary>The side to move, and the player who must act for it now ("" nobody: an open side waiting for the host).</summary>
        public string turnSide = "";
        public string turnDriver = "";
        /// <summary>When the player to move saw "your turn" (UTC, round-trip format), "" not yet; and the entry count then (which turn it is the clock of).</summary>
        public string clockStarted = "";
        public int clockEntry = -1;
        /// <summary>A desync reported and not yet put right by the host ("" none): what differed, and who saw it.</summary>
        public string desync = "";
        public string desyncBy = "";
        public string winner = "";

        public string Title { get { return name + "#" + code; } }

        public MpSlot Slot(long side) { return slots.Find(s => s.side == side); }
        public MpMember Member(string id) { return members.Find(m => m.id == id); }
        public bool Holds(string player, long side) { MpSlot s = Slot(side); return s != null && s.kind == MpSlotKind.Player && s.player == player; }
        public bool IsPlayer(string id) { return slots.Exists(s => s.kind == MpSlotKind.Player && s.player == id); }
        public bool Finished { get { return state == MpState.Over || state == MpState.Terminated; } }

        /// <summary>Is it for <paramref name="player"/> to act: the game under way, no desync waiting, and the side to move theirs to play (or its AI theirs to run)?</summary>
        public bool TurnOf(string player) { return state == MpState.Playing && desync.Length == 0 && turnDriver.Length > 0 && turnDriver == player; }

        public string ToJson() {
            JSONObject j = new JSONObject(JSONObject.Type.Object);
            j.AddField("code", code); j.AddField("epoch", epoch); j.AddField("name", name); j.AddField("host", host); j.AddField("hostName", hostName);
            j.AddField("state", (int)state); j.AddField("appVersion", appVersion);
            JSONObject sl = new JSONObject(JSONObject.Type.Array); foreach (MpSlot s in slots) sl.Add(s.ToJson()); j.AddField("slots", sl);
            JSONObject ms = new JSONObject(JSONObject.Type.Array); foreach (MpMember m in members) ms.Add(m.ToJson()); j.AddField("members", ms);
            JSONObject bs = new JSONObject(JSONObject.Type.Array); foreach (string b in banned) bs.Add(b); j.AddField("banned", bs);
            j.AddField("turnSeconds", turnSeconds); j.AddField("fog", fog); j.AddField("purchases", purchases);
            j.AddField("specialUnits", specialUnits); j.AddField("allies", allies);
            j.AddField("entries", entries); j.AddField("baseEntry", baseEntry);
            j.AddField("turnSide", turnSide); j.AddField("turnDriver", turnDriver);
            j.AddField("clockStarted", clockStarted); j.AddField("clockEntry", clockEntry);
            j.AddField("desync", desync); j.AddField("desyncBy", desyncBy); j.AddField("winner", winner);
            return j.Print();
        }

        public static MpGame FromJson(string text) {
            if (string.IsNullOrEmpty(text)) return null;
            JSONObject j = new JSONObject(text);
            if (j.GetField("code") == null) return null;
            MpGame g = new MpGame {
                code = MpJson.Str(j, "code"), epoch = MpJson.Str(j, "epoch"), name = MpJson.Str(j, "name"), host = MpJson.Str(j, "host"), hostName = MpJson.Str(j, "hostName"),
                state = (MpState)MpJson.Int(j, "state"), appVersion = MpJson.Str(j, "appVersion"),
                turnSeconds = MpJson.Int(j, "turnSeconds"), fog = MpJson.Bool(j, "fog"), purchases = MpJson.Bool(j, "purchases"),
                specialUnits = MpJson.Bool(j, "specialUnits"), allies = MpJson.Bool(j, "allies"),
                entries = MpJson.Int(j, "entries"), baseEntry = MpJson.Int(j, "baseEntry"),
                turnSide = MpJson.Str(j, "turnSide"), turnDriver = MpJson.Str(j, "turnDriver"),
                clockStarted = MpJson.Str(j, "clockStarted"), clockEntry = MpJson.Int(j, "clockEntry"),
                desync = MpJson.Str(j, "desync"), desyncBy = MpJson.Str(j, "desyncBy"), winner = MpJson.Str(j, "winner")
            };
            foreach (JSONObject s in MpJson.List(j, "slots")) g.slots.Add(MpSlot.FromJson(s));
            foreach (JSONObject m in MpJson.List(j, "members")) g.members.Add(MpMember.FromJson(m));
            foreach (JSONObject b in MpJson.List(j, "banned")) g.banned.Add(b.stringValue ?? "");
            return g;
        }
    }

    /// <summary>What a log entry is.</summary>
    public static class MpEntryKind {
        /// <summary>Orders, in the game log's own lines (GameLog.Lines): played on every device by the game's rules.</summary>
        public const string Orders = "orders";
        /// <summary>The turn was ended: the next turn's heading line and the board's check line (GameLog.BoardCheck) as its author's device has it.</summary>
        public const string Turn = "turn";
        /// <summary>The game ended: the log's game-over line and the board's check line.</summary>
        public const string Over = "over";
        /// <summary>The host put a desync right: every device loads the base board (Mp.KeyBase) and goes on from the next entry.</summary>
        public const string Base = "base";
        /// <summary>The host ended the game for everyone.</summary>
        public const string End = "end";
    }

    /// <summary>One entry of a networked game's log: what one device added in one go, in order.</summary>
    public class MpEntry {
        public int n;
        public string by = "";
        public string kind = MpEntryKind.Orders;
        /// <summary>When it was written (the writer's clock, UTC).</summary>
        public string at = "";
        public List<string> lines = new List<string>();

        public JSONObject ToJson() {
            JSONObject j = new JSONObject(JSONObject.Type.Object);
            j.AddField("n", n); j.AddField("by", by); j.AddField("kind", kind); j.AddField("at", at);
            JSONObject ls = new JSONObject(JSONObject.Type.Array); foreach (string l in lines) ls.Add(l); j.AddField("lines", ls);
            return j;
        }
        public static MpEntry FromJson(JSONObject j) {
            MpEntry e = new MpEntry { n = MpJson.Int(j, "n"), by = MpJson.Str(j, "by"), kind = MpJson.Str(j, "kind"), at = MpJson.Str(j, "at") };
            foreach (JSONObject l in MpJson.List(j, "lines")) e.lines.Add(l.stringValue ?? "");
            return e;
        }

        public static string ChunkToJson(List<MpEntry> entries) {
            JSONObject a = new JSONObject(JSONObject.Type.Array);
            foreach (MpEntry e in entries) a.Add(e.ToJson());
            return a.Print();
        }
        public static List<MpEntry> ChunkFromJson(string text) {
            List<MpEntry> list = new List<MpEntry>();
            if (string.IsNullOrEmpty(text)) return list;
            JSONObject a = new JSONObject(text);
            if (a.list != null) foreach (JSONObject e in a.list) list.Add(FromJson(e));
            return list;
        }
    }

    /// <summary>A game in a player's list (the main menu's): enough to show it and say whether it waits on them.</summary>
    public class MpSummary {
        public string code, title, turnSide, epoch;
        public MpState state;
        /// <summary>Log entries so far: with the epoch, which turn it is (a your-turn notice is shown once per turn).</summary>
        public int entries;
        public bool myTurn, host, spectator;
        /// <summary>The player is no longer in it (removed by the host).</summary>
        public bool removed;
    }

    static class MpJson {
        public static string Str(JSONObject j, string k) { JSONObject v = j != null ? j.GetField(k) : null; return v != null && v.stringValue != null ? v.stringValue : ""; }
        public static int Int(JSONObject j, string k) { JSONObject v = j != null ? j.GetField(k) : null; return v != null ? (int)v.longValue : 0; }
        public static long Long(JSONObject j, string k) { JSONObject v = j != null ? j.GetField(k) : null; return v != null ? v.longValue : 0; }
        public static bool Bool(JSONObject j, string k) { JSONObject v = j != null ? j.GetField(k) : null; return v != null && v.boolValue; }
        public static List<JSONObject> List(JSONObject j, string k) { JSONObject v = j != null ? j.GetField(k) : null; return v != null && v.list != null ? v.list : new List<JSONObject>(); }
        public static string Now() { return DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture); }
        public static bool Time(string s, out DateTime t) { return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out t); }
    }
}
