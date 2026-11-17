using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using HexTactica.Multiplayer;

/// <summary>
/// Networked games' part of the front end (Assets/Multiplayer): made by the same tool, the same way.
///
/// Create Missing Scenes (and the first run) makes what is missing and touches nothing that exists:
/// - Assets/Resources/MultiplayerSettings.asset (the backend, how often to look for news, join codes, turn limits);
/// - the MultiplayerOverlay scene (over a networked game: whose turn, the turn clock, Skip, desyncs, the host's
///   Players panel) and the TurnNotice scene (the your-turn notice over any screen), listed in the build settings;
/// - the row prefabs Mp Game Row, Mp Member Row, Mp Side Row in Assets/FrontEnd/Prefabs.
///
/// Update Scenes adds the networked game's widgets to scenes made before them, changing nothing else:
/// - MainMenu: Host Game and Join Game (copies of your Load Scenario button), the Join Game window, and the
///   player's name and games drop-down in the top right corner;
/// - PreGame: the lobby (its name, turn limit, who is in it) at the top of the setup panel, the join code in a panel of
///   its own on the right (with Copy), and a Take / Give up button on the Side Row prefab (an earlier Player label is
///   removed: the side's drop-down says who plays it);
/// - InGameMenu: saving and loading hidden in a networked game (an earlier Leave game button is removed);
/// - MultiplayerOverlay: an earlier whose-turn panel replaced by the strip under the turn banner;
/// - PostGame: Play again hidden for a networked game.
/// Each step is skipped once done.
/// </summary>
static partial class FrontEndScenes {
    const string MpSettingsPath = "Assets/Resources/" + MultiplayerSettings.ResourceName + ".asset";

    static MultiplayerSettings MpSettings() {
        MultiplayerSettings s = AssetDatabase.LoadAssetAtPath<MultiplayerSettings>(MpSettingsPath);
        if (s != null) return s;
        Directory.CreateDirectory(Path.GetDirectoryName(MpSettingsPath));
        s = ScriptableObject.CreateInstance<MultiplayerSettings>();
        AssetDatabase.CreateAsset(s, MpSettingsPath);
        Debug.Log("Created " + MpSettingsPath + " (networked games: the backend, polling, join codes, turn limits).");
        return s;
    }

    /// <summary>Networked games' scenes, made if missing (Create Missing Scenes, Update Scenes).</summary>
    static void MakeMultiplayerScenes(bool fromMenu, List<string> made) {
        MultiplayerSettings s = MpSettings();
        Make(s.overlayScene, BuildMpOverlay, fromMenu, made);
        Make(s.noticeScene, BuildTurnNotice, fromMenu, made);
    }

    static IEnumerable<string> MultiplayerSceneNames() {
        MultiplayerSettings s = MpSettings();
        yield return s.overlayScene;
        yield return s.noticeScene;
    }

    // ------------------------------------------------------------------ new scenes

