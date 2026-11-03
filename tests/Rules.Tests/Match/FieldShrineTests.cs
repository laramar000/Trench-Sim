using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;

namespace Rules.Tests.Match;

// Field Shrine, stage 1 (Battlekit p. 82; U127): deployed with its carrier, an Impassable base that is not a model, and
// worth 3 models (up to 9) in the Morale half test.
public class FieldShrineTests
{
    private static readonly ModelProfile Carrier = Rifleman with { FieldShrine = true };

    // Deployment up to the moment A1 (the carrier) is placed; its shrine decision is then pending.
    private static BoardPlay AtShrineDecision(int perSide = 2)
    {
        var play = new BoardPlay(perSide, id => id.Value == "A1" ? Carrier : Rifleman);
        play.Do(new StartMatch(), 6, 1);
        play.Do(new ChooseDeploymentZone(Seat.One, Rules.Core.Scenarios.ClaimNoMansLand.West), 5, 2);
        while (play.State.Pending is null)
        {
            Place(play);
        }
        return play;
    }

    // The next model to deploy is set up at its usual spot (A at x 2, B at x 34, 8" apart down the table).
    private static void Place(BoardPlay play)
    {
        var seat = TurnRules.NextDeployer(play.State)!.Value;
        var next = play.State.Models.First(m => m.Seat == seat && !m.Deployed);
        var index = int.Parse(next.Id.Value[1..]);
        var last = play.State.Models.Count(m => !m.Deployed) == 1;
        play.Do(new PlaceModel(seat, next.Id, P(seat == Seat.One ? "2" : "34", (3 + 8 * (index - 1)).ToString())), last ? [6, 1] : []);
    }

    private static void FinishDeployment(BoardPlay play)
    {
        while (play.State.Phase == Phase.Deployment)
        {
            if (play.State.Pending is { Kind: DecisionKind.PlaceShrine } waiting)
            {
                play.Do(new DeclineShrine(waiting.Seat, FieldShrines.CarrierWaiting(play.State, waiting.Seat)!.Id));
            }
            else
            {
                Place(play);
            }
        }
    }

    [Fact]
    [Trait("Rule", "Field Shrine p. 82: after the carrier is deployed its player may deploy the shrine, before the next model")]
    public void AShrineIsOfferedRightAfterItsCarrierIsDeployed()
    {
        var play = AtShrineDecision();
        Assert.Equal(new PendingDecision(DecisionKind.PlaceShrine, Seat.One), play.State.Pending);
        Assert.Equal(A(1), FieldShrines.CarrierWaiting(play.State, Seat.One)!.Id);
        play.Refused(new PlaceModel(Seat.One, A(2), P("2", "11")), ReasonCode.DecisionPending);
        var legal = TurnSequence.LegalCommands(play.State);
        Assert.Contains(new DeclineShrine(Seat.One, A(1)), legal);
        Assert.Contains(legal, c => c is PlaceShrine p && p.Carrier == A(1));
    }

    [Fact]
    [Trait("Rule", "Field Shrine p. 82: deployed wholly within the zone, clear of bases; only the carrier's player decides")]
    public void AShrineMustStandLegallyInItsOwnZone()
    {
        var play = AtShrineDecision();
        play.Refused(new PlaceShrine(Seat.One, A(1), P("10", "20")), ReasonCode.PlacementIllegal);   // outside the zone
        play.Refused(new PlaceShrine(Seat.One, A(1), P("5.5", "20")), ReasonCode.PlacementIllegal);  // base would cross the zone edge
        play.Refused(new PlaceShrine(Seat.One, A(1), play.M(A(1)).Position!.Value), ReasonCode.PlacementIllegal); // on A1's base
        play.Refused(new PlaceShrine(Seat.Two, A(1), P("3", "20")), ReasonCode.NotYourTurn);
        play.Refused(new PlaceShrine(Seat.One, A(2), P("3", "20")), ReasonCode.UnknownModel);
        play.Refused(new DeclineShrine(Seat.One, A(2)), ReasonCode.UnknownModel);

        var placed = play.Do(new PlaceShrine(Seat.One, A(1), P("3", "20"))).Events.OfType<ShrinePlaced>().Single();
        Assert.Equal((A(1), P("3", "20")), (placed.Carrier, placed.At));
        Assert.Null(play.State.Pending);
        Assert.Equal(new[] { new ShrineState(A(1), Seat.One, P("3", "20")) }, play.State.Shrines.ToArray());
        Assert.Equal(new[] { A(1) }, play.State.ShrineSettled.ToArray());
        play.Refused(new DeclineShrine(Seat.One, A(1)), ReasonCode.NoDecisionPending);
    }

    [Fact]
    [Trait("Rule", "Field Shrine p. 82: the player can also choose not to deploy it")]
    public void ADeclinedShrineIsNeverOfferedAgain()
    {
        var play = AtShrineDecision();
        Assert.Single(play.Do(new DeclineShrine(Seat.One, A(1))).Events.OfType<ShrineDeclined>());
        Assert.Empty(play.State.Shrines);
        Assert.Equal(new[] { A(1) }, play.State.ShrineSettled.ToArray());
        FinishDeployment(play);
        Assert.Equal(Phase.Initiative, play.State.Phase);
        Assert.Null(FieldShrines.CarrierWaiting(play.State, Seat.One));
    }

