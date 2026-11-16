using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudCode.Shared;
using Unity.Services.CloudSave.Model;

namespace HexTacticaMp;

/// <summary>The module's services: the client for Cloud Save.</summary>
public class ModuleConfig : ICloudCodeSetup {
    public void Setup(ICloudCodeConfig config) {
        config.Dependencies.AddSingleton(GameApiClient.Create());
    }
}

/// <summary>What Write did (read by the game: MpUgsStore).</summary>
public class WriteResult {
    public bool ok { get; set; }
    /// <summary>Not written: the value is not as the writer last read it (someone else wrote first), or it exists and the writer said it should not.</summary>
    public bool conflict { get; set; }
    /// <summary>The value's new write lock.</summary>
    public string writeLock { get; set; } = "";
    public string error { get; set; } = "";
}

/// <summary>
/// Stores a value of a networked game for a player: Cloud Save lets players read game data (custom items) but only a
/// server write it. No game rules here: the devices work everything out, and the write lock keeps their writes in
/// order (a write naming a lock that has moved on is refused, and the device reads again).
/// </summary>
public class Store {
    readonly ILogger<Store> logger;
    public Store(ILogger<Store> logger) { this.logger = logger; }

    /// <param name="item">The custom item: "mp-" and the game's id.</param>
    /// <param name="writeLock">The lock the value was read with; null or empty: it must not exist yet.</param>
    [CloudCodeFunction("Write")]
    public async Task<WriteResult> Write(IExecutionContext context, IGameApiClient api, string item, string key, string value, string writeLock) {
        if (string.IsNullOrEmpty(item) || !item.StartsWith("mp-") || item.Length > 50) return new WriteResult { error = "Not a HexTactica game." };
        if (string.IsNullOrEmpty(key) || key.Length > 255) return new WriteResult { error = "No such key." };
        try {
            if (string.IsNullOrEmpty(writeLock)) {
                // Cloud Save writes over a value when no lock is given: "must not exist yet" is checked here.
                var existing = await api.CloudSaveData.GetCustomItemsAsync(context, context.ServiceToken, context.ProjectId, item, new List<string> { key });
                if (existing.Data.Results.Count > 0) return new WriteResult { conflict = true };
            }
            var written = await api.CloudSaveData.SetCustomItemAsync(context, context.ServiceToken, context.ProjectId, item,
                new SetItemBody(key, value ?? "", string.IsNullOrEmpty(writeLock) ? null : writeLock));
            return new WriteResult { ok = true, writeLock = written.Data.WriteLock };
        } catch (ApiException e) when (e.Response != null && e.Response.StatusCode == HttpStatusCode.Conflict) {
            return new WriteResult { conflict = true };
        } catch (ApiException e) {
            logger.LogError("Write {Item}/{Key} by {Player} failed: {Message}", item, key, context.PlayerId, e.Message);
            return new WriteResult { error = e.Message };
        }
    }
}