    /// <summary>Over a networked game, under the HUD's menus: the turn line and clock at the top, Players top right, the desync window in the middle.</summary>
    static void BuildMpOverlay() {
        Canvas canvas = MakeCanvas("Multiplayer", 95);
        MpOverlay m = canvas.gameObject.AddComponent<MpOverlay>();
        Transform root = canvas.transform;

        StatusStrip(root, m);
        m.clock = ClockFace(root);
        m.clock.gameObject.SetActive(false);

        m.playersButton = Btn(root, "Players", m.TogglePlayers, 200f, 22f);
        Place((RectTransform)m.playersButton.transform, new Vector2(1f, 1f), new Vector2(-130f, -150f), new Vector2(200f, 46f));
        GameObject players = Go("Players", root, typeof(Image));
        Panel(players, PanelColor);
        Place((RectTransform)players.transform, new Vector2(1f, 1f), new Vector2(-330f, -470f), new Vector2(620f, 560f));
        VLayout(players, 20, 10f);
        Text(players.transform, "Heading", "Players", 30, TextAlignmentOptions.Left, FontStyles.Bold);
        m.memberRows = ScrollList(players.transform, "Members", -1f, 220f);
        m.memberTemplate = RowPrefab("Mp Member Row", MemberRow);
        Text(players.transform, "Open heading", "Sides with nobody to play them", 22, TextAlignmentOptions.Left, FontStyles.Bold);
        m.sideRows = ScrollList(players.transform, "Open sides", -1f, -1f);
        m.sideTemplate = RowPrefab("Mp Side Row", SideHandRow);
        Btn(players.transform, "Close", m.TogglePlayers, -1f, 22f);
        m.playersPanel = players;
        players.SetActive(false);

        GameObject desync = Window(root, "Desync", 980f, 560f);
        Text(desync.transform, "Heading", "The game is out of step", 36, TextAlignmentOptions.Left, FontStyles.Bold);
        m.desyncText = Text(desync.transform, "What happened", "", 22, TextAlignmentOptions.TopLeft, FontStyles.Normal);
        Flexible(m.desyncText.gameObject, true);
        GameObject buttons = Row(desync.transform, "Host's buttons", 64f);
        Btn(buttons.transform, "Continue from my board", m.ContinueFromMyBoard, 420f);
        Spacer(buttons.transform);
        Btn(buttons.transform, "End the game", m.EndGame, 300f);
        m.hostDesyncButtons = buttons;
        m.desyncPanel = desync;
        desync.SetActive(false);
    }

