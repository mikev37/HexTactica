using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HexTactica.Multiplayer {

    /// <summary>
    /// The pre-game screen as a networked game's lobby (on the PreGame scene beside its PreGameMenu, which hands over to
    /// it when MatchSetup.Networked). Shows the join code (name#code) for the others, and for each side who plays it:
    /// open (any joined player can take it), the host's device ("Local"), or the AI. The host sets the sides, the
    /// rules, the turn limit and the lobby's name (the scenario's at first, locked once the game starts), removes
    /// players, and starts the game once no side is open. A player takes an open side or gives theirs up, and picks
    /// its faction. Everyone sees the others' changes within a couple of seconds (MultiplayerSettings.livePollSeconds).
    /// When the host starts, each device goes into the game.
    /// </summary>
    public class MpLobby : MonoBehaviour {
        [Tooltip("The game's name#code, for the others to join with.")]
        public TMP_Text joinCode;
        public string joinCodeFormat = "Join code: <b>{0}</b>";
        [Tooltip("The lobby's name: the host's to change until the game starts. Optional.")]
        public TMP_InputField lobbyName;
        [Tooltip("How long a player has for a turn (MultiplayerSettings.turnLimits). Optional.")]
        public TMP_Dropdown turnLimit;
        [Tooltip("Who is in the lobby (players and spectators). Optional.")]
        public RectTransform members;
        public MpMemberRow memberTemplate;
        [Tooltip("What the lobby is waiting for, or what went wrong.")]
        public TMP_Text status;

        [Header("Words")]
        public string openLabel = "Open (to join)";
        public string localLabel = "Local (host)";
        public string openSeat = "open";
        public string takeSeat = "Take";
        public string giveUpSeat = "Give up";
        public string waitingForHost = "Waiting for the host to start.";
        public string readyToStart = "Every side is filled: ready to start.";
        public string waitingForSides = "Waiting for someone to take: {0}.";

        PreGameMenu menu;
        List<PlayerSetupRow> rows = new List<PlayerSetupRow>();
        List<AI.AIProfile> profiles = new List<AI.AIProfile>();
        List<Faction> factions = new List<Faction>();
        readonly List<MpMemberRow> memberRows = new List<MpMemberRow>();
        MpGame game;
        float nextPoll;
        bool polling, starting, leaving;

        const int ControlOpen = 0, ControlLocal = 1, ControlAi = 2;

        static bool Hosting { get { return MatchSetup.NetHosting; } }
        static string Code { get { return MatchSetup.NetCode; } }

        void Awake() { if (memberTemplate != null && memberTemplate.gameObject.scene.IsValid()) memberTemplate.gameObject.SetActive(false); }

        /// <summary>Who plays a side, as the lobby offers it: open, the host's device, the AI (default), the AI with each profile.</summary>
        public List<string> Controls(string defaultAi, string profileAi, List<AI.AIProfile> withProfiles) {
            List<string> c = new List<string> { openLabel, localLabel, defaultAi };
            c.AddRange(withProfiles.Select(x => string.Format(profileAi, x.profileName)));
            return c;
        }

        /// <summary>The pre-game screen is a lobby: take over its rows and rules, host the game if it is not hosted yet, else show it as it stands.</summary>
        public void Begin(PreGameMenu m, List<PlayerSetupRow> made, List<AI.AIProfile> withProfiles, List<Faction> withFactions) {
            menu = m; rows = made; profiles = withProfiles; factions = withFactions;
            foreach (PlayerSetupRow row in rows) {
                PlayerSetupRow r = row;
                if (r.control != null) r.control.onValueChanged.AddListener(i => OnControl(r, i));
                if (r.faction != null) r.faction.onValueChanged.AddListener(i => OnFaction(r, i));
                if (r.seatButton != null) r.seatButton.onClick.AddListener(() => Seat(r));
            }
            if (menu.fog != null) menu.fog.onValueChanged.AddListener(v => HostRules());
            if (menu.build != null) menu.build.onValueChanged.AddListener(v => HostRules());
            if (menu.specialUnits != null) menu.specialUnits.onValueChanged.AddListener(v => HostRules());
            if (menu.allies != null) menu.allies.onValueChanged.AddListener(v => HostRules());
            if (lobbyName != null) lobbyName.onEndEdit.AddListener(Rename);
            if (turnLimit != null) {
                turnLimit.ClearOptions();
                turnLimit.AddOptions(MultiplayerSettings.Get.turnLimits.Select(t => t.label).ToList());
                turnLimit.onValueChanged.AddListener(SetTurnLimit);
            }
            if (menu.startButton != null) menu.startButton.gameObject.SetActive(Hosting);
            if (Hosting && Code.Length == 0) HostNew();
            else nextPoll = 0f;
        }

        void Update() {
            if (menu == null || Code.Length == 0 || starting || leaving) return;
            if (Time.unscaledTime < nextPoll || polling) return;
            nextPoll = Time.unscaledTime + Mathf.Max(0.5f, MultiplayerSettings.Get.livePollSeconds);
            Refresh();
        }

        void Say(string s) { if (status != null) status.text = s; }

        // ------------------------------------------------------------------ hosting

        async void HostNew() {
            Say("Hosting...");
            string text;
            try { text = File.ReadAllText(MatchSetup.FilePath); }
            catch (System.Exception e) { Say("Could not read " + MatchSetup.FilePath + ": " + e.Message); return; }
            MpResult r = await Mp.Host(Path.GetFileNameWithoutExtension(MatchSetup.FilePath), text);
            if (!r.ok) { Say(r.error); return; }
            MatchSetup.NetCode = r.game.code;
            // The sides as the scenario has them: the AI's stay the AI's, the first human side is the host's, the
            // other human sides are open for others to take.
            List<MpSlot> slots = new List<MpSlot>();
            bool hostSeated = false;
            foreach (MatchSetup.Side s in MatchSetup.Sides) {
                MpSlot slot = new MpSlot { side = s.id, sideName = s.name, faction = s.faction ?? "", factionLocked = s.factionLocked, team = s.team, profile = s.profile ?? "" };
                if (s.ai) slot.kind = MpSlotKind.AI;
                else if (!hostSeated) { slot.kind = MpSlotKind.Player; slot.player = Mp.Me; slot.playerName = Mp.PlayerName; hostSeated = true; }
                slots.Add(slot);
            }
            MpResult set = await Mp.SetSides(Code, slots);
            if (!set.ok) { Say(set.error); return; }
            await Mp.HostChange(Code, g => { g.fog = MatchSetup.Fog; g.purchases = MatchSetup.Purchases; g.specialUnits = MatchSetup.SpecialUnits; g.allies = MatchSetup.Allies; });
            nextPoll = 0f;
        }

        void HostRules() {
            if (!Hosting || game == null) return;
            bool fog = menu.fog != null && menu.fog.isOn, buy = menu.build == null || menu.build.isOn;
            bool special = menu.specialUnits == null || menu.specialUnits.isOn, allies = menu.allies == null || menu.allies.isOn;
            Do(Mp.HostChange(Code, g => { g.fog = fog; g.purchases = buy; g.specialUnits = special; g.allies = allies; }));
        }

        void Rename(string name) {
            if (!Hosting || game == null) return;
            name = (name ?? "").Trim().Replace("#", "");
            if (name.Length == 0) { if (lobbyName != null) lobbyName.text = game.name; return; }
            Do(Mp.HostChange(Code, g => g.name = name));
        }

        void SetTurnLimit(int i) {
            if (!Hosting || game == null) return;
            List<MultiplayerSettings.TurnLimit> limits = MultiplayerSettings.Get.turnLimits;
            int seconds = i >= 0 && i < limits.Count ? limits[i].seconds : 0;
            Do(Mp.HostChange(Code, g => g.turnSeconds = seconds));
        }

        void OnControl(PlayerSetupRow row, int i) {
            if (!Hosting || game == null || row.Side == null) return;
            MpSlotKind kind = i == ControlOpen ? MpSlotKind.Open : i == ControlLocal ? MpSlotKind.Player : MpSlotKind.AI;
            string profile = i > ControlAi && i - ControlAi - 1 < profiles.Count ? profiles[i - ControlAi - 1].profileName : "";
            Do(Mp.HostSetSlot(Code, row.Side.id, kind, profile));
        }

        void OnFaction(PlayerSetupRow row, int i) {
            if (game == null || row.Side == null) return;
            Faction f = i > 0 && i <= factions.Count ? factions[i - 1] : null;
            Do(Mp.SetFaction(Code, row.Side.id, f != null ? f.name : ""));
        }

        void Seat(PlayerSetupRow row) {
            if (game == null || row.Side == null) return;
            MpSlot s = game.Slot(row.Side.id);
            if (s == null) return;
            if (s.kind == MpSlotKind.Open) Do(Mp.Claim(Code, s.side));
            else if (s.kind == MpSlotKind.Player && s.player == Mp.Me) Do(Mp.Release(Code, s.side));
        }

        void Kick(string player) { if (Hosting) Do(Mp.Kick(Code, player)); }

        async void Do(Task<MpResult> change) {
            MpResult r = await change;
            if (r.game != null) Apply(r.game);
            if (!r.ok) Say(r.error);
        }

        // ------------------------------------------------------------------ the lobby as it stands

        async void Refresh() {
            polling = true;
            try {
                MpGame g = await Mp.Load(Code);
                if (this == null || starting || leaving) return;
                if (g == null) { Say(Mp.LastError.Length > 0 ? Mp.LastError : "The lobby is gone."); return; }
                if (g.Member(Mp.Me) == null) { Say(g.banned.Contains(Mp.Me) ? "The host removed you from this game." : "You are no longer in this lobby."); return; }
                if (g.Finished) { Say("The host closed the lobby."); return; }
                if (g.state == MpState.Playing) {
                    // The host started it (here or on another device of theirs): into the game.
                    starting = true;
                    MatchSetup.PlayNetworked(Code);
                    return;
                }
                Apply(g);
            } finally { polling = false; }
        }

        int ControlOf(MpSlot s) {
            if (s.kind == MpSlotKind.Open) return ControlOpen;
            if (s.kind == MpSlotKind.Player) return ControlLocal;
            int p = profiles.FindIndex(x => x.profileName == s.profile);
            return p >= 0 ? ControlAi + 1 + p : ControlAi;
        }

        void Apply(MpGame g) {
            game = g;
            bool host = g.host == Mp.Me;
            bool player = g.Member(Mp.Me) != null && !g.Member(Mp.Me).spectator || g.IsPlayer(Mp.Me);
            if (joinCode != null) joinCode.text = string.Format(joinCodeFormat, g.Title);
            else if (menu.details != null) menu.details.text = string.Format(joinCodeFormat, g.Title);   // (a pre-game scene not updated for lobbies)
            if (menu.title != null) menu.title.text = g.name;
            if (lobbyName != null) {
                if (!lobbyName.isFocused) lobbyName.text = g.name;
                lobbyName.interactable = host;
            }
            if (turnLimit != null) {
                int i = MultiplayerSettings.Get.turnLimits.FindIndex(t => t.seconds == g.turnSeconds);
                turnLimit.SetValueWithoutNotify(Mathf.Max(0, i));
                turnLimit.interactable = host;
            }
            if (menu.fog != null) { menu.fog.SetIsOnWithoutNotify(g.fog); menu.fog.interactable = host && !MatchSetup.FogFixed; }
            if (menu.build != null) { menu.build.SetIsOnWithoutNotify(g.purchases); menu.build.interactable = host && !MatchSetup.PurchasesFixed; }
            if (menu.specialUnits != null) { menu.specialUnits.SetIsOnWithoutNotify(g.specialUnits); menu.specialUnits.interactable = host && !MatchSetup.SpecialUnitsFixed; }
            if (menu.allies != null) { menu.allies.SetIsOnWithoutNotify(g.allies); menu.allies.interactable = host && !MatchSetup.AlliesFixed; }
            List<string> open = new List<string>();
            foreach (PlayerSetupRow row in rows) {
                if (row.Side == null) continue;
                MpSlot s = g.Slot(row.Side.id);
                if (s == null) continue;
                if (s.kind == MpSlotKind.Open) open.Add(s.sideName);
                if (row.control != null) { row.control.SetValueWithoutNotify(ControlOf(s)); row.control.interactable = host; }
                bool mine = s.kind == MpSlotKind.Player && s.player == Mp.Me;
                if (row.faction != null) {
                    row.faction.SetValueWithoutNotify(factions.FindIndex(f => f.name == s.faction) + 1);
                    row.faction.interactable = !s.factionLocked && (host || mine);
                }
                if (row.playerLabel != null)
                    row.playerLabel.text = s.kind == MpSlotKind.Player ? s.playerName + (s.player == g.host ? " (host)" : "") : s.kind == MpSlotKind.Open ? openSeat : "";
                if (row.seatButton != null) {
                    bool take = !host && player && s.kind == MpSlotKind.Open, leave = !host && mine;
                    row.seatButton.gameObject.SetActive(take || leave);
                    if (row.seatLabel != null) row.seatLabel.text = take ? takeSeat : giveUpSeat;
                }
            }
            DrawMembers(g, host);
            if (menu.startButton != null) { menu.startButton.gameObject.SetActive(host); menu.startButton.interactable = host && open.Count == 0; }
            Say(open.Count > 0 ? string.Format(waitingForSides, string.Join(", ", open.ToArray())) : host ? readyToStart : waitingForHost);
        }

        void DrawMembers(MpGame g, bool host) {
            if (members == null || memberTemplate == null) return;
            foreach (MpMemberRow r in memberRows) if (r != null) Destroy(r.gameObject);
            memberRows.Clear();
            foreach (MpMember m in g.members) {
                MpMemberRow row = Instantiate(memberTemplate, members);
                row.gameObject.SetActive(true);
                string what = m.id == g.host ? " (host)" : m.spectator && !g.IsPlayer(m.id) ? " (watching)" : "";
                string id = m.id;
                row.Show(m.name + what + (m.id == Mp.Me ? " - you" : ""), host && m.id != g.host, () => Kick(id));
                memberRows.Add(row);
            }
        }

        // ------------------------------------------------------------------ starting and leaving

        /// <summary>The host starts the game: the scenario loaded with the lobby's sides and rules, sent to the store as the board it is played on from.</summary>
        public async void StartPressed() {
            if (!Hosting || game == null || starting) return;
            if (game.slots.Exists(s => s.kind == MpSlotKind.Open)) { Say("Every side needs a player or the AI before the game starts."); return; }
            starting = true;
            foreach (MatchSetup.Side side in MatchSetup.Sides) {
                MpSlot s = game.Slot(side.id);
                if (s == null) continue;
                side.ai = s.kind == MpSlotKind.AI;
                side.profile = s.profile ?? "";
                if (!side.factionLocked) side.faction = s.faction ?? "";
            }
            MatchSetup.Fog = game.fog; MatchSetup.Purchases = game.purchases; MatchSetup.SpecialUnits = game.specialUnits; MatchSetup.Allies = game.allies;
            if (!MatchSetup.StartGame()) { starting = false; Say("Could not load " + MatchSetup.FilePath); return; }
            Say("Starting...");
            MpResult r = await MpSession.BeginAsHost(Code);
            if (!r.ok) { starting = false; Say(r.error); return; }
            GameSession.Frame();
            if (GameSession.Instance != null) GameSession.Instance.NetworkedStarted();
        }

        /// <summary>Back: out of the lobby. A player's side is open again; the host closes the lobby for everyone.</summary>
        public async void Back() {
            if (leaving) return;
            leaving = true;
            if (Code.Length > 0) await Mp.Leave(Code);
            MatchSetup.ToMainMenu();
        }
    }
}
