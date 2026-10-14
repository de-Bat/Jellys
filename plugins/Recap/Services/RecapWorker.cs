using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Jellyfin.Data.Enums;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Recap.Services;

/// <summary>Generates recap checkpoints in the background, and queues work when playback starts.</summary>
public class RecapWorker : BackgroundService
{
    private readonly RecapQueue _queue;
    private readonly RecapService _recap;
    private readonly ILibraryManager _library;
    private readonly ISessionManager _sessions;
    private readonly ILogger<RecapWorker> _logger;

    public RecapWorker(RecapQueue queue, RecapService recap, ILibraryManager library, ISessionManager sessions, ILogger<RecapWorker> logger)
    {
        _queue = queue;
        _recap = recap;
        _library = library;
        _sessions = sessions;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _sessions.PlaybackStart += OnPlaybackStart;
        try
        {
            await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    var item = _library.GetItemById(job.ItemId);
                    if (item is not null)
                    {
                        await _recap.EnsureAsync(item, job.Language, job.UpToCheckpoint, stoppingToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Recap background job failed for {ItemId}", job.ItemId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        finally
        {
            _sessions.PlaybackStart -= OnPlaybackStart;
        }
    }

    private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.PrecomputeEnabled || e.Item is null || e.Item.MediaType != MediaType.Video)
        {
            return;
        }

        var cp = RecapService.CheckpointTicks(config);
        var k = RecapService.PositionToCheckpoint(e.Item, e.PlaybackPositionTicks ?? 0, cp);
        _queue.Enqueue(e.Item.Id, RecapService.ResolveLanguage(null), k + Math.Max(0, config.LookAheadCheckpoints));
    }
}
