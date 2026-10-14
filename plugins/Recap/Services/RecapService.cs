using System.Collections.Concurrent;
using Jellyfin.Plugin.Recap.Configuration;
using Jellyfin.Plugin.Recap.Llm;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.Recap.Services;

public record RecapResult(string Text, string Language, string Model, bool Cached);

/// <summary>
/// Recaps are built from cumulative checkpoint summaries (every N minutes of runtime) stored on disk,
/// so resuming is usually a cache hit. Missing checkpoints are filled in the background.
/// </summary>
public class RecapService
{
    private readonly TranscriptService _transcripts;
    private readonly ILlmProviderFactory _providers;
    private readonly RecapStore _store;
    private readonly RecapQueue _queue;
    private readonly ConcurrentDictionary<string, string> _oneShot = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public RecapService(TranscriptService transcripts, ILlmProviderFactory providers, RecapStore store, RecapQueue queue)
    {
        _transcripts = transcripts;
        _providers = providers;
        _store = store;
        _queue = queue;
    }

    public static string ResolveLanguage(string? language) =>
        string.IsNullOrWhiteSpace(language)
            ? (Plugin.Instance?.Configuration.DefaultLanguage ?? "English")
            : language.Trim();

    /// <summary>Returns null when the item has no usable subtitles or nothing has been watched yet.</summary>
    public async Task<RecapResult?> GetRecapAsync(BaseItem item, long positionTicks, string? language, CancellationToken cancellationToken)
    {
        var config = Config();
        var lang = ResolveLanguage(language);
        var cp = CheckpointTicks(config);
        var k = PositionToCheckpoint(item, positionTicks, cp);

        if (k >= 1)
        {
            _queue.Enqueue(item.Id, lang, k + Math.Max(0, config.LookAheadCheckpoints));

            var stored = _store.Load(item.Id, lang, config);
            var best = stored.Summaries.Keys.Where(i => i <= k).DefaultIfEmpty(0).Max();
            if (best == k)
            {
                return new RecapResult(stored.Summaries[k], lang, config.Model, true);
            }

            if (best >= 1 && k - best == 1)
            {
                // One checkpoint behind: cheap incremental step, do it now.
                var done = await EnsureAsync(item, lang, k, cancellationToken).ConfigureAwait(false);
                if (done?.Summaries.TryGetValue(k, out var text) == true)
                {
                    return new RecapResult(text, lang, config.Model, false);
                }
            }
            else if (best >= 1)
            {
                // Several behind: serve the latest known summary now, background job catches up.
                return new RecapResult(stored.Summaries[best], lang, config.Model, true);
            }
        }

        return await OneShotAsync(item, positionTicks, lang, config, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Builds and stores cumulative summaries for checkpoints 1..<paramref name="upTo"/>. Returns the store, or null without subtitles.</summary>
    public async Task<StoredRecap?> EnsureAsync(BaseItem item, string language, int upTo, CancellationToken cancellationToken)
    {
        var config = Config();
        var cp = CheckpointTicks(config);
        var maxIndex = item.RunTimeTicks is > 0 ? (int)(item.RunTimeTicks.Value / cp) : upTo;
        upTo = Math.Min(upTo, maxIndex);

        var gate = _locks.GetOrAdd(item.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stored = _store.Load(item.Id, language, config);
            var last = stored.Summaries.Keys.DefaultIfEmpty(0).Max();
            if (last >= upTo)
            {
                return stored;
            }

            var cues = await _transcripts.GetCuesAsync(item, null, cancellationToken).ConfigureAwait(false);
            if (cues is null || cues.Count == 0)
            {
                return null;
            }

            var llm = _providers.Create(config);
            var system = BuildSystem(config, language);
            for (var i = last + 1; i <= upTo; i++)
            {
                var chunk = Truncate(TranscriptService.Slice(cues, (i - 1) * cp, i * cp), config.MaxTranscriptChars);
                var previous = i > 1 ? stored.Summaries[i - 1] : string.Empty;
                if (string.IsNullOrWhiteSpace(chunk))
                {
                    stored.Summaries[i] = previous;
                }
                else
                {
                    var user = $"{Header(item)}\n\nSummary so far (up to minute {(i - 1) * config.CheckpointMinutes}):\n"
                        + (previous.Length == 0 ? "(nothing yet)" : previous)
                        + $"\n\nNew transcript (minutes {(i - 1) * config.CheckpointMinutes}-{i * config.CheckpointMinutes}):\n{chunk}"
                        + "\n\nWrite the updated summary of everything up to minute "
                        + (i * config.CheckpointMinutes).ToString(System.Globalization.CultureInfo.InvariantCulture) + ".";
                    stored.Summaries[i] = await llm.CompleteAsync(system, user, cancellationToken).ConfigureAwait(false);
                }

                _store.Save(item.Id, language, config, stored);
            }

            return stored;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<RecapResult?> OneShotAsync(BaseItem item, long positionTicks, string lang, PluginConfiguration config, CancellationToken cancellationToken)
    {
        var bucket = positionTicks / TimeSpan.TicksPerMinute;
        var key = $"{item.Id:N}|{bucket}|{lang}|{config.Provider}|{config.Model}";
        if (_oneShot.TryGetValue(key, out var hit))
        {
            return new RecapResult(hit, lang, config.Model, true);
        }

        var cues = await _transcripts.GetCuesAsync(item, null, cancellationToken).ConfigureAwait(false);
        if (cues is null)
        {
            return null;
        }

        var transcript = TranscriptService.Slice(cues, 0, bucket * TimeSpan.TicksPerMinute + 1);
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return null;
        }

        if (transcript.Length > config.MaxTranscriptChars)
        {
            transcript = transcript[^config.MaxTranscriptChars..];
        }

        var user = $"{Header(item)}\n\nTranscript so far:\n{transcript}";
        var text = await _providers.Create(config).CompleteAsync(BuildSystem(config, lang), user, cancellationToken).ConfigureAwait(false);
        if (_oneShot.Count > 200)
        {
            _oneShot.Clear();
        }

        _oneShot[key] = text;
        return new RecapResult(text, lang, config.Model, false);
    }

    private static PluginConfiguration Config() =>
        Plugin.Instance?.Configuration ?? throw new InvalidOperationException("Plugin not initialised");

    public static long CheckpointTicks(PluginConfiguration config) =>
        TimeSpan.FromMinutes(Math.Max(1, config.CheckpointMinutes)).Ticks;

    public static int PositionToCheckpoint(BaseItem item, long positionTicks, long cp)
    {
        var k = (int)(positionTicks / cp);
        if (item.RunTimeTicks is > 0)
        {
            k = Math.Min(k, (int)(item.RunTimeTicks.Value / cp));
        }

        return Math.Max(0, k);
    }

    private static string BuildSystem(PluginConfiguration config, string language) =>
        config.SystemPrompt.Replace("{language}", language, StringComparison.Ordinal);

    private static string Header(BaseItem item)
    {
        var series = item is Episode ep ? ep.SeriesName : null;
        return $"Episode: {item.Name}" + (string.IsNullOrEmpty(series) ? string.Empty : $" ({series})");
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[^max..];
}
