using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum UnitStatusKind {
    LowAmmo,
    OutOfAmmo,
    LowFuel,
    OutOfFuel,
    LowSupplies,
    OutOfSupplies,
    Unsupplied,
    HubDisconnected
}

/// <summary>
/// Shows a warning icon on a unit when one of its resources is running out.
///
/// Follows the same shape as HealthDot and VeterancyChevron: drop it on a child
/// object of the unit prefab alongside a setOpacity (and optionally setSize), pick
/// the condition in the inspector, and it drives its own visibility.
///
/// This exists because ammo and fuel are now actually consumed. Before, they were
/// declared and never spent, so there was nothing to warn about; now a unit can be
/// unable to move or shoot for reasons the player cannot see on the board, which is
/// the worst kind of rule. Every consequence the resource system can produce should
/// have a matching icon here.
/// </summary>
public class UnitStatusIcon : MonoBehaviour {

    public UnitStatusKind condition = UnitStatusKind.OutOfAmmo;

    [Tooltip("Fraction at or below which the 'Low' conditions trigger")]
    [Range(0f, 1f)]
    public float lowThreshold = 0.34f;

    [Tooltip("Pulse the icon rather than showing it steady")]
    public bool pulse = true;
    public float pulseSpeed = 3f;

    [Tooltip("Optional: tint applied when active")]
    public SpriteRenderer render;
    public Color activeColor = Color.white;

    Unit unit;
    setOpacity opac;
    setSize size;
    SupplyPoint hub;

    void Start() {
        unit = GetComponentInParent<Unit>();
        opac = GetComponent<setOpacity>();
        size = GetComponent<setSize>();
        if (render == null) render = GetComponent<SpriteRenderer>();
        if (unit != null) hub = unit.GetComponent<SupplyPoint>();
    }

    void Update() {
        if (unit == null) return;

        bool active = Evaluate();

        float alpha = 0f;
        if (active) {
            alpha = 1f;
            if (pulse) {
                alpha = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.time * pulseSpeed));
            }
        }

        if (opac != null) opac.alpha = alpha;
        else if (render != null) {
            Color c = activeColor;
            c.a = alpha;
            render.color = c;
        }

        if (size != null) size.sizeF = active ? 1f : 0f;
    }

    bool Evaluate() {
        switch (condition) {

            case UnitStatusKind.OutOfAmmo:
                return unit.ammoMax > 0 && unit.ammo <= 0;

            case UnitStatusKind.LowAmmo:
                return unit.ammoMax > 0
                    && unit.ammo > 0
                    && unit.ammo <= Mathf.CeilToInt(unit.ammoMax * lowThreshold);

            case UnitStatusKind.OutOfFuel:
                return unit.fuelMax > 0 && unit.fuel <= 0;

            case UnitStatusKind.LowFuel:
                return unit.fuelMax > 0
                    && unit.fuel > 0
                    && unit.fuel <= Mathf.CeilToInt(unit.fuelMax * lowThreshold);

            case UnitStatusKind.OutOfSupplies:
                return unit.suppliesMax > 0 && unit.supplies <= 0;

            case UnitStatusKind.LowSupplies:
                return unit.suppliesMax > 0
                    && unit.supplies > 0
                    && unit.supplies <= Mathf.CeilToInt(unit.suppliesMax * lowThreshold);

            case UnitStatusKind.Unsupplied:
                return unit.tile != null
                    && unit.owner != null
                    && !unit.tile.isSupplied(unit.owner);

            case UnitStatusKind.HubDisconnected:
                return hub != null && !hub.connected;
        }
        return false;
    }
}
