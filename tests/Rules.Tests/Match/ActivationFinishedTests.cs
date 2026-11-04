using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;
using Sim;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;
using static Rules.Tests.Match.MeleePlay;

namespace Rules.Tests.Match;

// Battlefield UI 2, stage N2: Activation.Finished, the read-only door the client asks before it ends an activation by
// itself. It is true only when the rules' own checks leave the activating model nothing but EndActivation, and never
// while a roll, a decision or free attacks are waiting.
public class ActivationFinishedTests
{
    private static ChargeModel Charge() => new(Seat.One, A(1), B(1));

    // Movement 6", 1" base, no weapon of any kind.
    private static readonly ModelProfile Unarmed = new(L("6"), L("0.5"), Length.Zero);

    private static void AssertOnlyEndIsLeft(BoardPlay play)
    {
        Assert.True(Activation.Finished(play.State));
        Assert.Equal([new EndActivation(Seat.One)], TurnSequence.LegalCommands(play.State));
        Assert.Null(TurnSequence.Check(play.State, new EndActivation(Seat.One)));
    }

    [Fact]
    public void AFreshActivationIsNotFinished()
    {
        var play = Skirmish("4", "2", "30", "2");
        Assert.False(Activation.Finished(play.State));
    }

    [Fact]
    [Trait("Rule", "Actions p. 34: after a Shoot the model may still Move and Dash")]
    public void AfterAShotWithAMoveLeftItIsNotFinishedUntilEveryActionIsSpent()
    {
        var play = Skirmish("4", "2", "12", "2", Gunman);
        play.Put(B(2), "30", "30");
        play.Do(new Shoot(Seat.One, A(1), B(1)), 1, 1);
        Assert.False(Activation.Finished(play.State));
        play.Do(new MoveModel(Seat.One, A(1), Path(("5", "2"))));
        Assert.False(Activation.Finished(play.State)); // a Dash is left
        play.Do(new DashModel(Seat.One, A(1), Path(("6", "2"))), 6, 5);
        AssertOnlyEndIsLeft(play);
    }

    [Fact]
    [Trait("Rule", "Actions p. 34: a failed Dash ends the activation by itself")]
    public void AFailedDashEndsTheActivationInTheRulesNotByFinished()
    {
        var play = Skirmish("4", "2", "30", "2");
        play.Do(new DashModel(Seat.One, A(1), Path(("8", "2"))), 3, 3);
        Assert.Null(play.State.ActiveModel);
        Assert.False(Activation.Finished(play.State));
    }

    [Fact]
    [Trait("Rule", "Actions p. 34, Melee pp. 45-46: after a charge the model may still Fight")]
    public void AfterACharge_AModelWithAMeleeWeaponStillHasItsFight()
    {
        var play = Skirmish("4", "2", "16", "2");
        play.Put(B(2), "30", "30");
        play.Do(new DashModel(Seat.One, A(1), Path(("6", "2"))), 6, 5);
        Assert.False(Activation.Finished(play.State));
        Assert.True(play.Do(Charge(), 3).Events.OfType<ChargeMoved>().Single().Reached);
        Assert.False(Activation.Finished(play.State)); // the Fight
        play.Do(new FightModel(Seat.One, A(1), B(1)), 3, 3); // a miss
        AssertOnlyEndIsLeft(play);
    }

    [Fact]
    [Trait("Rule", "Melee pp. 45-46: a model with no Melee Weapon cannot Fight after its charge")]
    public void AfterACharge_AModelWithNoMeleeWeaponIsFinished()
    {
        var play = Skirmish("4", "2", "16", "2", Unarmed);
        play.Put(B(2), "30", "30");
        play.Do(new DashModel(Seat.One, A(1), Path(("6", "2"))), 6, 5);
        Assert.True(play.Do(Charge(), 3).Events.OfType<ChargeMoved>().Single().Reached);
        AssertOnlyEndIsLeft(play);
    }

    [Fact]
    [Trait("Rule", "Down Results p. 49: a model taken Down during its own activation")]
    public void AModelDownOrOutOfActionInItsOwnActivationHasNothingLeft()
    {
        // The rules end the activation themselves when a free attack takes the retreating model down.
        var retreat = Skirmish("4", "2", "5.4", "2");
        retreat.Put(B(2), "30", "30");
        retreat.Do(new RetreatModel(Seat.One, A(1), Path(("1.5", "2"))));
        retreat.Do(new FreeFight(Seat.Two, B(1)), 4, 3, 4, 3);
        Assert.Null(retreat.State.ActiveModel);
        Assert.False(Activation.Finished(retreat.State));

        // A state made by hand, with the activating model Down or Out of Action: only EndActivation is left.
        foreach (var status in new[] { ModelStatus.Down, ModelStatus.OutOfAction })
        {
            var play = Skirmish("4", "2", "30", "2");
            play.SetStatus(A(1), status);
            AssertOnlyEndIsLeft(play);
        }
    }

