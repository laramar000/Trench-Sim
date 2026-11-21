using Client.Core;
using Rules.Core.Geometry;
using Rules.Core.Scenarios;
using Rules.Core.State;

namespace Client.Tests;

// Trench-maps stage S3: what the client shows of trenches. The default tower map has six 1" trenches in the deployment
// zones; the board draws them as pits (TrenchPits) and stands a model in one on its floor, as the rules say (U81).
public class TrenchClientTests
{
    private static MatchState Tower(params (string Id, double X, double Y)[] at)
    {
        var state = PreviewAgreementTests.States.Value.First(s => s.Setup?.Scenario?.Battlefield.TrenchDepth is not null && s.Phase == Phase.Activation);
        return state with
        {
            Models = [.. state.Models.Select(m => at.FirstOrDefault(a => a.Id == m.Id.Value) is { Id: not null } a ? m with { Position = Previews.PointAt(a.X, a.Y) } : m)],
        };
    }

    [Fact]
    public void TheGroundAroundTheTrenchesCoversTheTableExactlyOnce()
    {
        var holes = new[] { new TableRect(1.5, 4, 4, 12), new TableRect(4, 9.5, 6, 12) };
        var table = new TableRect(0, 0, 36, 36);
        var ground = TrenchPits.Around(table, holes);
        Assert.Equal(36 * 36 - 2.5 * 8 - 2 * 2.5, ground.Sum(c => c.Width * c.Depth), 9);
        foreach (var cell in ground)
        {
            Assert.DoesNotContain(holes, h => h.X0 < cell.X1 && cell.X0 < h.X1 && h.Y0 < cell.Y1 && cell.Y0 < h.Y1);
            Assert.DoesNotContain(ground, o => o != cell && o.X0 < cell.X1 && cell.X0 < o.X1 && o.Y0 < cell.Y1 && cell.Y0 < o.Y1);
        }
        Assert.Equal([table], TrenchPits.Around(table, []));
        // A zone only partly over a trench keeps the rest.
        Assert.Equal(6 * 36 - 25, TrenchPits.Around(new TableRect(0, 0, 6, 36), holes).Sum(c => c.Width * c.Depth), 9);
    }

    [Fact]
    public void TheTowerMapHasSixTrenchesDrawnAsPits()
    {
        var scene = BoardScene.Of(Tower(), Seat.One, ContentNames.None)!;
        var trenches = scene.Terrain.Where(t => t.Trench).ToList();
        Assert.Equal(["trench-ne", "trench-ne-2", "trench-nw", "trench-nw-2", "trench-se", "trench-sw"], trenches.Select(t => t.Id));
        Assert.All(trenches, t => Assert.Equal(1, t.Depth));
        Assert.All(trenches, t => Assert.Equal(0, t.Height));
        Assert.Equal(2, trenches.Single(t => t.Id == "trench-sw").Parts.Count);
        Assert.All(trenches, t => Assert.StartsWith("Trench, 1.0\" deep: its sides are crossed as open ground", t.Label, StringComparison.Ordinal));
        Assert.Empty(TerrainDress.For(trenches[0]));
        Assert.Equal(1, TrenchPits.Deepest(scene));
        Assert.Equal(8, TrenchPits.Holes(scene).Count);
        Assert.Equal("trench-sw", TrenchPits.At(scene, 5, 10.5)?.Id);
        Assert.Null(TrenchPits.At(scene, 5, 8));
        // Every other piece is drawn as before.
        Assert.All(scene.Terrain.Where(t => !t.Trench), t => Assert.Equal(0, t.Depth));
    }

    [Fact]
    public void AModelInATrenchStandsOnItsFloorAndTheCardSaysWhatTrenchesDo()
    {
        var state = Tower(("a1", 2.75, 8), ("a2", 9, 18));
        var scene = BoardScene.Of(state, Seat.One, ContentNames.None)!;
        Assert.Equal(-1, scene.Find(new ModelId("a1"))!.Standing);
        Assert.Equal(0, scene.Find(new ModelId("a2"))!.Standing);
        Assert.Contains(-1.0, scene.StandingLevels);
        var card = ScenarioCard.Of(state, Seat.One);
        Assert.Contains("TRENCHES", card.Lines);
        Assert.Contains(card.Lines, l => l.Contains("6 trench sections, 1.0\" deep", StringComparison.Ordinal) && l.Contains("U80", StringComparison.Ordinal));
        Assert.Contains(card.Lines, l => l.Contains("U83", StringComparison.Ordinal));
        // The plain map has no trench words.
        var plain = PreviewAgreementTests.States.Value.First(s => s.Setup?.Scenario?.Battlefield.TrenchDepth is null && s.Phase == Phase.Activation);
        Assert.DoesNotContain("TRENCHES", ScenarioCard.Of(plain, Seat.One).Lines);
    }

    [Fact]
    public void TheSightIconsFromTheRulesSeeIntoTheTrench()
    {
        // a1 deep in the south-west trench, b1 far off on open ground: b1 sees only a1's head, so a1 has cover.
        var state = Tower(("a1", 2.75, 8), ("b1", 20, 8.5));
        var sights = TargetSights.Of(state, new ModelId("b1"));
        var a1 = sights.Single(s => s.Model == new ModelId("a1"));
        var field = state.Setup!.Scenario!.Battlefield;
        var cover = Cover.Of(field, new ModelBody("b1", Previews.PointAt(20, 8.5), state.Setup.ProfileOf(new ModelId("b1"))!.BaseRadius),
            new ModelBody("a1", Previews.PointAt(2.75, 8), state.Setup.ProfileOf(new ModelId("a1"))!.BaseRadius));
        Assert.Equal(cover.Sight.Visible, a1.Visible);
        Assert.Equal(cover.InCover, a1.Cover);
        Assert.Equal(ClaimNoMansLand.TrenchDepth, "1");
    }
}
