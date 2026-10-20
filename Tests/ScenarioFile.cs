using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using HexTactica.AI;
using HexTactica.Harness;
using HexTactica.Testing;

/// <summary>
/// Scenario files: the level editor's own save format (what LevelEditor.Save writes and
/// LevelEditor.Load / ScenarioLoader read), plus a flat "test" block both of those ignore.
///
/// The same file is what Unity loads (HexTactica ▸ Load Scenario File…) and what the headless
/// harness loads, so every test is something you can open in the scene and watch.
///
/// Players are SampleScene's: Blue, id 112, has the AIPlayer (this AI); Red, id 0, is the
/// opponent — you, or a baseline AI when the test block names one.
/// </summary>
public static class ScenarioFile {

    public const long AI_ID = 112;
    public const long OTHER_ID = 0;
    /// <summary>
    /// Radius of the board being written or read. SampleScene's board is radius 15 (HexGenerator
    /// width 31); a file records its own width, and tiles outside it load as Water.
    /// </summary>
    public static int Board = 7;

    public class Meta {
        public readonly Dictionary<string, string> fields = new Dictionary<string, string>();
        public string Get(string k, string fallback) { string v; return fields.TryGetValue(k, out v) ? v : fallback; }
        public int GetInt(string k, int fallback) { string v; int n; return fields.TryGetValue(k, out v) && int.TryParse(v, out n) ? n : fallback; }
        public string Name { get { return Get("name", "?"); } }
    }

    public class Loaded {
        public Meta meta;
        public Player ai, other;
        public string path;
    }

    // ------------------------------------------------------------------ terrain

    static readonly Dictionary<string, TerrainData> terrain = new Dictionary<string, TerrainData>();

    /// <summary>A terrain asset from Assets/Tiles, read from its YAML.</summary>
    public static TerrainData Terrain(string name) {
        TerrainData td;
        if (terrain.TryGetValue(name, out td)) return td;
        string path = Path.Combine(PrefabLoader.AssetsRoot, "Tiles", name + ".asset");
        td = new TerrainData();
        td.name = name;
        if (File.Exists(path)) {
            foreach (string raw in File.ReadAllLines(path)) {
                string line = raw.Trim();
                int c = line.IndexOf(':');
                if (c < 0) continue;
                string k = line.Substring(0, c).Trim(), v = line.Substring(c + 1).Trim();
                int n;
                if (k == "road") td.road = v == "1";
                if (!int.TryParse(v, out n)) continue;
                if (k == "cover") td.cover = n;
                else if (k == "moveability") td.moveability = n;
                else if (k == "elevation") td.elevation = n;
            }
        } else {
            throw new Exception("No terrain asset " + path);
        }
        terrain[name] = td;
        return td;
    }

    // ------------------------------------------------------------------ writing

