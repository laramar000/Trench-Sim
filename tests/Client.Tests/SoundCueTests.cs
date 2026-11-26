using Client.Core;
using Rules.Core.Events;

namespace Client.Tests;

// QoL I3: each sound comes from the server's event: activations, rolls the rules count as passed or failed, scoring.
// The turn alert asks for attention once when the move turns to a window that is not in front, and ends on focus.
public class SoundCueTests
{
    [Fact]
    public void CuesFollowTheEventsAndTheRulesVerdicts()
    {
        var seen = new Dictionary<SoundCue, int>();
        foreach (var (_, log) in ClientCoreTests.Matches.Value)
        {
            foreach (var e in log)
            {
                var cue = SoundCues.Of(e);
                var roll = DiceRolls.Of(e);
                SoundCue? wanted = e is ActivationStarted ? SoundCue.Activation
                    : e is ObjectivesScored ? SoundCue.Scoring
                    : roll?.Verdict is RollVerdict.Pass or RollVerdict.Critical ? SoundCue.Pass
                    : roll?.Verdict is RollVerdict.Fail ? SoundCue.Fail : null;
                Assert.Equal(wanted, cue);
                if (cue is { } c)
                {
                    seen[c] = seen.GetValueOrDefault(c) + 1;
                }
            }
        }
        Assert.All(new[] { SoundCue.Activation, SoundCue.Scoring, SoundCue.Pass, SoundCue.Fail }, c => Assert.True(seen.GetValueOrDefault(c) > 0, c.ToString()));
    }

    [Fact]
    public void TheTurnAlertAsksOnceAndEndsOnFocus()
    {
        var alert = new TurnAlert();
        Assert.False(alert.Update(yourMove: false, focused: false));
        Assert.True(alert.Update(yourMove: true, focused: false));
        Assert.Equal(TurnAlert.Prefix + "Trench Sim", alert.Title("Trench Sim"));
        Assert.False(alert.Update(yourMove: true, focused: false));
        Assert.True(alert.Alerted);
        Assert.False(alert.Update(yourMove: true, focused: true));
        Assert.Equal("Trench Sim", alert.Title("Trench Sim"));
        // In front when the move turns: no alert at all.
        alert.Update(yourMove: false, focused: true);
        Assert.False(alert.Update(yourMove: true, focused: true));
        Assert.False(alert.Alerted);
        // The opponent's move ends it too.
        alert.Update(yourMove: false, focused: false);
        Assert.True(alert.Update(yourMove: true, focused: false));
        alert.Update(yourMove: false, focused: false);
        Assert.False(alert.Alerted);
    }
}
