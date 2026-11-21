using Client.Core;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.Scenarios;
using Rules.Core.State;
using Sim;

namespace Client.Tests;

// Towers proposal, stage T4: what the client shows of the towers and how a click reaches a platform. The south tower
// stands at x 16.5-19.5", y 5-8", 2" high, with ladders at (16.5, 6.5) and (19.5, 6.5). For a 25 mm base (radius 0.492")
// the west ladder's foot is (16.008, 6.5) and its top (16.992, 6.5); the east ladder's top (19.008, 6.5) and foot
// (19.992, 6.5). For a 1" base (the pure route tests) they are 16, 17, 19 and 20. Every verdict here is the rules' own: a
// preview is checked against TurnSequence.Decide, the server's function.
public class TowerClientTests
{
    private static readonly Battlefield Field = ClaimNoMansLand.CreateWithTowers().Battlefield;
    private static readonly Length Half = Length.FromThousandths(500);
    private static readonly Length Small = Length.FromThousandths(492);

    private static Point2 P(double x, double y) => Previews.PointAt(x, y);

    // A tower match at the start of an activation, the active (standing, 25 mm base, Movement 5" or more) model put at
    // (x, y), and nobody else near the south tower.
    private static (MatchState State, Seat Seat, ModelId Model) Activating(double x, double y)
    {
        foreach (var state in PreviewAgreementTests.States.Value.Where(s => s.Setup?.Scenario?.Battlefield.Terrain.Any(t => t.IsTower) == true
                     && s.Phase == Phase.Activation && s.Pending is null && s.Actions == ActionsTaken.None && !s.MovementHalved && s.ActiveModel is not null))
        {
            var active = state.FindModel(state.ActiveModel!.Value)!;
            var profile = state.Setup!.ProfileOf(active.Id)!;
            if (active.Status != ModelStatus.Standing || profile.BaseRadius != Small || profile.Movement < Length.FromInches(5)
                || state.Models.Any(m => m.Id != active.Id && m.Position is { } p && Math.Abs(p.X.Thousandths / 1000.0 - 18) < 7 && Math.Abs(p.Y.Thousandths / 1000.0 - 6.5) < 5))
            {
                continue;
            }
            var moved = state with { Models = [.. state.Models.Select(m => m.Id == active.Id ? m with { Position = P(x, y) } : m)] };
            return (moved, active.Seat, active.Id);
        }
        throw new InvalidOperationException("No tower state to start from.");
    }

    [Fact]
    public void AClickOnThePlatformGoesUpTheNearestLadderAndTheServerAgrees()
    {
        var (state, seat, model) = Activating(15, 6.5);
        var interaction = new Interaction();
        interaction.Update(state, seat);
        Assert.True(interaction.Start(InteractionMode.Move));
        Assert.Equal(ClickResult.Send, interaction.ClickAt(18, 6.5));
        var preview = interaction.Preview!;
        var command = Assert.IsType<MoveModel>(preview.Command);
        Assert.Equal([P(16.008, 6.5), P(16.992, 6.5), P(18, 6.5)], command.Path.ToArray());
        Assert.Equal(5, preview.Used); // 3" across and 2" up
        Assert.Equal([0, 0, 2, 2], preview.Path.Select(m => m.Height));
        Assert.Contains(preview.Facts, f => f.StartsWith("One ladder climb: each counts its 2.0\" height", StringComparison.Ordinal));
        var decided = TurnSequence.Decide(state, command, new SeededDice(1));
        Assert.True(decided.IsAccepted, decided.Rejection?.Text);
        Assert.Equal(Length.FromInches(5), decided.Events.OfType<ModelMoved>().Single().Cost);

        // After the move: the scene stands the model on the tower, and the panel says so.
        var scene = BoardScene.Of(decided.State!, seat, ContentNames.None)!;
        var shape = scene.Find(model)!;
        Assert.Equal(2, shape.Standing);
        Assert.Equal("tower-s", shape.TowerId);
        Assert.Contains("on the tower (2.0\" up)", shape.Text, StringComparison.Ordinal);
        Assert.Equal("▲ On the tower (2.0\" up): it leaves by a ladder, or down a side with a Risky roll", UnitPanel.TowerLine(shape));
        Assert.Same(shape, scene.ModelAt(18, 6.5, 2));
        Assert.Null(scene.ModelAt(18, 6.5));
    }

