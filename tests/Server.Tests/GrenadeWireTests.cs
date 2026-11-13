using System.Text;
using Match.Protocol;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;
using Sim;

namespace Server.Tests;

// Roster stage R4a (U101): a model's grenade and what it NEGATEs travel on the wire and back unchanged, and so do the
// extra BLOOD MARKERS of an Injury Roll and a mine's NEGATE SHRAPNEL; without them nothing new is written, so every
// existing record keeps its bytes.
public class GrenadeWireTests
{
    private static MatchSetup Plain() => ((MatchCreated)Runner.Run(1, Content.Load(Content.DefaultPath())).Log[0]).Setup;

    private static string Encoded(GameEvent e) =>
        Encoding.UTF8.GetString(Codec.Encode(new EventPublished(1, e, new string('0', 64))));

    private static MatchSetup Bombing(MatchSetup setup)
    {
        var first = setup.Models[0];
        var p = first.Profile!;
        var bomber = new ModelProfile(p.Movement, p.BaseRadius, p.RangedRange, p.RangedInjuryModifier, p.Armour, p.HasMeleeWeapon, p.MeleeInjuryModifier)
        {
            Grenade = new Weapon(Length.Parse("8"), 0, 0, 0, WeaponKeywords.Shrapnel | WeaponKeywords.Assault),
            Negates = WeaponKeywords.Gas | WeaponKeywords.Fire,
        };
        return new MatchSetup(setup.RulesVersion, setup.ScenarioId, setup.TurnLimit, setup.Profile, setup.ZoneIds,
            setup.Models.Select(m => m.Id == first.Id ? m with { Profile = bomber } : m), setup.Scenario);
    }

    [Fact]
    public void AGrenadeAndNegatesRoundTrip()
    {
        var plain = Encoded(new MatchCreated(Plain()));
        Assert.DoesNotContain("\"grenade\"", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("\"negates\"", plain, StringComparison.Ordinal);

        var setup = Bombing(Plain());
        var created = new MatchCreated(setup);
        var text = Encoded(created);
        Assert.Contains("\"grenade\":", text, StringComparison.Ordinal);
        Assert.Contains("\"negates\":", text, StringComparison.Ordinal);
        var read = Assert.IsType<EventPublished>(Codec.ParseServer(Encoding.UTF8.GetBytes(text), out var failure));
        Assert.Null(failure);
        Assert.Equal(text, Encoded(read.Event));
        Assert.Equal(setup.Models[0].Profile, ((MatchCreated)read.Event).Setup.Models[0].Profile);
        var hash = StateHash.Compute(Reducer.Apply(MatchState.None, created));
        Assert.Equal(hash, StateHash.Compute(Reducer.Apply(MatchState.None, read.Event)));
        Assert.NotEqual(hash, StateHash.Compute(Reducer.Apply(MatchState.None, new MatchCreated(Plain()))));
    }

    [Fact]
    public void ABadNegateIsAnInvalidValue()
    {
        var text = Encoded(new MatchCreated(Bombing(Plain())));
        var negates = text.IndexOf("\"negates\":", StringComparison.Ordinal);
        var end = text.IndexOfAny([',', '}'], negates);
        var bad = text[..negates] + "\"negates\":1" + text[end..]; // Critical
        Assert.Null(Codec.ParseServer(Encoding.UTF8.GetBytes(bad), out var failure));
        Assert.NotNull(failure);
    }

    [Fact]
    public void ExtraMarkersAndAMinesNegateRoundTrip()
    {
        var injury = InjuryRoll.Roll(new SeededDice(4));
        var plain = new InjuryRolled(new ModelId("A1"), new ModelId("B1"), injury);
        Assert.DoesNotContain("\"extraMarkers\"", Encoded(plain), StringComparison.Ordinal);
        var marked = plain with { ExtraMarkers = 2 };
        var back = Assert.IsType<EventPublished>(Codec.ParseServer(Encoding.UTF8.GetBytes(Encoded(marked)), out var failure));
        Assert.Null(failure);
        Assert.Equal(2, Assert.IsType<InjuryRolled>(back.Event).ExtraMarkers);

        var mine = new MineExploded(new ModelId("A1"), "mine-1", injury);
        Assert.DoesNotContain("\"negated\"", Encoded(mine), StringComparison.Ordinal);
        var negated = Assert.IsType<EventPublished>(Codec.ParseServer(Encoding.UTF8.GetBytes(Encoded(mine with { Negated = true })), out failure));
        Assert.Null(failure);
        Assert.True(Assert.IsType<MineExploded>(negated.Event).Negated);

        Command grenade = new Shoot(Seat.One, new ModelId("A1"), new ModelId("B1")) { Weapon = ModelProfile.GrenadeWeapon };
        var bytes = Codec.Encode(new SubmitCommand("c1", 1, 0, grenade));
        Assert.Contains("\"weapon\":2", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        Assert.Equal(grenade, Assert.IsType<SubmitCommand>(Codec.ParseClient(bytes, out failure)).Command);
        Assert.Null(failure);
    }
}
