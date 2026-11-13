using System.Text;
using Match.Protocol;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Server.Tests;

// Line-of-sight stage L3: the Elevated position flag travels on the wire, and an attack without it is written exactly
// as before (no "elevated" field), so every existing record keeps its bytes.
public class ElevatedWireTests
{
    private static RangedAttackDeclared Shot(bool elevated) =>
        new(new ModelId("A1"), new ModelId("B1"), false, false, null, ModelLevel.High, ModelLevel.Ground, 0) { Elevated = elevated };

    private static string Encoded(GameEvent e) => Encoding.UTF8.GetString(Codec.Encode(new EventPublished(7, e, new string('0', 64))));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheFlagIsLeftOutWhenFalseAndRoundTripsWhenTrue(bool elevated)
    {
        foreach (GameEvent e in new GameEvent[] { Shot(elevated), new FriendlyTargetChosen(new ModelId("A1"), new ModelId("A2"), false, false, null) { Elevated = elevated } })
        {
            var text = Encoded(e);
            Assert.Equal(elevated, text.Contains("\"elevated\":true", StringComparison.Ordinal));
            Assert.Equal(elevated, text.Contains("elevated", StringComparison.Ordinal));
            var read = Assert.IsType<EventPublished>(Codec.ParseServer(Encoding.UTF8.GetBytes(text), out var failure));
            Assert.Null(failure);
            Assert.Equal(text, Encoded(read.Event));
        }
        Assert.Equal(elevated ? 1 : 0, Shot(elevated).BonusDice);
    }
}
