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
            string p = Path.Combine(AssetsRoot, "Units", name + ".prefab");
            string now;
            // A unit renamed since the file was saved loads as the game loads it (LevelEditor.renamedUnits).
            if (!File.Exists(p) && LevelEditor.renamedUnits.TryGetValue(name, out now)) return Path.Combine(AssetsRoot, "Units", now + ".prefab");
            return p;
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

        /// <summary>A unit prefab's serialized Unit and SupplyPoint fields (and their documents' ids), and its carries list.</summary>
        class Doc {
            public Dictionary<string, string> unit, hub;
            public string unitId, hubId;
            public List<Locomotion> carries = new List<Locomotion>();
        }

        /// <summary>
        /// Read a unit prefab. A prefab variant (Truck Variant, say) has no Unit document of its own:
        /// it is its base prefab with the changes listed on the instance of it at its root, applied here
        /// (scalar fields, and the carries list).
        /// </summary>
        static Doc Read(string name, int depth = 0) {
            string text = File.ReadAllText(PathFor(name)).Replace("\r", "");
            string[] docs = text.Split(new[] { "\n--- " }, StringSplitOptions.None);
            Doc d = new Doc();
            foreach (string doc in docs) {
                Dictionary<string, string> f = Fields(doc);
                string id = DocId(doc);
                if (f.ContainsKey("Firepower")) { d.unit = f; d.unitId = id; }
                else if (f.ContainsKey("producer")) { d.hub = f; d.hubId = id; }
            }
            if (d.unit != null) { d.carries = Carries(text); return d; }
            if (depth > 4) throw new Exception("no Unit component in " + name);
            // A variant: the root instance's source prefab, then its changes.
            foreach (string doc in docs) {
                if (!doc.StartsWith("!u!1001 ")) continue;
                if (!doc.Contains("m_TransformParent: {fileID: 0}")) continue;
                int sp = doc.IndexOf("m_SourcePrefab:");
                int spEnd = sp >= 0 ? doc.IndexOf('\n', sp) : -1;
                string guid = sp >= 0 ? GuidIn(spEnd >= 0 ? doc.Substring(sp, spEnd - sp) : doc.Substring(sp)) : null;
                string baseName = guid != null ? AssetByGuid(Path.Combine(AssetsRoot, "Units"), "*.prefab.meta", guid) : null;
                if (baseName == null || baseName == name) continue;
                Doc b = Read(baseName, depth + 1);
                Doc v = new Doc { unit = new Dictionary<string, string>(b.unit), unitId = b.unitId, hub = b.hub != null ? new Dictionary<string, string>(b.hub) : null, hubId = b.hubId, carries = new List<Locomotion>(b.carries) };
                string[] lines = doc.Split('\n');
                int carriesSize = -1;
                Dictionary<int, int> carriesAt = new Dictionary<int, int>();
                for (int i = 0; i + 2 < lines.Length; i++) {
                    if (!lines[i].TrimStart().StartsWith("- target:")) continue;
                    int fi = lines[i].IndexOf("fileID: ");
                    if (fi < 0) continue;
                    int te = fi + 8;
                    while (te < lines[i].Length && char.IsDigit(lines[i][te])) te++;
                    string target = lines[i].Substring(fi + 8, te - fi - 8);
                    string path = lines[i + 1].Trim().StartsWith("propertyPath:") ? lines[i + 1].Trim().Substring("propertyPath:".Length).Trim().Trim('\'') : null;
                    string value = lines[i + 2].Trim().StartsWith("value:") ? lines[i + 2].Trim().Substring("value:".Length).Trim() : null;
                    if (path == null || value == null) continue;
                    Dictionary<string, string> into = target == v.unitId ? v.unit : target == v.hubId ? v.hub : null;
                    if (into == null) continue;
                    if (into == v.unit && path == "carries.Array.size") { int n; if (int.TryParse(value, out n)) carriesSize = n; continue; }
                    if (into == v.unit && path.StartsWith("carries.Array.data[")) {
                        int k, val;
                        if (int.TryParse(path.Substring(19).TrimEnd(']'), out k) && int.TryParse(value, out val)) carriesAt[k] = val;
                        continue;
                    }
                    if (path.Contains(".") || value.Length == 0) continue;
                    into[path] = value;
                }
                if (carriesSize >= 0) {
                    List<Locomotion> c = new List<Locomotion>();
                    for (int k = 0; k < carriesSize; k++) c.Add(carriesAt.ContainsKey(k) ? (Locomotion)carriesAt[k] : (k < v.carries.Count ? v.carries[k] : Locomotion.Boots));
                    v.carries = c;
                } else foreach (KeyValuePair<int, int> kv in carriesAt) if (kv.Key < v.carries.Count) v.carries[kv.Key] = (Locomotion)kv.Value;
                return v;
            }
            throw new Exception("no Unit component in " + name);
        }

        /// <summary>The id of a YAML document ("!u!114 &amp;9081080694409079478" gives 9081080694409079478).</summary>
        static string DocId(string doc) {
            int amp = doc.IndexOf('&');
            int nl = doc.IndexOf('\n');
            if (amp < 0 || (nl >= 0 && amp > nl)) return null;
            int end = amp + 1;
            while (end < doc.Length && char.IsDigit(doc[end])) end++;
            return doc.Substring(amp + 1, end - amp - 1);
        }

        static Unit Parse(string name) {
            Doc d = Read(name);
            Dictionary<string, string> unitFields = d.unit;
            Dictionary<string, string> hubFields = d.hub;
            hubCache[name] = hubFields;

            Unit u = Rig.New<Unit>();
            u.name = name;
            u.personal_name = name;
            u.path = new List<HexTile>();
            u.transported = new List<Unit>();

            foreach (FieldInfo fi in SerializedFields(typeof(Unit))) {
                string raw;
                if (!unitFields.TryGetValue(fi.Name, out raw) && !unitFields.TryGetValue(SerializedName(fi), out raw)) continue;
                try {
                    if (fi.FieldType == typeof(int)) fi.SetValue(u, int.Parse(raw, CultureInfo.InvariantCulture));
                    else if (fi.FieldType == typeof(float)) fi.SetValue(u, float.Parse(raw, CultureInfo.InvariantCulture));
                    else if (fi.FieldType == typeof(bool)) fi.SetValue(u, raw.Trim() == "1");
                    else if (fi.FieldType.IsEnum) fi.SetValue(u, Enum.ToObject(fi.FieldType, int.Parse(raw)));
                } catch (FormatException) { /* complex value: skip */ }
            }
            // Lists: the locomotion types a transport carries.
            u.carries = new List<Locomotion>(d.carries);
            return u;
        }

        /// <summary>The carries list of a prefab's Unit (as a YAML list, or as Unity's hex string of little-endian ints).</summary>
        /// <summary>A packed int list as Unity writes it: 8 hex digits per int, little-endian.</summary>
        static List<int> PackedInts(string hex) {
            List<int> r = new List<int>();
            for (int k = 0; k + 8 <= hex.Length; k += 8) {
                int v = 0; bool ok = true;
                for (int byteI = 0; byteI < 4; byteI++) {
                    int bv;
                    if (!int.TryParse(hex.Substring(k + byteI * 2, 2), System.Globalization.NumberStyles.HexNumber, null, out bv)) { ok = false; break; }
                    v |= bv << (8 * byteI);
                }
                if (ok) r.Add(v);
            }
            return r;
        }

        static List<Locomotion> Carries(string text) {
            List<Locomotion> carries = new List<Locomotion>();
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++) {
                if (lines[i].StartsWith("  carries: ") && lines[i].Trim() != "carries:") {
                    string hex = lines[i].Substring("  carries: ".Length).Trim();
                    for (int k = 0; k + 8 <= hex.Length; k += 8) {
                        int v = 0; bool ok = true;
                        for (int byteI = 0; byteI < 4; byteI++) {
                            int bv;
                            if (!int.TryParse(hex.Substring(k + byteI * 2, 2), System.Globalization.NumberStyles.HexNumber, null, out bv)) { ok = false; break; }
                            v |= bv << (8 * byteI);
                        }
                        if (ok) carries.Add((Locomotion)v);
                    }
                    break;
                }
                if (lines[i] != "  carries:") continue;
                for (int j = i + 1; j < lines.Length && lines[j].StartsWith("  - "); j++) carries.Add((Locomotion)int.Parse(lines[j].Substring(4).Trim()));
                break;
            }
            return carries;
        }

        static readonly Dictionary<string, Building> buildingCache = new Dictionary<string, Building>();

        public static bool HasBuilding(string name) {
            return File.Exists(Path.Combine(AssetsRoot, "Buildings", "Prefabs", name + ".prefab"));
        }

        /// <summary>
        /// A building archetype from Assets/Buildings/Prefabs/&lt;name&gt;.prefab: its Building fields,
        /// footprint, services, build list (unit prefab names) and ground terrain.
        /// </summary>
        /// <summary>The fields Unity serializes: public ones, and private ones marked [SerializeField] (a property's backing field).</summary>
        public static List<FieldInfo> SerializedFields(Type t) {
            List<FieldInfo> list = new List<FieldInfo>();
            foreach (FieldInfo fi in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)) {
                if (fi.IsInitOnly) continue;
                if (fi.IsPublic || fi.GetCustomAttributes(typeof(SerializeFieldAttribute), false).Length > 0) list.Add(fi);
            }
            return list;
        }

        /// <summary>
    /// A field's old name if it was renamed ([FormerlySerializedAs]): a prefab saved before the rename
    /// has it under that name, one saved since under the field's own (both are read, as Unity does).
    /// </summary>
        public static string SerializedName(FieldInfo fi) {
            object[] old = fi.GetCustomAttributes(typeof(UnityEngine.Serialization.FormerlySerializedAsAttribute), false);
            return old.Length > 0 ? ((UnityEngine.Serialization.FormerlySerializedAsAttribute)old[0]).oldName : fi.Name;
        }

        public static Building LoadBuilding(string name) {
            Building cached;
            if (!buildingCache.TryGetValue(name, out cached)) {
                cached = ParseBuilding(name);
                buildingCache[name] = cached;
            }
            Building b = Rig.New<Building>();
            b.name = cached.name;
            b.gameObject.name = cached.name;   // what a save writes for it (GameLog, LevelEditor), as in Unity
            foreach (FieldInfo fi in SerializedFields(typeof(Building))) {
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
            foreach (FieldInfo fi in SerializedFields(typeof(Building))) {
                string raw;
                if (!f.TryGetValue(fi.Name, out raw) && !f.TryGetValue(SerializedName(fi), out raw)) continue;
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
                    // Unity writes an int list either as "- n" lines or packed on the line as hex (4 bytes each, little-endian, then "i").
                    string packed = l.Substring("  services:".Length).Trim();
                    if (packed.Length > 0) foreach (int v in PackedInts(packed)) b.services.Add((Locomotion)v);
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
