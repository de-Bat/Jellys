using Jellyfin.Plugin.SubtitleSync.Core;
using Xunit;

namespace Jellyfin.Plugin.SubtitleSync.Tests;

public class SyncTests
{
    private const string Sample = "1\r\n00:00:01,000 --> 00:00:02,500\r\nHello\r\nworld\r\n\r\n2\r\n00:01:00,250 --> 00:01:02,000\r\nBye\r\n";

    [Fact]
    public void ParseAndWrite_RoundTrips()
    {
        var cues = SrtFile.Parse(Sample);
        Assert.Equal(2, cues.Count);
        Assert.Equal("Hello\nworld", cues[0].Text);
        Assert.Equal(TimeSpan.FromMilliseconds(60250), cues[1].Start);
        Assert.Equal(cues.Select(c => c.Start), SrtFile.Parse(SrtFile.Write(cues)).Select(c => c.Start));
    }

    [Fact]
    public void Shift_MovesAndDropsNegative()
    {
        var cues = SrtFile.Parse(Sample);
        var shifted = SubtitleSyncer.Shift(cues, TimeSpan.FromSeconds(-2));
        Assert.Equal(2, shifted.Count);
        Assert.Equal(TimeSpan.Zero, shifted[0].Start);
        Assert.Equal(TimeSpan.FromMilliseconds(500), shifted[0].End);
        Assert.Equal(TimeSpan.FromMilliseconds(58250), shifted[1].Start);
        Assert.Single(SubtitleSyncer.Shift(cues, TimeSpan.FromSeconds(-3)));
    }

    [Fact]
    public void EstimateOffset_FindsShift()
    {
        var rng = new Random(1);
        var reference = Enumerable.Range(0, 100)
            .Select(i => new SubtitleCue(i, TimeSpan.FromMilliseconds(i * 3000 + rng.Next(1000)), TimeSpan.Zero, "x"))
            .ToList();
        var target = reference.Where((_, i) => i % 10 < 7)
            .Select(c => c with { Start = c.Start - TimeSpan.FromMilliseconds(4200 + rng.Next(-50, 50)) })
            .Where(c => c.Start >= TimeSpan.Zero)
            .ToList();

        var offset = SubtitleSyncer.EstimateOffset(reference, target, TimeSpan.FromSeconds(30));
        Assert.NotNull(offset);
        Assert.InRange(offset!.Value.TotalMilliseconds, 4100, 4300);
    }

    [Fact]
    public void EstimateOffset_NoMatch_ReturnsNull()
    {
        Assert.Null(SubtitleSyncer.EstimateOffset(new List<SubtitleCue>(), SrtFile.Parse(Sample), TimeSpan.FromSeconds(10)));
    }
}
