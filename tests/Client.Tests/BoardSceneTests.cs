using Client.Core;
using Rules.Core;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;
using Sim;

namespace Client.Tests;

// M7.4 stage 4a: the battlefield the 3D view draws is built from the server's state alone and agrees with the rules
// geometry (positions, bases, levels), through whole simulated matches.
public class BoardSceneTests
{
    private static readonly Lazy<List<MatchState>> States = new(() =>
    {
        var all = new List<MatchState>();
        var warbands = Content.Load(Path.Combine(Repository.Root, "content", "test-warbands.json"));
        for (var seed = 0; seed < 15; seed++)
        {
            var result = Runner.Run(seed, warbands);
            Assert.True(result.Terminal, result.Status);
            var state = MatchState.None;
            foreach (var e in result.Log)
            {
                state = Reducer.Apply(state, e);
                all.Add(state);
            }
        }
        return all;
    });

    [Fact]
    public void TheSceneShowsExactlyTheModelsOnTheTableWhereTheRulesHaveThem()
    {
        var checkedModels = 0;
        foreach (var state in States.Value)
        {
            var scene = BoardScene.Of(state, Seat.One, ContentNames.None);
            if (state.Setup?.Scenario is not { } scenario)
            {
                Assert.Null(scene);
                continue;
            }
            Assert.NotNull(scene);
            Assert.Equal(36, scene.Width);
            Assert.Equal(36, scene.Depth);
            var onTable = state.Models.Where(m => m.Position is not null).ToList();
            Assert.Equal(onTable.Select(m => m.Id), scene.Models.Select(m => m.Id));
            foreach (var model in onTable)
            {
                var shape = scene.Find(model.Id)!;
                Assert.Equal(model.Position!.Value.X.Thousandths / 1000.0, shape.X);
                Assert.Equal(model.Position!.Value.Y.Thousandths / 1000.0, shape.Y);
                Assert.Equal(state.Setup.ProfileOf(model.Id)!.BaseRadius.Thousandths / 1000.0, shape.Radius);
                var level = Elevation.Of(scenario.Battlefield, new ModelBody(model.Id.Value, model.Position.Value, state.Setup.ProfileOf(model.Id)!.BaseRadius));
                Assert.Equal(level.Level == ModelLevel.High, shape.HighGround);
                Assert.Equal(model.Seat == Seat.One, shape.Yours);
                Assert.Equal(state.ActiveModel == model.Id, shape.Active);
                Assert.Equal(model.Status, shape.Status);
                Assert.Contains($"[{(model.Seat == Seat.One ? "A" : "B")}]", shape.Label);
                checkedModels++;
            }
            Assert.Equal(scenario.Battlefield.Terrain.Length, scene.Terrain.Count);
            Assert.Equal(scenario.Battlefield.Zones.Length, scene.Zones.Count);
        }
        Assert.True(checkedModels > 1000, $"only {checkedModels} model positions checked");
    }

    // XCOM-style UX, improvement 2, and visual pass Stage 3: every model's Blood Markers are the server's state's, both
    // seats see the same count, the label stays short and no longer carries the count, and the drop row under it fills
    // one drop per marker (none drawn at 0).
    [Fact]
    public void LabelsShowEachModelsBloodMarkers()
    {
        var withMarkers = 0;
        var counts = new HashSet<int>();
        foreach (var state in States.Value.Where(s => s.Setup?.Scenario is not null))
        {
            var one = BoardScene.Of(state, Seat.One, ContentNames.None)!;
            var two = BoardScene.Of(state, Seat.Two, ContentNames.None)!;
            foreach (var shape in one.Models)
            {
                var markers = state.FindModel(shape.Id)!.BloodMarkers;
                Assert.Equal(markers, shape.BloodMarkers);
                Assert.Equal(markers, two.Find(shape.Id)!.BloodMarkers);
                var label = shape.ShortLabel();
                Assert.True(label.Length <= 18, label);
                Assert.DoesNotContain(ModelShape.MarkerMark, label, StringComparison.Ordinal);
                var row = BloodDrops.Row(shape.BloodMarkers);
                Assert.Equal(markers > 0, row is not null);
                Assert.Equal(markers, row is null ? 0 : BloodDrops.Filled(row));
                Assert.StartsWith(shape.Id.Value.ToUpperInvariant(), label, StringComparison.Ordinal);
                if (markers > 0)
                {
                    withMarkers++;
                    counts.Add(markers);
                }
            }
        }
        Assert.True(withMarkers > 100 && counts.Count > 1, $"{withMarkers} labels with markers; counts {string.Join(",", counts)}");
    }

