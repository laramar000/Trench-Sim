using System.Text;
using Match.Protocol;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.State;
using Xunit;

namespace Server.Tests;

// Charge carry-on (p. 37; U126): the command's and the declaration's flag travel on the wire and back unchanged, and a
// plain charge writes nothing new, so every existing record keeps its bytes.
public class CarryOnWireTests
{
    private static readonly ModelId A1 = new("A1");
    private static readonly ModelId B1 = new("B1");

    private static string Encoded(GameEvent e) =>
        Encoding.UTF8.GetString(Codec.Encode(new EventPublished(1, e, new string('0', 64))));

    [Fact]
    public void ACommandWithTheFlagRoundTripsAndOneWithoutWritesNothingNew()
    {
        var plain = Encoding.UTF8.GetString(Codec.Encode(new SubmitCommand("c1", 1, 0, new ChargeModel(Seat.One, A1, B1))));
        Assert.DoesNotContain("carryon", plain, StringComparison.OrdinalIgnoreCase);

        var flagged = new ChargeModel(Seat.One, A1, B1) { CarryOn = true };
        var bytes = Codec.Encode(new SubmitCommand("c1", 1, 0, flagged));
        Assert.Contains("\"carryOn\":true", Encoding.UTF8.GetString(bytes), StringComparison.OrdinalIgnoreCase);
        var read = Assert.IsType<SubmitCommand>(Codec.ParseClient(bytes, out var failure));
        Assert.Null(failure);
        Assert.Equal(flagged, read.Command);
        Assert.False(((ChargeModel)Assert.IsType<SubmitCommand>(Codec.ParseClient(Encoding.UTF8.GetBytes(plain), out failure)).Command).CarryOn);
    }

    [Fact]
    public void ADeclarationWithTheFlagRoundTrips()
    {
        Assert.DoesNotContain("carryon", Encoded(new ChargeDeclared(A1, B1)), StringComparison.OrdinalIgnoreCase);
        var text = Encoded(new ChargeDeclared(A1, B1) { CarryOn = true });
        var read = Assert.IsType<EventPublished>(Codec.ParseServer(Encoding.UTF8.GetBytes(text), out var failure));
        Assert.Null(failure);
        Assert.True(((ChargeDeclared)read.Event).CarryOn);
        Assert.Equal(text, Encoded(read.Event));
    }
}
