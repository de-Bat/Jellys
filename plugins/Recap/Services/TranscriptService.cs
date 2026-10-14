using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Recap.Services;

/// <summary>Builds a plain-text transcript of an item's subtitles up to a playback position.</summary>
public partial class TranscriptService
{
    private readonly ISubtitleEncoder _subtitleEncoder;

    public TranscriptService(ISubtitleEncoder subtitleEncoder)
    {
        _subtitleEncoder = subtitleEncoder;
    }

    /// <summary>Returns the transcript up to <paramref name="positionTicks"/>, or null when the item has no text subtitles.</summary>
    public async Task<string?> GetTranscriptAsync(BaseItem item, long positionTicks, string? preferredLanguage, CancellationToken cancellationToken)
    {
        var stream = PickSubtitleStream(item, preferredLanguage);
        if (stream is null)
        {
            return null;
        }

        await using var srt = await _subtitleEncoder
            .GetSubtitles(item, item.Id.ToString("N"), stream.Index, "srt", 0, 0, false, cancellationToken)
            .ConfigureAwait(false);
        using var reader = new StreamReader(srt, Encoding.UTF8);
        var text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        return SrtToText(text, positionTicks);
    }

    private static MediaStream? PickSubtitleStream(BaseItem item, string? preferredLanguage)
    {
        var candidates = item.GetMediaStreams()
            .Where(s => s.Type == MediaStreamType.Subtitle && s.IsTextSubtitleStream)
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates
            .OrderByDescending(s => !string.IsNullOrEmpty(preferredLanguage)
                && string.Equals(s.Language, preferredLanguage, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(s => s.IsDefault)
            .ThenBy(s => s.IsForced)
            .ThenBy(s => s.IsHearingImpaired)
            .First();
    }

    /// <summary>Strips SRT numbering, timing and markup; keeps only cues that start before the position.</summary>
    internal static string SrtToText(string srt, long positionTicks)
    {
        var sb = new StringBuilder();
        var blocks = BlockSplit().Split(srt.Replace("\r\n", "\n").Trim());
        foreach (var block in blocks)
        {
            var lines = block.Split('\n');
            var timeIdx = Array.FindIndex(lines, l => l.Contains("-->", StringComparison.Ordinal));
            if (timeIdx < 0 || !TryParseStart(lines[timeIdx], out var startTicks))
            {
                continue;
            }

            if (positionTicks > 0 && startTicks > positionTicks)
            {
                break;
            }

            var cue = string.Join(' ', lines.Skip(timeIdx + 1));
            cue = Markup().Replace(cue, string.Empty).Trim();
            if (cue.Length > 0)
            {
                sb.AppendLine(cue);
            }
        }

        return sb.ToString();
    }

    private static bool TryParseStart(string line, out long ticks)
    {
        ticks = 0;
        var start = line.Split("-->")[0].Trim().Replace(',', '.');
        if (!TimeSpan.TryParse(start, CultureInfo.InvariantCulture, out var ts))
        {
            return false;
        }

        ticks = ts.Ticks;
        return true;
    }

    [GeneratedRegex(@"\n\s*\n")]
    private static partial Regex BlockSplit();

    [GeneratedRegex(@"<[^>]+>|\{\\[^}]*\}")]
    private static partial Regex Markup();
}
