using System.Text;
using Match.Protocol;
using Rules.Core;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.State;
using Sim;

namespace Server.Tests;

// Roster stage R5b (U106): a profile's traits (FEAR, TOUGH, LEADER...) and the events' FEAR and TOUGH flags travel on the
// wire and back unchanged; a setup and events without them write nothing new, so every existing record keeps its bytes.
public class TraitWireTests
{
    private static MatchSetup Plain() => ((MatchCreated)Runner.Run(1, Content.Load(Content.DefaultPath())).Log[0]).Setup;

    private static string Encoded(GameEvent e) =>
        Encoding.UTF8.GetString(Codec.Encode(new EventPublished(1, e, new string('0', 64))));

    private static GameEvent RoundTrip(GameEvent e)
    {
        var text = Encoded(e);
        var read = Assert.IsType<EventPublished>(Codec.ParseServer(Encoding.UTF8.GetBytes(text), out var failure));
        Assert.Null(failure);
        Assert.Equal(text, Encoded(read.Event));
        return read.Event;
    }

    [Fact]
    public void TraitsRoundTripAndAreLeftOutWhenNone()
    {
        Assert.DoesNotContain("traits", Encoded(new MatchCreated(Plain())), StringComparison.OrdinalIgnoreCase);
        var setup = Plain();
        var first = setup.Models[0];
        var leader = first.Profile! with { Traits = ModelTraits.Leader | ModelTraits.Tough | ModelTraits.NegateHeavy };
        var created = new MatchCreated(new MatchSetup(setup.RulesVersion, setup.ScenarioId, setup.TurnLimit, setup.Profile, setup.ZoneIds,
            setup.Models.Select(m => m.Id == first.Id ? m with { Profile = leader } : m), setup.Scenario));
        Assert.Contains("\"traits\"", Encoded(created), StringComparison.Ordinal);
        var back = (MatchCreated)RoundTrip(created);
        Assert.Equal(leader, back.Setup.Models[0].Profile);
        Assert.Equal(StateHash.Compute(Reducer.Apply(MatchState.None, created)), StateHash.Compute(Reducer.Apply(MatchState.None, back)));
        Assert.NotEqual(StateHash.Compute(Reducer.Apply(MatchState.None, created)), StateHash.Compute(Reducer.Apply(MatchState.None, new MatchCreated(Plain()))));
    }

