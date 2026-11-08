using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
[CreateAssetMenu(menuName = "HexTileUI")]
public class HexTileUIData : ScriptableSingleton<HexTileUIData> {

    public Sprite selected;
    public Sprite move_possible;
    public Sprite move_arrow;
    public Sprite attack_possible;
    public Sprite move_confirm;
    public Sprite attack_Confirm;
    public Sprite load;
    public Sprite unload;
    [Tooltip("A move tile where ending the move would capture a building (Sprites/UI/CapOption). Empty: the ordinary move sprite.")]
    public Sprite capture;
    [Tooltip("A move tile where ending the move would leave the unit unable to reach a building that refuels it before it runs dry next turn (Unit.WouldDitchFrom). Empty: the ordinary move sprite.")]
    public Sprite move_warning;
    [Header("An enemy unit clicked (SupplyOverlay.ShowEnemy)")]
    [Tooltip("The tiles it can move to.")]
    public Color enemyMove = new Color(0.6f, 0.6f, 0.6f, 0.5f);
    [Tooltip("The tiles it can fire on (for a unit that cannot move and fire, its whole range from where it stands, over its moves).")]
    public Color enemyShoot = new Color(1f, 0.55f, 0.1f, 0.5f);
       
}

public enum UIState {
	SELECTED,
    MOVE,
    MOVE_ARROW,
    ATTACK,
    ATTACK_CONFIRM,
    MOVE_CONFIRM,
    LOAD_CONFIRM,
    UNLOAD_CONFIRM,
    FREE,
    /// <summary>An enemy unit was clicked: a tile it can move to (SupplyOverlay.ShowEnemy).</summary>
    ENEMY_MOVE,
    /// <summary>An enemy unit was clicked: a tile it could fire on.</summary>
    ENEMY_THREAT,
    /// <summary>A selected unit that fires without moving (artillery): a tile in its range with nothing to hit now.</summary>
    RANGE
}
