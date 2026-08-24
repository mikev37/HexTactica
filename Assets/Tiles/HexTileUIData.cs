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
    FREE
}
