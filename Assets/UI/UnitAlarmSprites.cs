using UnityEngine;

/// <summary>
/// The pictures and timing for unit alarms (UnitAlarmIcon): one asset, Assets/Resources/UnitAlarmSprites.asset
/// (made automatically the first time the editor opens without it; edit it there). An alarm with
/// no picture is not shown.
/// </summary>
[CreateAssetMenu(menuName = "HexTactica/Unit Alarm Sprites", fileName = "UnitAlarmSprites")]
public class UnitAlarmSprites : ScriptableObject {

    [Tooltip("Shown when the unit is low on fuel (lowShare of a full tank or less), or out.")]
    public Sprite outOfFuel;
    [Tooltip("Shown when the unit is low on ammunition (lowShare of a full load or less), or out.")]
    public Sprite outOfAmmo;
    [Tooltip("Shown when the unit is starving: out of supplies and out of supply, so it loses strength each turn.")]
    public Sprite starving;
    [Tooltip("The fuel and ammunition alarms show once the unit is down to this share of its full load or less (and stay when it is empty).")]
    [Range(0, 1)] public float lowShare = 1f / 3f;
    [Tooltip("Seconds each alarm shows before the next one, when a unit has more than one.")]
    public float secondsEach = 1.5f;

    public const string ResourceName = "UnitAlarmSprites";
    static UnitAlarmSprites instance;

    /// <summary>The asset in Resources; an empty one (no alarms shown) if there is none.</summary>
    public static UnitAlarmSprites Instance {
        get {
            if (instance == null) instance = Resources.Load<UnitAlarmSprites>(ResourceName);
            if (instance == null) instance = CreateInstance<UnitAlarmSprites>();
            return instance;
        }
    }
}
