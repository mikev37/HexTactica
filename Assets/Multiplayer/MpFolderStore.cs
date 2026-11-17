using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace HexTactica.Multiplayer {

    /// <summary>
    /// Networked games kept as files in a folder: one folder per game, one file per key. For trying the game out with
    /// no cloud at all: two copies of the game on one computer pointed at the same folder, or a folder that is shared
    /// or synced between computers. The headless tests use it too. A value's lock is a counter kept in its first line.
    /// (Two devices writing the same key in the same instant over a slow synced folder can still both win: it is not
    /// for games that matter.)
    /// </summary>
    public class MpFolderStore : IMpStore, IMpLinking {
        readonly string root;
        string player;

        /// <param name="root">The shared folder.</param>
        /// <param name="player">This device's player id (kept by the caller: Mp keeps one in PlayerPrefs).</param>
        public MpFolderStore(string root, string player) { this.root = root; this.player = player; }

        public string PlayerId { get { return player; } }

        public override string ToString() { return "a shared folder (" + root + ")"; }

        public Task<string> SignIn() {
            try { Directory.CreateDirectory(root); return Task.FromResult<string>(null); }
            catch (Exception e) { return Task.FromResult("Could not open the folder " + root + ": " + e.Message); }
        }

        static string Safe(string s) {
            StringBuilder sb = new StringBuilder();
            foreach (char c in s ?? "") sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return sb.ToString();
        }

        string PathOf(string game, string key) { return Path.Combine(Path.Combine(root, "game-" + Safe(game)), Safe(key) + ".txt"); }

        static MpValue ReadFile(string path) {
            if (!File.Exists(path)) return null;
            string text = File.ReadAllText(path);
            int nl = text.IndexOf('\n');
            if (nl < 0) return null;
            return new MpValue { writeLock = text.Substring(0, nl), value = text.Substring(nl + 1) };
        }

        public Task<Dictionary<string, MpValue>> Read(string game, IList<string> keys) {
            try {
                Dictionary<string, MpValue> found = new Dictionary<string, MpValue>();
                foreach (string k in keys) { MpValue v = ReadFile(PathOf(game, k)); if (v != null) found[k] = v; }
                return Task.FromResult(found);
            } catch (Exception) { return Task.FromResult<Dictionary<string, MpValue>>(null); }
        }

        public Task<MpWritten> Write(string game, string key, string value, string writeLock) {
            try {
                string path = PathOf(game, key);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                MpValue now = ReadFile(path);
                if ((now == null) != (writeLock == null) || (now != null && now.writeLock != writeLock))
                    return Task.FromResult(new MpWritten { conflict = true });
                long n = 0;
                if (now != null) long.TryParse(now.writeLock, out n);
                string next = (n + 1).ToString();
                string temp = path + "." + Safe(player) + ".tmp";
                File.WriteAllText(temp, next + "\n" + value);
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
                return Task.FromResult(new MpWritten { ok = true, writeLock = next });
            } catch (Exception e) { return Task.FromResult(new MpWritten { error = e.Message }); }
        }

        string MinePath { get { return Path.Combine(root, "player-" + Safe(player) + ".txt"); } }

        public Task<string> ReadMine() {
            try { return Task.FromResult(File.Exists(MinePath) ? File.ReadAllText(MinePath) : ""); }
            catch (Exception) { return Task.FromResult<string>(null); }
        }

        public Task<bool> WriteMine(string value) {
            try { Directory.CreateDirectory(root); File.WriteAllText(MinePath, value); return Task.FromResult(true); }
            catch (Exception) { return Task.FromResult(false); }
        }

        // ------------------------------------------------------------------ linking devices: a file per code, the player's id written in it once confirmed

        /// <summary>This device's player is now <see cref="PlayerId"/>, after linking (the caller keeps it for next time).</summary>
        public event Action<string> PlayerChanged;
        string linkCode;
        public string LinkError { get; private set; }

        string LinkPath(string code) { return Path.Combine(root, "link-" + Safe((code ?? "").Trim().ToUpperInvariant()) + ".txt"); }

        public Task<string> StartLink() {
            try {
                Directory.CreateDirectory(root);
                linkCode = Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant();
                File.WriteAllText(LinkPath(linkCode), "");
                return Task.FromResult(linkCode);
            } catch (Exception e) { LinkError = e.Message; return Task.FromResult<string>(null); }
        }

        public Task<string> ConfirmLink(string code) {
            string path = LinkPath(code);
            if (!File.Exists(path)) return Task.FromResult("No device is waiting with the code " + code + ".");
            try { File.WriteAllText(path, player); return Task.FromResult<string>(null); }
            catch (Exception e) { return Task.FromResult(e.Message); }
        }

        public Task<string> FinishLink() {
            if (linkCode == null) return Task.FromResult("Make a code on this device first.");
            string path = LinkPath(linkCode), id = File.Exists(path) ? File.ReadAllText(path).Trim() : "";
            if (id.Length == 0) return Task.FromResult("Not confirmed yet: type the code on your other device.");
            try { File.Delete(path); } catch (Exception) { }
            linkCode = null;
            player = id;
            if (PlayerChanged != null) PlayerChanged(id);
            return Task.FromResult<string>(null);
        }
    }
}
