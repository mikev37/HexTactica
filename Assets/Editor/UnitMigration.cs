#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot migration and validation for unit prefabs.
///
/// Most of the new fields fail safe — ammoMax 0 means infinite ammo, minFuelPerTile 0
/// means infinite range — so an unmigrated prefab keeps working. Locomotion is the
/// exception: it defaults to Boots, and Boots means infantry, and infantry capture
/// buildings. Left unset, every tank in the game becomes a capture unit.
///
/// Inference lives in LocomotionInference (shared with the headless tests). Run Validate
/// first, read what it intends to do, then run Migrate. The shipped Tank and Artillery
/// prefabs already have locomotion written in, so on the current roster Validate should
/// report nothing to change.
/// </summary>
public static class UnitMigration {

    [MenuItem("HexTactica/Prefabs/Validate Units")]
    public static void Validate() {
        StringBuilder report = new StringBuilder();
        int issues = 0;

        foreach (Unit prefab in AllUnitPrefabs()) {
            List<string> problems = Inspect(prefab);
            if (problems.Count == 0) continue;
            issues += problems.Count;
            report.Append(prefab.name).Append(":\n");
            for (int i = 0; i < problems.Count; i++) report.Append("   - ").Append(problems[i]).Append('\n');
        }

        if (issues == 0) UnityEngine.Debug.Log("Unit validation: all prefabs look configured.");
        else UnityEngine.Debug.LogWarning("Unit validation found " + issues + " issue(s):\n" + report);
    }

    [MenuItem("HexTactica/Prefabs/Migrate Units (locomotion, weight)")]
    public static void Migrate() {
        int changed = 0;
        foreach (Unit prefab in AllUnitPrefabs()) {
            Locomotion inferred = Infer(prefab);
            bool fixLoco = prefab.locomotion != inferred;
            // Weight 0 is already treated as 1 when transported; write the 1 so it is explicit.
            bool fixWeight = prefab.weight <= 0;
            if (!fixLoco && !fixWeight) continue;
            Undo.RecordObject(prefab, "Migrate unit");
            if (fixLoco) { prefab.locomotion = inferred; UnityEngine.Debug.Log(prefab.name + ": locomotion -> " + inferred); }
            if (fixWeight) { prefab.weight = 1; UnityEngine.Debug.Log(prefab.name + ": weight 0 -> 1 (what it was already treated as)"); }
            EditorUtility.SetDirty(prefab);
            changed++;
        }
        AssetDatabase.SaveAssets();
        UnityEngine.Debug.Log("Migration complete: " + changed + " prefab(s) updated. Re-run Validate.");
    }

    /// <summary>Delegates to LocomotionInference so the editor and the tests share one rule.</summary>
    public static Locomotion Infer(Unit u) { return LocomotionInference.Infer(u); }

    static List<string> Inspect(Unit u) {
        List<string> problems = new List<string>();

        Locomotion inferred = Infer(u);
        if (u.locomotion != inferred) {
            problems.Add("locomotion is " + u.locomotion + " but data suggests " + inferred
                       + " (" + LocomotionInference.Why(u) + ")");
        }

        if (u.locomotion == Locomotion.Boots && u.Armor >= 4) {
            problems.Add("classed as infantry but has Armor " + u.Armor
                       + " — it will be able to capture buildings");
        }

        if (u.Firepower > 0 && u.ammoMax <= 0) {
            problems.Add("has Firepower but ammoMax is 0, so it has infinite ammo "
                       + "(fine if intended; set ammoMax to make resupply matter)");
        }
        if (u.Firepower > 0 && u.ammoPerShot <= 0) {
            problems.Add("ammoPerShot is 0 — set at least 1 or shots are free");
        }
        if (u.moveRange > 0 && u.fuelMax <= 0) {
            problems.Add("mobile but fuelMax is 0, so it has infinite range "
                       + "(fine if intended)");
        }
        if (u.suppliesMax <= 0) {
            problems.Add("suppliesMax is 0 — this unit will never suffer attrition out of supply");
        }

        if (u.transportSlots > 0 && u.maxWeight <= 0) {
            problems.Add("has transportSlots but maxWeight 0 — it can never actually carry anything");
        }
        if (u.transportSlots <= 0 && u.maxWeight > 0) {
            problems.Add("has maxWeight but no transportSlots — it can never actually carry anything");
        }
        if (u.weight <= 0) {
            problems.Add("weight is 0 — it will be treated as weight 1 when transported");
        }

        if (u.Hub != null) {
            SupplyPoint sp = u.Hub;
            if (sp.range <= 0) problems.Add("is a SupplyPoint with range 0 — it supplies nothing");
            if (u.Firepower > 1) problems.Add("is a SupplyPoint with Firepower " + u.Firepower
                                            + " — it will be classified as a combat unit, not a hub");
        }

        if (u.AttackRange >= 2 && u.moveAndAttack) {
            problems.Add("has range " + u.AttackRange + " and moveAndAttack — it will be treated as "
                       + "a ranged line unit, not artillery. Uncheck moveAndAttack for indirect fire.");
        }

        return problems;
    }

    static List<Unit> AllUnitPrefabs() {
        List<Unit> found = new List<Unit>();
        string[] guids = AssetDatabase.FindAssets("t:Prefab");
        for (int i = 0; i < guids.Length; i++) {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) continue;
            Unit u = go.GetComponent<Unit>();
            if (u != null) found.Add(u);
        }
        return found;
    }
}
#endif
