using System.Text;
using Match.Protocol;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;
using Sim;
using Xunit;

namespace Server.Tests;

// Field Shrine, stage 1 (U127): its commands, events and the profile flag travel on the wire and back unchanged, and a
// setup without one writes nothing new, so every existing record keeps its bytes.
public class FieldShrineWireTests
{
    private static readonly ModelId A1 = new("A1");

    private static string Encoded(GameEvent e) =>
        Encoding.UTF8.GetString(Codec.Encode(new EventPublished(1, e, new string('0', 64))));

    private static MatchSetup Plain() => ((MatchCreated)Runner.Run(1, Content.Load(Content.DefaultPath())).Log[0]).Setup;

    [Fact]
    public void ShrineEventsAndCommandsRoundTrip()
    {
        foreach (GameEvent e in new GameEvent[] { new ShrinePlaced(A1, new Point2(Length.FromInches(3), Length.FromInches(20))), new ShrineDeclined(A1) })
        {
            var text = Encoded(e);
            var read = Assert.IsType<EventPublished>(Codec.ParseServer(Encoding.UTF8.GetBytes(text), out var failure));
            Assert.Null(failure);
            Assert.Equal(e, read.Event);
            Assert.Equal(text, Encoded(read.Event));
        }
        foreach (Command c in new Command[] { new PlaceShrine(Seat.One, A1, new Point2(Length.FromInches(3), Length.FromInches(20))), new DeclineShrine(Seat.One, A1) })
        {
            var read = Assert.IsType<SubmitCommand>(Codec.ParseClient(Codec.Encode(new SubmitCommand("c1", 1, 0, c)), out var failure));
            Assert.Null(failure);
            Assert.Equal(c, read.Command);
        }
    }

    [Fact]
    public void TheProfileFlagRoundTripsAndIsLeftOutWhenFalse()
    {
        Assert.DoesNotContain("fieldShrine", Encoded(new MatchCreated(Plain())), StringComparison.OrdinalIgnoreCase);
        var setup = Plain();
        var first = setup.Models[0];
        var carrier = first.Profile! with { FieldShrine = true };
        var created = new MatchCreated(new MatchSetup(setup.RulesVersion, setup.ScenarioId, setup.TurnLimit, setup.Profile, setup.ZoneIds,
            setup.Models.Select(m => m.Id == first.Id ? m with { Profile = carrier } : m), setup.Scenario));
        var text = Encoded(created);
        Assert.Contains("\"fieldShrine\":true", text, StringComparison.OrdinalIgnoreCase);
        var back = (MatchCreated)Assert.IsType<EventPublished>(Codec.ParseServer(Encoding.UTF8.GetBytes(text), out var failure)).Event;
        Assert.Null(failure);
        Assert.True(back.Setup.Models[0].Profile!.FieldShrine);
        Assert.NotEqual(StateHash.Compute(Reducer.Apply(MatchState.None, created)), StateHash.Compute(Reducer.Apply(MatchState.None, new MatchCreated(Plain()))));
    }
}