    [Fact]
    [Trait("Rule", "Retreating p. 37: free attacks come before the move")]
    public void ARetreatWithFreeAttacksWaitingIsNotFinished()
    {
        var play = Skirmish("4", "2", "5.4", "2");
        play.Put(B(2), "30", "30");
        play.Do(new DashModel(Seat.One, A(1), Path(("4", "2.2"))), 6, 5); // stays engaged
        play.Do(new RetreatModel(Seat.One, A(1), Path(("1.5", "2.2"))));
        Assert.NotNull(play.State.Retreat);
        Assert.NotNull(play.State.Pending);
        Assert.False(Activation.Finished(play.State));
        play.Do(new DeclineFreeFights(Seat.Two));
        // Dash and Retreat spent, Move excluded by the Retreat, no Melee Weapon target within 1" and no ranged weapon.
        AssertOnlyEndIsLeft(play);
    }

    [Fact]
    [Trait("Rule", "Activation p. 33: the last model of the turn")]
    public void TheLastModelOfTheTurnIsFinishedAndItsEndIsAccepted()
    {
        var play = new BoardPlay(1, Everyone(Brawler)).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), "30", "30");
        play.Do(new ActivateModel(Seat.One, A(1)));
        play.Do(new MoveModel(Seat.One, A(1), Path(("8", "2"))));
        Assert.False(Activation.Finished(play.State));
        play.Do(new DashModel(Seat.One, A(1), Path(("12", "2"))), 6, 5);
        AssertOnlyEndIsLeft(play);
        var ended = play.Do(new EndActivation(Seat.One));
        Assert.Contains(ended.Events, e => e is ActivationEnded { Reason: ActivationEndReason.Voluntary });
        Assert.False(Activation.Finished(play.State));
    }

    // Over every state of simulated ranged and melee matches: whenever Finished says true, every action the rules know
    // (moves in many directions and lengths, and every attack at every model, and whatever the bot would try) is
    // refused, and EndActivation is accepted; it is never true while a roll, a decision or free attacks are waiting.
    [Fact]
    public void WhenFinishedEveryActionIsRefusedInSimulatedMatches()
    {
        var (finished, open) = (0, 0);
        foreach (var path in new[] { Content.DefaultPath(), Content.MeleePath() })
        {
            var warbands = Content.Load(path);
            for (var seed = 0; seed < 6; seed++)
            {
                var one = warbands[seed % 2];
                var two = warbands[(seed + 1) % 2];
                var models = one.Models.Select((m, i) => new SetupModel(new ModelId($"a{i + 1}"), Seat.One, m.Profile))
                    .Concat(two.Models.Select((m, i) => new SetupModel(new ModelId($"b{i + 1}"), Seat.Two, m.Profile)));
                var state = Reducer.Apply(MatchState.None, new MatchCreated(Rules.Core.Scenarios.ClaimNoMansLand.CreateSetup(models)));
                var bot = new Bot(new Random(seed));
                var play = new SeededDice(seed * 31 + 5);
                for (var step = 0; step < 3000 && state.Phase != Phase.Ended; step++)
                {
                    if (Activation.Finished(state))
                    {
                        finished++;
                        Assert.Null(state.Pending);
                        Assert.Null(state.InProgress);
                        Assert.Null(state.Retreat);
                        var seat = state.FindModel(state.ActiveModel!.Value)!.Seat;
                        Assert.Null(TurnSequence.Check(state, new EndActivation(seat)));
                        foreach (var command in Tries(state).Concat(bot.Propose(state)).Where(c => c is not EndActivation))
                        {
                            Assert.NotNull(TurnSequence.Check(state, command));
                        }
                    }
                    else if (state.ActiveModel is not null && state.Pending is null && state.InProgress is null)
                    {
                        open++;
                    }
                    var next = bot.Propose(state).Select(c => TurnSequence.Decide(state, c, play)).FirstOrDefault(r => r.IsAccepted);
                    Assert.NotNull(next);
                    state = next.State!;
                }
            }
        }
        Assert.True(finished > 5 && open > 100, $"{finished} finished, {open} not");
    }

    private static IEnumerable<Command> Tries(MatchState state)
    {
        var active = state.ActiveModel!.Value;
        var actor = state.FindModel(active)!;
        foreach (var other in state.Models)
        {
            yield return new Shoot(actor.Seat, active, other.Id);
            yield return new ChargeModel(actor.Seat, active, other.Id);
            yield return new FightModel(actor.Seat, active, other.Id);
        }
        if (actor.Position is not { } at)
        {
            yield break;
        }
        foreach (var inches in new[] { 0.5, 1, 2, 4, 6 })
        {
            for (var turn = 0; turn < 8; turn++)
            {
                var angle = turn * Math.PI / 4;
                var to = new Point2(at.X + Length.FromThousandths((long)Math.Round(inches * 1000 * Math.Cos(angle))), at.Y + Length.FromThousandths((long)Math.Round(inches * 1000 * Math.Sin(angle))));
                yield return new MoveModel(actor.Seat, active, [to]);
                yield return new DashModel(actor.Seat, active, [to]);
                yield return new RetreatModel(actor.Seat, active, [to]);
            }
        }
    }
}
