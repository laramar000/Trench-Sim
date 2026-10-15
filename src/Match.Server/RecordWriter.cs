using System.Threading.Channels;
using Match.Protocol;

namespace Match.Server;

// Where a match reports that it started and that it closed. The Lobby calls it under its lock, so it must only
// enqueue: file work happens elsewhere.
public interface IMatchRecorder
{
    void MatchStarted(OpenMatch match);

    void MatchClosed(MatchRecordBody record);
}

public sealed class NullRecorder : IMatchRecorder
{
    public static readonly NullRecorder Instance = new();

    public void MatchStarted(OpenMatch match)
    {
    }

    public void MatchClosed(MatchRecordBody record)
    {
    }
}

// Writes markers and records one at a time, in the order they were reported, on its own task. A marker is therefore
// always durable before its match's record is written.
public sealed class RecordWriter : IMatchRecorder, IAsyncDisposable
{
    private readonly RecordStore _store;
    private readonly Action<string> _log;
    private readonly Channel<Func<RecordWriteOutcome?>> _work = Channel.CreateUnbounded<Func<RecordWriteOutcome?>>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _running;
    private readonly List<(string MatchId, RecordKind Kind, RecordWriteOutcome Outcome)> _results = [];

    public RecordWriter(RecordStore store, Action<string>? log = null)
    {
        _store = store;
        _log = log ?? (_ => { });
        _running = Task.Run(RunAsync);
    }

    public RecordStore Store => _store;

    // What happened to each record so far, for tests and diagnostics.
    public IReadOnlyList<(string MatchId, RecordKind Kind, RecordWriteOutcome Outcome)> Results
    {
        get
        {
            lock (_results)
            {
                return [.. _results];
            }
        }
    }

    public void MatchStarted(OpenMatch match) => _work.Writer.TryWrite(() =>
    {
        try
        {
            _store.WriteMarker(match);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Without a marker a crash would leave no trace of the match; say so. The record is still attempted.
            _log($"marker {match.MatchId}: NOT written ({e.GetType().Name})");
        }
        return null;
    });

    public void MatchClosed(MatchRecordBody record) => _work.Writer.TryWrite(() =>
    {
        var outcome = _store.WriteRecord(record);
        lock (_results)
        {
            _results.Add((record.MatchId, record.Result.Kind, outcome));
        }
        return outcome;
    });

    // Waits until everything reported so far has been written, or the time runs out. Returns true if it all was.
    public async Task<bool> DrainAsync(TimeSpan timeout)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_work.Writer.TryWrite(() =>
            {
                done.TrySetResult();
                return null;
            }))
        {
            return _running.IsCompleted;
        }
        return await Task.WhenAny(done.Task, Task.Delay(timeout)) == done.Task;
    }

    // Stops taking work and waits (up to the timeout) for what is queued.
    public async Task<bool> CompleteAsync(TimeSpan timeout)
    {
        _work.Writer.TryComplete();
        return await Task.WhenAny(_running, Task.Delay(timeout)) == _running;
    }

    public async ValueTask DisposeAsync() => await CompleteAsync(TimeSpan.FromSeconds(30));

    private async Task RunAsync()
    {
        await foreach (var item in _work.Reader.ReadAllAsync())
        {
            try
            {
                item();
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                // Never lose the writer to one bad record: the marker stays and recovery settles it.
                _log($"record writer: {e.GetType().Name}");
            }
        }
    }
}
