using System.Text;
using Match.Protocol;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;
using Sim;

namespace Server.Tests;

// Roster stage R4b (U102): a weapon's BLAST travels on the wire and back unchanged, and so does the BlastHit event; a
// setup without BLAST writes nothing new, so every existing record keeps its bytes. R4c (U103): a SCATTER attack's command
// and events do too.
public class BlastWireTests
{
    private static MatchSetup Plain() => ((MatchCreated)Runner.Run(1, Content.Load(Content.DefaultPath())).Log[0]).Setup;

    private static string Encoded(GameEvent e) =>
        Encoding.UTF8.GetString(Codec.Encode(new EventPublished(1, e, new string('0', 64))));

    [Fact]
    public void BlastFieldsRoundTripAndAreLeftOutWhenZero()
    {
        var plain = Encoded(new MatchCreated(Plain()));
        Assert.DoesNotContain("blast", plain, StringComparison.OrdinalIgnoreCase);

        var setup = Plain();
        var first = setup.Models[0];
        var bomber = first.Profile! with
        {
            RangedWeaponBlast = 3,
            Grenade = new Weapon(Length.Parse("8")) { Blast = 2 },
        };
        var blasting = new MatchSetup(setup.RulesVersion, setup.ScenarioId, setup.TurnLimit, setup.Profile, setup.ZoneIds,
            setup.Models.Select(m => m.Id == first.Id ? m with { Profile = bomber } : m), setup.Scenario);
        var created = new MatchCreated(blasting);
        var text = Encoded(created);
        Assert.Contains("\"rangedWeaponBlast\":3", text, StringComparison.Ordinal);
        Assert.Contains("\"blast\":2", text, StringComparison.Ordinal);
        var read = Assert.IsType<EventPublished>(Codec.ParseServer(Encoding.UTF8.GetBytes(text), out var failure));
        Assert.Null(failure);
        Assert.Equal(text, Encoded(read.Event));
        Assert.Equal(bomber, ((MatchCreated)read.Event).Setup.Models[0].Profile);
        var hash = StateHash.Compute(Reducer.Apply(MatchState.None, created));
        Assert.Equal(hash, StateHash.Compute(Reducer.Apply(MatchState.None, read.Event)));
        Assert.NotEqual(hash, StateHash.Compute(Reducer.Apply(MatchState.None, new MatchCreated(Plain()))));
    }

    [Fact]
    public void ABlastHitRoundTrips()
    {
        var hit = new BlastHit(new ModelId("A1"), new ModelId("B1"), [new ModelId("B1"), new ModelId("A2")]);
        var read = Assert.IsType<EventPublished>(Codec.ParseServer(Encoding.UTF8.GetBytes(Encoded(hit)), out var failure));
        Assert.Null(failure);
        var back = Assert.IsType<BlastHit>(read.Event);
        Assert.Equal((hit.Attacker, hit.Target), (back.Attacker, back.Target));
        Assert.Equal(hit.Hit.ToArray(), back.Hit.ToArray());

        // A hit list that does not start with the target is not a valid event.
        var bad = Encoded(hit).Replace("\"hit\":[\"B1\",\"A2\"]", "\"hit\":[\"A2\",\"B1\"]", StringComparison.Ordinal);
        Assert.NotEqual(Encoded(hit), bad);
        Assert.Null(Codec.ParseServer(Encoding.UTF8.GetBytes(bad), out failure));
        Assert.NotNull(failure);
    }

    [Fact]
    public void ABlastTooBigIsAnInvalidValue()
    {
        var setup = Plain();
        var first = setup.Models[0];
        var text = Encoded(new MatchCreated(new MatchSetup(setup.RulesVersion, setup.ScenarioId, setup.TurnLimit, setup.Profile, setup.ZoneIds,
            setup.Models.Select(m => m.Id == first.Id ? m with { Profile = first.Profile! with { RangedWeaponBlast = 3 } } : m), setup.Scenario)));
        var bad = text.Replace("\"rangedWeaponBlast\":3", "\"rangedWeaponBlast\":9", StringComparison.Ordinal);
        Assert.Null(Codec.ParseServer(Encoding.UTF8.GetBytes(bad), out var failure));
        Assert.NotNull(failure);
    }

