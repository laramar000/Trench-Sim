using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;

namespace Rules.Tests.Match;

// Roster stage R8d (U133): an oval base in a match: chosen at set-up, turned in place by the active model, measured as a capsule.
public class OvalPlayTests
{
    // 1" wide and 2" long.
    private static readonly ModelProfile Oval = Rifleman with { BaseHalfSpan = L("0.5") };

    private static BoardPlay Active(Func<ModelId, ModelProfile>? profiles = null)
    {
        var play = new BoardPlay(2, profiles ?? (id => id == A(1) ? Oval : Rifleman)).ToActivation();
        play.Put(A(1), "10", "10").Put(A(2), "2", "30").Put(B(1), "30", "10").Put(B(2), "30", "30");
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    [Fact]
    public void AnOvalIsSetUpAlongTheAxisItsPlayerChooses()
    {
        var play = new BoardPlay(2, id => id == A(1) ? Oval : Rifleman);
        play.Do(new StartMatch(), 6, 1);
        play.Do(new ChooseDeploymentZone(Seat.One, Rules.Core.Scenarios.ClaimNoMansLand.West), 5, 2);
        // The west zone ends at x 6 (a 1" by 2" base lying along X needs x + 1 <= 6).
        play.Refused(new PlaceModel(Seat.One, A(1), P("5.5", "3")) { Axis = BaseAxis.X }, ReasonCode.PlacementIllegal);
        var placed = play.Do(new PlaceModel(Seat.One, A(1), P("5.5", "3")) { Axis = BaseAxis.Y });
        Assert.Equal(new ModelDeployed(Seat.One, A(1), P("5.5", "3")), placed.Events[0]);
        Assert.Equal(BaseAxis.Y, play.M(A(1)).Axis);
        play.Do(new PlaceModel(Seat.Two, B(1), P("34", "3")));
        // Seat One's next model is a round one: it has no axis to choose.
        play.Refused(new PlaceModel(Seat.One, A(2), P("2", "11")) { Axis = BaseAxis.X }, ReasonCode.InvalidChoice);
        play.Refused(new PlaceModel(Seat.One, A(2), P("2", "11")) { Axis = (BaseAxis)5 }, ReasonCode.InvalidChoice);
        play.Do(new PlaceModel(Seat.One, A(2), P("2", "11")));
    }

    [Fact]
    public void AnOvalCanBeSetUpLyingAlongXWhenThereIsRoom()
    {
        var play = new BoardPlay(2, id => id == A(1) ? Oval : Rifleman);
        play.Do(new StartMatch(), 6, 1);
        play.Do(new ChooseDeploymentZone(Seat.One, Rules.Core.Scenarios.ClaimNoMansLand.West), 5, 2);
        var placed = play.Do(new PlaceModel(Seat.One, A(1), P("4", "3")) { Axis = BaseAxis.X });
        Assert.Equal(new ModelDeployed(Seat.One, A(1), P("4", "3")) { Axis = BaseAxis.X }, placed.Events[0]);
        Assert.Equal(BaseAxis.X, play.M(A(1)).Axis);
    }

    [Fact]
    public void TheActiveModelTurnsItsBaseInPlace()
    {
        var play = Active();
        Assert.Equal(BaseAxis.Y, play.M(A(1)).Axis);
        var turned = play.Do(new TurnBase(Seat.One, A(1), BaseAxis.X));
        Assert.Equal<GameEvent>([new BaseTurned(A(1), BaseAxis.X)], turned.Events);
        Assert.Equal(BaseAxis.X, play.M(A(1)).Axis);
        // It is free: the model still has its ACTIONS, and can turn back.
        play.Do(new TurnBase(Seat.One, A(1), BaseAxis.Y));
        play.Do(new MoveModel(Seat.One, A(1), Path(("12", "10"))));
        Assert.Equal(P("12", "10"), play.M(A(1)).Position);
        // The body the engine measures follows the axis.
        Assert.Equal(L("1.0"), Board.BodyOf(play.State, play.M(A(1)))!.ExtentY);
    }

    [Fact]
    public void ATurnIsRefusedWhenItCannotBeMade()
    {
        var play = Active();
        play.Refused(new TurnBase(Seat.One, A(1), BaseAxis.Y), ReasonCode.BaseTurnIllegal);   // already along Y
        play.Refused(new TurnBase(Seat.Two, A(1), BaseAxis.X), ReasonCode.NotYourModel);
        play.Refused(new TurnBase(Seat.One, A(2), BaseAxis.X), ReasonCode.ActivationInProgress);
        play.Refused(new TurnBase(Seat.One, A(1), (BaseAxis)9), ReasonCode.InvalidChoice);
        // A round base has no axis.
        var round = Active(_ => Rifleman);
        round.Refused(new TurnBase(Seat.One, A(1), BaseAxis.X), ReasonCode.BaseTurnIllegal);
        // No room beside a wall: the turned base would overlap it.
        var scenario = new Scenario(Field("36", [Wall("w", "10.8", "9", "12", "11")], [new DeploymentZone("west", F("0", "0", "6", "36")), new DeploymentZone("east", F("30", "0", "36", "36"))]), []);
        var tight = new BoardPlay(2, id => id == A(1) ? Oval : Rifleman, scenario: scenario).ToActivation();
        tight.Put(A(1), "10", "10").Put(A(2), "2", "30").Put(B(1), "30", "10").Put(B(2), "30", "30");
        tight.Do(new ActivateModel(Seat.One, A(1)));
        tight.Refused(new TurnBase(Seat.One, A(1), BaseAxis.X), ReasonCode.BaseTurnIllegal);
    }

    [Fact]
    public void TurningNeverChangesWhoIsWithinOneInch()
    {
        // B1's edge is 1" from A1's centre line when the oval lies along Y: x 11.5; lying along X it would reach to 11.0 (core to 10.5 + 0.5).
        var play = Active();
        play.Put(B(1), "12.4", "10");
        play.Refused(new TurnBase(Seat.One, A(1), BaseAxis.X), ReasonCode.BaseTurnIllegal);
        // Far from everyone it is fine.
        var far = Active();
        far.Do(new TurnBase(Seat.One, A(1), BaseAxis.X));
    }

    [Fact]
    public void ATurnIsNotAllowedWhileAnActionIsBeingResolved()
    {
        var play = Active();
        play.Put(B(1), "20", "10").Mark(B(1), 2);
        play.Do(new Shoot(Seat.One, A(1), B(1)), 4, 3);
        // The shot hit and the attacker is asked about B1's Blood Markers: nothing else may happen first.
        Assert.NotNull(play.State.Pending);
        play.Refused(new TurnBase(Seat.One, A(1), BaseAxis.X), ReasonCode.DecisionPending);
    }

    [Fact]
    public void ForgedEventsAreRefused()
    {
        var play = Active();
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(play.State, new BaseTurned(A(2), BaseAxis.X)));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(play.State, new BaseTurned(A(1), BaseAxis.Y)));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(play.State, new BaseTurned(A(1), (BaseAxis)4)));
        var round = Active(_ => Rifleman);
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(round.State, new BaseTurned(A(1), BaseAxis.X)));
    }

    [Fact]
    public void AnOvalChargesAndFightsAsACapsule()
    {
        // A1 (oval, along Y) at (10, 10); B1 at (10, 14.2): the core's end is 4.2 - 0.5 - 0.5 - 0.5 = 2.7" from B1's edge.
        var play = Active(id => id == A(1) ? MeleePlay.Brawler with { BaseHalfSpan = L("0.5") } : MeleePlay.Brawler);
        play.Put(B(1), "10", "14.2");
        play.Do(new ChargeModel(Seat.One, A(1), B(1)), 4);
        // It ends touching: the core's end meets B1's base.
        var body = Board.BodyOf(play.State, play.M(A(1)))!;
        Assert.True(Measure.IsWithin(body, Board.BodyOf(play.State, play.M(B(1)))!, L("0.01")));
        Assert.False(Measure.Overlap(body, Board.BodyOf(play.State, play.M(B(1)))!));
    }

    [Fact]
    public void TheStateHashSeesTheAxis()
    {
        var play = Active();
        var before = StateHash.Compute(play.State);
        play.Do(new TurnBase(Seat.One, A(1), BaseAxis.X));
        Assert.NotEqual(before, StateHash.Compute(play.State));
        play.Do(new TurnBase(Seat.One, A(1), BaseAxis.Y));
        Assert.Equal(before, StateHash.Compute(play.State));
        // A round-based match hashes with no mention of any of it.
        Assert.DoesNotContain("axis", StateHash.Canonical(Active(_ => Rifleman).State), StringComparison.Ordinal);
        Assert.DoesNotContain("halfSpan", StateHash.Canonical(Active(_ => Rifleman).State), StringComparison.Ordinal);
    }
}