    /// <summary>
    /// Write the current headless board as a scenario file: every tile of SampleScene's board
    /// (those outside the generated map as Water), every unit, and the test block.
    /// </summary>
    public static void Save(string path, Dictionary<string, string> test, Player ai, Player other, Player first) {
        StringBuilder sb = new StringBuilder();
        sb.Append("{\n\"width\":").Append(2 * Board + 1).Append(",\n\"active\":").Append(first == ai ? AI_ID : OTHER_ID).Append(",\n\"test\":{");
        bool f = true;
        foreach (KeyValuePair<string, string> kv in test) {
            if (!f) sb.Append(',');
            f = false;
            sb.Append('"').Append(kv.Key).Append("\":\"").Append(kv.Value.Replace("\"", "'")).Append('"');
        }
        sb.Append("},\n\"tiles\":[");
        f = true;
        for (int x = -Board; x <= Board; x++) for (int y = -Board; y <= Board; y++) {
            int z = -x - y;
            if (Math.Abs(z) > Board) continue;
            HexTile t = Rig.At(x, y);
            string name = t != null && t.terrain != null ? t.terrain.name : "Water";
            if (!f) sb.Append(',');
            f = false;
            sb.Append(string.Format("\n{{\"x\":{0},\"y\":{1},\"z\":{2},\"damage\":0,\"combatCount\":0,\"terrain\":\"{3}\"}}", x, y, z, name));
        }
        sb.Append("\n],\n\"units\":[");
        f = true;
        foreach (Player p in new[] { ai, other }) {
            long id = p == ai ? AI_ID : OTHER_ID;
            foreach (Unit u in p.units) {
                if (u == null || u.tile == null) continue;
                if (!f) sb.Append(',');
                f = false;
                string prefab = u.name.Replace("(Clone)", "");
                sb.Append(string.Format("\n{{\"x\":{0},\"y\":{1},\"z\":{2},\"Strength\":{3},\"Veterancy\":{4},\"personalName\":\"{5}\",\"active\":true,\"owner_id\":{6},\"supplies\":{7},\"fuel\":{8},\"ammo\":{9},\"gameobject_name\":\"{10}\",\"transporterIndexPlusOne\":0}}",
                    u.tile.x, u.tile.y, u.tile.z, u.Strength, u.Veterancy, prefab, id, u.supplies, u.fuel, u.ammo, prefab));
            }
        }
        sb.Append("\n]\n}\n");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, sb.ToString());
    }

    // ------------------------------------------------------------------ reading

    static readonly Regex Pair = new Regex("\"(\\w+)\"\\s*:\\s*(\"((?:[^\"\\\\]|\\\\.)*)\"|-?[0-9.]+|true|false)");

    static Dictionary<string, string> Flat(string obj) {
        Dictionary<string, string> d = new Dictionary<string, string>();
        foreach (Match m in Pair.Matches(obj)) d[m.Groups[1].Value] = m.Groups[3].Success && m.Groups[2].Value.StartsWith("\"") ? m.Groups[3].Value : m.Groups[2].Value;
        return d;
    }

    /// <summary>The flat objects inside the array named <paramref name="key"/>.</summary>
    static List<Dictionary<string, string>> Array(string json, string key) {
        List<Dictionary<string, string>> list = new List<Dictionary<string, string>>();
        int i = json.IndexOf("\"" + key + "\"");
        if (i < 0) return list;
        int open = json.IndexOf('[', i);
        int depth = 0, start = -1;
        for (int k = open + 1; k < json.Length; k++) {
            char c = json[k];
            if (c == '{') { if (depth == 0) start = k; depth++; }
            else if (c == '}') { depth--; if (depth == 0) list.Add(Flat(json.Substring(start, k - start + 1))); }
            else if (c == ']' && depth == 0) break;
        }
        return list;
    }

    public static Meta ReadMeta(string path) {
        string json = File.ReadAllText(path);
        Meta meta = new Meta();
        int i = json.IndexOf("\"test\"");
        if (i >= 0) {
            int open = json.IndexOf('{', i), close = json.IndexOf('}', open);
            foreach (KeyValuePair<string, string> kv in Flat(json.Substring(open, close - open + 1))) meta.fields[kv.Key] = kv.Value;
        }
        if (!meta.fields.ContainsKey("name")) meta.fields["name"] = Path.GetFileNameWithoutExtension(path);
        return meta;
    }

    /// <summary>
    /// Load a scenario file into a fresh headless board, attach this AI to Blue and the opponent
    /// the test block names (or <paramref name="opponentOverride"/>) to Red.
    /// </summary>
    public static Loaded Load(string path, string opponentOverride, Action<AIProfile> tweak) {
        string json = File.ReadAllText(path);
        Loaded l = new Loaded { path = path, meta = ReadMeta(path) };
        Match w = Regex.Match(json, "\"width\"\\s*:\\s*(\\d+)");
        Board = w.Success ? int.Parse(w.Groups[1].Value) / 2 : 7;
        Rig.NewGame(Board);
        foreach (Dictionary<string, string> t in Array(json, "tiles")) {
            HexTile tile = Rig.hg.get(int.Parse(t["x"]), int.Parse(t["y"]), int.Parse(t["z"]));
            if (tile != null) tile.terrain = Terrain(t["terrain"]);
        }
        l.ai = Rig.AddPlayer("Blue", AI_ID);
        l.other = Rig.AddPlayer("Red", OTHER_ID);
        foreach (Dictionary<string, string> u in Array(json, "units")) {
            Player owner = long.Parse(u["owner_id"]) == AI_ID ? l.ai : l.other;
            string prefab = u["gameobject_name"];
            HexTile at = Rig.hg.get(int.Parse(u["x"]), int.Parse(u["y"]), int.Parse(u["z"]));
            Unit unit = Rig.Spawn(PrefabLoader.Load(prefab), owner, at.x, at.y);
            PrefabLoader.AttachHubIfAny(prefab, unit);
            int n;
            if (u.ContainsKey("Strength") && int.TryParse(u["Strength"], out n)) unit.Strength = n;
            if (u.ContainsKey("supplies") && int.TryParse(u["supplies"], out n)) unit.supplies = n;
            if (u.ContainsKey("ammo") && int.TryParse(u["ammo"], out n)) unit.ammo = n;
            if (u.ContainsKey("fuel") && int.TryParse(u["fuel"], out n)) unit.fuel = n;
        }

        AIProfile profile = ScriptableObject.CreateInstance<AIProfile>();
        profile.annotateBoard = false;
        if (tweak != null) tweak(profile);
        Rig.AttachAI(l.ai, profile);
        string opponent = opponentOverride ?? l.meta.Get("opponent", "Legacy");
        if (opponent == "Full") {
            AIProfile p2 = ScriptableObject.CreateInstance<AIProfile>();
            p2.annotateBoard = false;
            if (tweak != null) tweak(p2);
            Rig.AttachAI(l.other, p2);
        } else if (opponent == "Greedy") Rig.AttachBaseline(l.other, AIKind.Greedy);
        else if (opponent == "Legacy") Rig.AttachBaseline(l.other, AIKind.Legacy);
        // "Human": nobody acts for Red; the harness ends its turns.

        string active = Regex.Match(json, "\"active\"\\s*:\\s*(\\d+)").Groups[1].Value;
        Rig.gc.activePlayer = active == OTHER_ID.ToString() ? l.other : l.ai;
        return l;
    }

    /// <summary>Play the active player's whole turn, whoever it is.</summary>
    public static void PlayTurn() {
        GameController gc = Rig.gc;
        Player active = gc.activePlayer;
        if (active == null || active.eliminated) { gc.EndTurn(); return; }
        AIPlayer full = active.gameObject.GetComponent<AIPlayer>();
        BaselineAIPlayer baseline = active.gameObject.GetComponent<BaselineAIPlayer>();
        if (full != null) full.PlayWholeTurn();
        else if (baseline != null) baseline.PlayWholeTurn();
        else gc.EndTurn();
    }

    /// <summary>Material: strength times material worth, every unit.</summary>
    public static int Material(Player p) {
        float m = 0;
        foreach (Unit u in p.units) if (u != null) m += u.Strength * AIScore.Worth(u);
        return (int)m;
    }
}
