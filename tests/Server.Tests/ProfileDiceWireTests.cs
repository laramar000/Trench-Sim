using System.Text;
using Match.Protocol;
using Rules.Core;
using Rules.Core.Events;
using Rules.Core.State;
using Sim;

namespace Server.Tests;

// Roster stage R1: a profile's dice travel on the wire and back unchanged, and a setup without them is written exactly
// as before (no dice fields at all), so every existing record and content fingerprint keeps its bytes.
public class ProfileDiceWireTests
{
    private static readonly string[] Fields =
        ["rangedDice", "meleeDice", "rangedWeaponDice", "rangedWeaponInjuryDice", "meleeWeaponDice", "meleeWeaponInjuryDice"];

    private static MatchSetup Plain() => ((MatchCreated)Runner.Run(1, Content.Load(Content.DefaultPath())).Log[0]).Setup;

    private static string Encoded(MatchSetup setup)
    {
        var created = new MatchCreated(setup);
        return Encoding.UTF8.GetString(Codec.Encode(new EventPublished(1, created, StateHash.Compute(Reducer.Apply(MatchState.None, created)))));
    }

    private static MatchSetup WithDice(MatchSetup setup, int melee = 2)
    {
        var first = setup.Models[0];
        var p = first.Profile!;
        var armed = new ModelProfile(p.Movement, p.BaseRadius, p.RangedRange, p.RangedInjuryModifier, p.Armour, hasMeleeWeapon: true)
        {
            RangedDice = 1, MeleeDice = melee, RangedWeaponDice = -1, RangedWeaponInjuryDice = 2, MeleeWeaponDice = 1, MeleeWeaponInjuryDice = -1,
        };
        return new MatchSetup(setup.RulesVersion, setup.ScenarioId, setup.TurnLimit, setup.Profile, setup.ZoneIds,
            setup.Models.Select(m => m.Id == first.Id ? m with { Profile = armed } : m), setup.Scenario);
    }

    [Fact]
    public void ASetupWithoutDiceHasNoDiceFields()
    {
        var text = Encoded(Plain());
        Assert.All(Fields, f => Assert.DoesNotContain($"\"{f}\"", text, StringComparison.Ordinal));
        var read = Assert.IsType<EventPublished>(Codec.ParseServer(Encoding.UTF8.GetBytes(text), out var failure));
        Assert.Null(failure);
        Assert.Equal(text, Encoding.UTF8.GetString(Codec.Encode(read)));
    }

    [Fact]
    public void DiceRoundTrip()
    {
        var setup = WithDice(Plain());
        var text = Encoded(setup);
        Assert.All(Fields, f => Assert.Contains($"\"{f}\":", text, StringComparison.Ordinal));
        var read = Assert.IsType<EventPublished>(Codec.ParseServer(Encoding.UTF8.GetBytes(text), out var failure));
        Assert.Null(failure);
        Assert.Equal(text, Encoding.UTF8.GetString(Codec.Encode(read)));
        var profile = ((MatchCreated)read.Event).Setup.Models[0].Profile!;
        Assert.Equal((1, 2, -1, 2, 1, -1), (profile.RangedDice, profile.MeleeDice, profile.RangedWeaponDice, profile.RangedWeaponInjuryDice, profile.MeleeWeaponDice, profile.MeleeWeaponInjuryDice));
        Assert.Equal(StateHash.Compute(Reducer.Apply(MatchState.None, new MatchCreated(setup))), StateHash.Compute(Reducer.Apply(MatchState.None, read.Event)));
    }

    [Fact]
    public void DiceOutOfRangeAreAnInvalidValue()
    {
        var text = Encoded(WithDice(Plain())).Replace("\"meleeDice\":2", "\"meleeDice\":9", StringComparison.Ordinal);
        Assert.Contains("\"meleeDice\":9", text, StringComparison.Ordinal);
        Assert.Null(Codec.ParseServer(Encoding.UTF8.GetBytes(text), out var failure));
        Assert.NotNull(failure);
    }
}
