using Match.Protocol;
using Match.Server;
using Rules.Core.Geometry;
using Rules.Core.Scenarios;
using Rules.Core.State;
using Sim;

namespace Server.Tests;

// Trench-maps stage S8: maps made from a seed. Every seed makes a map the set-up rules allow, the same seed always
// makes the same map (its hash is pinned here, so another machine or build that differs fails), and the id says how.
public class MapGeneratorTests
{
    [Fact]
    public void TheRandomNumbersAreSplitMix64()
    {
        // The published SplitMix64 outputs for seed 0.
        var random = new MapRandom(0);
        Assert.Equal(0xE220A8397B1DCDAFUL, random.Next());
        Assert.Equal(0x6E789E6AA1B965F4UL, random.Next());
        Assert.Equal(0x06C45D188009454FUL, random.Next());
        var counts = new int[6];
        var dice = new MapRandom(7);
        for (var i = 0; i < 6000; i++)
        {
            counts[dice.Below(6)]++;
        }
        Assert.All(counts, c => Assert.InRange(c, 850, 1150));
        Assert.Throws<ArgumentOutOfRangeException>(() => dice.Below(0));
    }

    [Fact]
    public void AnIdNamesTheKindTheVersionAndTheSeed()
    {
        Assert.Equal("generated-trench-lines-v1-42", MapGenerator.IdOf(MapGenerator.TrenchLines, 42));
        Assert.True(MapGenerator.TryParse("generated-trench-lines-v1-42", out var kind, out var seed));
        Assert.Equal((MapGenerator.TrenchLines, 42), (kind, seed));
        Assert.True(MapGenerator.TryParse("generated-trench-lines-v1-2147483647", out _, out seed));
        Assert.Equal(int.MaxValue, seed);
        foreach (var bad in new[] { "generated-trench-lines-v2-42", "generated-trench-lines-v1-042", "generated-trench-lines-v1--1", "generated-trench-lines-v1-+1",
            "generated-trench-lines-v1-", "generated-trench-lines-v1-1a", "generated-trench-lines-v1-2147483648", "generated-ruins-v1-1", "trench-warfare" })
        {
            Assert.False(MapGenerator.TryParse(bad, out _, out _), bad);
            Assert.Null(MapGenerator.FromId(bad));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => MapGenerator.IdOf(MapGenerator.TrenchLines, -1));
        Assert.Throws<ArgumentException>(() => MapGenerator.IdOf("towers", 1));
    }

    [Fact]
    public void ThousandTrenchLinesMapsAllKeepTheSetUpRules()
    {
        var acrossNorth = 0;
        var all = Thousand(MapGenerator.TrenchLines);
        for (var seed = 0; seed < all.Count; seed++)
        {
            var map = all[seed];
            Assert.Equal(MapGenerator.IdOf(MapGenerator.TrenchLines, seed), map.Id);
            Assert.Equal(Archetype.TrenchLines, map.ArchetypeKind);
            Assert.Empty(MapLayout.Check(map.Battlefield, map.ObjectiveIds, MapGenerator.DesignBaseRadius, Archetype.TrenchLines, map.RuinIds));
            var trenches = map.Battlefield.Terrain.Where(t => t.IsTrench).ToList();
            foreach (var t in trenches)
            {
                // p. 62: 4" to 12" long, 2" to 4" wide.
                var b = t.Footprint.Bounds;
                var (length, width) = b.Width > b.Height ? (b.Width, b.Height) : (b.Height, b.Width);
                Assert.InRange(length.Thousandths, 4000, 12000);
                Assert.InRange(width.Thousandths, 2000, 4000);
            }
            var others = map.Battlefield.Terrain.Where(t => !t.IsTrench).ToList();
            // p. 67: at least two of each other sort (dangerous ground left out, U89).
            Assert.Equal(2, others.Count(t => t.Id.StartsWith("corner-", StringComparison.Ordinal) && map.RuinIds.Contains(t.Id)));
            Assert.Equal(2, others.Count(t => t.IsDifficult));
            Assert.Equal(2, others.Count(t => t.Id.StartsWith("landmark-", StringComparison.Ordinal)));
            Assert.DoesNotContain(map.Battlefield.Terrain, t => t.Kind.HasFlag(TerrainKind.Dangerous));
            if (trenches.Any(t => t.Footprint.Bounds.Min.Y == Length.Zero))
            {
                acrossNorth++;
            }
        }
        Assert.InRange(acrossNorth, 200, 450);
    }

    [Fact]
    public void TheSameSeedMakesTheSameMapEverywhere()
    {
        var a = MapGenerator.Generate(MapGenerator.TrenchLines, 1);
        var b = MapGenerator.FromId("generated-trench-lines-v1-1")!;
        Assert.Equal(RecordFormat.MapHash(a.ToScenario()), RecordFormat.MapHash(b.ToScenario()));
        Assert.NotEqual(RecordFormat.MapHash(a.ToScenario()), RecordFormat.MapHash(MapGenerator.Generate(MapGenerator.TrenchLines, 2).ToScenario()));
        // Pinned: a change to the generator that moves any map must come with a new Version.
        Assert.Equal(PinnedHash, RecordFormat.MapHash(a.ToScenario()));
    }