    /// <summary>
    /// The strip just under the turn banner's line (top centre): what is going on (catching up, watching), a problem
    /// reaching the game store, and Skip their turn under them. Hidden while it has nothing to say.
    /// </summary>
    static void StatusStrip(Transform root, MpOverlay m) {
        GameObject top = Go("Status", root, typeof(Image));
        Panel(top, new Color(PanelColor.r, PanelColor.g, PanelColor.b, 0.8f));
        RectTransform rt = (RectTransform)top.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -52f); rt.sizeDelta = new Vector2(520f, 0f);
        VLayout(top, 8, 4f);
        top.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        m.turnLine = Text(top.transform, "What is going on", "Catching up on what happened...", 20, TextAlignmentOptions.Center, FontStyles.Bold);
        m.notice = Text(top.transform, "Notice", "", 18, TextAlignmentOptions.Center, FontStyles.Italic);
        m.skipButton = Btn(top.transform, "Skip their turn", m.Skip, -1f, 20f);
        m.skipButton.gameObject.SetActive(false);
        m.statusPanel = top;
        top.SetActive(false);
    }

    /// <summary>MultiplayerOverlay made before the turn banner named people: its big turn panel becomes the strip under the banner, the clock moves beside it.</summary>
    static void UpdateMpOverlay(MpOverlay m, List<string> done) {
        if (m.statusPanel != null) return;
        Transform old = m.transform.Find("Turn");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        StatusStrip(m.transform, m);
        if (m.clock != null) Place((RectTransform)m.clock.transform, new Vector2(0.5f, 1f), new Vector2(300f, -70f), new Vector2(120f, 120f));
        done.Add("whose-turn panel replaced by a status strip under the turn banner; the clock moved beside the banner");
    }

    /// <summary>The turn clock: a round face, a minute hand and a second hand turning about its centre, the time in digits under it.</summary>
    static MpClock ClockFace(Transform root) {
        GameObject g = Go("Turn Clock", root, typeof(Image));
        MpClock c = g.AddComponent<MpClock>();
        Image face = g.GetComponent<Image>();
        face.sprite = Builtin("Knob"); face.color = TextColor;
        c.face = face;
        Place((RectTransform)g.transform, new Vector2(0.5f, 1f), new Vector2(300f, -70f), new Vector2(120f, 120f));
        c.minuteHand = Hand(g.transform, "Minute hand", 8f, 44f, new Color(0.1f, 0.12f, 0.16f, 1f));
        c.secondHand = Hand(g.transform, "Second hand", 3f, 54f, new Color(0.85f, 0.2f, 0.2f, 1f));
        c.digits = Text(g.transform, "Time left", "0:00", 22, TextAlignmentOptions.Center, FontStyles.Bold);
        Place(c.digits.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, -22f), new Vector2(140f, 30f));
        return c;
    }

    static RectTransform Hand(Transform face, string name, float width, float length, Color color) {
        GameObject g = Go(name, face, typeof(Image));
        g.GetComponent<Image>().color = color;
        RectTransform rt = (RectTransform)g.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0f);   // (turns about the face's centre)
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(width, length);
        return rt;
    }

    /// <summary>The your-turn notice: a strip at the top of the screen over anything, with Open and Dismiss.</summary>
    static void BuildTurnNotice() {
        Canvas canvas = MakeCanvas("Turn Notice", 200);
        MpNotice n = canvas.gameObject.AddComponent<MpNotice>();
        GameObject strip = Go("Notice", canvas.transform, typeof(Image));
        Panel(strip, new Color(Accent.r * 0.6f, Accent.g * 0.6f, Accent.b * 0.6f, 0.97f));
        Place((RectTransform)strip.transform, new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(980f, 84f));
        HLayout(strip, 16, 14f);
        n.message = Text(strip.transform, "Message", "Your turn in Tidal Ridge#K7Q2MX", 28, TextAlignmentOptions.Left, FontStyles.Bold);
        Flexible(n.message.gameObject);
        n.openButton = Btn(strip.transform, "Open", n.Open, 160f, 24f);
        Btn(strip.transform, "Dismiss", n.Dismiss, 160f, 24f);
    }

    // ------------------------------------------------------------------ row prefabs

    static MpGameRow GameRow(Transform parent) {
        GameObject g = ListRow(parent, "Mp Game Row", 52f);
        MpGameRow r = g.AddComponent<MpGameRow>();
        r.background = g.GetComponent<Image>();
        r.button = g.GetComponent<Button>();
        r.title = Text(g.transform, "Name", "Tidal Ridge#K7Q2MX", 24, TextAlignmentOptions.Left, FontStyles.Bold);
        Flexible(r.title.gameObject);
        r.state = Text(g.transform, "State", "Your turn", 20, TextAlignmentOptions.Right, FontStyles.Italic);
        Size(r.state.gameObject, 220f, -1f);
        return r;
    }

    static MpMemberRow MemberRow(Transform parent) {
        GameObject g = Row(parent, "Mp Member Row", 44f);
        MpMemberRow r = g.AddComponent<MpMemberRow>();
        r.label = Text(g.transform, "Name", "Bob", 22, TextAlignmentOptions.Left, FontStyles.Normal);
        Flexible(r.label.gameObject);
        r.kickButton = Btn(g.transform, "Remove", null, 130f, 20f);
        return r;
    }

    static MpSideRow SideHandRow(Transform parent) {
        GameObject g = Row(parent, "Mp Side Row", 48f);
        MpSideRow r = g.AddComponent<MpSideRow>();
        r.sideName = Text(g.transform, "Side", "Red", 22, TextAlignmentOptions.Left, FontStyles.Bold);
        Size(r.sideName.gameObject, 110f, -1f);
        r.players = Drop(g.transform, 170f);
        r.giveButton = Btn(g.transform, "Give", null, 90f, 20f);
        r.aiButton = Btn(g.transform, "AI", null, 70f, 20f);
        r.eliminateButton = Btn(g.transform, "Eliminate", null, 120f, 20f);
        return r;
    }

    // ------------------------------------------------------------------ updates to existing scenes

    /// <summary>Host Game, Join Game, the join window, and the player's name and games list.</summary>
    static void UpdateMainMenuMultiplayer(MainMenu menu, List<string> done) {
        Transform mainButtons = menu.mainPanel != null ? menu.mainPanel.transform : null;
        Button load = FindButton(mainButtons, "ShowLoadScenario");
        if (mainButtons != null && FindButton(mainButtons, "ShowHostGame") == null) {
            Button host = load != null ? CopyButton(load, "Host Game", menu.ShowHostGame) : Btn(mainButtons, "Host Game", menu.ShowHostGame);
            CopyButton(host, "Join Game", menu.ShowJoinGame);
            done.Add("Host Game and Join Game buttons added");
        }
        if (menu.loadPanel != null && menu.loadHeading == null) {
            Transform h = menu.loadPanel.transform.Find("Heading");
            if (h != null) { menu.loadHeading = h.GetComponent<TMP_Text>(); if (menu.loadHeading != null) menu.loadHeadingText = menu.loadHeading.text; }
            Transform tabs = menu.loadPanel.transform.Find("Tabs");
            if (tabs != null) menu.loadTabs = tabs.gameObject;
            done.Add("Load Scenario's heading and tabs linked (Host Game changes them)");
        }
        if (menu.joinPanel == null) {
            GameObject w = Window(menu.transform, "Join Game", 900f, 420f);
            MpJoinPanel jp = w.AddComponent<MpJoinPanel>();
            Text(w.transform, "Heading", "Join Game", 40, TextAlignmentOptions.Left, FontStyles.Bold);
            Text(w.transform, "Hint", "Type the join code the host shows (with or without the game's name before it).", 22, TextAlignmentOptions.Left, FontStyles.Normal);
            jp.code = Field(w.transform, "K7Q2MX");
            jp.status = Text(w.transform, "Status", "", 22, TextAlignmentOptions.Left, FontStyles.Italic);
            Flexible(jp.status.gameObject, true);
            GameObject buttons = Row(w.transform, "Buttons", 60f);
            Btn(buttons.transform, "Back", menu.ShowMain, 200f);
            Spacer(buttons.transform);
            Btn(buttons.transform, "Watch", jp.Watch, 200f);
            Btn(buttons.transform, "Join", jp.Join, 200f);
            menu.joinPanel = w;
            w.SetActive(false);
            done.Add("Join Game window added");
        }
        if (menu.GetComponentInChildren<MpGamesMenu>(true) == null) {
            GameObject corner = Go("Networked Games", menu.transform);
            RectTransform rt = (RectTransform)corner.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-30f, -30f); rt.sizeDelta = new Vector2(560f, 640f);
            VLayout(corner, 0, 8f).childForceExpandHeight = false;
            MpGamesMenu gm = corner.AddComponent<MpGamesMenu>();
            GameObject top = Row(corner.transform, "Name and games", 50f);
            Text(top.transform, "Name label", "Name", 22, TextAlignmentOptions.Right, FontStyles.Normal).gameObject.Let(g => Size(g, 70f, -1f));
            gm.playerName = Field(top.transform, "Your name");
            Flexible(gm.playerName.gameObject);
            gm.toggleButton = Btn(top.transform, "Games .", gm.Toggle, 160f, 24f);
            gm.toggleLabel = gm.toggleButton.GetComponentInChildren<TMP_Text>();
            GameObject list = Go("Games", corner.transform, typeof(Image));
            Panel(list, PanelColor);
            VLayout(list, 14, 8f);
            Size(list, -1f, 520f);
            gm.rows = ScrollList(list.transform, "Rows", -1f, -1f);
            gm.rowTemplate = RowPrefab("Mp Game Row", GameRow);
            gm.status = Text(list.transform, "Status", "", 20, TextAlignmentOptions.Left, FontStyles.Italic);
            gm.listPanel = list;
            list.SetActive(false);
            done.Add("networked games list and player name added (top right)");
        }
        MpGamesMenu games = menu.GetComponentInChildren<MpGamesMenu>(true);
        if (games != null && games.linkPanel == null && games.listPanel != null) {
            Btn(games.listPanel.transform, "Link another device", games.ToggleLink, -1f, 20f);
            GameObject link = Go("Link devices", games.transform, typeof(Image));
            Panel(link, PanelColor);
            VLayout(link, 14, 8f);
            Size(link, -1f, 300f);
            Text(link.transform, "Heading", "Link another device", 24, TextAlignmentOptions.Left, FontStyles.Bold);
            games.linkStatus = Text(link.transform, "Help", "", 18, TextAlignmentOptions.TopLeft, FontStyles.Normal);
            Flexible(games.linkStatus.gameObject, true);
            games.linkCode = Field(link.transform, "Code");
            GameObject row = Row(link.transform, "Buttons", 46f);
            Btn(row.transform, "Get code", games.GetCode, -1f, 20f);
            Btn(row.transform, "Confirm", games.ConfirmCode, -1f, 20f);
            Btn(row.transform, "Use it", games.UseLinked, -1f, 20f);
            games.linkPanel = link;
            link.SetActive(false);
            done.Add("linking another device added to the games list");
        }
        if (games != null && (menu.mainPanelOnly == null || menu.mainPanelOnly.Length == 0)) {
            menu.mainPanelOnly = new[] { games.gameObject };
            done.Add("networked games list shown on the main panel only");
        }
    }

    /// <summary>The lobby on the setup panel, and on the Side Row prefab who holds the side with Take / Give up.</summary>
    static void UpdatePreGameMultiplayer(PreGameMenu m, List<string> done) {
        if (m.lobby == null) {
            MpLobby lobby = m.gameObject.AddComponent<MpLobby>();
            Transform panel = m.title != null ? m.title.transform.parent : m.transform;
            int at = m.title != null ? m.title.transform.GetSiblingIndex() + 1 : 0;
            GameObject nameRow = Row(panel, "Lobby name", 48f);
            nameRow.transform.SetSiblingIndex(at++);
            Text(nameRow.transform, "Label", "Name", 22, TextAlignmentOptions.Left, FontStyles.Normal).gameObject.Let(g => Size(g, 150f, -1f));
            lobby.lobbyName = Field(nameRow.transform, "The lobby's name");
            Flexible(lobby.lobbyName.gameObject);
            GameObject limitRow = Row(panel, "Turn limit", 48f);
            limitRow.transform.SetSiblingIndex(at++);
            Text(limitRow.transform, "Label", "Turn limit", 22, TextAlignmentOptions.Left, FontStyles.Normal).gameObject.Let(g => Size(g, 150f, -1f));
            lobby.turnLimit = Drop(limitRow.transform, 260f);
            Flexible(lobby.turnLimit.gameObject);
            Text(panel, "Players heading", "In the lobby", 24, TextAlignmentOptions.Left, FontStyles.Bold).transform.SetSiblingIndex(at++);
            lobby.members = ScrollList(panel, "Players", -1f, 150f);
            lobby.members.GetComponentInParent<ScrollRect>().transform.SetSiblingIndex(at++);
            lobby.memberTemplate = RowPrefab("Mp Member Row", MemberRow);
            lobby.status = Text(panel, "Lobby status", "", 22, TextAlignmentOptions.Left, FontStyles.Italic);
            lobby.status.transform.SetSiblingIndex(at++);
            // Shown only for a networked lobby.
            GameObject[] parts = { nameRow, limitRow, panel.Find("Players heading").gameObject, lobby.members.GetComponentInParent<ScrollRect>().gameObject, lobby.status.gameObject };
            m.lobby = lobby;
            m.lobbyOnly = parts;
            done.Add("networked lobby added (name, turn limit, players)");
        }
        if (m.lobby.joinCodePanel == null) {
            MpLobby lobby = m.lobby;
            // A lobby made before the panel had the code as a line on the setup panel: the panel replaces it.
            if (lobby.joinCode != null) Object.DestroyImmediate(lobby.joinCode.gameObject);
            GameObject p = Go("Join code", m.transform, typeof(Image));
            Panel(p, PanelColor);
            Place((RectTransform)p.transform, new Vector2(1f, 1f), new Vector2(-250f, -170f), new Vector2(420f, 260f));
            VLayout(p, 20, 8f);
            Text(p.transform, "Heading", "Join code", 26, TextAlignmentOptions.Left, FontStyles.Bold);
            lobby.joinCode = Text(p.transform, "Code", "K7Q2MX", 56, TextAlignmentOptions.Center, FontStyles.Bold);
            Text(p.transform, "Hint", "Give this to the other players: they type it in Join Game.", 18, TextAlignmentOptions.Left, FontStyles.Italic);
            Btn(p.transform, "Copy", lobby.CopyCode, -1f, 22f);
            lobby.joinCodePanel = p;
            List<GameObject> parts = new List<GameObject>();
            foreach (GameObject g in m.lobbyOnly) if (g != null) parts.Add(g);
            parts.Add(p);
            m.lobbyOnly = parts.ToArray();
            p.SetActive(false);
            done.Add("join code moved to its own panel on the right, with Copy");
        }
        if (m.rowTemplate != null && m.rowTemplate.seatButton == null) {
            string path = AssetDatabase.GetAssetPath(m.rowTemplate.gameObject);
            if (!string.IsNullOrEmpty(path)) {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try {
                    PlayerSetupRow r = root.GetComponent<PlayerSetupRow>();
                    if (r != null && r.seatButton == null) {
                        int at = r.sideName != null ? r.sideName.transform.GetSiblingIndex() + 1 : root.transform.childCount;
                        r.seatButton = Btn(root.transform, "Take", null, 110f, 20f);
                        r.seatButton.transform.SetSiblingIndex(at);
                        r.seatLabel = r.seatButton.GetComponentInChildren<TMP_Text>();
                        r.seatButton.gameObject.SetActive(false);
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        done.Add("Side Row prefab: Take / Give up");
                    }
                } finally { PrefabUtility.UnloadPrefabContents(root); }
            }
        }
        // Who holds a side is in its drop-down: the Player label an earlier version added only squeezed the side's name.
        if (m.rowTemplate != null && m.rowTemplate.playerLabel != null) {
            string path = AssetDatabase.GetAssetPath(m.rowTemplate.gameObject);
            if (!string.IsNullOrEmpty(path)) {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try {
                    PlayerSetupRow r = root.GetComponent<PlayerSetupRow>();
                    if (r != null && r.playerLabel != null) {
                        Object.DestroyImmediate(r.playerLabel.gameObject);
                        r.playerLabel = null;
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        done.Add("Side Row prefab: the Player label removed (the side's drop-down says who plays it)");
                    }
                } finally { PrefabUtility.UnloadPrefabContents(root); }
            }
        }
    }

    /// <summary>Saving and loading hidden in a networked game (Resign and Exit to main menu cover leaving one).</summary>
    static void UpdateInGameMenuMultiplayer(InGameMenu m, List<string> done) {
        if (m.leaveButton != null) {
            Object.DestroyImmediate(m.leaveButton.gameObject);
            m.leaveButton = null;
            done.Add("Leave game button removed (Resign and Exit to main menu cover it)");
        }
        if (m.hiddenInNetworked != null && m.hiddenInNetworked.Length > 0) return;
        List<GameObject> hide = new List<GameObject>();
        Transform w = m.window != null ? m.window.transform : m.transform;
        foreach (string n in new[] { "Save heading", "Save", "Load heading", "Saves" }) { Transform t = w.Find(n); if (t != null) hide.Add(t.gameObject); }
        if (m.loadButton != null) hide.Add(m.loadButton.gameObject);
        m.hiddenInNetworked = hide.ToArray();
        if (hide.Count > 0) done.Add("saving and loading hidden in a networked game");
    }

    static void UpdatePostGameMultiplayer(PostGameScreen m, List<string> done) {
        if (m.playAgainButton != null) return;
        m.playAgainButton = FindButton(m.transform, "PlayAgain");
        if (m.playAgainButton != null) done.Add("Play again linked (hidden for a networked game)");
    }
}