    [Fact]
    public void AScatterRoundTrips()
    {
        var at = new Point2(Length.Parse("15.25"), Length.Parse("2"));
        Command scatter = new ScatterAttack(Seat.Two, at);
        var bytes = Codec.Encode(new SubmitCommand("c1", 1, 0, scatter));
        Assert.Equal(scatter, Assert.IsType<SubmitCommand>(Codec.ParseClient(bytes, out var failure)).Command);
        Assert.Null(failure);

        GameEvent[] events =
        [
            new AttackScattered(new ModelId("A1"), new ModelId("B1"), at, 3, [new ModelId("A2"), new ModelId("B1")]),
            new AttackScattered(new ModelId("A1"), new ModelId("B1"), at, 3, []),
            new ScatterMissed(new ModelId("A1"), new ModelId("B1"), 5),
        ];
        foreach (var e in events)
        {
            var text = Encoded(e);
            var read = Assert.IsType<EventPublished>(Codec.ParseServer(Encoding.UTF8.GetBytes(text), out failure));
            Assert.Null(failure);
            Assert.Equal(text, Encoded(read.Event));
        }
        var bad = Encoded(events[2]).Replace("\"inches\":5", "\"inches\":6", StringComparison.Ordinal);
        Assert.Null(Codec.ParseServer(Encoding.UTF8.GetBytes(bad), out failure));
        Assert.NotNull(failure);
    }

    // Roster stage R5a (U105, U104): a BLAST attack aimed at a point, a scatter from a point (no target) and a DEADLY
    // Injury Roll (three dice kept) travel and come back unchanged; a ShootPoint with the first weapon writes no weapon.
    [Fact]
    public void APointAttackAndADeadlyRollRoundTrip()
    {
        var at = new Point2(Length.Parse("13.5"), Length.Parse("2"));
        foreach (var command in new Command[] { new ShootPoint(Seat.One, new ModelId("A1"), at), new ShootPoint(Seat.One, new ModelId("A1"), at) { Weapon = 1 } })
        {
            var bytes = Codec.Encode(new SubmitCommand("c1", 1, 0, command));
            Assert.Equal(command, Assert.IsType<SubmitCommand>(Codec.ParseClient(bytes, out var failure)).Command);
            Assert.Null(failure);
            Assert.Equal(((ShootPoint)command).Weapon != 0, Encoding.UTF8.GetString(bytes).Contains("\"weapon\"", StringComparison.Ordinal));
        }

        GameEvent[] events =
        [
            new PointAttackDeclared(new ModelId("A1"), at, true),
            new PointAttackDeclared(new ModelId("A1"), at, false) { Weapon = 1 },
            new PointSuccessRolled(new ModelId("A1"), at, Rules.Core.Dice.SuccessRoll.Roll(new SeededDice(3), 1, 0, risky: false)),
            new PointBlastHit(new ModelId("A1"), at, [new ModelId("B1"), new ModelId("A2")]),
            new PointBlastHit(new ModelId("A1"), at, []),
            new AttackScattered(new ModelId("A1"), null, at, 3, [new ModelId("B1")]),
            new ScatterMissed(new ModelId("A1"), null, 5),
            new InjuryRolled(new ModelId("A1"), new ModelId("B1"), Rules.Core.Dice.InjuryRoll.Roll(new SeededDice(4), 1, 0, deadly: true)),
        ];
        foreach (var e in events)
        {
            var text = Encoded(e);
            var read = Assert.IsType<EventPublished>(Codec.ParseServer(Encoding.UTF8.GetBytes(text), out var failure));
            Assert.Null(failure);
            Assert.Equal(text, Encoded(read.Event));
        }
        Assert.DoesNotContain("weapon", Encoded(events[0]), StringComparison.Ordinal);
        Assert.True(((InjuryRolled)events[^1]).Injury.Deadly);
        Assert.Equal(3, ((InjuryRolled)events[^1]).Injury.Dice.Kept.Count);
        var doubled = Encoded(events[3]).Replace("\"hit\":[\"B1\",\"A2\"]", "\"hit\":[\"B1\",\"B1\"]", StringComparison.Ordinal);
        Assert.NotEqual(Encoded(events[3]), doubled);
        Assert.Null(Codec.ParseServer(Encoding.UTF8.GetBytes(doubled), out var bad));
        Assert.NotNull(bad);
    }
}