    private const string PinnedHash = "sha256:cc145c97dd908cb444801bc90bf415f5448f193a492c973095a3f570b7f95f3d";

    [Fact]
    public void AGeneratedMapReadsBackFromItsFile()
    {
        foreach (var seed in new[] { 0, 1, 99, int.MaxValue })
        {
            var map = MapGenerator.Generate(MapGenerator.TrenchLines, seed);
            var again = MapFiles.Parse(MapFiles.ToJson(map, "notes"), "generated");
            Assert.Equal(RecordFormat.MapHash(map.ToScenario()), RecordFormat.MapHash(again.ToScenario()));
            Assert.Equal(map.RuinIds.AsEnumerable(), again.RuinIds.AsEnumerable());
            Assert.Equal(map.Archetype, again.Archetype);
            Assert.Equal(map.Name, again.Name);
        }
        // Every content map too.
        foreach (var map in ScenarioCatalog.Maps.Values)
        {
            var again = MapFiles.Parse(MapFiles.ToJson(map), map.Id);
            Assert.Equal(RecordFormat.MapHash(map.ToScenario()), RecordFormat.MapHash(again.ToScenario()));
            Assert.Equal(map.RuinIds.AsEnumerable(), again.RuinIds.AsEnumerable());
            Assert.Equal(map.Supports.AsEnumerable(), again.Supports.AsEnumerable());
        }
    }

    [Fact]
    public void TheServerMakesAGeneratedMapFromItsId()
    {
        var harness = new Harness();
        var one = harness.Connect();
        // No scenario this build plays uses Trench Lines yet, so the server makes the map and finds it does not fit.
        one.Send(new CreateMatch("ranged") { Map = "generated-trench-lines-v1-5" });
        var rejected = Assert.IsType<Rejected>(one.LastAnswer);
        Assert.Equal(nameof(ProtocolError.MapDoesNotFit), rejected.Code);
        Assert.Contains("not made for claim-no-mans-land", rejected.Text);
        // A generator version this build does not have is an unknown map.
        var two = harness.Connect();
        two.Send(new CreateMatch("ranged") { Map = "generated-trench-lines-v9-5" });
        Assert.Equal(nameof(ProtocolError.UnknownMap), Assert.IsType<Rejected>(two.LastAnswer).Code);
    }

    // Stage S9: a thousand seeds of each kind, made in parallel (each map on its own; the result does not depend on order).
    private static List<MapDefinition> Thousand(string kind)
    {
        var maps = new MapDefinition[1000];
        Parallel.For(0, maps.Length, seed => maps[seed] = MapGenerator.Generate(kind, seed));
        return [.. maps];
    }

    private static void KeepsTheRules(MapDefinition map)
    {
        Assert.Empty(MapLayout.Check(map.Battlefield, map.ObjectiveIds, MapGenerator.DesignBaseRadius, map.ArchetypeKind, map.RuinIds));
        Assert.DoesNotContain(map.Battlefield.Terrain, t => t.Kind.HasFlag(TerrainKind.Dangerous));
        foreach (var t in map.Battlefield.Terrain.Where(t => t.IsTrench))
        {
            var b = t.Footprint.Bounds;
            var (length, width) = b.Width > b.Height ? (b.Width, b.Height) : (b.Height, b.Width);
            Assert.InRange(length.Thousandths, 4000, 12000);
            Assert.InRange(width.Thousandths, 2000, 4000);
        }
    }

    [Fact]
    public void ThousandClaimNoMansLandMapsAllFitTheScenario()
    {
        var withTrenches = 0;
        foreach (var map in Thousand(MapGenerator.ClaimNoMansLand))
        {
            KeepsTheRules(map);
            Assert.Equal(Archetype.NoMansLand, map.ArchetypeKind);
            Assert.Equal(["obj-1", "obj-2", "obj-3", "obj-4", "obj-5"], map.ObjectiveIds.AsEnumerable());
            // The Objectives on the printed marker positions (U32), as on the fixed map.
            var plain = ScenarioCatalog.Maps[ScenarioSetups.PlainMap].Battlefield;
            Assert.All(map.ObjectiveIds, o => Assert.Equal(plain.Terrain.Single(t => t.Id == o).Footprint, map.Battlefield.Terrain.Single(t => t.Id == o).Footprint));
            var setup = ScenarioSetups.OnMap(Scenarios.All["ranged"], map);
            Assert.Equal(ClaimNoMansLand.ScenarioId, setup.ScenarioId);
            withTrenches += map.Battlefield.Terrain.Any(t => t.IsTrench) ? 1 : 0;
        }
        Assert.InRange(withTrenches, 500, 1000);
    }

