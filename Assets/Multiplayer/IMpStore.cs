using System.Collections.Generic;
using System.Threading.Tasks;

namespace HexTactica.Multiplayer {

    /// <summary>A value read from the store, with the lock that a write must name to replace it.</summary>
    public class MpValue {
        public string value;
        public string writeLock;
    }

    /// <summary>What a write came to.</summary>
    public class MpWritten {
        /// <summary>Written: <see cref="writeLock"/> is the value's new lock.</summary>
        public bool ok;
        /// <summary>Not written because the value was not as the writer last read it (someone else wrote first): read again.</summary>
        public bool conflict;
        public string writeLock;
        /// <summary>Not written for another reason (no connection, not signed in): what went wrong.</summary>
        public string error;
    }

    /// <summary>
    /// Where networked games are kept: all the game asks of a backend, so one can be replaced by another (the Unity
    /// Gaming Services one, MpUgsStore; a shared folder, MpFolderStore; one in memory for the tests). No game logic
    /// lives behind it: it stores text under keys, per game, and refuses a write when the value changed since the
    /// writer read it. Everything else (lobbies, turns, clocks) is worked out on the devices (Mp, MpSession).
    ///
    /// Calls may finish at once or later; in Unity their continuations run on the main thread.
    /// </summary>
    public interface IMpStore {
        /// <summary>This device's player: an id that is the same every time ("" before SignIn).</summary>
        string PlayerId { get; }
        /// <summary>Make ready (sign in). Null when done, else what went wrong.</summary>
        Task<string> SignIn();
        /// <summary>The values under <paramref name="keys"/> of game <paramref name="game"/>: a key with no value is left out. Null on failure.</summary>
        Task<Dictionary<string, MpValue>> Read(string game, IList<string> keys);
        /// <summary>
        /// Write a value. <paramref name="writeLock"/> is the lock it was read with: the write is refused (conflict) if
        /// the value has changed since. Null: the key must not have a value yet.
        /// </summary>
        Task<MpWritten> Write(string game, string key, string value, string writeLock);
        /// <summary>The player's own record (the list of their games), "" none yet. Null on failure.</summary>
        Task<string> ReadMine();
        Task<bool> WriteMine(string value);
    }
}
