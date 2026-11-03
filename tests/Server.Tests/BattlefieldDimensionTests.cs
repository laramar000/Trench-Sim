using Rules.Core.Geometry;
using Rules.Core.Scenarios;
using Sim;
using Xunit;

namespace Server.Tests;

// Map size and scale (owner's standing instruction, 2026-11-03; docs/map-dimensions.md). The book recommends a 3 ft by 3 ft
// table (36"), with 4 ft by 4 ft (48") as the alternative for the larger scenarios. 36" is the default and every map in
// the game is that size today. One in-game inch is one tabletop inch (lengths are thousandths of an inch, U1).
public class BattlefieldDimensionTests
{
    private static readonly Length Standard = Length.FromInches(36);

    private static IEnumerable<MapDefinition> Shipped() =>
        MapFiles.LoadFolder(Path.Combine(Repository.Root(), "content", MapFiles.Folder)).Values;

    private static IEnumerable<MapDefinition> Generated() =>
        MapGenerator.Kinds.SelectMany(kind => Enumerable.Range(1, 5).Select(seed => MapGenerator.Generate(kind, seed)));

    [Fact]
    public void OneInGameInchIsOneTabletopInch()
    {
        Assert.Equal(1000, Length.FromInches(1).Thousandths);
        Assert.Equal(1500, Length.Parse("1.5").Thousandths);
        Assert.Equal(36_000, Standard.Thousandths);
    }

    [Fact]
    public void EveryShippedMapIsTheStandardTable()
    {
        Assert.NotEmpty(Shipped());
        Assert.All(Shipped(), m => Assert.Equal((Standard, Standard), (m.Battlefield.Width, m.Battlefield.Height)));
    }

    [Fact]
    public void EveryGeneratedMapIsTheStandardTable() =>
        Assert.All(Generated(), m => Assert.Equal((Standard, Standard), (m.Battlefield.Width, m.Battlefield.Height)));

    [Fact]
    public void ZonesAreSixInchesDeepAndTwentyFourApartOnTheStandardTable()
    {
        foreach (var map in Shipped().Concat(Generated()))
        {
            var zones = map.Battlefield.Zones.Select(z => z.Area.Bounds).OrderBy(b => b.Min.X.Thousandths).ToList();
            Assert.Equal(2, zones.Count);
            Assert.Equal((Length.Zero, Length.FromInches(6)), (zones[0].Min.X, zones[0].Max.X));
            Assert.Equal((Length.FromInches(30), Standard), (zones[1].Min.X, zones[1].Max.X));
            Assert.All(zones, z => Assert.Equal((Length.Zero, Standard), (z.Min.Y, z.Max.Y)));
            Assert.Equal(Length.FromInches(24), zones[1].Min.X - zones[0].Max.X);
        }
    }

    [Fact]
    public void DistancesAreMeasuredInTrueInches()
    {
        var field = new Battlefield(Standard, Standard, [], []); // bare ground: rubble would double the cost
        var mover = new ModelBody("m", new Point2(Length.FromInches(1), Length.FromInches(18)), Length.Parse("0.5"));
        Assert.Equal(Length.FromInches(3), Movement.Cost(field, mover, [new Point2(Length.FromInches(4), Length.FromInches(18))]));
        Assert.Equal(Length.FromInches(34), Movement.Cost(field, mover, [new Point2(Length.FromInches(35), Length.FromInches(18))]));
    }

    // Map size (U125): the book's recommended 36" table is the default and 48" the alternative; a scenario takes a map of
    // either size, and refuses any other, or a table that is not square. The layout on a 48" table is the map's own data.
    [Fact]
    public void AScenarioTakesA36Or48InchTableAndNoOther()
    {
        var plain = MapFiles.Load(Path.Combine(Repository.Root(), "content", MapFiles.Folder, "claim-no-mans-land.json")).ToScenario();
        Rules.Core.State.Scenario Resized(int w, int h) => new(new Battlefield(Length.FromInches(w), Length.FromInches(h), plain.Battlefield.Terrain, plain.Battlefield.Zones), plain.ObjectiveIds);

        Assert.Equal(new[] { 36, 48 }, ClaimNoMansLand.Definition.Needs.TableSizes.ToArray());
        Assert.Equal(new[] { 36, 48 }, TrenchWarfare.Definition.Needs.TableSizes.ToArray());
        Assert.Empty(ClaimNoMansLand.Definition.Unmet(plain, null));
        Assert.Empty(ClaimNoMansLand.Definition.Unmet(Resized(48, 48), null));
        Assert.Contains("square table of 36\" or 48\"", Assert.Single(ClaimNoMansLand.Definition.Unmet(Resized(40, 40), null)));
        Assert.Contains("square table", Assert.Single(ClaimNoMansLand.Definition.Unmet(Resized(36, 48), null)));
    }

    [Fact]
    public void TheTableEdgesStopAModel()
    {
        var field = ClaimNoMansLand.Create().Battlefield;
        var mover = new ModelBody("m", new Point2(Length.FromInches(34), Length.FromInches(18)), Length.Parse("0.5"));
        var allowance = Length.FromInches(6);
        Assert.True(Movement.CheckPath(field, mover, [new Point2(Length.Parse("35.9"), Length.FromInches(18))], allowance, []).IsLegal);
        Assert.False(Movement.CheckPath(field, mover, [new Point2(Length.Parse("36.4"), Length.FromInches(18))], allowance, []).IsLegal);
        var south = new ModelBody("s", new Point2(Length.FromInches(18), Length.FromInches(2)), Length.Parse("0.5"));
        Assert.False(Movement.CheckPath(field, south, [new Point2(Length.FromInches(18), Length.Parse("-0.4"))], allowance, []).IsLegal);
    }
}
