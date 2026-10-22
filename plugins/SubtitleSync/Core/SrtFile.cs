using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.SubtitleSync.Core;

/// <summary>Parse and write SubRip (.srt) subtitles.</summary>
public static class SrtFile
{
    private static readonly Regex TimingLine = new(
        @"^\s*(\d+):(\d{2}):(\d{2})[,.](\d{1,3})\s*-->\s*(\d+):(\d{2}):(\d{2})[,.](\d{1,3})",
        RegexOptions.Compiled);

    public static List<SubtitleCue> Parse(string content)
    {
        var cues = new List<SubtitleCue>();
        var lines = content.TrimStart('﻿').Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var i = 0;
        while (i < lines.Length)
        {
            var m = TimingLine.Match(lines[i]);
            if (!m.Success)
            {
                i++;
                continue;
            }

            i++;
            var text = new StringBuilder();
            while (i < lines.Length && lines[i].Trim().Length > 0)
            {
                if (text.Length > 0)
                {
                    text.Append('\n');
                }

                text.Append(lines[i]);
                i++;
            }

            cues.Add(new SubtitleCue(cues.Count + 1, ToSpan(m, 1), ToSpan(m, 5), text.ToString()));
        }

        return cues;
    }

    public static string Write(IEnumerable<SubtitleCue> cues)
    {
        var sb = new StringBuilder();
        var n = 1;
        foreach (var cue in cues)
        {
            sb.Append(n++).Append('\n')
              .Append(Format(cue.Start)).Append(" --> ").Append(Format(cue.End)).Append('\n')
              .Append(cue.Text).Append("\n\n");
        }

        return sb.ToString();
    }

    public static string Format(TimeSpan t)
    {
        if (t < TimeSpan.Zero)
        {
            t = TimeSpan.Zero;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00},{t.Milliseconds:000}");
    }

    private static TimeSpan ToSpan(Match m, int g)
    {
        var ms = int.Parse(m.Groups[g + 3].Value.PadRight(3, '0'), CultureInfo.InvariantCulture);
        return new TimeSpan(
            0,
            int.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture),
            int.Parse(m.Groups[g + 1].Value, CultureInfo.InvariantCulture),
            int.Parse(m.Groups[g + 2].Value, CultureInfo.InvariantCulture),
            ms);
    }
}
