using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One icon on a unit (put it on the Image, or SpriteRenderer, in the unit's canvas) that cycles
/// through every alarm the unit has — low on fuel, low on ammo (a third of a full load or less,
/// UnitAlarmSprites.lowShare), starving — each for
/// UnitAlarmSprites.secondsEach, and hides when there is none. Pictures: UnitAlarmSprites.
/// </summary>
public class UnitAlarmIcon : MonoBehaviour {

    [Tooltip("Seconds per alarm here; 0 or less: the UnitAlarmSprites asset's.")]
    public float secondsEach = 0f;

    Unit unit;
    Image image;
    SpriteRenderer render;
    readonly Sprite[] showing = new Sprite[3];

    void Start() {
        unit = GetComponentInParent<Unit>();
        image = GetComponent<Image>();
        render = GetComponent<SpriteRenderer>();
    }

    void Update() {
        if (unit == null) return;
        UnitAlarmSprites art = UnitAlarmSprites.Instance;
        int n = 0;
        if (Low(unit.fuel, unit.fuelMax, art.lowShare) && art.outOfFuel != null) showing[n++] = art.outOfFuel;
        if (Low(unit.ammo, unit.ammoMax, art.lowShare) && art.outOfAmmo != null) showing[n++] = art.outOfAmmo;
        if (Starving(unit) && art.starving != null) showing[n++] = art.starving;

        Sprite s = null;
        if (n > 0) {
            float each = secondsEach > 0f ? secondsEach : Mathf.Max(0.1f, art.secondsEach);
            s = showing[(int)(Time.time / each) % n];
        }
        if (image != null) { image.enabled = s != null; if (s != null) image.sprite = s; }
        if (render != null) { render.enabled = s != null; if (s != null) render.sprite = s; }
    }

    /// <summary>Down to <paramref name="share"/> of a full load or less (a unit that carries none never is).</summary>
    public static bool Low(int have, int max, float share) {
        return max > 0 && have <= Mathf.Floor(max * share + 1e-4f);
    }

    /// <summary>Out of supplies and out of supply: it loses a strength point each turn (its transport's supply, if aboard).</summary>
    public static bool Starving(Unit u) {
        if (u == null || u.suppliesMax <= 0 || u.supplies > 0 || u.owner == null) return false;
        Unit at = u;
        while (at.trasnporter != null) at = at.trasnporter;
        if (u.locomotion == Locomotion.Jet) return at.tile != null && at.tile.building != null && at.tile.building.services.Contains(u.locomotion);
        else return at.tile != null && !at.tile.isSupplied(u.owner);
    }
}
