using System.Text;
using Match.Protocol;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;
using Sim;

namespace Server.Tests;

// Roster stage R3: second weapons, CLEAVE, BLOCK and IMPERVIOUS travel on the wire and back unchanged; without them a
// setup, a command and an event are written exactly as before, so every existing record keeps its bytes.
public class WeaponWireTests
{
    private static readonly string[] ProfileFields = ["secondRangedWeapon", "secondMeleeWeapon", "meleeWeaponCleave", "block", "imperviousArmour"];

    private static MatchSetup Plain() => ((MatchCreated)Runner.Run(1, Content.Load(Content.DefaultPath())).Log[0]).Setup;

    private static string Encoded(MatchSetup setup)
    {
        var created = new MatchCreated(setup);
        return Encoding.UTF8.GetString(Codec.Encode(new EventPublished(1, created, StateHash.Compute(Reducer.Apply(MatchState.None, created)))));
    }

    private static MatchSetup Armed(MatchSetup setup)
    {
        var first = setup.Models[0];
        var p = first.Profile!;
        var armed = new ModelProfile(p.Movement, p.BaseRadius, p.RangedRange, p.RangedInjuryModifier, -2, hasMeleeWeapon: true)
        {
            MeleeWeaponCleave = 2,
            Block = true,
            ImperviousArmour = -1,
            SecondRangedWeapon = new Weapon(Length.Parse("12"), 1, -1, 1, WeaponKeywords.Assault, alsoMelee: true),
            SecondMeleeWeapon = new Weapon(Length.Zero, -1, 0, 2, WeaponKeywords.Block | WeaponKeywords.Critical, cleave: 3),
        };
        return new MatchSetup(setup.RulesVersion, setup.ScenarioId, setup.TurnLimit, setup.Profile, setup.ZoneIds,
            setup.Models.Select(m => m.Id == first.Id ? m with { Profile = armed } : m), setup.Scenario);
    }

    [Fact]
    public void ASetupWithoutThemHasNoNewFields()
    {
        var text = Encoded(Plain());
        Assert.All(ProfileFields, f => Assert.DoesNotContain($"\"{f}\"", text, StringComparison.Ordinal));
    }

    [Fact]
    public void WeaponsRoundTrip()
    {
        var setup = Armed(Plain());
        var text = Encoded(setup);
        Assert.All(ProfileFields, f => Assert.Contains($"\"{f}\":", text, StringComparison.Ordinal));
        var read = Assert.IsType<EventPublished>(Codec.ParseServer(Encoding.UTF8.GetBytes(text), out var failure));
        Assert.Null(failure);
        Assert.Equal(text, Encoding.UTF8.GetString(Codec.Encode(read)));
        var profile = ((MatchCreated)read.Event).Setup.Models[0].Profile!;
        Assert.Equal(setup.Models[0].Profile, profile);
        Assert.Equal(StateHash.Compute(Reducer.Apply(MatchState.None, new MatchCreated(setup))), StateHash.Compute(Reducer.Apply(MatchState.None, read.Event)));
        Assert.NotEqual(StateHash.Compute(Reducer.Apply(MatchState.None, new MatchCreated(setup))), StateHash.Compute(Reducer.Apply(MatchState.None, new MatchCreated(Plain()))));
    }

    [Fact]
    public void ABadCleaveIsAnInvalidValue()
    {
        var text = Encoded(Armed(Plain())).Replace("\"meleeWeaponCleave\":2", "\"meleeWeaponCleave\":9", StringComparison.Ordinal);
        Assert.Contains("\"meleeWeaponCleave\":9", text, StringComparison.Ordinal);
        Assert.Null(Codec.ParseServer(Encoding.UTF8.GetBytes(text), out var failure));
        Assert.NotNull(failure);
    }

    [Fact]
    public void CommandsNameTheWeaponOnlyWhenItIsTheSecond()
    {
        Command[] firsts = [new Shoot(Seat.One, new ModelId("A1"), new ModelId("B1")), new FightModel(Seat.One, new ModelId("A1"), new ModelId("B1")), new FreeFight(Seat.Two, new ModelId("B1"))];
        foreach (var command in firsts)
        {
            var text = Encoding.UTF8.GetString(Codec.Encode(new SubmitCommand("c1", 1, 0, command)));
            Assert.DoesNotContain("\"weapon\"", text, StringComparison.Ordinal);
        }
        Command[] seconds = [new Shoot(Seat.One, new ModelId("A1"), new ModelId("B1")) { Weapon = 1 }, new FightModel(Seat.One, new ModelId("A1"), new ModelId("B1")) { Weapon = 1 }, new FreeFight(Seat.Two, new ModelId("B1")) { Weapon = 1 }];
        foreach (var command in seconds)
        {
            var bytes = Codec.Encode(new SubmitCommand("c1", 1, 0, command));
            Assert.Contains("\"weapon\":1", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
            var read = Assert.IsType<SubmitCommand>(Codec.ParseClient(bytes, out var failure));
            Assert.Null(failure);
            Assert.Equal(command, read.Command);
        }
    }

    [Fact]
    public void MeleeAttackFactsRoundTrip()
    {
        var plain = new MeleeAttackDeclared(new ModelId("A1"), new ModelId("B1"), false, null);
        var plainText = Encoding.UTF8.GetString(Codec.Encode(new EventPublished(5, plain, new string('0', 64))));
        foreach (var field in new[] { "weapon", "offHand", "block" })
        {
            Assert.DoesNotContain($"\"{field}\"", plainText, StringComparison.Ordinal);
        }
        var full = plain with { Weapon = 1, OffHand = true, Block = true };
        var bytes = Codec.Encode(new EventPublished(5, full, new string('0', 64)));
        var read = Assert.IsType<EventPublished>(Codec.ParseServer(bytes, out var failure));
        Assert.Null(failure);
        var back = Assert.IsType<MeleeAttackDeclared>(read.Event);
        Assert.Equal((1, true, true, 2), (back.Weapon, back.OffHand, back.Block, back.PenaltyDice));
    }
}
