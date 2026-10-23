using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace HexTactica.Testing {

    /// <summary>
    /// Reads unit stats straight out of the project's .prefab YAML, so tests run
    /// against the units the game actually ships rather than invented fixtures.
    ///
    /// Only scalar fields are read. A prefab is a multi-document YAML file; the Unit
    /// component is the MonoBehaviour document that has a "Firepower:" key, and the
    /// SupplyPoint is the one with a "producer:" key.
    /// </summary>
    public static class PrefabLoader {

        public static string AssetsRoot = "../HexTactica/Assets";

        static readonly Dictionary<string, Unit> cache = new Dictionary<string, Unit>();
        static readonly Dictionary<string, Dictionary<string, string>> hubCache =
            new Dictionary<string, Dictionary<string, string>>();

        public static bool Has(string name) {
            return File.Exists(PathFor(name));
        }

        static string PathFor(string name) {
            return Path.Combine(AssetsRoot, "Units", name + ".prefab");
        }

        /// <summary>A fresh archetype built from the prefab. Throws if the prefab is missing.</summary>
        public static Unit Load(string name) {
            Unit cached;
            if (!cache.TryGetValue(name, out cached)) {
                cached = Parse(name);
                cache[name] = cached;
            }
            return Rig.Clone(cached);
        }

        /// <summary>Attach the prefab's SupplyPoint, if it has one, with its real range and producer flag.</summary>
        public static SupplyPoint AttachHubIfAny(string name, Unit unit) {
            Dictionary<string, string> hub;
            if (!hubCache.TryGetValue(name, out hub) || hub == null) return null;
            SupplyPoint sp = unit.gameObject.AddComponent<SupplyPoint>();
            sp.myUnit = unit;
            sp.range = ParseInt(hub, "range", 4);
            sp.producer = ParseInt(hub, "producer", 0) != 0;
            return sp;
        }

        static Unit Parse(string name) {
            string text = File.ReadAllText(PathFor(name));
            string[] docs = text.Split(new[] { "\n--- " }, StringSplitOptions.None);

            Dictionary<string, string> unitFields = null;
            Dictionary<string, string> hubFields = null;
            foreach (string doc in docs) {
                Dictionary<string, string> f = Fields(doc);
                if (f.ContainsKey("Firepower")) unitFields = f;
                else if (f.ContainsKey("producer")) hubFields = f;
            }
            if (unitFields == null) throw new Exception("no Unit component in " + name);
            hubCache[name] = hubFields;

            Unit u = Rig.New<Unit>();
            u.name = name;
            u.personal_name = name;
            u.path = new List<HexTile>();
            u.transported = new List<Unit>();

            foreach (FieldInfo fi in typeof(Unit).GetFields(BindingFlags.Public | BindingFlags.Instance)) {
                string raw;
                if (!unitFields.TryGetValue(fi.Name, out raw)) continue;
                try {
                    if (fi.FieldType == typeof(int)) fi.SetValue(u, int.Parse(raw, CultureInfo.InvariantCulture));
                    else if (fi.FieldType == typeof(float)) fi.SetValue(u, float.Parse(raw, CultureInfo.InvariantCulture));
                    else if (fi.FieldType == typeof(bool)) fi.SetValue(u, raw.Trim() == "1");
                    else if (fi.FieldType.IsEnum) fi.SetValue(u, Enum.ToObject(fi.FieldType, int.Parse(raw)));
                } catch (FormatException) { /* complex value: skip */ }
            }
            // Lists: the locomotion types a transport carries.
            u.carries = new List<Locomotion>();
            string[] lines = text.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++) {
                // Unity also writes an int list as one hex string, four bytes little-endian per entry
                // ("  carries: 00000000" is [Boots]; a trailing type letter may follow).
                if (lines[i].StartsWith("  carries: ") && lines[i].Trim() != "carries:") {
                    string hex = lines[i].Substring("  carries: ".Length).Trim();
                    for (int k = 0; k + 8 <= hex.Length; k += 8) {
                        int v = 0; bool ok = true;
                        for (int byteI = 0; byteI < 4; byteI++) {
                            int bv;
                            if (!int.TryParse(hex.Substring(k + byteI * 2, 2), System.Globalization.NumberStyles.HexNumber, null, out bv)) { ok = false; break; }
                            v |= bv << (8 * byteI);
                        }
                        if (ok) u.carries.Add((Locomotion)v);
                    }
                    break;
                }
                if (lines[i] != "  carries:") continue;
                for (int j = i + 1; j < lines.Length && lines[j].StartsWith("  - "); j++) u.carries.Add((Locomotion)int.Parse(lines[j].Substring(4).Trim()));
                break;
            }
            return u;
        }

        static readonly Dictionary<string, Building> buildingCache = new Dictionary<string, Building>();

        public static bool HasBuilding(string name) {
            return File.Exists(Path.Combine(AssetsRoot, "Buildings", "Prefabs", name + ".prefab"));
        }

        /// <summary>
        /// A building archetype from Assets/Buildings/Prefabs/&lt;name&gt;.prefab: its Building fields,
        /// footprint, services, build list (unit prefab names) and ground terrain.
        /// </summary>
        public static Building LoadBuilding(string name) {
            Building cached;
            if (!buildingCache.TryGetValue(name, out cached)) {
                cached = ParseBuilding(name);
                buildingCache[name] = cached;
            }
            Building b = Rig.New<Building>();
            b.name = cached.name;
            foreach (FieldInfo fi in typeof(Building).GetFields(BindingFlags.Public | BindingFlags.Instance)) {
                object v = fi.GetValue(cached);
                if (v is List<TileOffset>) v = new List<TileOffset>((List<TileOffset>)v);
                else if (v is List<Locomotion>) v = new List<Locomotion>((List<Locomotion>)v);
                else if (v is List<Unit>) v = new List<Unit>((List<Unit>)v);
                fi.SetValue(b, v);
            }
            b.tiles = new List<HexTile>();
            b.runway = new List<HexTile>();
            b.tile = null;
            b.owner = null;
            return b;
        }

        static Building ParseBuilding(string name) {
            string path = Path.Combine(AssetsRoot, "Buildings", "Prefabs", name + ".prefab");
            string text = File.ReadAllText(path);
            string doc = null;
            foreach (string d in text.Split(new[] { "\n--- " }, StringSplitOptions.None)) if (d.Contains("\n  supplyRange:")) doc = d;
            if (doc == null) throw new Exception("no Building component in " + name);
            Dictionary<string, string> f = Fields(doc);
            Building b = Rig.New<Building>();
            b.name = name;
            foreach (FieldInfo fi in typeof(Building).GetFields(BindingFlags.Public | BindingFlags.Instance)) {
                string raw;
                if (!f.TryGetValue(fi.Name, out raw)) continue;
                try {
                    if (fi.FieldType == typeof(int)) fi.SetValue(b, int.Parse(raw, CultureInfo.InvariantCulture));
                    else if (fi.FieldType == typeof(float)) fi.SetValue(b, float.Parse(raw, CultureInfo.InvariantCulture));
                    else if (fi.FieldType == typeof(bool)) fi.SetValue(b, raw.Trim() == "1");
                    else if (fi.FieldType == typeof(string)) fi.SetValue(b, raw);
                    else if (fi.FieldType.IsEnum) fi.SetValue(b, Enum.ToObject(fi.FieldType, int.Parse(raw)));
                } catch (FormatException) { }
            }
            b.footprint = new List<TileOffset>();
            b.serviceTiles = new List<TileOffset>();
            b.services = new List<Locomotion>();
            b.buildable = new List<Unit>();
            List<string> lines = new List<string>(doc.Split('\n'));
            for (int i = 0; i < lines.Count; i++) {
                string l = lines[i];
                if (l.StartsWith("  footprint:") || l.StartsWith("  serviceTiles:")) {
                    List<TileOffset> into = l.StartsWith("  footprint:") ? b.footprint : b.serviceTiles;
                    for (int j = i + 1; j + 1 < lines.Count && lines[j].StartsWith("  - x:"); j += 2)
                        into.Add(new TileOffset(int.Parse(lines[j].Substring(lines[j].IndexOf(':') + 1).Trim()), int.Parse(lines[j + 1].Substring(lines[j + 1].IndexOf(':') + 1).Trim())));
                } else if (l.StartsWith("  services:")) {
                    for (int j = i + 1; j < lines.Count && lines[j].StartsWith("  - "); j++) b.services.Add((Locomotion)int.Parse(lines[j].Substring(4).Trim()));
                } else if (l.StartsWith("  buildable:")) {
                    for (int j = i + 1; j < lines.Count && lines[j].StartsWith("  - "); j++) {
                        string g = GuidIn(lines[j]);
                        string unit = g != null ? AssetByGuid(Path.Combine(AssetsRoot, "Units"), "*.prefab.meta", g) : null;
                        if (unit != null && Has(unit)) { Unit arch = Load(unit); AttachHubIfAny(unit, arch); b.buildable.Add(arch); }   // as in Unity: the prefab carries its SupplyPoint
                    }
                } else if (l.StartsWith("  ground:") || l.StartsWith("  serviceGround:")) {
                    string g = GuidIn(l);
                    string terrain = g != null ? AssetByGuid(Path.Combine(AssetsRoot, "Tiles"), "*.asset.meta", g) : null;
                    TerrainData td = terrain != null ? ScenarioFile.Terrain(terrain) : null;
                    if (l.StartsWith("  ground:")) b.ground = td; else b.serviceGround = td;
                }
            }
            return b;
        }

        static string GuidIn(string line) {
            int i = line.IndexOf("guid: ");
            if (i < 0) return null;
            int j = line.IndexOf(',', i);
            return line.Substring(i + 6, (j < 0 ? line.Length : j) - i - 6).Trim();
        }

        /// <summary>The asset in <paramref name="dir"/> whose meta carries <paramref name="guid"/> (name without extensions).</summary>
        static string AssetByGuid(string dir, string pattern, string guid) {
            foreach (string meta in Directory.GetFiles(dir, pattern)) {
                if (File.ReadAllText(meta).Contains("guid: " + guid)) {
                    string n = Path.GetFileName(meta);
                    return n.Substring(0, n.IndexOf('.'));
                }
            }
            return null;
        }

        /// <summary>Top-level scalar keys of one YAML document (exactly two-space indent).</summary>
        static Dictionary<string, string> Fields(string doc) {
            Dictionary<string, string> f = new Dictionary<string, string>();
            foreach (string line in doc.Split('\n')) {
                if (line.Length < 4 || line[0] != ' ' || line[1] != ' ' || line[2] == ' ') continue;
                int colon = line.IndexOf(':');
                if (colon < 0) continue;
                string key = line.Substring(2, colon - 2).Trim();
                string val = line.Substring(colon + 1).Trim();
                if (val.Length == 0 || val.StartsWith("{")) continue;
                if (!f.ContainsKey(key)) f[key] = val;
            }
            return f;
        }

        static int ParseInt(Dictionary<string, string> d, string key, int fallback) {
            string raw;
            int v;
            if (d != null && d.TryGetValue(key, out raw) && int.TryParse(raw, out v)) return v;
            return fallback;
        }
    }
}
