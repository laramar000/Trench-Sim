using Client.Core;
using Rules.Core;
using Rules.Core.Events;
using Rules.Core.State;

namespace Client.Tests;

// Animation stage A3: the tracker names exactly the status changes each event makes in the server's state (compared
// here with the state before and after every event of the simulated matches): Standing to Down, Down to Standing, and
// to Out of Action, from Injury Rolls and standing up among others; a model seen for the first time changes nothing.
public class StatusChangesTests
{
    [Fact]
    public void EachEventNamesTheStatusChangesItMakes()
    {
        var kinds = new Dictionary<(ModelStatus, ModelStatus), int>();
        var events = new HashSet<string>();
        foreach (var (_, log) in ClientCoreTests.Matches.Value)
        {
            var tracker = new StatusTracker();
            var state = MatchState.None;
            foreach (var e in log)
            {
                var before = state;
                state = Reducer.Apply(state, e);
                var changes = tracker.Next(state);
                var expected = state.Models
                    .Where(m => before.FindModel(m.Id) is { } was && was.Status != m.Status)
                    .Select(m => new StatusChange(m.Id, before.FindModel(m.Id)!.Status, m.Status)).ToList();
                Assert.Equal(expected, changes);
                foreach (var change in changes)
                {
                    kinds[(change.From, change.To)] = kinds.GetValueOrDefault((change.From, change.To)) + 1;
                    events.Add(e.GetType().Name);
                }
            }
        }
        Assert.True(kinds.GetValueOrDefault((ModelStatus.Standing, ModelStatus.Down)) > 0, string.Join(", ", kinds));
        Assert.True(kinds.GetValueOrDefault((ModelStatus.Down, ModelStatus.Standing)) > 0, string.Join(", ", kinds));
        Assert.True(kinds.Where(k => k.Key.Item2 == ModelStatus.OutOfAction).Sum(k => k.Value) > 0, string.Join(", ", kinds));
        Assert.Contains(nameof(InjuryRolled), events);
        Assert.Contains(nameof(ModelStoodUp), events);
    }

    [Fact]
    public void AModelSeenForTheFirstTimeChangesNothing()
    {
        var tracker = new StatusTracker();
        foreach (var (_, log) in ClientCoreTests.Matches.Value.Take(3))
        {
            var state = MatchState.None;
            foreach (var e in log)
            {
                state = Reducer.Apply(state, e);
            }
            // Cleared, the tracker meets the end of a match as new: nothing changed.
            tracker.Clear();
            Assert.Empty(tracker.Next(state));
            Assert.Empty(tracker.Next(state));
        }
    }
}
