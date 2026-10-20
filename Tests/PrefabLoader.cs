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
            return u;
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