    [Fact]
    public void AClickOnTheGroundComesDownTheNearestLadder()
    {
        var (state, seat, _) = Activating(18, 6.5);
        var interaction = new Interaction();
        interaction.Update(state, seat);
        Assert.True(interaction.Start(InteractionMode.Move));
        Assert.Equal(ClickResult.Send, interaction.ClickAt(21, 6.5));
        var command = Assert.IsType<MoveModel>(interaction.Preview!.Command);
        Assert.Equal([P(19.008, 6.5), P(19.992, 6.5), P(21, 6.5)], command.Path.ToArray());
        Assert.True(TurnSequence.Decide(state, command, new SeededDice(1)).IsAccepted);
    }

    [Fact]
    public void APointPickedAtThePlatformsEdgeIsMovedOntoIt()
    {
        Assert.Equal(P(18, 7.5), TowerRoute.OnPlatform(Field, Half, P(18, 7.9)));
        Assert.Equal(P(17, 5.5), TowerRoute.OnPlatform(Field, Half, P(16.6, 5.1)));
        Assert.Equal(P(15, 6.5), TowerRoute.OnPlatform(Field, Half, P(15, 6.5)));
        var (state, seat, _) = Activating(15, 6.5);
        var interaction = new Interaction();
        interaction.Update(state, seat);
        Assert.True(interaction.Start(InteractionMode.Move));
        interaction.SetCursor(17.5, 7.9);
        Assert.Equal([P(16.008, 6.5), P(16.992, 6.5), P(17.5, 7.508)], Assert.IsType<MoveModel>(interaction.Preview!.Command).Path.ToArray());
    }

    [Fact]
    public void FromOneTowerToTheOtherTheRouteGoesDownAndUp()
    {
        // Down the west ladder (nearer the way on), along the ground, up the north tower's west ladder.
        var route = TowerRoute.Expand(Field, Half, P(17.5, 6.5), [P(17.5, 29.5)]);
        Assert.Equal([P(17, 6.5), P(16, 6.5), P(16, 29.5), P(17, 29.5), P(17.5, 29.5)], route);
    }

    [Fact]
    public void WithoutTowersPointsAreUnchanged()
    {
        var plain = ClaimNoMansLand.Create().Battlefield;
        var points = new[] { P(15, 6.5), P(30, 30) };
        Assert.Equal(points, TowerRoute.Expand(plain, Half, P(1, 1), points));
        Assert.Equal(points, TowerRoute.Expand(Field, Half, P(1, 1), points)); // same level all the way: nothing added
    }

    [Fact]
    public void TheMovementAreaReachesThePlatformOnlyByALadder()
    {
        var (state, seat, model) = Activating(14.5, 6.5);
        var area = Previews.Reachable(state, seat, PreviewKind.Move, model, []);
        bool OnTop(TablePoint p) => p.X > 16.5 && p.X < 19.5 && p.Y > 5 && p.Y < 8;
        // 2.5" across to the top and 2" up leave at least 0.5" on the platform (the profile's Movement is at least 5").
        Assert.Contains(area, OnTop);
        foreach (var point in area.Where(OnTop))
        {
            var path = Previews.Route(state, model, [P(point.X, point.Y)]);
            Assert.True(TurnSequence.Decide(state, new MoveModel(seat, model, [.. path]), new SeededDice(1)).IsAccepted, $"{point}");
        }
    }

    [Fact]
    public void TheSceneDrawsEachTowerAtItsHeightWithItsLadders()
    {
        var (state, _, _) = Activating(14, 6.5);
        var scene = BoardScene.Of(state, Seat.One, ContentNames.None)!;
        var towers = scene.Terrain.Where(t => t.Tower).ToList();
        Assert.Equal(["tower-n", "tower-s"], towers.Select(t => t.Id));
        foreach (var tower in towers)
        {
            Assert.Equal(2, tower.Height);
            Assert.Equal("TOWER 2.0\"", tower.TowerMark);
            Assert.Equal([Side.East, Side.West], tower.Ladders.Select(l => l.Side).Order().Reverse());
            Assert.StartsWith("Tower, 2.0\" high: models stand and fight on its platform, reached by its ladders (west and east sides) or by climbing a side with a Risky roll", tower.Label, StringComparison.Ordinal);
        }
        Assert.All(scene.Terrain.Where(t => !t.Tower), t => Assert.Empty(t.Ladders));
        // The Scenario card says what the towers allow.
        var card = ScenarioCard.Of(state, Seat.One);
        Assert.Contains("TOWERS", card.Lines);
        Assert.Contains(card.Lines, l => l.Contains("by its ladders", StringComparison.Ordinal) && l.Contains("U66", StringComparison.Ordinal));
        Assert.Contains(card.Lines, l => l.Contains("Risky Success Roll", StringComparison.Ordinal) && l.Contains("U75", StringComparison.Ordinal));
    }

