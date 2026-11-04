using System.Text;
using Match.Protocol;
using Rules.Core;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;
using Sim;

namespace Server.Tests;

// Proposal M1: a model whose ranged weapon is also a Melee Weapon travels on the wire and back unchanged, and a setup
// without one is written exactly as before (no "rangedAlsoMelee" field at all), so every existing record and content
// fingerprint keeps its bytes.
public class DualPurposeWireTests
{
    private static MatchSetup Plain() => ((MatchCreated)Runner.Run(1, Content.Load(Content.DefaultPath())).Log[0]).Setup;

    private static string Encoded(MatchSetup setup)
    {
        var created = new MatchCreated(setup);
        return Encoding.UTF8.GetString(Codec.Encode(new EventPublished(1, created, StateHash.Compute(Reducer.Apply(MatchState.None, created)))));
    }

    private static MatchSetup WithPistol(MatchSetup setup)
    {
        var first = setup.Models[0];
        var pistol = new ModelProfile(first.Profile!.Movement, first.Profile.BaseRadius, Length.FromInches(12)) { RangedAlsoMelee = true };
        return new MatchSetup(setup.RulesVersion, setup.ScenarioId, setup.TurnLimit, setup.Profile, setup.ZoneIds,
            setup.Models.Select(m => m.Id == first.Id ? m with { Profile = pistol } : m), setup.Scenario);
    }

    [Fact]
    public void ASetupWithoutDualPurposeWeaponsHasNoField()
    {
        var text = Encoded(Plain());
        Assert.DoesNotContain("rangedAlsoMelee", text, StringComparison.Ordinal);
        var read = Assert.IsType<EventPublished>(Codec.ParseServer(Encoding.UTF8.GetBytes(text), out var failure));
        Assert.Null(failure);
        Assert.All(((MatchCreated)read.Event).Setup.Models, m => Assert.False(m.Profile!.RangedAlsoMelee));
        Assert.Equal(text, Encoding.UTF8.GetString(Codec.Encode(read)));
    }

    [Fact]
    public void ADualPurposeWeaponRoundTrips()
    {
        var setup = WithPistol(Plain());
        var text = Encoded(setup);
        Assert.Contains("\"rangedAlsoMelee\":true", text, StringComparison.Ordinal);
        var read = Assert.IsType<EventPublished>(Codec.ParseServer(Encoding.UTF8.GetBytes(text), out var failure));
        Assert.Null(failure);
        Assert.Equal(text, Encoding.UTF8.GetString(Codec.Encode(read)));
        Assert.True(((MatchCreated)read.Event).Setup.Models[0].Profile!.RangedAlsoMelee);
        Assert.Equal(StateHash.Compute(Reducer.Apply(MatchState.None, new MatchCreated(setup))), StateHash.Compute(Reducer.Apply(MatchState.None, read.Event)));
    }

    [Fact]
    public void ADualPurposeFlagWithoutARangedWeaponIsAnInvalidValue()
    {
        var text = Encoded(WithPistol(Plain()))
            .Replace("\"rangedRange\":12000,\"rangedInjuryModifier\":0,\"armour\":0,\"hasMeleeWeapon\":false,\"meleeInjuryModifier\":0,\"rangedAlsoMelee\":true",
                "\"rangedRange\":0,\"rangedInjuryModifier\":0,\"armour\":0,\"hasMeleeWeapon\":false,\"meleeInjuryModifier\":0,\"rangedAlsoMelee\":true", StringComparison.Ordinal);
        Assert.Contains("\"rangedRange\":0,", text, StringComparison.Ordinal);
        Assert.Null(Codec.ParseServer(Encoding.UTF8.GetBytes(text), out var failure));
        Assert.NotNull(failure);
    }
}
