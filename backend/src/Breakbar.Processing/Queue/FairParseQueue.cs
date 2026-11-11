namespace Breakbar.Processing.Queue;

public enum EnqueueResult
{
    Accepted,

    /// <summary>The job's memory estimate exceeds <see cref="ParseQueueOptions.MaxJobMemoryBytes"/>.</summary>
    TooLarge,
}

/// <summary>Point-in-time view of the queue, for metrics and the admin page.</summary>
public sealed record QueueStats(int Queued, int Running, int ActiveUsers, long ReservedMemoryBytes);

/// <summary>
/// In-memory fair queue: one FIFO per user, users served round-robin (fair queuing over tenants).
/// Work-conserving: a single user gets every free worker; as soon as a second user enqueues,
/// both are served alternately, so a big batch never blocks a later user for more than one job.
///
/// Admission control on top of round-robin (docs/PLAN.md, section 3):
/// - at most <see cref="ParseQueueOptions.MaxParallel"/> jobs run at the same time,
/// - the memory estimates of running jobs must fit into <see cref="ParseQueueOptions.MemoryBudgetBytes"/>,
/// - jobs above <see cref="ParseQueueOptions.ExclusiveThresholdBytes"/> run alone.
/// If the next job in round-robin order does not fit, workers wait instead of letting smaller jobs
/// overtake it, so big logs cannot starve.
///
/// Thread-safe. Persistence (SQLite) is handled by the caller; after a restart, re-enqueue pending jobs.
/// </summary>
public sealed class FairParseQueue(ParseQueueOptions options)
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, LinkedList<ParseJob>> _jobsByUser = new(StringComparer.Ordinal);

    // Users with waiting jobs, in the order they will be served next.
    private readonly LinkedList<string> _ring = new();
    private readonly List<TaskCompletionSource> _waiters = [];

    private int _queued;
    private int _running;
    private long _reservedBytes;
    private bool _exclusiveRunning;

    public EnqueueResult Enqueue(ParseJob job)
    {
        if (job.EstimatedMemoryBytes > options.MaxJobMemoryBytes)
        {
            return EnqueueResult.TooLarge;
        }
        lock (_lock)
        {
            if (!_jobsByUser.TryGetValue(job.UserId, out var jobs))
            {
                jobs = new LinkedList<ParseJob>();
                _jobsByUser.Add(job.UserId, jobs);
                _ring.AddLast(job.UserId);
            }
            jobs.AddLast(job);
            _queued++;
            WakeWaiters();
        }
        return EnqueueResult.Accepted;
    }

    /// <summary>
    /// Waits until the next job in fair order may start, and returns it as a lease.
    /// Disposing the lease frees its slot and memory reservation.
    /// </summary>
    public async ValueTask<ParseLease> DequeueAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            TaskCompletionSource waiter;
            lock (_lock)
            {
                if (TryTake(out var lease))
                {
                    return lease;
                }
                waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add(waiter);
            }
            try
            {
                await waiter.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                lock (_lock)
                {
                    _waiters.Remove(waiter);
                }
                throw;
            }
        }
    }

    /// <summary>Removes a job that has not started yet. Returns null if it is unknown or already running.</summary>
    public ParseJob? TryRemove(Guid jobId)
    {
        lock (_lock)
        {
            foreach (var (userId, jobs) in _jobsByUser)
            {
                for (var node = jobs.First; node is not null; node = node.Next)
                {
                    if (node.Value.Id != jobId)
                    {
                        continue;
                    }
                    jobs.Remove(node);
                    _queued--;
                    if (jobs.Count == 0)
                    {
                        _jobsByUser.Remove(userId);
                        _ring.Remove(userId);
                    }
                    WakeWaiters(); // the removed job may have been blocking the head of the queue
                    return node.Value;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Number of jobs that will start before this one if no new jobs arrive (0 = next).
    /// Null if the job is not waiting (unknown, removed or already running).
    /// </summary>
    public int? GetPosition(Guid jobId)
    {
        lock (_lock)
        {
            return GetPositionLocked(jobId);
        }
    }

    private int? GetPositionLocked(Guid jobId)
    {
        // Round-robin order: round r takes the r-th job of every user that has more than r jobs,
        // users in ring order. A job at index k of user u is preceded by min(len, k) jobs of every
        // other user, plus one more job from each user ahead of u in the ring that has more than k jobs.
        int ringIndex = 0;
        foreach (string userId in _ring)
        {
            var jobs = _jobsByUser[userId];
            int k = IndexOf(jobs, jobId);
            if (k >= 0)
            {
                int position = k; // this user's own earlier jobs
                int otherIndex = 0;
                foreach (string other in _ring)
                {
                    if (other != userId)
                    {
                        int len = _jobsByUser[other].Count;
                        position += Math.Min(len, k) + (otherIndex < ringIndex && len > k ? 1 : 0);
                    }
                    otherIndex++;
                }
                return position;
            }
            ringIndex++;
        }
        return null;
    }

    /// <summary>Positions of all waiting jobs of one user (same meaning as <see cref="GetPosition"/>).</summary>
    public IReadOnlyDictionary<Guid, int> GetPositions(string userId)
    {
        lock (_lock)
        {
            if (!_jobsByUser.TryGetValue(userId, out var jobs))
            {
                return new Dictionary<Guid, int>();
            }
            var result = new Dictionary<Guid, int>(jobs.Count);
            foreach (var job in jobs)
            {
                result[job.Id] = GetPositionLocked(job.Id)!.Value;
            }
            return result;
        }
    }

    public QueueStats GetStats()
    {
        lock (_lock)
        {
            return new QueueStats(_queued, _running, _jobsByUser.Count, _reservedBytes);
        }
    }

    private bool TryTake(out ParseLease lease)
    {
        lease = null!;
        if (_ring.First is null || _running >= options.MaxParallel || _exclusiveRunning)
        {
            return false;
        }
        string userId = _ring.First.Value;
        var jobs = _jobsByUser[userId];
        var job = jobs.First!.Value;
        bool exclusive = job.EstimatedMemoryBytes > options.ExclusiveThresholdBytes;

        // An idle queue always admits the head job (oversized jobs were rejected in Enqueue).
        if (_running > 0 && (exclusive || _reservedBytes + job.EstimatedMemoryBytes > options.MemoryBudgetBytes))
        {
            return false;
        }

        jobs.RemoveFirst();
        _queued--;
        _ring.RemoveFirst();
        if (jobs.Count > 0)
        {
            _ring.AddLast(userId); // served once, back to the end of the line
        }
        else
        {
            _jobsByUser.Remove(userId);
        }

        _running++;
        _reservedBytes += job.EstimatedMemoryBytes;
        _exclusiveRunning = exclusive;
        lease = new ParseLease(this, job, exclusive);
        return true;
    }

    internal void Release(ParseLease lease)
    {
        lock (_lock)
        {
            _running--;
            _reservedBytes -= lease.Job.EstimatedMemoryBytes;
            if (lease.IsExclusive)
            {
                _exclusiveRunning = false;
            }
            WakeWaiters();
        }
    }

    private void WakeWaiters()
    {
        // Few workers (2 on the VPS): waking all and letting them re-check is simpler than targeted wake-ups.
        foreach (var waiter in _waiters)
        {
            waiter.TrySetResult();
        }
        _waiters.Clear();
    }

    private static int IndexOf(LinkedList<ParseJob> jobs, Guid jobId)
    {
        int index = 0;
        for (var node = jobs.First; node is not null; node = node.Next, index++)
        {
            if (node.Value.Id == jobId)
            {
                return index;
            }
        }
        return -1;
    }
}

/// <summary>A started job. Dispose when the parse is finished (success or failure) to free its slot.</summary>
public sealed class ParseLease : IDisposable
{
    private readonly FairParseQueue _queue;
    private int _disposed;

    internal ParseLease(FairParseQueue queue, ParseJob job, bool isExclusive)
    {
        _queue = queue;
        Job = job;
        IsExclusive = isExclusive;
    }

    public ParseJob Job { get; }

    /// <summary>True if this job runs alone because of its memory estimate.</summary>
    public bool IsExclusive { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _queue.Release(this);
        }
    }
}
