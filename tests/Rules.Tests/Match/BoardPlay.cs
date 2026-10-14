using System.Collections.Immutable;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.Scenarios;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;

namespace Rules.Tests.Match;

// Drives a Claim No Man's Land match for the tests with every die given by the test. Models are A1..An (seat One,
// west zone) and B1..Bn (seat Two, east zone). Put/Mark/SetStatus place models and wounds directly, the way the
// M2 tests set a status: they are test set-up, not events, so a test that uses them does not replay its log.
public sealed class BoardPlay
{
    public static readonly ModelProfile Rifleman = new(L("6"), L("0.5"), L("18"));

    public BoardPlay(int perSide = 2, Func<ModelId, ModelProfile>? profileFor = null, int turnLimit = ClaimNoMansLand.TurnLimit)
    {
        profileFor ??= _ => Rifleman;
        var models = Enumerable.Range(1, perSide).Select(i => new ModelId($"A{i}"))
            .Concat(Enumerable.Range(1, perSide).Select(i => new ModelId($"B{i}")))
            .Select(id => new SetupModel(id, id.Value[0] == 'A' ? Seat.One : Seat.Two, profileFor(id)))
            .ToList();
        var template = ClaimNoMansLand.CreateSetup(models);
        Setup = new MatchSetup(template.RulesVersion, template.ScenarioId, turnLimit, template.Profile, template.ZoneIds, models, template.Scenario);
        var created = new MatchCreated(Setup);
        State = Reducer.Apply(MatchState.None, created);
        Log.Add(created);
    }

    public MatchSetup Setup { get; }
    public MatchState State { get; set; }
    public List<GameEvent> Log { get; } = [];

    public static ModelId A(int n) => new($"A{n}");
    public static ModelId B(int n) => new($"B{n}");

    public DecideResult Do(Command command, params int[] dice)
    {
        var fixedDice = new FixedDice(dice);
        var result = TurnSequence.Decide(State, command, fixedDice);
        Assert.True(result.IsAccepted, result.Rejection?.Text);
        Assert.Equal(0, fixedDice.Remaining);
        State = result.State!;
        Log.AddRange(result.Events);
        return result;
    }

    // A command that must be refused: nothing changes and no die is rolled (FixedDice would throw if one were).
    public Rejection Refused(Command command, ReasonCode code)
    {
        var before = StateHash.Compute(State);
        var result = TurnSequence.Decide(State, command, new FixedDice());
        Assert.False(result.IsAccepted);
        Assert.Equal(code, result.Rejection!.Code);
        Assert.Empty(result.Events);
        Assert.Equal(before, StateHash.Compute(State));
        return result.Rejection;
    }

    public BoardPlay ToActivation(Seat first = Seat.One)
    {
        Do(new StartMatch(), 6, 1);
        var perSide = State.Models.Count(m => m.Seat == Seat.One);
        Do(new ChooseDeploymentZone(Seat.One, ClaimNoMansLand.West), 5, 2);
        var placed = 0;
        var total = State.Models.Length;
        while (State.Phase == Phase.Deployment)
        {
            var seat = NextDeployer();
            var next = State.Models.First(m => m.Seat == seat && !m.Deployed);
            var index = int.Parse(next.Id.Value[1..]);
            var x = seat == Seat.One ? "2" : "34";
            placed++;
            Do(new PlaceModel(seat, next.Id, P(x, (3 + 8 * (index - 1)).ToString())), placed == total ? [6, 1] : []);
        }
        Do(new ChooseFirstActivation(Seat.One, first));
        Assert.Equal(Phase.Activation, State.Phase);
        _ = perSide;
        return this;
    }

    private Seat NextDeployer() => TurnRules.NextDeployer(State) ?? throw new InvalidOperationException("Nobody deploys.");

    public ModelState M(ModelId id) => State.FindModel(id)!;

    public BoardPlay Put(ModelId id, string x, string y)
    {
        State = WithModel(M(id) with { Position = P(x, y) });
        return this;
    }

    public BoardPlay Mark(ModelId id, int markers)
    {
        State = WithModel(M(id) with { BloodMarkers = markers });
        return this;
    }

    public BoardPlay SetStatus(ModelId id, ModelStatus status)
    {
        State = WithModel(M(id) with { Status = status, Position = status == ModelStatus.OutOfAction ? null : M(id).Position });
        return this;
    }

    public BoardPlay Shaken(Seat seat)
    {
        State = WithSeat(seat, State.SeatFor(seat) with { Shaken = true });
        return this;
    }

    private MatchState WithModel(ModelState model) =>
        State with { Models = [.. State.Models.Select(m => m.Id == model.Id ? model : m)] };

    private MatchState WithSeat(Seat seat, SeatState value) =>
        seat == Seat.One ? State with { One = value } : State with { Two = value };

    public static ImmutableArray<Point2> Path(params (string X, string Y)[] points) => [.. points.Select(p => P(p.X, p.Y))];
}
