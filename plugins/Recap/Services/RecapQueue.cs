using System.Threading.Channels;

namespace Jellyfin.Plugin.Recap.Services;

public record RecapJob(Guid ItemId, string Language, int UpToCheckpoint);

/// <summary>Work queue for background recap generation. Newest jobs win when full.</summary>
public class RecapQueue
{
    private readonly Channel<RecapJob> _channel = Channel.CreateBounded<RecapJob>(
        new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public ChannelReader<RecapJob> Reader => _channel.Reader;

    public void Enqueue(Guid itemId, string language, int upToCheckpoint)
    {
        if (Plugin.Instance?.Configuration.PrecomputeEnabled == true)
        {
            _channel.Writer.TryWrite(new RecapJob(itemId, language, upToCheckpoint));
        }
    }
}
