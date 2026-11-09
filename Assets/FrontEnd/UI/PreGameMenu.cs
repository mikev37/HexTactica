using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The pre-game screen (the PreGame scene, over the game scene with the scenario's board shown and held): for each
/// side, who plays it (a human, or this AI with one of its profiles) and its faction (unless the map fixes it); fog of
/// war, purchases (building units), and faction special units. Start loads the scenario again with these choices and
/// lets it play; Back returns to the main menu. The side rows are copies of a row prefab.
/// </summary>
public class PreGameMenu : MonoBehaviour {
    public TMP_Text title;
    [Tooltip("What the scenario file says about itself.")]
    public TMP_Text details;
    public PlayerSetupRow rowTemplate;
    public RectTransform rows;
    public Toggle fog;
    [Tooltip("Purchases: units can be bought at buildings.")]
    public Toggle build;
    [Tooltip("Faction special units: off, a faction only changes how units look.")]
    public Toggle specialUnits;
    [Tooltip("What turning special units off changes for the factions picked. Optional.")]
    public TMP_Text specialNote;
    [Tooltip("The faction list's first entry: no faction (the base units).")]
    public string noFaction = "No faction";
    [Tooltip("Who plays a side: a human at the screen.")]
    public string humanLabel = "Human";
    [Tooltip("Who plays a side: this AI with its default profile.")]
    public string defaultAiLabel = "AI (default)";
    [Tooltip("Who plays a side: this AI with a profile from FrontEndSettings.aiProfiles; {0} the profile's name.")]
    public string profileAiLabel = "AI: {0}";
    public Button startButton;

    readonly List<PlayerSetupRow> made = new List<PlayerSetupRow>();
    List<Faction> factions;
    List<HexTactica.AI.AIProfile> profiles;

    void Awake() {
        if (rowTemplate != null && rowTemplate.gameObject.scene.IsValid()) rowTemplate.gameObject.SetActive(false);
        TransportPanel.EnsureEventSystem();
        if (specialUnits != null) specialUnits.onValueChanged.AddListener(v => ShowSpecialNote());
    }

    void Start() {
        factions = GameAssets.AllFactions.OrderBy(f => f.name).ToList();
        // (MatchSetup.ReadFromBoard was read by GameSession from the board as the file has it.)
        if (title != null) title.text = Path.GetFileNameWithoutExtension(MatchSetup.FilePath);
        if (details != null) details.text = GameFiles.Describe(MatchSetup.FilePath);
        // A rule the map fixes is shown as it is and cannot be changed.
        if (fog != null) { fog.SetIsOnWithoutNotify(MatchSetup.Fog); fog.interactable = !MatchSetup.FogFixed; }
        if (build != null) { build.SetIsOnWithoutNotify(MatchSetup.Purchases); build.interactable = !MatchSetup.PurchasesFixed; }
        if (specialUnits != null) { specialUnits.SetIsOnWithoutNotify(MatchSetup.SpecialUnits); specialUnits.interactable = !MatchSetup.SpecialUnitsFixed; }
        List<string> names = new List<string> { noFaction };
        names.AddRange(factions.Select(f => f.name));
        GameController gc = GameController.gc;
        profiles = FrontEndSettings.Get.aiProfiles.Where(x => x != null).ToList();
        List<string> controls = new List<string> { humanLabel, defaultAiLabel };
        controls.AddRange(profiles.Select(x => string.Format(profileAiLabel, x.profileName)));
        foreach (MatchSetup.Side side in MatchSetup.Sides) {
            if (rowTemplate == null || rows == null) break;
            PlayerSetupRow row = Instantiate(rowTemplate, rows);
            row.gameObject.SetActive(true);
            Player p = gc != null ? gc.players.Find(x => x != null && x.id == side.id) : null;
            int pick = factions.FindIndex(f => f.name == side.faction) + 1;
            int profile = profiles.FindIndex(x => x.profileName == side.profile);
            int control = !side.ai ? 0 : profile >= 0 ? 2 + profile : 1;
            row.Show(side, p != null ? p.displayColor : Color.white, controls, control, names, pick);
            if (row.faction != null) row.faction.onValueChanged.AddListener(i => ShowSpecialNote());
            made.Add(row);
        }
        ShowSpecialNote();
    }

    /// <summary>The special units the factions picked have, and so what turning them off takes away.</summary>
    void ShowSpecialNote() {
        if (specialNote == null) return;
        List<string> lines = new List<string>();
        foreach (PlayerSetupRow row in made) {
            Faction f = FactionAt(row.faction != null ? row.faction.value : 0);
            if (f == null) continue;
            List<string> special = new List<string>();
            if (f.replacements != null) foreach (Faction.Replacement r in f.replacements) if (r != null && r.replacement != null && !f.Cosmetic(r)) special.Add(r.replacement.name);
            if (f.extras != null) foreach (Faction.Extra e in f.extras) if (e != null && e.unit != null) special.Add(e.unit.name);
            if (f.removed != null && f.removed.Count > 0) special.Add("no " + string.Join(", no ", f.removed.Where(u => u != null).Select(u => u.name).ToArray()));
            if (special.Count > 0) lines.Add(f.name + ": " + string.Join(", ", special.ToArray()));
        }
        bool on = specialUnits == null || specialUnits.isOn;
        specialNote.text = lines.Count == 0 ? "" : (on ? "Special units in play — " : "Left out (looks only) — ") + string.Join("; ", lines.ToArray());
    }

    Faction FactionAt(int option) { return option > 0 && option <= factions.Count ? factions[option - 1] : null; }

    /// <summary>Start the game with these choices.</summary>
    public void StartGame() {
        foreach (PlayerSetupRow row in made) {
            if (row.Side == null) continue;
            int c = row.control != null ? row.control.value : 0;
            row.Side.ai = c > 0;
            row.Side.profile = c >= 2 && c - 2 < profiles.Count ? profiles[c - 2].profileName : "";
            if (!row.Side.factionLocked) {
                Faction f = FactionAt(row.faction != null ? row.faction.value : 0);
                row.Side.faction = f != null ? f.name : "";
            }
        }
        MatchSetup.Fog = fog != null && fog.isOn;
        MatchSetup.Purchases = build == null || build.isOn;
        MatchSetup.SpecialUnits = specialUnits == null || specialUnits.isOn;
        if (!MatchSetup.StartGame()) { if (details != null) details.text = "Could not load " + MatchSetup.FilePath; return; }
        GameSession.Frame();
        if (GameSession.Instance != null) GameSession.Instance.SetupDone();
    }

    public void Back() { MatchSetup.ToMainMenu(); }
}
