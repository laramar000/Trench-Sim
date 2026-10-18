using Match.Protocol;
using Rules.Core;
using Rules.Core.State;

namespace Client.Tests;

// The screen reads the client's copy of the match on the Godot thread while the network thread applies events. A
// reader must never see the state after one event with the count or hash of another: once, at the end of a match, a
// window showed "The match has ended." (the state before MATCH ENDED) with the final event count and hash.
public class MirrorCopyTests
{
    [Fact]
    public async Task AReaderOnAnotherThreadAlwaysSeesAStateItsCountAndItsHashTogether()
    {
        var (_, log) = ClientCoreTests.Matches.Value[0];
        var published = new List<EventPublished>();
        var state = MatchState.None;
        var hashes = new Dictionary<int, string> { [0] = StateHash.Compute(state) };
        foreach (var e in log)
        {
            state = Reducer.Apply(state, e);
            published.Add(new EventPublished(published.Count + 1, e, StateHash.Compute(state)));
            hashes[published.Count] = published[^1].Hash;
        }
        for (var round = 0; round < 20; round++)
        {
            var mirror = new MatchMirror();
            using var done = new CancellationTokenSource();
            var reads = 0;
            var reader = Task.Run(() =>
            {
                while (!done.IsCancellationRequested)
                {
                    var copy = mirror.Current;
                    Assert.Equal(hashes[copy.Events], copy.Hash);
                    // The state is the one after exactly that many events: it ends only with the last one.
                    Assert.Equal(copy.Events == published.Count, copy.State.Phase == Phase.Ended);
                    reads++;
                }
            });
            foreach (var p in published)
            {
                mirror.Apply(p);
            }
            done.Cancel();
            await reader;
            Assert.True(reads > 0);
            Assert.Equal(published.Count, mirror.Current.Events);
            Assert.Equal(published[^1].Hash, mirror.Hash);
        }
    }
}
