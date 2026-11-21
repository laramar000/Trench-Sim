using System.Runtime.CompilerServices;
using Client.Core;
using Rules.Core.Events;

namespace Client.Tests;

// QoL stage D: every kind of event the rules can emit has a kind (and so a tag and colour) in the event list; the line's
// words stay the narrator's.
public class EventKindTests
{
    private static readonly List<Type> EventTypes = typeof(GameEvent).Assembly.GetTypes()
        .Where(t => t.IsSubclassOf(typeof(GameEvent)) && !t.IsAbstract).ToList();

    private static GameEvent Blank(Type type) => (GameEvent)RuntimeHelpers.GetUninitializedObject(type);

    [Fact]
    public void EveryEventTypeHasAKindAndATag()
    {
        Assert.True(EventTypes.Count >= 35, $"{EventTypes.Count} event types");
        foreach (var type in EventTypes)
        {
            var kind = EventText.Kind(Blank(type));
            Assert.True(kind != EventKind.Other, $"{type.Name} has no kind");
            Assert.NotEqual("EVENT", EventText.Tag(kind));
        }
    }

    [Theory]
    [InlineData(typeof(ModelMoved), EventKind.Movement)]
    [InlineData(typeof(DashRolled), EventKind.Movement)]
    [InlineData(typeof(RetreatResolved), EventKind.Movement)]
    [InlineData(typeof(RangedAttackDeclared), EventKind.Shooting)]
    [InlineData(typeof(RangedSuccessRolled), EventKind.Shooting)]
    [InlineData(typeof(ChargeDeclared), EventKind.Charge)]
    [InlineData(typeof(ChargeMoved), EventKind.Charge)]
    [InlineData(typeof(MeleeAttackDeclared), EventKind.Fight)]
    [InlineData(typeof(InjuryRolled), EventKind.Injury)]
    [InlineData(typeof(ModelStoodUp), EventKind.Status)]
    [InlineData(typeof(WarbandFled), EventKind.Status)]
    [InlineData(typeof(ActivationStarted), EventKind.Activation)]
    [InlineData(typeof(TurnStarted), EventKind.Turn)]
    [InlineData(typeof(ObjectivesScored), EventKind.Scoring)]
    [InlineData(typeof(MatchEnded), EventKind.MatchEnd)]
    public void KindsFollowWhatTheEventIs(Type type, EventKind kind) => Assert.Equal(kind, EventText.Kind(Blank(type)));

    // The log line keeps the narrator's words and its own event number; the kind is extra.
    [Fact]
    public void ALogLineKeepsItsWords()
    {
        var line = new LogLine(5, "Two takes the west zone.", null, null, EventKind.Setup);
        Assert.Equal((5, "Two takes the west zone.", EventKind.Setup), (line.Seq, line.Text, line.Kind));
        Assert.Equal(EventKind.Other, new LogLine(1, "x", null).Kind);
    }
}
