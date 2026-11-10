using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The turn controls in the HUD prefab: the player's gold, a Next unit button and an End turn button. The buttons call
/// <see cref="NextUnit"/> and <see cref="EndTurn"/> from the prefab; those go the same way as the board and the keys
/// (GameController.SelectNextIdle, which clicks the unit's tile; GameController.HumanEndTurn, which Space calls).
/// Everything it looks like is the prefab's: this only fills in the gold and greys out the buttons when it is not a
/// human player's turn.
/// </summary>
public class TurnControls : MonoBehaviour {
    [Tooltip("The gold of the player at the screen.")]
    public TMP_Text gold;
    [Tooltip("Shown before the amount.")]
    public string goldPrefix = "Gold ";
    public Button nextUnitButton, endTurnButton;

    int shownGold = int.MinValue;

    void Update() {
        GameController gc = GameController.gc;
        Player p = gc != null ? (FogOfWar.Viewer ?? gc.activePlayer) : null;
        int money = p != null ? p.money : 0;
        if (gold != null && money != shownGold) { shownGold = money; gold.text = goldPrefix + money; }
        // No buying in this game (purchases off): gold means nothing, so it is not shown.
        bool buying = gc == null || gc.purchasesAllowed;
        if (gold != null && gold.gameObject.activeSelf != buying) gold.gameObject.SetActive(buying);
        bool ours = gc != null && !gc.gameOver && !gc.EndTurnPending && FogOfWar.IsHuman(gc.activePlayer);
        if (nextUnitButton != null && nextUnitButton.interactable != ours) nextUnitButton.interactable = ours;
        if (endTurnButton != null && endTurnButton.interactable != ours) endTurnButton.interactable = ours;
    }

    /// <summary>Next unit: the next idle unit, selected, and the view centred on it.</summary>
    public void NextUnit() {
        if (GameController.gc == null) return;
        Unit u = GameController.gc.SelectNextIdle();
        CameraRig rig = Camera.main != null ? Camera.main.GetComponent<CameraRig>() : null;
        if (u != null && u.tile != null && rig != null) rig.CenterOn(u.tile.transform.position);
    }

    /// <summary>End turn: as the Space key.</summary>
    public void EndTurn() {
        if (GameController.gc != null) GameController.gc.HumanEndTurn();
    }
}