    [Fact]
    public void TerrainUsesTheDisplayHeightsAndSaysWhatItIsInWords()
    {
        var state = States.Value.First(s => s.Setup?.Scenario is not null);
        var scene = BoardScene.Of(state, null, ContentNames.None)!;
        foreach (var piece in scene.Terrain)
        {
            Assert.Equal(piece.Profile switch { TerrainProfile.Low => 0.25, TerrainProfile.Cover => 0.75, _ => 3.0 }, piece.Height);
        }
        Assert.Equal("Wall, blocks sight", scene.Terrain.Single(t => t.Id == "wall-1").Label);
        Assert.Equal("Low, difficult", scene.Terrain.Single(t => t.Id == "rough-1").Label);
        Assert.Equal("Cover, Objective", scene.Terrain.Single(t => t.Id == "obj-5").Label);
        Assert.Equal(new TableRect(14, 11, 20, 12), scene.Terrain.Single(t => t.Id == "wall-1").Area);
    }

    [Fact]
    public void ZonesNameTheSeatThatChoseThem()
    {
        var state = States.Value.First(s => s.SeatFor(Seat.One).ZoneId is not null);
        var scene = BoardScene.Of(state, Seat.One, ContentNames.None)!;
        var zone = scene.Zones.Single(z => z.Id == state.SeatFor(Seat.One).ZoneId);
        Assert.EndsWith(", Seat One [A]", zone.Label);
        Assert.Contains(scene.Zones, z => z.Label.EndsWith(", Seat Two [B]", StringComparison.Ordinal));
    }

    [Fact]
    public void APointInsideABasePicksThatModelAndAPointOutsideDoesNot()
    {
        foreach (var state in States.Value.Where(s => s.Models.Count(m => m.Position is not null) == 8).Take(200))
        {
            var scene = BoardScene.Of(state, Seat.One, ContentNames.None)!;
            foreach (var model in scene.Models)
            {
                Assert.Equal(model.Id, scene.ModelAt(model.X, model.Y)!.Id);
                Assert.Equal(model.Id, scene.ModelAt(model.X + model.Radius * 0.99, model.Y)!.Id);
                var outside = scene.ModelAt(model.X + model.Radius + 0.01, model.Y);
                Assert.True(outside is null || outside.Id != model.Id);
                Assert.Null(scene.ModelAt(model.X, model.Y, standing: 3.0));
            }
        }
    }

    [Fact]
    public void KeyboardSelectionVisitsEveryModelOnceYoursFirstAndWrapsBothWays()
    {
        var state = States.Value.First(s => s.Models.Count(m => m.Position is not null) == 8);
        var scene = BoardScene.Of(state, Seat.Two, ContentNames.None)!;
        var seen = new List<ModelId>();
        ModelId? at = null;
        for (var i = 0; i < 8; i++)
        {
            at = scene.Next(at)!.Id;
            seen.Add(at.Value);
        }
        Assert.Equal(8, seen.Distinct().Count());
        Assert.All(seen.Take(4), id => Assert.Equal(Seat.Two, scene.Find(id)!.Seat));
        Assert.Equal(seen[0], scene.Next(at)!.Id);
        Assert.Equal(seen[^1], scene.Next(null, forward: false)!.Id);
        Assert.Equal(seen[^2], scene.Next(seen[^1], forward: false)!.Id);
    }

    [Fact]
    public void AModelOnAHighPlatformStandsOnTopOfIt()
    {
        var state = States.Value.First(s => s.Models.Count(m => m.Position is not null) == 8);
        var setup = state.Setup!;
        var model = state.Models.First(m => m.Position is not null);
        var at = model.Position!.Value;
        var inch = Length.Inch;
        var platform = new TerrainBlock("tower", Footprint.Of(new Rect(new Point2(at.X - inch * 2, at.Y - inch * 2), new Point2(at.X + inch * 2, at.Y + inch * 2))),
            TerrainProfile.Wall, surface: SurfaceLevel.High, walkable: true);
        var field = setup.Scenario!.Battlefield;
        var raised = new Scenario(new Battlefield(field.Width, field.Height, [platform], field.Zones), []);
        var moved = state with { Setup = new MatchSetup(setup.RulesVersion, setup.ScenarioId, setup.TurnLimit, setup.Profile, setup.ZoneIds, setup.Models, raised) };

        var scene = BoardScene.Of(moved, Seat.One, ContentNames.None)!;
        var shape = scene.Find(model.Id)!;
        Assert.True(shape.HighGround);
        Assert.Equal(3.0, shape.Standing);
        Assert.Contains("on high ground", shape.Text);
        Assert.Equal("Platform, high ground", scene.Terrain.Single().Label);
        Assert.Equal(model.Id, scene.ModelAt(shape.X, shape.Y, standing: 3.0)!.Id);
        Assert.Equal([3.0, 0.0], scene.StandingLevels);
    }

    [Fact]
    public void NoBattlefieldBeforeTheMatchHasOne()
    {
        Assert.Null(BoardScene.Of(MatchState.None, null, ContentNames.None));
    }
}
