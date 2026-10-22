namespace Jellyfin.Plugin.SubtitleSync.Core;

/// <summary>Offset shifting and automatic offset detection against a reference subtitle.</summary>
public static class SubtitleSyncer
{
    /// <summary>Shift all cues by <paramref name="offset"/>. Cues ending before zero are dropped.</summary>
    public static List<SubtitleCue> Shift(IEnumerable<SubtitleCue> cues, TimeSpan offset)
    {
        var result = new List<SubtitleCue>();
        foreach (var c in cues)
        {
            var end = c.End + offset;
            if (end <= TimeSpan.Zero)
            {
                continue;
            }

            var start = c.Start + offset;
            result.Add(c with { Start = start < TimeSpan.Zero ? TimeSpan.Zero : start, End = end });
        }

        return result;
    }

    /// <summary>
    /// Estimate the offset to add to <paramref name="target"/> so it lines up with <paramref name="reference"/>.
    /// Votes over all cue-start differences within +/- <paramref name="maxOffset"/>, bucketed by
    /// <paramref name="tolerance"/>; the most voted bucket wins. Returns null when there is no clear match.
    /// </summary>
    public static TimeSpan? EstimateOffset(
        IReadOnlyList<SubtitleCue> reference,
        IReadOnlyList<SubtitleCue> target,
        TimeSpan maxOffset,
        TimeSpan? tolerance = null)
    {
        var tol = (tolerance ?? TimeSpan.FromMilliseconds(250)).TotalMilliseconds;
        var max = maxOffset.TotalMilliseconds;
        if (reference.Count == 0 || target.Count == 0 || tol <= 0)
        {
            return null;
        }

        var refStarts = reference.Select(c => c.Start.TotalMilliseconds).OrderBy(x => x).ToArray();
        var votes = new Dictionary<long, List<double>>();
        foreach (var t in target)
        {
            var ts = t.Start.TotalMilliseconds;
            var lo = Array.BinarySearch(refStarts, ts - max);
            if (lo < 0)
            {
                lo = ~lo;
            }

            for (var k = lo; k < refStarts.Length && refStarts[k] <= ts + max; k++)
            {
                var d = refStarts[k] - ts;
                var bucket = (long)Math.Floor(d / tol);
                if (!votes.TryGetValue(bucket, out var list))
                {
                    votes[bucket] = list = new List<double>();
                }

                list.Add(d);
            }
        }

        if (votes.Count == 0)
        {
            return null;
        }

        // Score a bucket together with its neighbour so a peak straddling a boundary is not split.
        long bestBucket = 0;
        var bestScore = 0;
        foreach (var (b, list) in votes)
        {
            var score = list.Count + (votes.TryGetValue(b + 1, out var n) ? n.Count : 0);
            if (score > bestScore || (score == bestScore && Math.Abs(b) < Math.Abs(bestBucket)))
            {
                bestScore = score;
                bestBucket = b;
            }
        }

        // Require a meaningful share of target cues to agree.
        if (bestScore < Math.Max(3, target.Count / 10))
        {
            return null;
        }

        var diffs = votes[bestBucket]
            .Concat(votes.TryGetValue(bestBucket + 1, out var nb) ? nb : new List<double>())
            .OrderBy(x => x)
            .ToList();
        return TimeSpan.FromMilliseconds(Math.Round(diffs[diffs.Count / 2]));
    }
}