    [Fact]
    public void ThousandTrenchWarfareMapsAllFitTheScenario()
    {
        var zones = new HashSet<string>();
        foreach (var map in Thousand(MapGenerator.TrenchWarfare))
        {
            KeepsTheRules(map);
            Assert.Equal(3, TrenchWarfare.ScoringSections(map.Battlefield).Length);
            zones.Add(TrenchWarfare.DefenderZone(map.Battlefield)!);
            var setup = ScenarioSetups.OnMap(Scenarios.All["melee-2v2" + ScenarioSetups.TrenchWarfareSuffix], map);
            Assert.Equal(TrenchWarfare.ScenarioId, setup.ScenarioId);
            Assert.Throws<MapFormatException>(() => ScenarioSetups.OnMap(Scenarios.All["ranged"], map));
        }
        Assert.Equal(["east", "west"], zones.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ThousandDecimatedRuinsMapsAllKeepTheRuinsRules()
    {
        foreach (var map in Thousand(MapGenerator.DecimatedRuins))
        {
            KeepsTheRules(map);
            Assert.Equal(Archetype.DecimatedRuins, map.ArchetypeKind);
            // p. 66: at least six ruined buildings, chained within 9" (checked above), no trenches.
            Assert.True(map.RuinIds.Count(r => r.StartsWith("ruin-", StringComparison.Ordinal)) >= 6, map.Id);
            Assert.DoesNotContain(map.Battlefield.Terrain, t => t.IsTrench);
            Assert.Empty(map.Supports);
        }
    }

    [Fact]
    public void EachKindsFirstMapIsPinned()
    {
        // Pinned like the Trench Lines map: a change that moves any of them must come with a new Version.
        Assert.Equal(PinnedClaim, RecordFormat.MapHash(MapGenerator.Generate(MapGenerator.ClaimNoMansLand, 1).ToScenario()));
        Assert.Equal(PinnedTrenchWarfare, RecordFormat.MapHash(MapGenerator.Generate(MapGenerator.TrenchWarfare, 1).ToScenario()));
        Assert.Equal(PinnedRuins, RecordFormat.MapHash(MapGenerator.Generate(MapGenerator.DecimatedRuins, 1).ToScenario()));
        foreach (var kind in MapGenerator.Kinds)
        {
            var map = MapGenerator.Generate(kind, 77);
            var again = MapFiles.Parse(MapFiles.ToJson(map), kind);
            Assert.Equal(RecordFormat.MapHash(map.ToScenario()), RecordFormat.MapHash(again.ToScenario()));
            Assert.Equal(map.RuinIds.AsEnumerable(), again.RuinIds.AsEnumerable());
        }
    }

    private const string PinnedClaim = "sha256:0e6a5244390ec730eb543425c3651f031faccb09d51d91cf9b4324e8f65048cb";
    private const string PinnedTrenchWarfare = "sha256:3959b1d4e771657e0b3da3735c28864ec44e8eaa6c3a1ea21b0de2dabff2a2f5";
    private const string PinnedRuins = "sha256:fb35e17da65e7c66deaa58d55f75932b69ae4f1b128f7ac3063cf0d027df7a26";

    [Fact]
    public void ARandomMapRequestGetsANewMapAndTheRecordNamesIt()
    {
        var recorder = new CapturingRecorder();
        var harness = new Harness(recorder: recorder, mapSeed: () => 4242);
        var one = harness.Connect();
        one.Send(new CreateMatch("ranged-1v1") { Map = "generated-claim-no-mans-land" });
        var two = harness.Connect();
        two.Send(new JoinMatch(one.Welcome!.Code));
        var created = Assert.IsType<Rules.Core.Events.MatchCreated>(harness.Lobby.LogOf(one.Welcome.Code)![0].Event);
        var made = MapGenerator.Generate(MapGenerator.ClaimNoMansLand, 4242);
        Assert.Equal(RecordFormat.MapHash(made.ToScenario()), RecordFormat.MapHash(created.Setup.Scenario!));
        Play.ToEnd(one, two, 1);
        var record = Assert.Single(recorder.Closed);
        Assert.Equal("generated-claim-no-mans-land-v1-4242", record.Map);
        Assert.Equal(RecordFormat.MapHash(made.ToScenario()), record.MapHash);
        // Trench Warfare takes its own random map and not Claim's.
        var three = harness.Connect();
        three.Send(new CreateMatch("ranged-1v1" + ScenarioSetups.TrenchWarfareSuffix) { Map = "generated-trench-warfare" });
        Assert.NotNull(three.Welcome);
        var four = harness.Connect();
        four.Send(new CreateMatch("ranged-1v1" + ScenarioSetups.TrenchWarfareSuffix) { Map = "generated-claim-no-mans-land" });
        Assert.Equal(nameof(ProtocolError.MapDoesNotFit), Assert.IsType<Rejected>(four.LastAnswer).Code);
    }
}
