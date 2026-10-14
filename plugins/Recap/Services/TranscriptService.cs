using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Recap.Services;

/// <summary>One subtitle cue: when it starts and its plain text.</summary>
public record Cue(long StartTicks, string Text);

/// <summary>Reads an item's text subtitles as timed plain-text cues.</summary>
public partial class TranscriptService
{
    private readonly ISubtitleEncoder _subtitleEncoder;

    public TranscriptService(ISubtitleEncoder subtitleEncoder)
    {
        _subtitleEncoder = subtitleEncoder;
    }

    /// <summary>Returns all cues, or null when the item has no text subtitles.</summary>
    public async Task<IReadOnlyList<Cue>?> GetCuesAsync(BaseItem item, string? preferredLanguage, CancellationToken cancellationToken)
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
        return ParseSrt(text);
    }

    /// <summary>Joins the cues starting in [fromTicks, toTicks) into plain text, one cue per line.</summary>
    public static string Slice(IReadOnlyList<Cue> cues, long fromTicks, long toTicks)
    {
        var sb = new StringBuilder();
        foreach (var cue in cues)
        {
            if (cue.StartTicks >= toTicks)
            {
                break;
            }

            if (cue.StartTicks >= fromTicks)
            {
                sb.AppendLine(cue.Text);
            }
        }

        return sb.ToString();
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

    /// <summary>Strips SRT numbering, timing and markup into cues ordered by start time.</summary>
    internal static List<Cue> ParseSrt(string srt)
    {
        var cues = new List<Cue>();
        var blocks = BlockSplit().Split(srt.Replace("\r\n", "\n").Trim());
        foreach (var block in blocks)
        {
            var lines = block.Split('\n');
            var timeIdx = Array.FindIndex(lines, l => l.Contains("-->", StringComparison.Ordinal));
            if (timeIdx < 0 || !TryParseStart(lines[timeIdx], out var startTicks))
            {
                continue;
            }

            var cue = Markup().Replace(string.Join(' ', lines.Skip(timeIdx + 1)), string.Empty).Trim();
            if (cue.Length > 0)
            {
                cues.Add(new Cue(startTicks, cue));
            }
        }

        cues.Sort((a, b) => a.StartTicks.CompareTo(b.StartTicks));
        return cues;
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
