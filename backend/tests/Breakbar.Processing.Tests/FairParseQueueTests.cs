using Breakbar.Processing.Queue;

namespace Breakbar.Processing.Tests;

public sealed class FairParseQueueTests
{
    private const long MB = 1024L * 1024L;
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(100);

    private static readonly ParseQueueOptions Options = new()
    {
        MaxParallel = 2,
        MemoryBudgetBytes = 2400 * MB,
        ExclusiveThresholdBytes = 1200 * MB,
        MaxJobMemoryBytes = 2400 * MB,
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ParseJob Job(string user, string name, long memoryMb = 200)
        => new(Guid.NewGuid(), user, name, "out/" + name, memoryMb * MB);

    /// <summary>Dequeues one job and releases it immediately, i.e. one worker processing jobs back to back.</summary>
    private static async Task<string> TakeAndRelease(FairParseQueue queue)
    {
        using var lease = await queue.DequeueAsync(Ct);
        return lease.Job.LogPath;
    }

    [Fact]
    public async Task Users_are_served_round_robin()
    {
        var queue = new FairParseQueue(Options);
        foreach (var n in new[] { "A1", "A2", "A3", "A4", "A5" })
        {
            queue.Enqueue(Job("A", n));
        }
        queue.Enqueue(Job("B", "B1"));
        queue.Enqueue(Job("B", "B2"));

        var order = new List<string>();
        for (int i = 0; i < 7; i++)
        {
            order.Add(await TakeAndRelease(queue));
        }

        Assert.Equal(["A1", "B1", "A2", "B2", "A3", "A4", "A5"], order);
    }

    [Fact]
    public async Task Late_user_waits_for_at_most_one_job_of_a_big_batch()
    {
        var queue = new FairParseQueue(Options);
        for (int i = 1; i <= 100; i++)
        {
            queue.Enqueue(Job("A", $"A{i}"));
        }
        Assert.Equal("A1", await TakeAndRelease(queue));

        var late = Job("B", "B1");
        queue.Enqueue(late);

        Assert.Equal(1, queue.GetPosition(late.Id));
        Assert.Equal("A2", await TakeAndRelease(queue));
        Assert.Equal("B1", await TakeAndRelease(queue));
    }

    [Fact]
    public async Task Single_user_gets_all_workers()
    {
        var queue = new FairParseQueue(Options);
        for (int i = 1; i <= 5; i++)
        {
            queue.Enqueue(Job("A", $"A{i}"));
        }

        using var first = await queue.DequeueAsync(Ct);
        using var second = await queue.DequeueAsync(Ct);

        Assert.Equal("A1", first.Job.LogPath);
        Assert.Equal("A2", second.Job.LogPath);
        Assert.Equal(new QueueStats(Queued: 3, Running: 2, ActiveUsers: 1, ReservedMemoryBytes: 400 * MB), queue.GetStats());
    }

    [Fact]
    public async Task No_more_than_MaxParallel_jobs_run_at_once()
    {
        var queue = new FairParseQueue(Options);
        for (int i = 1; i <= 3; i++)
        {
            queue.Enqueue(Job("A", $"A{i}"));
        }
        var first = await queue.DequeueAsync(Ct);
        using var second = await queue.DequeueAsync(Ct);

        var third = queue.DequeueAsync(Ct).AsTask();
        await Task.Delay(Short, Ct);
        Assert.False(third.IsCompleted);

        first.Dispose();
        using var lease = await third.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.Equal("A3", lease.Job.LogPath);
    }

    [Fact]
    public async Task Exclusive_job_waits_until_running_jobs_are_finished()
    {
        var queue = new FairParseQueue(Options);
        queue.Enqueue(Job("A", "small", memoryMb: 300));
        queue.Enqueue(Job("B", "wvw", memoryMb: 1300)); // above the 1200 MB exclusive threshold
        var running = await queue.DequeueAsync(Ct);

        // A slot is free and 300 + 1300 fits the budget, but "wvw" must run alone.
        var next = queue.DequeueAsync(Ct).AsTask();
        await Task.Delay(Short, Ct);
        Assert.False(next.IsCompleted);

        running.Dispose();
        using var lease = await next.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.Equal("wvw", lease.Job.LogPath);
        Assert.True(lease.IsExclusive);
    }

    [Fact]
    public async Task Memory_budget_limits_parallel_jobs()
    {
        var queue = new FairParseQueue(Options);
        queue.Enqueue(Job("A", "a-1000", memoryMb: 1000));
        queue.Enqueue(Job("B", "b-1000", memoryMb: 1000));
        queue.Enqueue(Job("C", "c-600", memoryMb: 600));
        queue.Enqueue(Job("D", "d-100", memoryMb: 100));
        using var a = await queue.DequeueAsync(Ct);
        var b = await queue.DequeueAsync(Ct); // 2000 MB reserved

        b.Dispose();
        using var c = await queue.DequeueAsync(Ct); // 1000 + 600 fits
        Assert.Equal("c-600", c.Job.LogPath);
        Assert.Equal(1600 * MB, queue.GetStats().ReservedMemoryBytes);
    }

    [Fact]
    public async Task Head_job_over_budget_waits_even_if_a_slot_is_free()
    {
        var options = new ParseQueueOptions { MaxParallel = 2, MemoryBudgetBytes = 1000 * MB, ExclusiveThresholdBytes = 1000 * MB, MaxJobMemoryBytes = 1000 * MB };
        var queue = new FairParseQueue(options);
        queue.Enqueue(Job("A", "a-700", memoryMb: 700));
        queue.Enqueue(Job("B", "b-400", memoryMb: 400));
        queue.Enqueue(Job("C", "c-100", memoryMb: 100));
        var a = await queue.DequeueAsync(Ct);

        // b-400 is next and does not fit (700 + 400 > 1000); c-100 would fit but must not overtake.
        var next = queue.DequeueAsync(Ct).AsTask();
        await Task.Delay(Short, Ct);
        Assert.False(next.IsCompleted);

        a.Dispose();
        using var lease = await next.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.Equal("b-400", lease.Job.LogPath);
    }

    [Fact]
    public async Task Exclusive_job_runs_alone()
    {
        var queue = new FairParseQueue(Options);
        queue.Enqueue(Job("A", "wvw", memoryMb: 1500));
        queue.Enqueue(Job("B", "small", memoryMb: 100));
        var wvw = await queue.DequeueAsync(Ct);
        Assert.True(wvw.IsExclusive);

        var next = queue.DequeueAsync(Ct).AsTask();
        await Task.Delay(Short, Ct);
        Assert.False(next.IsCompleted);

        wvw.Dispose();
        using var lease = await next.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.Equal("small", lease.Job.LogPath);
        Assert.False(lease.IsExclusive);
    }

    [Fact]
    public void Too_large_job_is_rejected()
    {
        var queue = new FairParseQueue(Options);

        Assert.Equal(EnqueueResult.TooLarge, queue.Enqueue(Job("A", "huge", memoryMb: 2401)));
        Assert.Equal(0, queue.GetStats().Queued);
    }

    [Fact]
    public void Position_follows_round_robin_order()
    {
        var queue = new FairParseQueue(Options);
        var a = new[] { Job("A", "A0"), Job("A", "A1"), Job("A", "A2") };
        var b = new[] { Job("B", "B0"), Job("B", "B1") };
        foreach (var job in a.Concat(b))
        {
            queue.Enqueue(job);
        }

        // Expected order: A0, B0, A1, B1, A2
        Assert.Equal(0, queue.GetPosition(a[0].Id));
        Assert.Equal(1, queue.GetPosition(b[0].Id));
        Assert.Equal(2, queue.GetPosition(a[1].Id));
        Assert.Equal(3, queue.GetPosition(b[1].Id));
        Assert.Equal(4, queue.GetPosition(a[2].Id));
        Assert.Null(queue.GetPosition(Guid.NewGuid()));

        var positionsOfB = queue.GetPositions("B");
        Assert.Equal(2, positionsOfB.Count);
        Assert.Equal(1, positionsOfB[b[0].Id]);
        Assert.Equal(3, positionsOfB[b[1].Id]);
        Assert.Empty(queue.GetPositions("nobody"));
    }

    [Fact]
    public async Task Removed_job_is_not_dequeued()
    {
        var queue = new FairParseQueue(Options);
        var keep = Job("A", "keep");
        var drop = Job("B", "drop");
        queue.Enqueue(drop);
        queue.Enqueue(keep);

        Assert.Equal(drop, queue.TryRemove(drop.Id));
        Assert.Null(queue.TryRemove(drop.Id));
        Assert.Equal("keep", await TakeAndRelease(queue));
        Assert.Equal(new QueueStats(0, 0, 0, 0), queue.GetStats());
    }

    [Fact]
    public async Task Waiting_dequeue_can_be_cancelled()
    {
        var queue = new FairParseQueue(Options);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var waiting = queue.DequeueAsync(cts.Token).AsTask();

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }

    [Fact]
    public async Task Waiting_worker_is_woken_by_enqueue()
    {
        var queue = new FairParseQueue(Options);
        var waiting = queue.DequeueAsync(Ct).AsTask();
        await Task.Delay(Short, Ct);
        Assert.False(waiting.IsCompleted);

        queue.Enqueue(Job("A", "A1"));

        using var lease = await waiting.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.Equal("A1", lease.Job.LogPath);
    }

    [Fact]
    public async Task Disposing_a_lease_twice_releases_only_once()
    {
        var queue = new FairParseQueue(Options);
        queue.Enqueue(Job("A", "A1"));
        var lease = await queue.DequeueAsync(Ct);

        lease.Dispose();
        lease.Dispose();

        Assert.Equal(new QueueStats(0, 0, 0, 0), queue.GetStats());
    }
}
