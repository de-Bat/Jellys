using System.Collections.Concurrent;
using Jellyfin.Plugin.Recap.Llm;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.Recap.Services;

public record RecapResult(string Text, string Language, string Model, bool Cached);

public class RecapService
{
    private const long BucketTicks = TimeSpan.TicksPerMinute;

    private readonly TranscriptService _transcripts;
    private readonly ILlmProviderFactory _providers;
    private readonly ConcurrentDictionary<string, string> _cache = new();

    public RecapService(TranscriptService transcripts, ILlmProviderFactory providers)
    {
        _transcripts = transcripts;
        _providers = providers;
    }

    /// <summary>Returns null when the item has no usable subtitles or nothing has been watched yet.</summary>
    public async Task<RecapResult?> GetRecapAsync(BaseItem item, long positionTicks, string? language, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration ?? throw new InvalidOperationException("Plugin not initialised");
        var lang = string.IsNullOrWhiteSpace(language) ? config.DefaultLanguage : language.Trim();
        var bucket = positionTicks / BucketTicks;
        var key = $"{item.Id:N}|{bucket}|{lang}|{config.Provider}|{config.Model}";

        if (_cache.TryGetValue(key, out var hit))
        {
            return new RecapResult(hit, lang, config.Model, true);
        }

        var transcript = await _transcripts.GetTranscriptAsync(item, bucket * BucketTicks, null, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return null;
        }

        if (transcript.Length > config.MaxTranscriptChars)
        {
            transcript = transcript[^config.MaxTranscriptChars..];
        }

        var series = item is MediaBrowser.Controller.Entities.TV.Episode ep ? ep.SeriesName : null;
        var header = $"Episode: {item.Name}" + (string.IsNullOrEmpty(series) ? string.Empty : $" ({series})");
        var system = config.SystemPrompt.Replace("{language}", lang, StringComparison.Ordinal);
        var user = $"{header}\n\nTranscript so far:\n{transcript}";

        var text = await _providers.Create(config).CompleteAsync(system, user, cancellationToken).ConfigureAwait(false);
        if (_cache.Count > 500)
        {
            _cache.Clear();
        }

        _cache[key] = text;
        return new RecapResult(text, lang, config.Model, false);
    }
}
