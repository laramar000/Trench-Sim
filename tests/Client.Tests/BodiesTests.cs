using Client.Core;
using Rules.Core;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Client.Tests;

// Bodies (Jason, 2026-11-23: "bodies lay on the ground, not disappear"): through every simulated match, after each event
// the scene lists every model Out of Action as fallen, each at the last place the server's table had it (the tracker
// keeps it: the server clears it), and never among the models on the table; without the tracker's places none is listed
// (nothing to place it by), and the models on the table are the same either way (display only).
public class BodiesTests
{
    [Fact]
    public void EveryModelOutOfActionLiesWhereTheTableLastHadIt()
    {
        var fallenSeen = 0;
        foreach (var (_, log) in ClientCoreTests.Matches.Value)
        {
            var tracker = new StatusTracker();
            var state = MatchState.None;
            var last = new Dictionary<ModelId, Point2>();
            foreach (var e in log)
            {
                state = Reducer.Apply(state, e);
                tracker.Next(state);
                foreach (var model in state.Models.Where(m => m.Position is not null))
                {
                    last[model.Id] = model.Position!.Value;
                }
                if (BoardScene.Of(state, Seat.One, ContentNames.None, tracker.Fallen) is not { } scene)
                {
                    continue;
                }
                var outOfAction = state.Models.Where(m => m.Status == ModelStatus.OutOfAction && last.ContainsKey(m.Id)).Select(m => m.Id).ToHashSet();
                Assert.True(outOfAction.SetEquals(scene.Fallen.Select(f => f.Id)), $"after {e.GetType().Name}");
                foreach (var body in scene.Fallen)
                {
                    Assert.Equal(TablePoint.Of(last[body.Id]), new TablePoint(body.X, body.Y));
                    Assert.Equal(ModelStatus.OutOfAction, body.Status);
                    Assert.Null(scene.Find(body.Id));
                }
                var plain = BoardScene.Of(state, Seat.One, ContentNames.None)!;
                Assert.Empty(plain.Fallen);
                Assert.Equal(plain.Models, scene.Models);
                fallenSeen = Math.Max(fallenSeen, scene.Fallen.Count);
            }
        }
        Assert.True(fallenSeen > 0, "no model went Out of Action in the simulated matches");
    }

    [Fact]
    public void ClearingTheTrackerForgetsTheFallen()
    {
        var log = ClientCoreTests.Matches.Value.First(m => m.Log.Any(e => e is Rules.Core.Events.InjuryRolled)).Log;
        var tracker = new StatusTracker();
        var state = MatchState.None;
        foreach (var e in log)
        {
            state = Reducer.Apply(state, e);
            tracker.Next(state);
        }
        tracker.Clear();
        Assert.Empty(tracker.Fallen);
    }
}
