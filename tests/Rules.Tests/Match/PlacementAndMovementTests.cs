using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.Scenarios;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;

namespace Rules.Tests.Match;

public class PlacementTests
{
    private static BoardPlay ReadyToDeploy()
    {
        var play = new BoardPlay();
        play.Do(new StartMatch(), 6, 1);
        play.Do(new ChooseDeploymentZone(Seat.One, "west"), 5, 2);
        return play;
    }

    [Fact]
    [Trait("Rule", "Scenario I p. 152: models are set up wholly within their own Deployment Zone")]
    public void AModelIsSetUpAtAPointInsideItsOwnZone()
    {
        var play = ReadyToDeploy();
        var result = play.Do(new PlaceModel(Seat.One, A(1), P("3", "10")));

        Assert.Equal(P("3", "10"), play.M(A(1)).Position);
        Assert.True(play.M(A(1)).Deployed);
        Assert.Contains(result.Events, e => e is ModelDeployed { Position: { } at } && at == P("3", "10"));
    }

    [Theory]
    [InlineData("7", "10")]   // beyond the 6" zone
    [InlineData("5.6", "10")] // base would stick out of the zone (radius 0.5)
    [InlineData("34", "10")]  // the opponent's zone
    [InlineData("-1", "10")]  // off the table
    [Trait("Rule", "Scenario I p. 152: wholly within the zone")]
    public void APlacementOutsideTheZoneIsRefusedWithoutChangingAnything(string x, string y)
    {
        var play = ReadyToDeploy();
        var rejection = play.Refused(new PlaceModel(Seat.One, A(1), P(x, y)), ReasonCode.PlacementIllegal);
        Assert.False(string.IsNullOrEmpty(rejection.RuleRef));
    }

    [Fact]
    [Trait("Rule", "Placement p. 31: bases cannot overlap")]
    public void AModelCannotBeSetUpOnAnotherBase()
    {
        var play = ReadyToDeploy();
        play.Do(new PlaceModel(Seat.One, A(1), P("3", "10")));
        play.Do(new PlaceModel(Seat.Two, B(1), P("34", "10")));
        play.Refused(new PlaceModel(Seat.One, A(2), P("3.5", "10")), ReasonCode.PlacementIllegal);
        play.Do(new PlaceModel(Seat.One, A(2), P("4", "10")));
    }

    [Fact]
    [Trait("Rule", "Scenario I p. 152: players alternate")]
    public void OnlyTheSeatWhoseTurnItIsMayPlaceAModel()
    {
        var play = ReadyToDeploy();
        play.Refused(new PlaceModel(Seat.Two, B(1), P("34", "10")), ReasonCode.NotYourTurn);
        play.Refused(new PlaceModel(Seat.One, B(1), P("3", "10")), ReasonCode.NotYourModel);
        play.Refused(new PlaceModel(Seat.One, new ModelId("Z9"), P("3", "10")), ReasonCode.UnknownModel);
    }

    [Fact]
    [Trait("Rule", "Architecture: a battlefield scenario places models by position")]
    public void DeployModelNeedsNoPositionOnlyWithoutABattlefield()
    {
        var play = ReadyToDeploy();
        play.Refused(new DeployModel(Seat.One, A(1)), ReasonCode.PositionRequired);

        var bare = new Play(Play.Setup(1, 1));
        bare.Do(new StartMatch(), 6, 1);
        bare.Do(new ChooseDeploymentZone(Seat.One, "north"), 5, 2);
        var result = bare.Try(new PlaceModel(Seat.One, Play.A(1), P("3", "10")));
        Assert.Equal(ReasonCode.NoBattlefield, result.Rejection!.Code);
    }

    [Fact]
    [Trait("Rule", "Architecture: delegates placement to Placement.Check")]
    public void TheDecisionAlwaysMatchesPlacementCheck()
    {
        var play = ReadyToDeploy();
        var field = play.Setup.Scenario!.Battlefield;
        var rng = new Random(11);
        for (var i = 0; i < 300; i++)
        {
            var at = new Point2(Length.FromThousandths(rng.Next(-2000, 40000)), Length.FromThousandths(rng.Next(-2000, 40000)));
            var expected = Placement.Check(field, new ModelBody("A1", at, L("0.5")), [], "west").IsLegal;
            var result = TurnSequence.Decide(play.State, new PlaceModel(Seat.One, A(1), at), new FixedDice());
            Assert.Equal(expected, result.IsAccepted);
        }
    }
}

