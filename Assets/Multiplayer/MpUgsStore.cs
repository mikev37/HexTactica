#if HEXTACTICA_UGS
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.CloudCode;
using Unity.Services.CloudSave;
using Unity.Services.Core;
using UnityEngine;

namespace HexTactica.Multiplayer {

    /// <summary>
    /// Networked games kept in Unity Gaming Services:
    /// - the player: Authentication, signed in anonymously the first time (the session is kept on the device);
    ///   Code-Link makes one player of two devices (IMpLinking);
    /// - a game: Cloud Save Game Data, one Custom Item per game ("mp-" + its id: the join code for its meta, the code
    ///   and epoch for its data). Players read custom items directly; only a server may write them, so writes go through
    ///   the Cloud Code module (CloudCode/HexTacticaMp: its Write function stores the value, refusing it if the
    ///   value's write lock has moved on);
    /// - the player's list of games: their own Player Data (key "mp-games").
    ///
    /// Compiled only with the scripting define HEXTACTICA_UGS, once the Authentication, Cloud Save and Cloud Code
    /// packages are installed (Assets/Multiplayer/README.md).
    /// </summary>
    public class MpUgsStore : IMpStore, IMpLinking {
        readonly string module;
        const string MineKey = "mp-games";

        public MpUgsStore(string module) { this.module = string.IsNullOrEmpty(module) ? "HexTacticaMp" : module; }

        public string PlayerId { get { return AuthenticationService.Instance.IsSignedIn ? AuthenticationService.Instance.PlayerId : ""; } }

        static string Item(string game) { return "mp-" + game; }

        public async Task<string> SignIn() {
            try {
                if (UnityServices.State != ServicesInitializationState.Initialized) await UnityServices.InitializeAsync();
                if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
                return null;
            } catch (Exception e) {
                Debug.LogWarning("Multiplayer: could not sign in to Unity Gaming Services: " + e);
                return "Could not sign in to Unity Gaming Services (" + e.Message + ").";
            }
        }

        public async Task<Dictionary<string, MpValue>> Read(string game, IList<string> keys) {
            Dictionary<string, MpValue> found = new Dictionary<string, MpValue>();
            try {
                var got = await CloudSaveService.Instance.Data.Custom.LoadAsync(Item(game), new HashSet<string>(keys));
                foreach (var kv in got) found[kv.Key] = new MpValue { value = kv.Value.Value.GetAs<string>(), writeLock = kv.Value.WriteLock };
                return found;
            } catch (CloudSaveException e) {
                // A game nobody has written to yet: nothing there.
                if (e.Reason.ToString().Contains("NotFound")) return found;
                Debug.LogWarning("Multiplayer: reading " + Item(game) + " failed: " + e);
                return null;
            } catch (Exception e) {
                Debug.LogWarning("Multiplayer: reading " + Item(game) + " failed: " + e);
                return null;
            }
        }

        /// <summary>What the module's Write function returns.</summary>
        class WriteResult { public bool ok; public bool conflict; public string writeLock; public string error; }

        public async Task<MpWritten> Write(string game, string key, string value, string writeLock) {
            try {
                WriteResult r = await CloudCodeService.Instance.CallModuleEndpointAsync<WriteResult>(module, "Write", new Dictionary<string, object> {
                    { "item", Item(game) }, { "key", key }, { "value", value }, { "writeLock", writeLock }
                });
                return new MpWritten { ok = r.ok, conflict = r.conflict, writeLock = r.writeLock, error = string.IsNullOrEmpty(r.error) ? null : r.error };
            } catch (Exception e) {
                Debug.LogWarning("Multiplayer: writing " + Item(game) + "/" + key + " failed: " + e);
                return new MpWritten { error = "Could not write to the game store (" + e.Message + ")." };
            }
        }

        public async Task<string> ReadMine() {
            try {
                var got = await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { MineKey });
                return got.TryGetValue(MineKey, out var item) ? item.Value.GetAs<string>() ?? "" : "";
            } catch (Exception e) {
                Debug.LogWarning("Multiplayer: reading the player's games failed: " + e);
                return null;
            }
        }

        public async Task<bool> WriteMine(string value) {
            try {
                await CloudSaveService.Instance.Data.Player.SaveAsync(new Dictionary<string, object> { { MineKey, value } });
                return true;
            } catch (Exception e) {
                Debug.LogWarning("Multiplayer: saving the player's games failed: " + e);
                return false;
            }
        }

        // ------------------------------------------------------------------ Code-Link (Project Settings ▸ Services ▸ Authentication: add the Code-Link provider)

        public string LinkError { get; private set; }

        public async Task<string> StartLink() {
            try {
                // The code signs this device in as someone else: its own anonymous player is left behind.
                if (AuthenticationService.Instance.IsSignedIn) AuthenticationService.Instance.SignOut(true);
                var info = await AuthenticationService.Instance.GenerateSignInCodeAsync(SystemInfo.deviceName);
                return info.SignInCode;
            } catch (Exception e) { LinkError = e.Message; return null; }
        }

        public async Task<string> ConfirmLink(string code) {
            try { await AuthenticationService.Instance.ConfirmCodeAsync(code.Trim()); return null; }
            catch (Exception e) { return "Could not confirm the code (" + e.Message + ")."; }
        }

        public async Task<string> FinishLink() {
            try {
                await AuthenticationService.Instance.SignInWithCodeAsync(true);
                return AuthenticationService.Instance.IsSignedIn ? null : "Not confirmed yet: type the code on your other device first.";
            } catch (Exception e) { return "Could not sign in with the code (" + e.Message + ")."; }
        }
    }
}
#endif
