using System.Text;
using Match.Protocol;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.State;
using Sim;

namespace Server.Tests;

// Bloodbath Rolls (p. 47, U123): the command's and the Injury Roll's Bloodbath flags travel on the wire and back; without
// them nothing new is written, so every existing record keeps its bytes.
public class BloodbathWireTests
{
    private static string Encoded(GameEvent e) =>
        Encoding.UTF8.GetString(Codec.Encode(new EventPublished(1, e, new string('0', 64))));

    [Fact]
    public void BloodbathFlagsRoundTripAndAreLeftOutWhenFalse()
    {
        foreach (var command in new[] { new SpendMarkers(Seat.One, 6), new SpendMarkers(Seat.One, 6) { Bloodbath = true } })
        {
            var bytes = Codec.Encode(new SubmitCommand("c1", 1, 0, command));
            Assert.Equal(command.Bloodbath, Encoding.UTF8.GetString(bytes).Contains("bloodbath", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(command, Assert.IsType<SubmitCommand>(Codec.ParseClient(bytes, out var failure)).Command);
            Assert.Null(failure);
        }

        var (a, b) = (new ModelId("A1"), new ModelId("B1"));
        var rolled = new InjuryRolled(a, b, InjuryRoll.Roll(new SeededDice(1), bloodbath: true));
        Assert.DoesNotContain("bloodbath", Encoded(rolled), StringComparison.OrdinalIgnoreCase);
        var text = Encoded(rolled with { Bloodbath = true });
        var read = Assert.IsType<EventPublished>(Codec.ParseServer(Encoding.UTF8.GetBytes(text), out var parse));
        Assert.Null(parse);
        Assert.True(((InjuryRolled)read.Event).Bloodbath);
        Assert.Equal(3, ((InjuryRolled)read.Event).Injury.Dice.Kept.Count);
        Assert.Equal(new MarkersSpent(Seat.One, b, 6, MarkerSpendPurpose.Bloodbath),
            ((EventPublished)Codec.ParseServer(Codec.Encode(new EventPublished(1, new MarkersSpent(Seat.One, b, 6, MarkerSpendPurpose.Bloodbath), new string('0', 64))), out _)!).Event);
    }
}
