using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Defective.JSON;
using UnityEngine;

/// <summary>
/// The scenario script (ScenarioScriptData) to and from the scenario file's JSON: every public field of the trigger
/// classes by name — numbers, flags, text, enums (by number), lists of text and lists of these classes. A field the
/// file lacks keeps its default, and a key the code no longer has is skipped, so files survive fields being added or
/// removed (renaming one drops its value: keep names). Fields marked [ScenarioState] (progress in a game) are written
/// only when <c>withState</c>.
/// </summary>
public static class ScenarioJson {

    public static JSONObject Write(object o, bool withState) {
        JSONObject j = new JSONObject(JSONObject.Type.Object);
        if (o == null) return j;
        foreach (FieldInfo f in Fields(o.GetType())) {
            if (!withState && f.GetCustomAttributes(typeof(ScenarioStateAttribute), false).Length > 0) continue;
            JSONObject v = Value(f.FieldType, f.GetValue(o), withState);
            if (v != null) j.AddField(f.Name, v);
        }
        return j;
    }

    static JSONObject Value(Type t, object v, bool withState) {
        if (t == typeof(string)) return JSONObject.CreateStringObject(Clean((string)v ?? ""));
        if (t == typeof(int)) return new JSONObject((int)v);
        if (t == typeof(long)) return new JSONObject((long)v);
        if (t == typeof(float)) return new JSONObject((float)v);
        if (t == typeof(bool)) return new JSONObject((bool)v);
        if (t.IsEnum) return new JSONObject(Convert.ToInt32(v));
        if (typeof(IList).IsAssignableFrom(t) && t.IsGenericType) {
            Type item = t.GetGenericArguments()[0];
            JSONObject a = new JSONObject(JSONObject.Type.Array);
            if (v != null) foreach (object e in (IList)v) { JSONObject x = Value(item, e, withState); if (x != null) a.Add(x); }
            return a;
        }
        if (t.IsClass && t.GetCustomAttributes(typeof(SerializableAttribute), false).Length > 0) return Write(v, withState);
        return null;   // (anything else is not part of a scenario's script)
    }

    /// <summary>The JSON library does not escape a backslash: written as a slash, rather than break the file.</summary>
    static string Clean(string s) { return s.IndexOf('\\') >= 0 ? s.Replace('\\', '/') : s; }

    public static T Read<T>(JSONObject j) where T : new() { return (T)Read(typeof(T), j); }

    static object Read(Type t, JSONObject j) {
        object o = Activator.CreateInstance(t);
        if (j == null || !j.isObject) return o;
        foreach (FieldInfo f in Fields(t)) {
            JSONObject v = j.GetField(f.Name);
            if (v == null) continue;
            object r = ReadValue(f.FieldType, v, f.GetValue(o));
            if (r != null) f.SetValue(o, r);
        }
        return o;
    }

    static object ReadValue(Type t, JSONObject v, object current) {
        if (t == typeof(string)) return v.isString ? v.stringValue : v.isNumber ? v.Print() : null;
        if (t == typeof(int)) return v.isNumber ? (object)(int)v.longValue : null;
        if (t == typeof(long)) return v.isNumber ? (object)v.longValue : null;
        if (t == typeof(float)) return v.isNumber ? (object)v.floatValue : null;
        if (t == typeof(bool)) return v.type == JSONObject.Type.Bool ? (object)v.boolValue : null;
        if (t.IsEnum) return v.isNumber ? Enum.ToObject(t, (int)v.longValue) : null;
        if (typeof(IList).IsAssignableFrom(t) && t.IsGenericType) {
            if (!v.isArray) return null;
            Type item = t.GetGenericArguments()[0];
            IList list = (IList)Activator.CreateInstance(t);
            if (v.list != null) foreach (JSONObject e in v.list) { object x = ReadValue(item, e, null); if (x != null) list.Add(x); }
            return list;
        }
        if (t.IsClass && t.GetCustomAttributes(typeof(SerializableAttribute), false).Length > 0) return Read(t, v);
        return null;
    }

    static IEnumerable<FieldInfo> Fields(Type t) {
        foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Instance)) {
            if (f.IsInitOnly || f.IsLiteral) continue;
            yield return f;
        }
    }

    /// <summary>A deep copy (Write then Read), state included.</summary>
    public static T Copy<T>(T o) where T : new() { return Read<T>(Write(o, true)); }
}
