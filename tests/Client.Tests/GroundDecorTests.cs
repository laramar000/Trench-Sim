using Client.Core;
using Rules.Core;
using Rules.Core.State;

namespace Client.Tests;

// Graphics stage G2: the ground's craters and duckboards are decoration only. Each battlefield layout gets the same
// ones every time, and they stay inside the table, flat, and clear of every terrain piece, zone and each other.
public class GroundDecorTests
{
    private static IEnumerable<BoardScene> Scenes => PreviewAgreementTests.States.Value
        .Where(s => s.Phase == Phase.Activation)
        .Select(s => BoardScene.Of(s, Seat.One, ContentNames.None))
        .OfType<BoardScene>()
        .GroupBy(s => (s.Width, s.Depth, string.Join(",", s.Terrain.Select(t => t.Id + t.Area))))
        .Select(g => g.First());

    [Fact]
    public void EveryLayoutGetsDecalsClearOfTerrainZonesAndEachOther()
    {
        var scenes = Scenes.ToList();
        Assert.NotEmpty(scenes);
        foreach (var scene in scenes)
        {
            var spots = GroundDecor.For(scene);
            Assert.Contains(spots, s => s.Kind == GroundDecor.Crater);
            for (var i = 0; i < spots.Count; i++)
            {
                Assert.True(GroundDecor.Clear(scene, spots.Where((_, j) => j != i), spots[i]), $"{spots[i]}");
            }
        }
    }

    [Fact]
    public void TheSameLayoutGetsTheSameDecalsEvenAfterModelsMove()
    {
        var scene = Scenes.First();
        var moved = scene with { Models = [] };
        Assert.Equal(GroundDecor.For(scene), GroundDecor.For(moved));
    }

    [Fact]
    public void DecalsSitBelowTheZoneTintAndUnderTheHeightLimit()
    {
        Assert.InRange(GroundDecor.Lift, 0.001, 0.019);
        Assert.True(GroundDecor.Lift < GroundDecor.MaxHeight);
    }
}
