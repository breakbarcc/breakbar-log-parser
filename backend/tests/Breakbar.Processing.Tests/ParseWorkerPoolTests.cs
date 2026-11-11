using System.Collections.Concurrent;
using Breakbar.Processing.EliteInsights;
using Breakbar.Processing.Queue;
using Microsoft.Extensions.Logging.Abstractions;

namespace Breakbar.Processing.Tests;

public sealed class ParseWorkerPoolTests
{
    private const long MB = 1024L * 1024L;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ParseJob Job(string user, string name)
        => new(Guid.NewGuid(), user, name, "out/" + name, 200 * MB);

    [Fact]
    public async Task Parses_all_jobs_with_at_most_two_in_parallel()
    {
        int running = 0, maxRunning = 0;
        var parser = new FakeParser((path, _) =>
        {
            int now = Interlocked.Increment(ref running);
            InterlockedMax(ref maxRunning, now);
            Thread.Sleep(50);
            Interlocked.Decrement(ref running);
            return Ok(path);
        });
        var observer = new RecordingObserver(expectedFinished: 6);
        var queue = new FairParseQueue(new ParseQueueOptions { MaxParallel = 2 });
        foreach (var job in new[] { Job("A", "A1"), Job("A", "A2"), Job("A", "A3"), Job("A", "A4"), Job("B", "B1"), Job("B", "B2") })
        {
            queue.Enqueue(job);
        }

        await RunPool(queue, parser, observer, () => observer.AllFinished.WaitAsync(Timeout, Ct));

        Assert.Equal(6, observer.Completed.Count);
        Assert.All(observer.Completed, r => Assert.True(r.Result.Success));
        Assert.Equal(2, maxRunning);
    }

    [Fact]
    public async Task Crashing_job_is_reported_and_the_worker_keeps_going()
    {
        var parser = new FakeParser((path, _) => path == "boom" ? throw new InvalidOperationException("parser bug") : Ok(path));
        var observer = new RecordingObserver(expectedFinished: 2);
        var queue = new FairParseQueue(new ParseQueueOptions { MaxParallel = 1 });
        queue.Enqueue(Job("A", "boom"));
        queue.Enqueue(Job("A", "fine"));

        await RunPool(queue, parser, observer, () => observer.AllFinished.WaitAsync(Timeout, Ct));

        var failed = Assert.Single(observer.Failed);
        Assert.Equal("boom", failed.Job.LogPath);
        Assert.IsType<InvalidOperationException>(failed.Exception);
        Assert.Equal("fine", Assert.Single(observer.Completed).Job.LogPath);
        Assert.Equal(new QueueStats(0, 0, 0, 0), queue.GetStats());
    }

    [Fact]
    public async Task Stopping_the_pool_interrupts_a_running_job()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var parser = new FakeParser((path, ct) =>
        {
            started.TrySetResult();
            ct.WaitHandle.WaitOne(Timeout); // simulates a long parse that honours cancellation
            ct.ThrowIfCancellationRequested();
            return Ok(path);
        });
        var observer = new RecordingObserver(expectedFinished: 1);
        var queue = new FairParseQueue(new ParseQueueOptions { MaxParallel = 1 });
        queue.Enqueue(Job("A", "long"));

        await RunPool(queue, parser, observer, () => started.Task.WaitAsync(Timeout, Ct));

        Assert.Equal("long", Assert.Single(observer.Interrupted).LogPath);
        Assert.Empty(observer.Completed);
    }

    private static async Task RunPool(FairParseQueue queue, IEliteInsightsParser parser, RecordingObserver observer, Func<Task> waitUntil)
    {
        var pool = new ParseWorkerPool(queue, parser, observer, new ParseQueueOptions { MaxParallel = 2 }, NullLogger<ParseWorkerPool>.Instance);
        await pool.StartAsync(Ct);
        try
        {
            await waitUntil();
        }
        finally
        {
            await pool.StopAsync(Ct);
        }
    }

    private static EliteInsightsResult Ok(string path)
        => new() { Success = true, HtmlPath = path + ".html", Elapsed = TimeSpan.FromMilliseconds(1) };

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }

    private sealed class FakeParser(Func<string, CancellationToken, EliteInsightsResult> parse) : IEliteInsightsParser
    {
        public EliteInsightsResult Parse(string logPath, string outputDirectory, CancellationToken cancellationToken = default)
            => parse(logPath, cancellationToken);
    }

    private sealed class RecordingObserver(int expectedFinished) : IParseJobObserver
    {
        private int _finished;

        public ConcurrentQueue<(ParseJob Job, EliteInsightsResult Result)> Completed { get; } = new();
        public ConcurrentQueue<(ParseJob Job, Exception Exception)> Failed { get; } = new();
        public ConcurrentQueue<ParseJob> Interrupted { get; } = new();
        private readonly TaskCompletionSource _allFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task AllFinished => _allFinished.Task;

        public ValueTask OnStartedAsync(ParseJob job) => ValueTask.CompletedTask;

        public ValueTask OnCompletedAsync(ParseJob job, EliteInsightsResult result)
        {
            Completed.Enqueue((job, result));
            return Finished();
        }

        public ValueTask OnFailedAsync(ParseJob job, Exception exception)
        {
            Failed.Enqueue((job, exception));
            return Finished();
        }

        public ValueTask OnInterruptedAsync(ParseJob job)
        {
            Interrupted.Enqueue(job);
            return Finished();
        }

        private ValueTask Finished()
        {
            if (Interlocked.Increment(ref _finished) >= expectedFinished)
            {
                _allFinished.TrySetResult();
            }
            return ValueTask.CompletedTask;
        }
    }
}
