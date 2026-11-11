using System.Collections.Concurrent;
using System.Threading.Channels;
using Breakbar.Processing.Queue;

namespace Breakbar.Api.Jobs;

public sealed record JobEvent(string Type, object Data);

/// <summary>
/// Fan-out of job events to connected SSE clients, per user. In-memory only: a client that reconnects
/// reloads the job list via GET /api/jobs and then follows the stream.
/// </summary>
public sealed class JobEventHub(FairParseQueue queue)
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Channel<JobEvent>>> _subscribers = new(StringComparer.Ordinal);

    public sealed class Subscription(JobEventHub hub, string userId, Guid id, Channel<JobEvent> channel) : IDisposable
    {
        public ChannelReader<JobEvent> Reader => channel.Reader;

        public void Dispose()
        {
            if (hub._subscribers.TryGetValue(userId, out var channels))
            {
                channels.TryRemove(id, out _);
                if (channels.IsEmpty)
                {
                    hub._subscribers.TryRemove(new KeyValuePair<string, ConcurrentDictionary<Guid, Channel<JobEvent>>>(userId, channels));
                }
            }
            channel.Writer.TryComplete();
        }
    }

    public Subscription Subscribe(string userId)
    {
        // Bounded: a stalled client drops old events instead of growing memory; it can always re-fetch the list.
        var channel = Channel.CreateBounded<JobEvent>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest });
        var id = Guid.NewGuid();
        _subscribers.GetOrAdd(userId, _ => new())[id] = channel;
        return new Subscription(this, userId, id, channel);
    }

    public void PublishJob(string userId, JobDto job) => Publish(userId, new JobEvent("job", job));

    /// <summary>Sends every connected user the current positions of their waiting jobs.</summary>
    public void PublishPositions()
    {
        foreach (var userId in _subscribers.Keys)
        {
            var positions = queue.GetPositions(userId);
            if (positions.Count > 0)
            {
                Publish(userId, new JobEvent("positions", new PositionsDto(positions.ToDictionary(p => p.Key.ToString("N"), p => p.Value))));
            }
        }
    }

    private void Publish(string userId, JobEvent jobEvent)
    {
        if (_subscribers.TryGetValue(userId, out var channels))
        {
            foreach (var channel in channels.Values)
            {
                channel.Writer.TryWrite(jobEvent);
            }
        }
    }
}
