using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HexTactica.Multiplayer {

    /// <summary>
    /// Runs for the whole time the game is open, on every screen (made once, kept across scenes): plays the networked
    /// game on the board, if one is open (MpSession.Tick), and looks at the player's games every so often
    /// (MultiplayerSettings.listPollSeconds) at launch and after. The first time it sees a game waiting on the player,
    /// it puts the your-turn notice up over whatever is on screen (the TurnNotice scene), unless that game is the
    /// one being played here (its own banner says so).
    ///
    /// Only while the app is running: a notice to a closed app (a push notification) is not done yet (see
    /// Multiplayer/README.md).
    /// </summary>
    public class MpWatcher : MonoBehaviour {

        public static MpWatcher Instance { get; private set; }

        float nextList;
        bool listing;
        /// <summary>The turns already announced (game, epoch, entry count), so each is announced once.</summary>
        readonly HashSet<string> announced = new HashSet<string>();
        /// <summary>Games waiting on the player, not announced yet (the notice shows one at a time).</summary>
        readonly Queue<MpSummary> toAnnounce = new Queue<MpSummary>();
        float noticeUntil;
        bool noticeUp;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install() {
            if (Instance != null) return;
            GameObject g = new GameObject("Multiplayer Watcher");
            DontDestroyOnLoad(g);
            g.AddComponent<MpWatcher>();
        }

        void Awake() { Instance = this; }

        void Update() {
            if (MpSession.Active) MpSession.Tick();
            if (Time.unscaledTime >= nextList && !listing) {
                nextList = Time.unscaledTime + Mathf.Max(2f, MultiplayerSettings.Get.listPollSeconds);
                List();
            }
            if (noticeUp && Time.unscaledTime >= noticeUntil) HideNotice();
            if (!noticeUp && toAnnounce.Count > 0) ShowNotice(toAnnounce.Dequeue());
        }

        /// <summary>Look at the player's games now (the main menu's list asks when it opens).</summary>
        public void ListNow() { nextList = 0f; }

        async void List() {
            listing = true;
            try {
                if (!await Mp.Refresh()) return;
                foreach (MpSummary s in Mp.Games) {
                    if (!s.myTurn) continue;
                    string key = s.code + "/" + s.epoch + "/" + s.entries;
                    if (announced.Contains(key)) continue;
                    announced.Add(key);
                    // The game on screen says so itself.
                    if (MpSession.Active && MpSession.Game != null && MpSession.Game.code == s.code) continue;
                    toAnnounce.Enqueue(s);
                }
            } finally { listing = false; }
        }

        void ShowNotice(MpSummary s) {
            MpNotice.Pending = s;
            string scene = MultiplayerSettings.Get.noticeScene;
            if (!Application.CanStreamedLevelBeLoaded(scene)) {
                Debug.Log("Your turn in " + s.title + " (no " + scene + " scene to show it in: HexTactica ▸ Front End ▸ Update Scenes makes it).");
                return;
            }
            if (!SceneManager.GetSceneByName(scene).isLoaded) SceneManager.LoadScene(scene, LoadSceneMode.Additive);
            else if (MpNotice.Instance != null) MpNotice.Instance.Show(s);
            noticeUp = true;
            noticeUntil = Time.unscaledTime + Mathf.Max(1f, MultiplayerSettings.Get.noticeSeconds);
        }

        /// <summary>Take the notice down (it timed out, or the player dismissed or followed it).</summary>
        public void HideNotice() {
            noticeUp = false;
            Scene sc = SceneManager.GetSceneByName(MultiplayerSettings.Get.noticeScene);
            if (sc.IsValid() && sc.isLoaded) SceneManager.UnloadSceneAsync(sc);
        }
    }
}