    // Roster stage R5c (U107): the INJURY DICE a model takes and its melee INJURY MODIFIER bonus.
    [Fact]
    public void InjuryBattlekitRoundTripsAndIsLeftOutWhenZero()
    {
        var plain = Encoded(new MatchCreated(Plain()));
        Assert.DoesNotContain("injuryDiceTaken", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("meleeInjuryBonus", plain, StringComparison.Ordinal);
        var setup = Plain();
        var first = setup.Models[0];
        var smoked = first.Profile! with { InjuryDiceTaken = -1, MeleeInjuryBonus = 2 };
        var created = new MatchCreated(new MatchSetup(setup.RulesVersion, setup.ScenarioId, setup.TurnLimit, setup.Profile, setup.ZoneIds,
            setup.Models.Select(m => m.Id == first.Id ? m with { Profile = smoked } : m), setup.Scenario));
        var text = Encoded(created);
        Assert.Contains("\"injuryDiceTaken\":-1", text, StringComparison.Ordinal);
        Assert.Contains("\"meleeInjuryBonus\":2", text, StringComparison.Ordinal);
        Assert.Equal(smoked, ((MatchCreated)RoundTrip(created)).Setup.Models[0].Profile);
        var bad = text.Replace("\"meleeInjuryBonus\":2", "\"meleeInjuryBonus\":-2", StringComparison.Ordinal);
        Assert.Null(Codec.ParseServer(Encoding.UTF8.GetBytes(bad), out var failure));
        Assert.NotNull(failure);
    }

    // Roster stage R5d (U108).
    [Fact]
    public void RiskyAndCoverBattlekitRoundTripsAndIsLeftOutWhenDefault()
    {
        var plain = Encoded(new MatchCreated(Plain()));
        foreach (var field in new[] { "keepsGoingAfterRisky", "climbDice", "fanfare", "blendsIn" })
        {
            Assert.DoesNotContain(field, plain, StringComparison.Ordinal);
        }
        var setup = Plain();
        var first = setup.Models[0];
        var kitted = first.Profile! with { KeepsGoingAfterRisky = true, ClimbDice = 1, Fanfare = true, BlendsIn = true };
        var created = new MatchCreated(new MatchSetup(setup.RulesVersion, setup.ScenarioId, setup.TurnLimit, setup.Profile, setup.ZoneIds,
            setup.Models.Select(m => m.Id == first.Id ? m with { Profile = kitted } : m), setup.Scenario));
        var text = Encoded(created);
        Assert.Contains("\"keepsGoingAfterRisky\":true", text, StringComparison.Ordinal);
        Assert.Contains("\"climbDice\":1", text, StringComparison.Ordinal);
        Assert.Contains("\"fanfare\":true", text, StringComparison.Ordinal);
        Assert.Contains("\"blendsIn\":true", text, StringComparison.Ordinal);
        Assert.Equal(kitted, ((MatchCreated)RoundTrip(created)).Setup.Models[0].Profile);
        var bad = text.Replace("\"climbDice\":1", "\"climbDice\":-1", StringComparison.Ordinal);
        Assert.Null(Codec.ParseServer(Encoding.UTF8.GetBytes(bad), out var failure));
        Assert.NotNull(failure);
    }

    // Roster stage R5e (U109).
    [Fact]
    public void UrnAndLambRoundTripAndAreLeftOutWhenFalse()
    {
        var plain = Encoded(new MatchCreated(Plain()));
        Assert.DoesNotContain("sinisterWhispers", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("wardedByBlood", plain, StringComparison.Ordinal);
        var setup = Plain();
        var first = setup.Models[0];
        var kitted = first.Profile! with { SinisterWhispers = true, WardedByBlood = true };
        var created = new MatchCreated(new MatchSetup(setup.RulesVersion, setup.ScenarioId, setup.TurnLimit, setup.Profile, setup.ZoneIds,
            setup.Models.Select(m => m.Id == first.Id ? m with { Profile = kitted } : m), setup.Scenario));
        var text = Encoded(created);
        Assert.Contains("\"sinisterWhispers\":true", text, StringComparison.Ordinal);
        Assert.Contains("\"wardedByBlood\":true", text, StringComparison.Ordinal);
        Assert.Equal(kitted, ((MatchCreated)RoundTrip(created)).Setup.Models[0].Profile);

        var (a, b) = (new ModelId("A1"), new ModelId("B1"));
        var downed = Enumerable.Range(1, 100).Select(seed => InjuryRoll.Roll(new SeededDice(seed))).First(r => r.Outcome == InjuryOutcome.Down);
        var rolled = new InjuryRolled(a, b, downed);
        Assert.DoesNotContain("warded", Encoded(rolled), StringComparison.OrdinalIgnoreCase);
        Assert.True(((InjuryRolled)RoundTrip(rolled with { Warded = true })).Warded);
        var mine = new MineExploded(b, "mine-1", downed);
        Assert.DoesNotContain("warded", Encoded(mine), StringComparison.OrdinalIgnoreCase);
        Assert.True(((MineExploded)RoundTrip(mine with { Warded = true })).Warded);
    }

    // Roster stage R5f (U110).
    [Fact]
    public void CoverKeywordRoundTripsAndIsLeftOutWhenFalse()
    {
        var plain = Encoded(new MatchCreated(Plain()));
        Assert.DoesNotContain("hasCover", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("digsIn", plain, StringComparison.Ordinal);
        var setup = Plain();
        var first = setup.Models[0];
        var kitted = first.Profile! with { HasCover = true, DigsIn = true };
        var created = new MatchCreated(new MatchSetup(setup.RulesVersion, setup.ScenarioId, setup.TurnLimit, setup.Profile, setup.ZoneIds,
            setup.Models.Select(m => m.Id == first.Id ? m with { Profile = kitted } : m), setup.Scenario));
        var text = Encoded(created);
        Assert.Contains("\"hasCover\":true", text, StringComparison.Ordinal);
        Assert.Contains("\"digsIn\":true", text, StringComparison.Ordinal);
        Assert.Equal(kitted, ((MatchCreated)RoundTrip(created)).Setup.Models[0].Profile);
    }

    [Fact]
    public void FearAndToughRoundTripAndAreLeftOutWhenFalse()
    {
        var (a, b) = (new ModelId("A1"), new ModelId("B1"));
        var fight = new MeleeAttackDeclared(a, b, false, null);
        Assert.DoesNotContain("fear", Encoded(fight), StringComparison.OrdinalIgnoreCase);
        Assert.True(((MeleeAttackDeclared)RoundTrip(fight with { Fear = true })).Fear);

        var killing = Enumerable.Range(1, 100).Select(seed => InjuryRoll.Roll(new SeededDice(seed))).First(r => r.Outcome == InjuryOutcome.OutOfAction);
        Assert.Equal(InjuryOutcome.OutOfAction, killing.Outcome);
        var rolled = new InjuryRolled(a, b, killing);
        Assert.DoesNotContain("tough", Encoded(rolled), StringComparison.OrdinalIgnoreCase);
        var saved = (InjuryRolled)RoundTrip(rolled with { Tough = true });
        Assert.Equal((true, InjuryOutcome.Down), (saved.Tough, saved.Result));

        var mine = new MineExploded(a, "mine-1", killing);
        Assert.DoesNotContain("tough", Encoded(mine), StringComparison.OrdinalIgnoreCase);
        Assert.True(((MineExploded)RoundTrip(mine with { Tough = true })).Tough);
    }
}
