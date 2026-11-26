using Client.Core;
using Rules.Core.Events;
using Rules.Core.State;

namespace Client.Tests;

// Animation suite (2026-11-25): a shooter recoils as it fires; an ACTION a model takes is played on it from the server's
// event (taking aim and reloading brace it, Fortify digs it in with dust at its feet, Enforced Orthodoxy rallies it,
// Treat and Laying on of Hands turn it to the friend and reach to it). Off plays none of it.
public class AnimationSuiteTests
{
    private static readonly ModelId A = new("a1");
    private static readonly ModelId B = new("a2");

    private static ActionCue? Cue(GameEvent e) => new BattleEffectsTracker().Next(e, MatchState.None, null, 0).Action;

    [Fact]
    public void EachActionEventNamesItsAction()
    {
        Assert.Equal(new ActionCue(A, ActionStyle.Aim), Cue(new SelfActionDeclared(A, SelfActionKind.Aim)));
        Assert.Equal(new ActionCue(A, ActionStyle.Fortify), Cue(new SelfActionDeclared(A, SelfActionKind.Fortify)));
        Assert.Equal(new ActionCue(A, ActionStyle.Rally), Cue(new SelfActionDeclared(A, SelfActionKind.EnforcedOrthodoxy)));
        Assert.Equal(new ActionCue(A, ActionStyle.Treat, B), Cue(new SelfActionDeclared(A, SelfActionKind.LayingOnHands) { Target = B }));
        Assert.Equal(new ActionCue(A, ActionStyle.Treat, B), Cue(new TreatDeclared(A, B, TreatChoice.StandUp)));
        Assert.Equal(new ActionCue(A, ActionStyle.Reload), Cue(new ActivationEnded(A, ActivationEndReason.Reload)));
        Assert.Null(Cue(new ActivationEnded(A, ActivationEndReason.Voluntary)));
        Assert.False(new BattleEffectsTracker().Next(new ActivationEnded(A, ActivationEndReason.Voluntary), MatchState.None, null, 0).Any);
    }

    private static Choreography Plan(ActionCue cue, AnimationSpeed speed = AnimationSpeed.Full) =>
        Choreography.Of([new LogLine(5, "", null, Models: [cue.Model]) { Fx = BattleFx.None with { Action = cue } }],
            id => id == A ? new TablePoint(4, 4) : new TablePoint(5, 4), speed);

    [Theory]
    [InlineData(ActionStyle.Aim, BeatKind.Brace, Choreography.BraceSeconds)]
    [InlineData(ActionStyle.Reload, BeatKind.Brace, Choreography.BraceSeconds)]
    [InlineData(ActionStyle.Fortify, BeatKind.DigIn, Choreography.DigInSeconds)]
    [InlineData(ActionStyle.Rally, BeatKind.Rally, Choreography.RallySeconds)]
    public void AnActionOnItselfPlaysItsGestureWithItsLine(ActionStyle style, BeatKind kind, double seconds)
    {
        var plan = Plan(new ActionCue(A, style));
        var gesture = Assert.Single(plan.Beats, b => b.Kind == kind);
        Assert.Equal((A, 0.0, seconds), (gesture.Model, gesture.Start, gesture.Seconds));
        Assert.Equal(0, plan.Lines.Single().At);
        Assert.Equal(seconds, plan.Length, 9);
        Assert.True(gesture.PosesModel);
        // Digging in kicks up dust at its feet.
        Assert.Equal(style == ActionStyle.Fortify, plan.Beats.Any(b => b.Kind == BeatKind.Dust && b.At == new TablePoint(4, 4)));
        Assert.Empty(Plan(new ActionCue(A, style), AnimationSpeed.Off).Beats);
    }

    [Fact]
    public void TendingAFriendTurnsToItThenReaches()
    {
        var plan = Plan(new ActionCue(A, ActionStyle.Treat, B));
        Assert.Equal([BeatKind.Turn, BeatKind.Reach], plan.Beats.Select(b => b.Kind));
        var reach = plan.Beats[1];
        Assert.Equal((A, (ModelId?)B, Choreography.TurnSeconds), (reach.Model, reach.Other, reach.Start));
        Assert.Equal(Choreography.TurnSeconds, plan.Lines.Single().At, 9);
    }

    [Fact]
    public void AShooterRecoilsAsItFires()
    {
        var shot = new LogLine(1, "", null, Models: [A, B]) { Effects = [new(EffectKind.MuzzleFlash, A), new(EffectKind.HitPuff, B)] };
        var plan = Choreography.Of([shot], id => id == A ? new TablePoint(0, 0) : new TablePoint(6, 0));
        var flash = plan.Beats.Single(b => b.Kind == BeatKind.MuzzleFlash);
        var recoil = plan.Beats.Single(b => b.Kind == BeatKind.Recoil);
        Assert.Equal((A, (ModelId?)B, flash.Start, Choreography.RecoilSeconds), (recoil.Model, recoil.Other, recoil.Start, recoil.Seconds));
        // A grenade is thrown, not fired: no recoil.
        var thrown = shot with { Fx = new BattleFx(true, null, [], []) };
        Assert.DoesNotContain(Choreography.Of([thrown], id => id == A ? new TablePoint(0, 0) : new TablePoint(6, 0)).Beats, b => b.Kind == BeatKind.Recoil);
    }
}
