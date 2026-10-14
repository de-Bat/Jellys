using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Recap.Services;

/// <summary>Scheduled task: precompute recap checkpoints for every user's in-progress items.</summary>
public class PrecomputeRecapsTask : IScheduledTask
{
    private readonly IUserManager _users;
    private readonly ILibraryManager _library;
    private readonly IUserDataManager _userData;
    private readonly RecapService _recap;

    public PrecomputeRecapsTask(IUserManager users, ILibraryManager library, IUserDataManager userData, RecapService recap)
    {
        _users = users;
        _library = library;
        _userData = userData;
        _recap = recap;
    }

    public string Name => "Precompute recaps";

    public string Key => "RecapPrecompute";

    public string Description => "Generates recaps for in-progress episodes so resuming is instant.";

    public string Category => "Recap";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.PrecomputeEnabled)
        {
            progress.Report(100);
            return;
        }

        var cp = RecapService.CheckpointTicks(config);
        var lang = RecapService.ResolveLanguage(null);
        var work = new Dictionary<Guid, (BaseItem Item, int K)>();

        foreach (var user in _users.GetUsers())
        {
            var items = _library.GetItemList(new InternalItemsQuery(user)
            {
                IsResumable = true,
                MediaTypes = [MediaType.Video],
                Limit = 50
            });
            foreach (var item in items)
            {
                var pos = _userData.GetUserData(user, item)?.PlaybackPositionTicks ?? 0;
                var k = RecapService.PositionToCheckpoint(item, pos, cp) + Math.Max(0, config.LookAheadCheckpoints);
                if (!work.TryGetValue(item.Id, out var existing) || existing.K < k)
                {
                    work[item.Id] = (item, k);
                }
            }
        }

        var done = 0;
        foreach (var (item, k) in work.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await _recap.EnsureAsync(item, lang, k, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One bad item (LLM down, bad subtitles) must not stop the sweep.
            }

            progress.Report(100.0 * ++done / work.Count);
        }
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromHours(6).Ticks
        };
    }
}
