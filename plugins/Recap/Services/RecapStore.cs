using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.Recap.Configuration;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.Recap.Services;

/// <summary>Cumulative summaries per checkpoint for one item/language/model/prompt.</summary>
public class StoredRecap
{
    /// <summary>Checkpoint index (1-based, minutes = index * CheckpointMinutes) to summary of everything up to it.</summary>
    public Dictionary<int, string> Summaries { get; set; } = new();
}

/// <summary>Disk cache of checkpoint summaries, one JSON file per item/language/model/prompt/checkpoint size.</summary>
public class RecapStore
{
    private readonly string _dir;

    public RecapStore(IApplicationPaths paths)
    {
        _dir = Path.Combine(paths.DataPath, "recap");
    }

    public StoredRecap Load(Guid itemId, string language, PluginConfiguration config)
    {
        try
        {
            var path = PathFor(itemId, language, config);
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<StoredRecap>(File.ReadAllText(path)) ?? new StoredRecap();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable cache: start over.
        }

        return new StoredRecap();
    }

    public void Save(Guid itemId, string language, PluginConfiguration config, StoredRecap recap)
    {
        Directory.CreateDirectory(_dir);
        var path = PathFor(itemId, language, config);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(recap));
        File.Move(tmp, path, overwrite: true);
    }

    private string PathFor(Guid itemId, string language, PluginConfiguration config)
    {
        var key = $"{language.ToLowerInvariant()}|{config.Provider}|{config.Model}|{config.CheckpointMinutes}|{config.SystemPrompt}";
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key)))[..12];
        return Path.Combine(_dir, $"{itemId:N}_{hash}.json");
    }
}
