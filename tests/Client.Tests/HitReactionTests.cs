using Client.Core;
using Rules.Core;
using Rules.Core.Events;
using Rules.Core.State;

namespace Client.Tests;

// Hit reactions (2026-11-26, "an animation for when a unit takes a hit but doesn't die ... when a unit takes a blood
// marker"): the tracker names exactly the models each event gave a BLOOD MARKER and left standing (compared with the
// state before and after every event of the simulated matches); the battlefield plays a Wound beat for each as the
// event's line comes up, reeling away from the model named with it; Off plays none.
public class HitReactionTests
{
    [Fact]
    public void EachEventNamesTheModelsItWoundedAndLeftStanding()
    {
        var wounds = 0;
        var events = new HashSet<string>();
        foreach (var (_, log) in ClientCoreTests.Matches.Value)
        {
            var tracker = new StatusTracker();
            var state = MatchState.None;
            foreach (var e in log)
            {
                var before = state;
                state = Reducer.Apply(state, e);
                tracker.Next(state);
                var expected = state.Models
                    .Where(m => before.FindModel(m.Id) is { } was && m.BloodMarkers > was.BloodMarkers
                        && m.Status == ModelStatus.Standing && was.Status == ModelStatus.Standing)
                    .Select(m => m.Id).ToList();
                Assert.Equal(expected, tracker.Wounded);
                wounds += expected.Count;
                if (expected.Count > 0)
                {
                    events.Add(e.GetType().Name);
                }
            }
        }
        Assert.True(wounds > 0, "the simulated matches wounded no one");
        Assert.Contains(nameof(InjuryRolled), events);
    }

    private static readonly ModelId A = new("a1");
    private static readonly ModelId B = new("b1");

    [Fact]
    public void AWoundedModelReelsAwayFromItsAttackerAsTheLineComesUp()
    {
        var hit = new LogLine(1, "", null, Models: [A, B]) { Effects = [new(EffectKind.MuzzleFlash, A), new(EffectKind.HitPuff, B)] };
        var injury = new LogLine(2, "", null, Models: [A, B]) { Wounded = [B] };
        var plan = Choreography.Of([hit, injury], id => id == A ? new TablePoint(0, 0) : new TablePoint(6, 0));
        var wound = Assert.Single(plan.Beats, b => b.Kind == BeatKind.Wound);
        Assert.Equal((B, (ModelId?)A, Choreography.WoundSeconds), (wound.Model, wound.Other, wound.Seconds));
        Assert.Equal(plan.Lines.Single(l => l.Line.Seq == 2).At, wound.Start, 9);
        Assert.True(wound.PosesModel);
        // Nothing waits for it: the update is no longer than the wound's end.
        Assert.Equal(wound.End, plan.Length, 9);
        Assert.DoesNotContain(Choreography.Of([hit, injury], _ => null, AnimationSpeed.Off).Beats, b => b.Kind == BeatKind.Wound);
        // A mine's wound (the event names only the model): reels with no one named.
        var mine = Choreography.Of([new LogLine(3, "", null, Models: [B]) { Wounded = [B] }], _ => null);
        Assert.Null(Assert.Single(mine.Beats, b => b.Kind == BeatKind.Wound).Other);
    }
}