public class MovementActionTests
{
    private static BoardPlay Active(Func<BoardPlay, BoardPlay>? arrange = null)
    {
        var play = new BoardPlay().ToActivation();
        arrange?.Invoke(play);
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    [Fact]
    [Trait("Rule", "Actions p. 34: Move up to Movement")]
    public void AMoveMovesTheActiveModelAndIsRecordedWithItsCost()
    {
        var play = Active();
        var result = play.Do(new MoveModel(Seat.One, A(1), Path(("6", "3"), ("8", "3"))));

        Assert.Equal(P("8", "3"), play.M(A(1)).Position);
        var moved = Assert.Single(result.Events.OfType<ModelMoved>());
        Assert.Equal(L("6"), moved.Cost);
        Assert.Equal(MoveKind.Move, moved.Kind);
        Assert.True(play.State.Actions.HasFlag(ActionsTaken.Move));
    }

    [Fact]
    [Trait("Rule", "Actions p. 34: each type of ACTION once per Activation")]
    public void AModelMovesOnlyOncePerActivation()
    {
        var play = Active();
        play.Do(new MoveModel(Seat.One, A(1), Path(("4", "3"))));
        play.Refused(new MoveModel(Seat.One, A(1), Path(("5", "3"))), ReasonCode.ActionAlreadyTaken);
    }

    [Fact]
    [Trait("Rule", "Movement p. 35: path no longer than Movement")]
    public void AMoveBeyondMovementIsRefused()
    {
        var play = Active();
        var rejection = play.Refused(new MoveModel(Seat.One, A(1), Path(("8.001", "3"))), ReasonCode.MoveIllegal);
        Assert.Contains("1.0.2", rejection.RuleRef);
        play.Do(new MoveModel(Seat.One, A(1), Path(("8", "3"))));
    }

    [Fact]
    [Trait("Rule", "Movement p. 35: blocking terrain")]
    public void AMoveThroughAWallIsRefused()
    {
        var play = Active(p => p.Put(A(1), "17", "9.5")); // wall-1 is the strip y 11 to 12
        play.Refused(new MoveModel(Seat.One, A(1), Path(("17", "13"))), ReasonCode.MoveIllegal);
    }

    [Fact]
    [Trait("Rule", "Movement p. 35: a Move cannot end within 1\" of an enemy")]
    public void AMoveToWithinAnInchOfAnEnemyIsRefused()
    {
        var play = Active(p => p.Put(A(1), "20", "3").Put(B(1), "24", "3"));
        play.Refused(new MoveModel(Seat.One, A(1), Path(("22.6", "3"))), ReasonCode.MoveIllegal);
        play.Do(new MoveModel(Seat.One, A(1), Path(("20.9", "3"))));
    }

    [Fact]
    [Trait("Rule", "Architecture: bounded command size")]
    public void MoreThanSixteenWaypointsAreRefusedBeforeAnyGeometryIsRun()
    {
        var play = Active();
        var points = Enumerable.Range(0, 17).Select(i => (i % 2 == 0 ? "3" : "3.1", "3")).ToArray();
        play.Refused(new MoveModel(Seat.One, A(1), Path(points)), ReasonCode.TooManyWaypoints);
        play.Refused(new MoveModel(Seat.One, A(1), []), ReasonCode.MoveIllegal);
        play.Refused(new MoveModel(Seat.One, A(1), default), ReasonCode.MoveIllegal);
    }

    [Fact]
    [Trait("Rule", "Rough ground p. 35 keyword: Difficult terrain costs double")]
    public void DifficultGroundCostsDoubleAndThePathIsRefusedWhenItIsTooLong()
    {
        var play = Active(p => p.Put(A(1), "5", "18")); // rough-1 is x 7 to 11, y 14 to 22
        play.Refused(new MoveModel(Seat.One, A(1), Path(("11.5", "18"))), ReasonCode.MoveIllegal); // 2" + 4"x2 + 0.5" = 10.5
        var result = play.Do(new MoveModel(Seat.One, A(1), Path(("8", "18"))));                    // 2" + 1"x2 = 4
        Assert.Equal(L("4"), result.Events.OfType<ModelMoved>().Single().Cost);
    }

    [Fact]
    [Trait("Rule", "Actions p. 34: only the active model acts")]
    public void OnlyTheActiveModelOfTheRightPlayerMayAct()
    {
        var play = Active();
        play.Refused(new MoveModel(Seat.One, A(2), Path(("4", "11"))), ReasonCode.ActivationInProgress);
        play.Refused(new MoveModel(Seat.Two, A(1), Path(("4", "3"))), ReasonCode.NotYourModel);
        play.Refused(new MoveModel(Seat.Two, B(1), Path(("30", "3"))), ReasonCode.ActivationInProgress);
        play.Refused(new MoveModel(Seat.One, new ModelId("Z9"), Path(("4", "3"))), ReasonCode.UnknownModel);
        play.Do(new EndActivation(Seat.One));
        play.Refused(new MoveModel(Seat.One, A(1), Path(("4", "3"))), ReasonCode.NoActivationInProgress);
    }

    [Fact]
    [Trait("Rule", "Architecture: movement decisions delegate to Movement.CheckPath")]
    public void TheDecisionAlwaysMatchesMovementCheckPath()
    {
        var play = Active(p => p.Put(A(1), "15", "15").Put(B(1), "20", "15").Put(A(2), "12", "17"));
        var field = play.Setup.Scenario!.Battlefield;
        var rng = new Random(5);
        for (var i = 0; i < 400; i++)
        {
            var path = Enumerable.Range(0, rng.Next(1, 4))
                .Select(_ => new Point2(Length.FromThousandths(rng.Next(8000, 24000)), Length.FromThousandths(rng.Next(8000, 24000)))).ToArray();
            var obstacles = new[] { new Obstacle(new ModelBody("B1", P("20", "15"), L("0.5")), Relation.Enemy), new Obstacle(new ModelBody("A2", P("12", "17"), L("0.5")), Relation.Friendly) };
            var expected = Movement.CheckPath(field, new ModelBody("A1", P("15", "15"), L("0.5")), path, L("6"), obstacles);
            var result = TurnSequence.Decide(play.State, new MoveModel(Seat.One, A(1), [.. path]), new FixedDice());
            Assert.Equal(expected.IsLegal, result.IsAccepted);
            if (result.IsAccepted)
            {
                Assert.Equal(expected.Cost, result.Events.OfType<ModelMoved>().Single().Cost);
            }
        }
    }

    // ---- Dash ----

    [Fact]
    [Trait("Rule", "Actions p. 34: a Dash is a Risky Success Roll, then a move")]
    public void ASuccessfulDashMovesTheModel()
    {
        var play = Active();
        var result = play.Do(new DashModel(Seat.One, A(1), Path(("8", "3"))), 4, 3);

        Assert.Equal(P("8", "3"), play.M(A(1)).Position);
        var roll = Assert.Single(result.Events.OfType<DashRolled>());
        Assert.True(roll.Check.Risky);
        Assert.Contains(result.Events, e => e is ModelMoved { Kind: MoveKind.Dash });
        Assert.Equal(A(1), play.State.ActiveModel);
    }

    [Fact]
    [Trait("Rule", "Actions p. 34: if you fail, the model's Activation ends")]
    public void AFailedDashEndsTheActivationWithoutMoving()
    {
        var play = Active();
        var result = play.Do(new DashModel(Seat.One, A(1), Path(("8", "3"))), 3, 3);

        Assert.Equal(P("2", "3"), play.M(A(1)).Position);
        Assert.Null(play.State.ActiveModel);
        Assert.Contains(result.Events, e => e is ActivationEnded { Reason: ActivationEndReason.RiskyFailure });
        Assert.DoesNotContain(result.Events, e => e is ModelMoved);
    }

    [Fact]
    [Trait("Rule", "Actions p. 34: a Dash may come before or after a Move")]
    public void ADashAndAMoveCombineInEitherOrderButEachOnlyOnce()
    {
        var play = Active();
        play.Do(new DashModel(Seat.One, A(1), Path(("8", "3"))), 4, 3);
        play.Do(new MoveModel(Seat.One, A(1), Path(("14", "3"))));
        Assert.Equal(P("14", "3"), play.M(A(1)).Position);
        play.Refused(new DashModel(Seat.One, A(1), Path(("15", "3"))), ReasonCode.ActionAlreadyTaken);

        var second = Active();
        second.Do(new MoveModel(Seat.One, A(1), Path(("8", "3"))));
        second.Do(new DashModel(Seat.One, A(1), Path(("14", "3"))), 5, 5);
        Assert.Equal(P("14", "3"), second.M(A(1)).Position);
        second.Refused(new MoveModel(Seat.One, A(1), Path(("15", "3"))), ReasonCode.ActionAlreadyTaken);
    }

    [Fact]
    [Trait("Rule", "Architecture: a rejected command rolls no dice")]
    public void AnIllegalDashPathIsRefusedBeforeTheRiskyRollIsMade()
    {
        var play = Active();
        play.Refused(new DashModel(Seat.One, A(1), Path(("40", "3"))), ReasonCode.MoveIllegal);
    }

    [Fact]
    [Trait("Rule", "Blood Markers p. 28: the opponent may spend them as -1 DICE on a Success Roll")]
    public void TheOpponentDecidesBeforeADashRollOfAModelWithMarkers()
    {
        var play = Active(p => p.Mark(A(1), 3));
        play.Do(new DashModel(Seat.One, A(1), Path(("8", "3")))); // no dice: the decision comes first
        Assert.Equal(new PendingDecision(DecisionKind.SpendMarkersOnSuccessRoll, Seat.Two), play.State.Pending);
        play.Refused(new SpendMarkers(Seat.One, 1), ReasonCode.NotYourTurn);
        play.Refused(new SpendMarkers(Seat.Two, 4), ReasonCode.InvalidMarkerCount);
        play.Refused(new SpendMarkers(Seat.Two, -1), ReasonCode.InvalidMarkerCount);
        play.Refused(new EndActivation(Seat.One), ReasonCode.DecisionPending);

        // Spending 2 markers makes it 2 penalty dice: 4 dice rolled, the lowest two kept (3 and 1 -> failure).
        var result = play.Do(new SpendMarkers(Seat.Two, 2), 6, 3, 1, 5);
        var roll = result.Events.OfType<DashRolled>().Single();
        Assert.Equal(-2, roll.Check.NetDice);
        Assert.Equal(4, roll.Check.Dice.Total);
        Assert.Equal(1, play.M(A(1)).BloodMarkers);
        Assert.Equal(P("2", "3"), play.M(A(1)).Position);
        Assert.Null(play.State.ActiveModel);
    }

    // ---- Down models ----

    [Fact]
    [Trait("Rule", "Down Results Core p. 20 / p. 49: stands up when next activated, Movement halved")]
    public void ADownModelStandsUpWhenActivatedAndMovesAtHalfMovement()
    {
        var play = new BoardPlay().ToActivation().SetStatus(A(1), ModelStatus.Down);
        var result = play.Do(new ActivateModel(Seat.One, A(1)));

        Assert.Contains(result.Events, e => e is ModelStoodUp);
        Assert.Equal(ModelStatus.Standing, play.M(A(1)).Status);
        play.Refused(new MoveModel(Seat.One, A(1), Path(("5.001", "3"))), ReasonCode.MoveIllegal);
        play.Do(new MoveModel(Seat.One, A(1), Path(("5", "3"))));
    }

    [Fact]
    [Trait("Rule", "Down Results p. 49: halved for the rest of the activation, including a Dash")]
    public void TheHalvedMovementAlsoLimitsADash()
    {
        var play = new BoardPlay().ToActivation().SetStatus(A(1), ModelStatus.Down);
        play.Do(new ActivateModel(Seat.One, A(1)));
        play.Refused(new DashModel(Seat.One, A(1), Path(("6", "3"))), ReasonCode.MoveIllegal);
        play.Do(new DashModel(Seat.One, A(1), Path(("5", "3"))), 6, 6);
        play.Do(new EndActivation(Seat.One));
        play.Do(new ActivateModel(Seat.Two, B(1)));
        play.Do(new EndActivation(Seat.Two));
        play.Do(new ActivateModel(Seat.One, A(2)));
        Assert.False(play.State.MovementHalved);
    }
}
