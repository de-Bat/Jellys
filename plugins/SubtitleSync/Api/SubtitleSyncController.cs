using System.Net.Mime;
using System.Text;
using Jellyfin.Plugin.SubtitleSync.Core;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.SubtitleSync.Api;

/// <summary>Sync external .srt subtitles of a library item.</summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("SubtitleSync")]
[Produces(MediaTypeNames.Application.Json)]
public class SubtitleSyncController : ControllerBase
{
    private readonly IMediaSourceManager _mediaSources;

    public SubtitleSyncController(IMediaSourceManager mediaSources)
    {
        _mediaSources = mediaSources;
    }

    /// <summary>Shift an external subtitle by a fixed offset (negative = earlier).</summary>
    [HttpPost("{itemId}/Offset")]
    public ActionResult<SyncResult> Offset(
        [FromRoute] Guid itemId,
        [FromQuery] int streamIndex,
        [FromQuery] int offsetMs)
    {
        var path = ResolveSrt(itemId, streamIndex, out var error);
        if (path is null)
        {
            return error!;
        }

        Apply(path, TimeSpan.FromMilliseconds(offsetMs));
        return new SyncResult(offsetMs);
    }

    /// <summary>Auto-sync an external subtitle against another subtitle stream of the same item.</summary>
    [HttpPost("{itemId}/Auto")]
    public ActionResult<SyncResult> Auto(
        [FromRoute] Guid itemId,
        [FromQuery] int streamIndex,
        [FromQuery] int referenceIndex,
        [FromQuery] int maxOffsetSeconds = 120)
    {
        var path = ResolveSrt(itemId, streamIndex, out var error);
        if (path is null)
        {
            return error!;
        }

        var refPath = ResolveSrt(itemId, referenceIndex, out error);
        if (refPath is null)
        {
            return error!;
        }

        var reference = SrtFile.Parse(System.IO.File.ReadAllText(refPath));
        var target = SrtFile.Parse(System.IO.File.ReadAllText(path));
        var offset = SubtitleSyncer.EstimateOffset(reference, target, TimeSpan.FromSeconds(maxOffsetSeconds));
        if (offset is null)
        {
            return UnprocessableEntity("Could not find a confident offset.");
        }

        Apply(path, offset.Value);
        return new SyncResult((int)offset.Value.TotalMilliseconds);
    }

    private static void Apply(string path, TimeSpan offset)
    {
        var original = System.IO.File.ReadAllText(path);
        var backup = path + ".bak";
        if (!System.IO.File.Exists(backup))
        {
            System.IO.File.WriteAllText(backup, original);
        }

        var shifted = SubtitleSyncer.Shift(SrtFile.Parse(original), offset);
        System.IO.File.WriteAllText(path, SrtFile.Write(shifted), new UTF8Encoding(false));
    }

    private string? ResolveSrt(Guid itemId, int streamIndex, out ActionResult? error)
    {
        var stream = _mediaSources.GetMediaStreams(itemId)
            .FirstOrDefault(s => s.Type == MediaStreamType.Subtitle && s.Index == streamIndex);
        if (stream is null)
        {
            error = NotFound("Subtitle stream not found.");
            return null;
        }

        if (!stream.IsExternal || string.IsNullOrEmpty(stream.Path)
            || !stream.Path.EndsWith(".srt", StringComparison.OrdinalIgnoreCase)
            || !System.IO.File.Exists(stream.Path))
        {
            error = BadRequest("Only external .srt subtitles are supported.");
            return null;
        }

        error = null;
        return stream.Path;
    }
}

/// <summary>Result of a sync operation.</summary>
public record SyncResult(int AppliedOffsetMs);