    [Fact]
    public void APlainMatchHasNoTowerWords()
    {
        var plain = PreviewAgreementTests.States.Value.First(s => s.Phase == Phase.Activation);
        Assert.DoesNotContain("TOWERS", ScenarioCard.Of(plain, Seat.One).Lines);
        var scene = BoardScene.Of(plain, Seat.One, ContentNames.None)!;
        Assert.DoesNotContain(scene.Terrain, t => t.Tower);
        Assert.All(scene.Models, m => Assert.Null(m.TowerId));
    }

    // ---- stage T6: climbing a tower's side (a Risky Success Roll, p. 39, U75) ----

    [Fact]
    public void WithKAMoveClimbsTheSideAndThePreviewSaysTheRollDecides()
    {
        var (state, seat, model) = Activating(15, 5.8);
        var interaction = new Interaction();
        interaction.Update(state, seat);
        Assert.True(interaction.Start(InteractionMode.Move));
        Assert.True(interaction.CanClimbSides);
        Assert.Contains(" K: climb a tower's side instead of a ladder", string.Join("\n", interaction.Lines()), StringComparison.Ordinal);
        Assert.True(interaction.ToggleClimbSides());
        interaction.SetCursor(18, 5.8);
        var preview = interaction.Preview!;
        Assert.True(preview.Legal, preview.Refusal?.Text);
        var command = Assert.IsType<MoveModel>(preview.Command);
        Assert.Equal([P(16.008, 5.8), P(16.992, 5.8), P(18, 5.8)], command.Path.ToArray());
        Assert.Contains(Previews.SideClimbFact, preview.Facts);
        Assert.Contains(preview.Facts, f => f.StartsWith("One climb up or down the side", StringComparison.Ordinal));
        // The server agrees, and the roll comes first.
        var decided = TurnSequence.Decide(state, command, new SeededDice(3));
        Assert.True(decided.IsAccepted, decided.Rejection?.Text);
        Assert.IsType<SideClimbDeclared>(decided.Events[0]);
        Assert.Contains(decided.Events, e => e is SideClimbRolled);
        // K again: back to the ladder.
        Assert.True(interaction.ToggleClimbSides());
        Assert.Equal(P(16.008, 6.5), Assert.IsType<MoveModel>(interaction.Preview!.Command).Path[0]);
        Assert.DoesNotContain(Previews.SideClimbFact, interaction.Preview!.Facts);
        _ = model;
    }

    [Fact]
    public void SideClimbingIsOffForARetreatAndOnAPlainBattlefield()
    {
        var plain = PreviewAgreementTests.States.Value.First(s => s.Phase == Phase.Activation && s.Setup?.Scenario?.Battlefield.Terrain.Any(t => t.IsTower) == false
            && s.Pending is null && s.Actions == ActionsTaken.None && s.ActiveModel is not null);
        var interaction = new Interaction();
        interaction.Update(plain, plain.FindModel(plain.ActiveModel!.Value)!.Seat);
        Assert.True(interaction.Start(InteractionMode.Move));
        Assert.False(interaction.CanClimbSides);
        Assert.False(interaction.ToggleClimbSides());
        Assert.DoesNotContain(" K:", string.Join("\n", interaction.Lines()), StringComparison.Ordinal);
    }

    [Fact]
    public void TheSideRouteTakesTheShortestWayAcrossASide()
    {
        // From the south (y 3), to a platform point near the south side: straight up the south side.
        var route = TowerRoute.Expand(Field, Half, P(18.2, 3), [P(18.2, 6)], sides: true);
        Assert.Equal([P(18.2, 4.5), P(18.2, 5.5), P(18.2, 6)], route);
        // Without sides the same click goes round to a ladder.
        Assert.Contains(P(20, 6.5), TowerRoute.Expand(Field, Half, P(18.2, 3), [P(18.2, 6)])); // the east ladder is nearer this way
    }

    [Fact]
    public void EverySideClimbPointTheAreaShowsIsOneTheServerAccepts()
    {
        var (state, seat, model) = Activating(14.8, 5.6);
        var area = Previews.Reachable(state, seat, PreviewKind.Move, model, [], sides: true);
        bool OnTop(TablePoint p) => p.X > 16.5 && p.X < 19.5 && p.Y > 5 && p.Y < 8;
        Assert.Contains(area, OnTop);
        foreach (var point in area.Where(OnTop))
        {
            var path = Previews.Route(state, model, [P(point.X, point.Y)], sides: true);
            Assert.True(TurnSequence.Check(state, new MoveModel(seat, model, [.. path])) is null, $"{point}");
        }
    }
}
