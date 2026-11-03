using System.Text;
using Match.Protocol;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.State;

namespace Server.Tests;

// Bloodbath Rolls (p. 47; U124): the command's and the roll's Bloodbath flag and the new spend purpose travel on the wire
// and back unchanged, and an ordinary spend or roll writes nothing new, so every existing record keeps its bytes.
public class BloodbathWireTests
{
    private static string Encoded(GameEvent e) =>
        Encoding.UTF8.GetString(Codec.Encode(new EventPublished(1, e, new string('0', 64))));

    private static InjuryRolled Roll(bool bloodbath) =>
        new(new ModelId("A1"), new ModelId("B1"), InjuryRoll.Roll(new RandomDice(), deadly: bloodbath)) { Bloodbath = bloodbath };

    [Fact]
    public void ACommandWithTheFlagRoundTripsAndOneWithoutWritesNothingNew()
    {
        var plain = Encoding.UTF8.GetString(Codec.Encode(new SubmitCommand("c1", 1, 0, new SpendMarkers(Seat.One, 2))));
        Assert.DoesNotContain("bloodbath", plain, StringComparison.OrdinalIgnoreCase);

        var flagged = new SpendMarkers(Seat.One, 6) { Bloodbath = true };
        var bytes = Codec.Encode(new SubmitCommand("c1", 1, 0, flagged));
        Assert.Contains("bloodbath", Encoding.UTF8.GetString(bytes), StringComparison.OrdinalIgnoreCase);
        var read = Assert.IsType<SubmitCommand>(Codec.ParseClient(bytes, out var failure));
        Assert.Null(failure);
        Assert.Equal(flagged, read.Command);

        var back = Assert.IsType<SubmitCommand>(Codec.ParseClient(Encoding.UTF8.GetBytes(plain), out failure));
        Assert.Null(failure);
        Assert.False(((SpendMarkers)back.Command).Bloodbath);
    }

    [Fact]
    public void ABloodbathRollAndItsSpendRoundTrip()
    {
        Assert.DoesNotContain("bloodbath", Encoded(Roll(false)), StringComparison.OrdinalIgnoreCase);
        var text = Encoded(Roll(true));
        Assert.Contains("\"bloodbath\":true", text, StringComparison.OrdinalIgnoreCase);
        var read = Assert.IsType<EventPublished>(Codec.ParseServer(Encoding.UTF8.GetBytes(text), out var failure));
        Assert.Null(failure);
        Assert.True(((InjuryRolled)read.Event).Bloodbath);
        Assert.Equal(text, Encoded(read.Event));

        var spent = new MarkersSpent(Seat.One, new ModelId("B1"), 6, MarkerSpendPurpose.Bloodbath);
        var spentText = Encoded(spent);
        var spentRead = Assert.IsType<EventPublished>(Codec.ParseServer(Encoding.UTF8.GetBytes(spentText), out failure));
        Assert.Null(failure);
        Assert.Equal(MarkerSpendPurpose.Bloodbath, ((MarkersSpent)spentRead.Event).Purpose);
        Assert.Equal(spentText, Encoded(spentRead.Event));
    }
}