    [Fact]
    [Trait("Rule", "Field Shrine p. 82: an Impassable piece, nothing else may be set up on it")]
    public void NoModelMayBeSetUpOnAShrine()
    {
        var play = AtShrineDecision();
        play.Do(new PlaceShrine(Seat.One, A(1), P("3", "20")));
        var seat = TurnRules.NextDeployer(play.State)!.Value;
        var next = play.State.Models.First(m => m.Seat == seat && !m.Deployed);
        // The next deployer is set up at the shrine's spot when in its own zone, else nothing to test with: place seat One's.
        var mine = play.State.Models.First(m => m.Seat == Seat.One && !m.Deployed);
        while (TurnRules.NextDeployer(play.State) != Seat.One)
        {
            Place(play);
        }
        play.Refused(new PlaceModel(Seat.One, mine.Id, P("3", "20")), ReasonCode.PlacementIllegal);
        play.Refused(new PlaceModel(Seat.One, mine.Id, P("3", "20.5")), ReasonCode.PlacementIllegal);
        play.Do(new PlaceModel(Seat.One, mine.Id, P("3", "24")), play.State.Models.Count(m => !m.Deployed) == 1 ? [6, 1] : []);
        _ = next;
    }

    [Fact]
    [Trait("Rule", "Field Shrine p. 82: Impassable, so a model cannot cross it, only go round")]
    public void AShrineBlocksMovement()
    {
        var play = AtShrineDecision();
        play.Do(new PlaceShrine(Seat.One, A(1), P("4", "11")));
        FinishDeployment(play);
        var mover = play.M(A(2));                                   // at (2, 11)
        var through = Board.CheckMove(play.State, mover, [P("6", "11")]);
        Assert.False(through.IsLegal);
        Assert.Equal(MoveRefusal.CrossesPiece, through.Reason);
        Assert.True(Board.CheckMove(play.State, mover, [P("4", "13"), P("6", "11")]).IsLegal);   // round it
        Assert.False(Board.CheckMove(play.State, mover, [P("4", "11")]).IsLegal);                // not onto it
    }

    [Fact]
    [Trait("Rule", "Field Shrine p. 82 with Charging p. 37: a charge goes round the shrine")]
    public void AChargeRoutesRoundAShrine()
    {
        var play = new BoardPlay(2).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), "10", "2");
        play.State = play.State with { Shrines = [new ShrineState(A(2), Seat.One, P("7", "2"))] };
        play.Do(new ActivateModel(Seat.One, A(1)));
        var moved = play.Do(new ChargeModel(Seat.One, A(1), B(1)), 6).Events.OfType<ChargeMoved>().Single();
        Assert.True(moved.Reached);
        Assert.True(moved.Path.Length > 2);   // a straight charge would be one or two legs; this one bends round the piece
    }

    [Fact]
    [Trait("Rule", "Field Shrine p. 82: each shrine adds 3 to the models that are not Down or Out of Action, up to 9")]
    public void ShrinesRaiseTheMoraleThreshold()
    {
        var play = new BoardPlay(4);
        Assert.Equal(0, FieldShrines.MoraleBonus(play.State, Seat.One));
        ShrineState S(int n, Seat seat) => new(new ModelId($"C{n}"), seat, P((2 + n).ToString(), "20"));
        play.SetStatus(A(1), ModelStatus.Down).SetStatus(A(2), ModelStatus.Down);
        Assert.True(TurnRules.NeedsMoraleCheck(play.State, Seat.One));   // 2 of 4 down: half
        play.State = play.State with { Shrines = [S(1, Seat.One)] };
        Assert.Equal(3, FieldShrines.MoraleBonus(play.State, Seat.One));
        Assert.False(TurnRules.NeedsMoraleCheck(play.State, Seat.One));  // 2 of 4 + 3: needs 4 down
        play.SetStatus(A(3), ModelStatus.Down).SetStatus(A(4), ModelStatus.OutOfAction);
        Assert.True(TurnRules.NeedsMoraleCheck(play.State, Seat.One));   // all four
        // The other seat's shrines do not help this seat, and the bonus stops at 9.
        play.State = play.State with { Shrines = [S(1, Seat.Two), S(2, Seat.Two), S(3, Seat.Two), S(4, Seat.Two), S(5, Seat.Two)] };
        Assert.Equal((0, 9), (FieldShrines.MoraleBonus(play.State, Seat.One), FieldShrines.MoraleBonus(play.State, Seat.Two)));
        Assert.True(TurnRules.NeedsMoraleCheck(play.State, Seat.One));
    }

    [Fact]
    [Trait("Rule", "Field Shrine p. 82: a replay accepts only a legal shrine, and the state hash covers it")]
    public void ForgedShrinesAreRefusedAndHashed()
    {
        var play = AtShrineDecision();
        var pending = play.State;
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(pending, new ShrinePlaced(A(1), P("10", "20"))));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(pending, new ShrinePlaced(A(2), P("3", "20"))));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(play.State with { Pending = null }, new ShrinePlaced(A(1), P("3", "20"))));
        var placed = Reducer.Apply(pending, new ShrinePlaced(A(1), P("3", "20")));
        var declined = Reducer.Apply(pending, new ShrineDeclined(A(1)));
        Assert.NotEqual(StateHash.Compute(placed), StateHash.Compute(declined));
        Assert.NotEqual(StateHash.Compute(declined), StateHash.Compute(pending with { Pending = null }));
        // A shrine decision is not requested for a model without one.
        var plain = new BoardPlay(2);
        plain.Do(new StartMatch(), 6, 1);
        plain.Do(new ChooseDeploymentZone(Seat.One, Rules.Core.Scenarios.ClaimNoMansLand.West), 5, 2);
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(plain.State, new DecisionRequested(DecisionKind.PlaceShrine, Seat.One)));
    }
}
