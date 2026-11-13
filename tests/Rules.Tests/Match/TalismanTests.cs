using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;
using static Rules.Tests.Match.MeleePlay;

namespace Rules.Tests.Match;

// Roster stage R5k (U116): a Talisman (a Blessed Icon or an Unholy Trinket, core rulebook pp. 81, 86). Once per game,
// when a Risky Success Roll of the model is a Failure, its player can say it uses the Talisman; then the activation does
// not end.
public class TalismanTests
{
    private static readonly ModelProfile Carrier = Rifleman with { Talismans = 1 };

    // A1 at (2, 3) and activated, A2 at (2, 11).
    private static BoardPlay Active(ModelProfile a1)
    {
        var play = new BoardPlay(2, id => id == A(1) ? a1 : Rifleman).ToActivation();
        play.Put(A(2), "2", "11");
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    private static DashModel Dash => new(Seat.One, A(1), Path(("8", "3")));

    [Fact]
    [Trait("Rule", "Talisman pp. 81, 86: after a failed Risky roll the player may use it, and the activation does not end")]
    public void UsingATalismanKeepsTheActivationGoing()
    {
        var play = Active(Carrier);
        var failed = play.Do(Dash, 3, 3);
        Assert.Equal(new DecisionRequested(DecisionKind.UseTalisman, Seat.One), failed.Events[^1]);
        Assert.DoesNotContain(failed.Events, e => e is ActivationEnded);
        Assert.Equal((new PendingDecision(DecisionKind.UseTalisman, Seat.One), true), (play.State.Pending, play.State.MustEndActivation));
        Assert.Equal([new UseTalisman(Seat.One), new DeclineTalisman(Seat.One)], TurnSequence.LegalCommands(play.State));

        var used = play.Do(new UseTalisman(Seat.One));
        Assert.Equal<GameEvent>([new TalismanUsed(A(1))], used.Events);
        Assert.Equal((A(1), false, (PendingDecision?)null, 1), (play.State.ActiveModel, play.State.MustEndActivation, play.State.Pending, play.M(A(1)).TalismansUsed));
        // The Dash is spent; a Move is still allowed.
        play.Refused(Dash, ReasonCode.ActionAlreadyTaken);
        play.Do(new MoveModel(Seat.One, A(1), Path(("6", "3"))));
        Assert.Equal(P("6", "3"), play.M(A(1)).Position);
    }

    [Fact]
    [Trait("Rule", "Talisman: the player may keep it, and then the activation ends")]
    public void DecliningLetsTheActivationEnd()
    {
        var play = Active(Carrier);
        play.Do(Dash, 3, 3);
        var kept = play.Do(new DeclineTalisman(Seat.One));
        Assert.Equal<GameEvent>([new ActivationEnded(A(1), ActivationEndReason.RiskyFailure)], kept.Events);
        Assert.Equal((null, false, (PendingDecision?)null, 0), ((ModelId?)play.State.ActiveModel, play.State.MustEndActivation, play.State.Pending, play.M(A(1)).TalismansUsed));
    }

    [Fact]
    [Trait("Rule", "Talisman: once per game; a model with two uses each once; without one the activation ends at once")]
    public void EachTalismanIsUsedOnce()
    {
        var spent = Active(Carrier);
        spent.State = spent.State with { Models = [.. spent.State.Models.Select(m => m.Id == A(1) ? m with { TalismansUsed = 1 } : m)] };
        Assert.False(TurnSequence.TalismanReady(spent.State, A(1)));
        Assert.Equal(new ActivationEnded(A(1), ActivationEndReason.RiskyFailure), spent.Do(Dash, 3, 3).Events[^1]);

        var two = Active(Rifleman with { Talismans = 2 });
        two.State = two.State with { Models = [.. two.State.Models.Select(m => m.Id == A(1) ? m with { TalismansUsed = 1 } : m)] };
        Assert.True(TurnSequence.TalismanReady(two.State, A(1)));
        two.Do(Dash, 3, 3);
        two.Do(new UseTalisman(Seat.One));
        Assert.Equal(2, two.M(A(1)).TalismansUsed);

        var plain = Active(Rifleman);
        Assert.Equal(new ActivationEnded(A(1), ActivationEndReason.RiskyFailure), plain.Do(Dash, 3, 3).Events[^1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => Rifleman with { Talismans = 3 });
        Assert.Throws<ArgumentOutOfRangeException>(() => Rifleman with { Talismans = -1 });
    }

    [Fact]
    [Trait("Rule", "Talisman: a model with a Crucifix keeps going anyway and is not asked")]
    public void ACrucifixNeedsNoTalisman()
    {
        var play = Active(Carrier with { KeepsGoingAfterRisky = true });
        var failed = play.Do(Dash, 3, 3);
        Assert.DoesNotContain(failed.Events, e => e is DecisionRequested or ActivationEnded);
        Assert.Equal((A(1), 0), (play.State.ActiveModel, play.M(A(1)).TalismansUsed));
    }

    [Fact]
    [Trait("Rule", "Talisman: a failed RISKY attack; RELOAD still ends the activation after the Talisman is used")]
    public void ATalismanAfterAFailedRiskyAttack()
    {
        var gun = Carrier with { RangedWeaponKeywords = WeaponKeywords.Risky };
        var shot = Skirmish("4", "2", "12", "2", gun);
        var missed = shot.Do(new Shoot(Seat.One, A(1), B(1)), 2, 2);
        Assert.True(missed.Events.OfType<RangedSuccessRolled>().Single().Check.RiskyFailure);
        Assert.Equal(new DecisionRequested(DecisionKind.UseTalisman, Seat.One), missed.Events[^1]);
        Assert.Equal<GameEvent>([new TalismanUsed(A(1))], shot.Do(new UseTalisman(Seat.One)).Events);
        Assert.Equal(A(1), shot.State.ActiveModel);

        var reload = Skirmish("4", "2", "12", "2", gun with { RangedWeaponKeywords = WeaponKeywords.Risky | WeaponKeywords.Reload });
        reload.Do(new Shoot(Seat.One, A(1), B(1)), 2, 2);
        Assert.Equal<GameEvent>([new TalismanUsed(A(1)), new ActivationEnded(A(1), ActivationEndReason.Reload)], reload.Do(new UseTalisman(Seat.One)).Events);
        // Declining ends it for the failed roll.
        var declined = Skirmish("4", "2", "12", "2", gun with { RangedWeaponKeywords = WeaponKeywords.Risky | WeaponKeywords.Reload });
        declined.Do(new Shoot(Seat.One, A(1), B(1)), 2, 2);
        Assert.Equal<GameEvent>([new ActivationEnded(A(1), ActivationEndReason.RiskyFailure)], declined.Do(new DeclineTalisman(Seat.One)).Events);

        var blade = Brawler with { Talismans = 1, MeleeWeaponKeywords = WeaponKeywords.Risky };
        var fight = Skirmish("4", "2", "5.1", "2", blade);
        var swung = fight.Do(new FightModel(Seat.One, A(1), B(1)), 1, 1);
        Assert.Equal(new DecisionRequested(DecisionKind.UseTalisman, Seat.One), swung.Events[^1]);
        fight.Do(new UseTalisman(Seat.One));
        Assert.Equal(A(1), fight.State.ActiveModel);
    }

    [Fact]
    [Trait("Rule", "Talisman: only its own player answers, only when asked; a log that skips the question was edited")]
    public void RefusalsAndForgeries()
    {
        var idle = Active(Carrier);
        idle.Refused(new UseTalisman(Seat.One), ReasonCode.NoDecisionPending);
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(idle.State, new TalismanUsed(A(1))));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(idle.State, new DecisionRequested(DecisionKind.UseTalisman, Seat.One)));

        var play = Active(Carrier);
        play.Do(Dash, 3, 3);
        play.Refused(new UseTalisman(Seat.Two), ReasonCode.NotYourTurn);
        play.Refused(new DeclineTalisman(Seat.Two), ReasonCode.NotYourTurn);
        play.Refused(new EndActivation(Seat.One), ReasonCode.DecisionPending);
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(play.State, new TalismanUsed(A(2))));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(play.State, new ActivationEnded(A(1), ActivationEndReason.Voluntary)));

        // Right after the failure the question has to come first.
        var asked = Active(Carrier);
        var before = asked.State;
        var failed = asked.Do(Dash, 3, 3);
        before = failed.Events.Take(failed.Events.Length - 1).Aggregate(before, Reducer.Apply);
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(before, new ActivationEnded(A(1), ActivationEndReason.RiskyFailure)));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(before, new DecisionRequested(DecisionKind.UseTalisman, Seat.Two)));
    }

    [Fact]
    [Trait("Rule", "Talisman: the canonical hash shows Talismans and their uses only when present; random games replay")]
    public void HashAndSimulatedGames()
    {
        var plain = StateHash.Canonical(Skirmish("4", "2", "12", "2").State);
        Assert.DoesNotContain("talisman", plain, StringComparison.Ordinal);
        var play = Active(Carrier);
        play.Do(Dash, 3, 3);
        play.Do(new UseTalisman(Seat.One));
        var hashed = StateHash.Canonical(play.State);
        Assert.Contains("talismans=1", hashed, StringComparison.Ordinal);
        Assert.Contains("talismansUsed=1", hashed, StringComparison.Ordinal);

        var (used, asked) = (0, 0);
        foreach (var path in new[] { Sim.Content.DefaultPath(), Sim.Content.MeleePath() })
        {
            var warbands = Sim.Content.Load(path).Select(w => w with
            {
                Models = w.Models.Select((m, i) => m with { Profile = m.Profile with { Talismans = i % 3 } }).ToList(),
            }).ToList();
            for (var seed = 1; seed <= 8; seed++)
            {
                var run = Sim.Runner.Run(seed, warbands);
                Assert.True(run.Terminal, run.Status);
                Assert.Equal(run.FinalHash, StateHash.Compute(Reducer.Replay(run.Log)));
                used += run.Log.OfType<TalismanUsed>().Count();
                asked += run.Log.OfType<DecisionRequested>().Count(d => d.Kind == DecisionKind.UseTalisman);
            }
        }
        Assert.True(used > 0 && asked >= used, $"{asked} asked, {used} used");
    }
}
