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

    // Roster stage R5g (U112).
    [Fact]
    public void CrownAndPactRoundTripAndAreLeftOutWhenFalse()
    {
        var plain = Encoded(new MatchCreated(Plain()));
        Assert.DoesNotContain("burns", plain, StringComparison.Ordinal);
        var setup = Plain();
        var first = setup.Models[0];
        var kitted = first.Profile! with { BurnsAtActivationEnd = true, BurnsWhenTakenOut = true };
        var created = new MatchCreated(new MatchSetup(setup.RulesVersion, setup.ScenarioId, setup.TurnLimit, setup.Profile, setup.ZoneIds,
            setup.Models.Select(m => m.Id == first.Id ? m with { Profile = kitted } : m), setup.Scenario));
        var text = Encoded(created);
        Assert.Contains("\"burnsAtActivationEnd\":true", text, StringComparison.Ordinal);
        Assert.Contains("\"burnsWhenTakenOut\":true", text, StringComparison.Ordinal);
        Assert.Equal(kitted, ((MatchCreated)RoundTrip(created)).Setup.Models[0].Profile);

        var placed = new BloodMarkersPlaced(new ModelId("A1"), [new ModelId("B1"), new ModelId("B2")], [new ModelId("B2")]);
        var back = (BloodMarkersPlaced)RoundTrip(placed);
        Assert.Equal((placed.Source, "B1,B2", "B2"), (back.Source, string.Join(",", back.Targets), string.Join(",", back.Warded)));
    }

    // Roster stage R5h (U113): a Medi-kit and Holy Water, the Treat command and its events.
    [Fact]
    public void TreatRoundTripsAndIsLeftOutWhenFalse()
    {
        var plain = Encoded(new MatchCreated(Plain()));
        Assert.DoesNotContain("treats", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("healingWaters", plain, StringComparison.Ordinal);
        var setup = Plain();
        var first = setup.Models[0];
        var kitted = first.Profile! with { Treats = true, HealingWaters = true };
        var created = new MatchCreated(new MatchSetup(setup.RulesVersion, setup.ScenarioId, setup.TurnLimit, setup.Profile, setup.ZoneIds,
            setup.Models.Select(m => m.Id == first.Id ? m with { Profile = kitted } : m), setup.Scenario));
        var text = Encoded(created);
        Assert.Contains("\"treats\":true", text, StringComparison.Ordinal);
        Assert.Contains("\"healingWaters\":true", text, StringComparison.Ordinal);
        Assert.Equal(kitted, ((MatchCreated)RoundTrip(created)).Setup.Models[0].Profile);

        var (a, b) = (new ModelId("A1"), new ModelId("A2"));
        var command = new Rules.Core.Commands.TreatModel(Seat.One, a, b, TreatChoice.StandUp);
        Assert.Equal(command, Assert.IsType<SubmitCommand>(Codec.ParseClient(Codec.Encode(new SubmitCommand("c1", 1, 0, command)), out var failure)).Command);
        Assert.Null(failure);
        Assert.Equal(new TreatDeclared(a, b, TreatChoice.RemoveMarker), RoundTrip(new TreatDeclared(a, b, TreatChoice.RemoveMarker)));
        var rolled = new TreatRolled(a, b, TreatChoice.StandUp, SuccessRoll.Roll(new SeededDice(4), 1, 0, risky: true), false);
        var back = (TreatRolled)RoundTrip(rolled);
        Assert.Equal((rolled.Model, rolled.Target, rolled.Choice, rolled.Treated, rolled.Check.Dice.Total), (back.Model, back.Target, back.Choice, back.Treated, back.Check.Dice.Total));
    }

    // Roster stage R5i (U114): Piston Legs and the Grind to Dust attack.
    [Fact]
    public void GrindRoundTripsAndIsLeftOutWhenFalse()
    {
        Assert.DoesNotContain("grinds", Encoded(new MatchCreated(Plain())), StringComparison.Ordinal);
        var setup = Plain();
        var first = setup.Models[0];
        var kitted = first.Profile! with { GrindsToDust = true };
        var created = new MatchCreated(new MatchSetup(setup.RulesVersion, setup.ScenarioId, setup.TurnLimit, setup.Profile, setup.ZoneIds,
            setup.Models.Select(m => m.Id == first.Id ? m with { Profile = kitted } : m), setup.Scenario));
        Assert.Contains("\"grindsToDust\":true", Encoded(created), StringComparison.Ordinal);
        Assert.Equal(kitted, ((MatchCreated)RoundTrip(created)).Setup.Models[0].Profile);
        var grind = new MeleeAttackDeclared(new ModelId("A1"), new ModelId("B1"), false, null) { Weapon = ModelProfile.GrindWeapon };
        Assert.Equal(ModelProfile.GrindWeapon, ((MeleeAttackDeclared)RoundTrip(grind)).Weapon);
        var command = new Rules.Core.Commands.FightModel(Seat.One, new ModelId("A1"), new ModelId("B1")) { Weapon = ModelProfile.GrindWeapon };
        Assert.Equal(command, Assert.IsType<SubmitCommand>(Codec.ParseClient(Codec.Encode(new SubmitCommand("c1", 1, 0, command)), out var failure)).Command);
        Assert.Null(failure);
    }

    // Roster stage R5j (U115): the Martyrdom Device, its trigger, its detonation and the bearer's 4D6 roll.
    [Fact]
    public void MartyrdomRoundTripsAndIsLeftOutWhenFalse()
    {
        Assert.DoesNotContain("martyrdom", Encoded(new MatchCreated(Plain())), StringComparison.OrdinalIgnoreCase);
        var setup = Plain();
        var first = setup.Models[0];
        var kitted = first.Profile! with { Martyrdom = true };
        var created = new MatchCreated(new MatchSetup(setup.RulesVersion, setup.ScenarioId, setup.TurnLimit, setup.Profile, setup.ZoneIds,
            setup.Models.Select(m => m.Id == first.Id ? m with { Profile = kitted } : m), setup.Scenario));
        Assert.Contains("\"martyrdom\":true", Encoded(created), StringComparison.Ordinal);
        Assert.Equal(kitted, ((MatchCreated)RoundTrip(created)).Setup.Models[0].Profile);
        var (a, b) = (new ModelId("A1"), new ModelId("B1"));
        var command = new Rules.Core.Commands.TriggerMartyrdom(Seat.One, a);
        Assert.Equal(command, Assert.IsType<SubmitCommand>(Codec.ParseClient(Codec.Encode(new SubmitCommand("c1", 1, 0, command)), out var failure)).Command);
        Assert.Null(failure);
        var triggered = (MartyrdomTriggered)RoundTrip(new MartyrdomTriggered(a, [b, a], [b]));
        Assert.Equal(("B1,A1", "B1"), (string.Join(",", triggered.Hit), string.Join(",", triggered.Close)));
        var four = InjuryRoll.Roll(new SeededDice(5), modifiers: [0, 0], allFour: true);
        var back = (InjuryRolled)RoundTrip(new InjuryRolled(a, a, four));
        Assert.Equal((true, four.Total), (back.Injury.AllFour, back.Injury.Total));
    }

    // Roster stage R5k (U116): Talismans, the decision, its two answers and the event.
    [Fact]
    public void TalismansRoundTripAndAreLeftOutWhenZero()
    {
        Assert.DoesNotContain("talisman", Encoded(new MatchCreated(Plain())), StringComparison.OrdinalIgnoreCase);
        var setup = Plain();
        var first = setup.Models[0];
        var kitted = first.Profile! with { Talismans = 2 };
        var created = new MatchCreated(new MatchSetup(setup.RulesVersion, setup.ScenarioId, setup.TurnLimit, setup.Profile, setup.ZoneIds,
            setup.Models.Select(m => m.Id == first.Id ? m with { Profile = kitted } : m), setup.Scenario));
        Assert.Contains("\"talismans\":2", Encoded(created), StringComparison.Ordinal);
        Assert.Equal(kitted, ((MatchCreated)RoundTrip(created)).Setup.Models[0].Profile);
        foreach (Rules.Core.Commands.Command command in new Rules.Core.Commands.Command[] { new Rules.Core.Commands.UseTalisman(Seat.One), new Rules.Core.Commands.DeclineTalisman(Seat.Two) })
        {
            Assert.Equal(command, Assert.IsType<SubmitCommand>(Codec.ParseClient(Codec.Encode(new SubmitCommand("c1", 1, 0, command)), out var failure)).Command);
            Assert.Null(failure);
        }
        Assert.Equal(new TalismanUsed(new ModelId("A1")), RoundTrip(new TalismanUsed(new ModelId("A1"))));
        Assert.Equal(new DecisionRequested(DecisionKind.UseTalisman, Seat.One), RoundTrip(new DecisionRequested(DecisionKind.UseTalisman, Seat.One)));
    }

    // Roster stage R6a (U117): New Antioch abilities, the new traits and Shock Charge's dropped die.
    [Fact]
    public void NewAntiochAbilitiesRoundTripAndAreLeftOutWhenFalse()
    {
        var plain = Encoded(new MatchCreated(Plain()));
        // Roster stage R6b (U118): with the Aim and Fortify ACTIONS.
        var fields = new[] { "shotsIgnoreMarkers", "expertMedic", "finishesTheFallen", "extraChargeDie", "inspiresFaithful", "aims", "fortifies" };
        Assert.All(fields, f => Assert.DoesNotContain(f, plain, StringComparison.Ordinal));
        var setup = Plain();
        var first = setup.Models[0];
        var kitted = first.Profile! with
        {
            ShotsIgnoreMarkers = true, ExpertMedic = true, FinishesTheFallen = true, ExtraChargeDie = true, InspiresFaithful = true,
            Aims = true, Fortifies = true,
            Traits = ModelTraits.Principality | ModelTraits.Cult | ModelTraits.Demonic,
        };
        var created = new MatchCreated(new MatchSetup(setup.RulesVersion, setup.ScenarioId, setup.TurnLimit, setup.Profile, setup.ZoneIds,
            setup.Models.Select(m => m.Id == first.Id ? m with { Profile = kitted } : m), setup.Scenario));
        var text = Encoded(created);
        Assert.All(fields, f => Assert.Contains($"\"{f}\":true", text, StringComparison.Ordinal));
        Assert.Equal(kitted, ((MatchCreated)RoundTrip(created)).Setup.Models[0].Profile);
        var (a, b) = (new ModelId("A1"), new ModelId("B1"));
        Assert.DoesNotContain("dropped", Encoded(new ChargeRolled(a, b, 4)), StringComparison.Ordinal);
        Assert.Equal(2, ((ChargeRolled)RoundTrip(new ChargeRolled(a, b, 4) { Dropped = 2 })).Dropped);
        Assert.Equal(new SelfActionDeclared(a, SelfActionKind.Aim), RoundTrip(new SelfActionDeclared(a, SelfActionKind.Aim)));
        var rolled = (SelfActionRolled)RoundTrip(new SelfActionRolled(a, SelfActionKind.Aim, SuccessRoll.Roll(new SeededDice(4), 2, 0, risky: true)));
        Assert.Equal((SelfActionKind.Aim, 2, true), (rolled.Action, rolled.Check.NetDice, rolled.Check.Risky));
    }

    // Roster stage R6c (U119): number-changing abilities, Frightening Speed's stand-up and Memento Mori's spared result.
    [Fact]
    public void NumberAbilitiesRoundTripAndAreLeftOutWhenFalse()
    {
        var plain = Encoded(new MatchCreated(Plain()));
        var fields = new[] { "agile", "swiftDash", "quickToRise", "undeadFortitude", "abioticLife", "stealthy", "huntsTheTough", "counterCharge", "mementoMori" };
        Assert.All(fields, f => Assert.DoesNotContain($"\"{f}\"", plain, StringComparison.Ordinal));
        var setup = Plain();
        var first = setup.Models[0];
        var kitted = first.Profile! with
        {
            Agile = true, SwiftDash = true, QuickToRise = true, UndeadFortitude = true, AbioticLife = true, Stealthy = true,
            HuntsTheTough = true, CounterCharge = true, MementoMori = true,
        };
        var created = new MatchCreated(new MatchSetup(setup.RulesVersion, setup.ScenarioId, setup.TurnLimit, setup.Profile, setup.ZoneIds,
            setup.Models.Select(m => m.Id == first.Id ? m with { Profile = kitted } : m), setup.Scenario));
        var text = Encoded(created);
        Assert.All(fields, f => Assert.Contains($"\"{f}\":true", text, StringComparison.Ordinal));
        Assert.Equal(kitted, ((MatchCreated)RoundTrip(created)).Setup.Models[0].Profile);
        var a = new ModelId("A1");
        Assert.DoesNotContain("quick", Encoded(new ModelStoodUp(a)), StringComparison.Ordinal);
        Assert.True(((ModelStoodUp)RoundTrip(new ModelStoodUp(a) { Quick = true })).Quick);
        var injury = InjuryRoll.Roll(new SeededDice(1), 6, 0, [], false);
        Assert.Equal(InjuryOutcome.OutOfAction, injury.Outcome);
        var spared = new InjuryRolled(a, new ModelId("B1"), injury) { Spared = true };
        Assert.DoesNotContain("spared", Encoded(new InjuryRolled(a, new ModelId("B1"), injury)), StringComparison.Ordinal);
        Assert.Equal((true, InjuryOutcome.NoEffect), (((InjuryRolled)RoundTrip(spared)).Spared, ((InjuryRolled)RoundTrip(spared)).Result));
    }

    // Roster stage R6d (U120): Trample and Overwhelming Horde, and a Trample attack's weapon 4.
    [Fact]
    public void TrampleAndHordeRoundTripAndAreLeftOutWhenFalse()
    {
        var plain = Encoded(new MatchCreated(Plain()));
        Assert.DoesNotContain("\"tramples\"", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("\"horde\"", plain, StringComparison.Ordinal);
        var setup = Plain();
        var first = setup.Models[0];
        var kitted = first.Profile! with { Tramples = true, Horde = true };
        var created = new MatchCreated(new MatchSetup(setup.RulesVersion, setup.ScenarioId, setup.TurnLimit, setup.Profile, setup.ZoneIds,
            setup.Models.Select(m => m.Id == first.Id ? m with { Profile = kitted } : m), setup.Scenario));
        var text = Encoded(created);
        Assert.Contains("\"tramples\":true", text, StringComparison.Ordinal);
        Assert.Contains("\"horde\":true", text, StringComparison.Ordinal);
        Assert.Equal(kitted, ((MatchCreated)RoundTrip(created)).Setup.Models[0].Profile);
        var trample = new MeleeAttackDeclared(new ModelId("A1"), new ModelId("B1"), false, null) { Weapon = ModelProfile.TrampleWeapon };
        Assert.Equal(ModelProfile.TrampleWeapon, ((MeleeAttackDeclared)RoundTrip(trample)).Weapon);
    }

    // Roster stage R6e (U121): the two flags, a Laying on of Hands' Target and Removed, and an Enforced Orthodoxy's StoodUp,
    // each left out when unset (so an Aim's events are written as before).
    [Fact]
    public void HelpingActionsRoundTripAndAreLeftOutWhenUnset()
    {
        var plain = Encoded(new MatchCreated(Plain()));
        Assert.DoesNotContain("\"enforcesOrthodoxy\"", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("\"laysOnHands\"", plain, StringComparison.Ordinal);
        var setup = Plain();
        var first = setup.Models[0];
        var kitted = first.Profile! with { EnforcesOrthodoxy = true, LaysOnHands = true };
        var created = new MatchCreated(new MatchSetup(setup.RulesVersion, setup.ScenarioId, setup.TurnLimit, setup.Profile, setup.ZoneIds,
            setup.Models.Select(m => m.Id == first.Id ? m with { Profile = kitted } : m), setup.Scenario));
        Assert.Equal(kitted, ((MatchCreated)RoundTrip(created)).Setup.Models[0].Profile);
        var (a, b) = (new ModelId("A1"), new ModelId("A2"));
        var check = SuccessRoll.Roll(new SeededDice(4), 1, 0, risky: true);
        var aim = Encoded(new SelfActionRolled(a, SelfActionKind.Aim, check));
        Assert.All(new[] { "target", "removed", "stoodUp" }, f => Assert.DoesNotContain($"\"{f}\"", aim, StringComparison.Ordinal));
        Assert.DoesNotContain("\"target\"", Encoded(new SelfActionDeclared(a, SelfActionKind.Aim)), StringComparison.Ordinal);
        var hands = (SelfActionRolled)RoundTrip(new SelfActionRolled(a, SelfActionKind.LayingOnHands, check) { Target = b, Removed = 3 });
        Assert.Equal(((ModelId?)b, 3), (hands.Target, hands.Removed));
        var ordered = (SelfActionRolled)RoundTrip(new SelfActionRolled(a, SelfActionKind.EnforcedOrthodoxy, check) { StoodUp = [b] });
        Assert.Equal([b], ordered.StoodUp.ToArray());
        Assert.Equal((ModelId?)b, ((SelfActionDeclared)RoundTrip(new SelfActionDeclared(a, SelfActionKind.LayingOnHands) { Target = b })).Target);
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

    // Roster stage R8d (U132, U133): an oval base's half span, the axis chosen at set-up and a turn travel on the wire and back;
    // round bases and axis Y write nothing new.
    [Fact]
    public void OvalBasesRoundTripAndAreLeftOutWhenRound()
    {
        var plain = Encoded(new MatchCreated(Plain()));
        Assert.DoesNotContain("baseHalfSpan", plain, StringComparison.OrdinalIgnoreCase);
        var setup = Plain();
        var first = setup.Models[0];
        var oval = first.Profile! with { BaseHalfSpan = Rules.Core.Geometry.Length.FromThousandths(591) };
        var created = new MatchCreated(new MatchSetup(setup.RulesVersion, setup.ScenarioId, setup.TurnLimit, setup.Profile, setup.ZoneIds,
            setup.Models.Select(m => m.Id == first.Id ? m with { Profile = oval } : m), setup.Scenario));
        Assert.Contains("\"baseHalfSpan\"", Encoded(created), StringComparison.Ordinal);
        Assert.Equal(oval, ((MatchCreated)RoundTrip(created)).Setup.Models[0].Profile);
        Assert.NotEqual(StateHash.Compute(Reducer.Apply(MatchState.None, created)), StateHash.Compute(Reducer.Apply(MatchState.None, new MatchCreated(Plain()))));

        var at = new Rules.Core.Geometry.Point2(Rules.Core.Geometry.Length.FromThousandths(2000), Rules.Core.Geometry.Length.FromThousandths(3000));
        var deployed = new ModelDeployed(Seat.One, new ModelId("A1"), at);
        Assert.DoesNotContain("axis", Encoded(deployed), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Rules.Core.Geometry.BaseAxis.X, ((ModelDeployed)RoundTrip(deployed with { Axis = Rules.Core.Geometry.BaseAxis.X })).Axis);
        Assert.Equal(Rules.Core.Geometry.BaseAxis.X, ((BaseTurned)RoundTrip(new BaseTurned(new ModelId("A1"), Rules.Core.Geometry.BaseAxis.X))).Axis);
        var place = new Rules.Core.Commands.PlaceModel(Seat.One, new ModelId("A1"), at) { Axis = Rules.Core.Geometry.BaseAxis.X };
        var text = Encoding.UTF8.GetString(Codec.Encode(new SubmitCommand("c1", 1, 0, place)));
        Assert.Contains("axis", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("axis", Encoding.UTF8.GetString(Codec.Encode(new SubmitCommand("c2", 2, 0, new Rules.Core.Commands.PlaceModel(Seat.One, new ModelId("A1"), at)))), StringComparison.OrdinalIgnoreCase);
    }

    // Roster stage R9a (U134): the two ACTIONs' flags, a Puppet Master's direction and the forced moves' event travel on the wire
    // and back; a setup and commands without them write nothing new.
    [Fact]
    public void ForcedMoveActionsRoundTripAndAreLeftOutWhenUnset()
    {
        var setup = Plain();
        var first = setup.Models[0];
        var kitted = first.Profile! with { Loudspeakers = true, PuppetMaster = true };
        var created = new MatchCreated(new MatchSetup(setup.RulesVersion, setup.ScenarioId, setup.TurnLimit, setup.Profile, setup.ZoneIds,
            setup.Models.Select(m => m.Id == first.Id ? m with { Profile = kitted } : m), setup.Scenario));
        var text = Encoded(created);
        Assert.Contains("\"loudspeakers\":true", text, StringComparison.Ordinal);
        Assert.Contains("\"puppetMaster\":true", text, StringComparison.Ordinal);
        Assert.Equal(kitted, ((MatchCreated)RoundTrip(created)).Setup.Models[0].Profile);

        var at = new Rules.Core.Geometry.Point2(Rules.Core.Geometry.Length.FromThousandths(2000), Rules.Core.Geometry.Length.FromThousandths(3000));
        var declared = new SelfActionDeclared(new ModelId("A1"), SelfActionKind.PuppetMaster) { Target = new ModelId("B1"), Direction = at };
        Assert.Equal(at, ((SelfActionDeclared)RoundTrip(declared)).Direction);
        Assert.DoesNotContain("direction", Encoded(new SelfActionDeclared(new ModelId("A1"), SelfActionKind.Aim)), StringComparison.OrdinalIgnoreCase);
        var moved = new ModelsMoved(new ModelId("A1"), [new ForcedMove(new ModelId("B1"), at)]) { Die = 4 };
        var back = (ModelsMoved)RoundTrip(moved);
        Assert.Equal((4, new ForcedMove(new ModelId("B1"), at)), (back.Die, Assert.Single(back.Moves)));
        Assert.DoesNotContain("die", Encoded(moved with { Die = 0 }), StringComparison.OrdinalIgnoreCase);
        var command = new SubmitCommand("c9", 9, 0, new Rules.Core.Commands.TakeSelfAction(Seat.One, new ModelId("A1"), SelfActionKind.PuppetMaster) { Target = new ModelId("B1"), Direction = at });
        Assert.Contains("direction", Encoding.UTF8.GetString(Codec.Encode(command)), StringComparison.OrdinalIgnoreCase);
    }

    // Roster stage R10 (U135): the INFECTION MARKERS keyword, the Blesses flag and the events' flags travel on the wire and back;
    // everything without them writes nothing new.
    [Fact]
    public void MarkerKindsRoundTripAndAreLeftOutWhenUnset()
    {
        var setup = Plain();
        var first = setup.Models[0];
        var kitted = first.Profile! with { Blesses = true, RangedWeaponKeywords = WeaponKeywords.InfectionMarkers };
        var created = new MatchCreated(new MatchSetup(setup.RulesVersion, setup.ScenarioId, setup.TurnLimit, setup.Profile, setup.ZoneIds,
            setup.Models.Select(m => m.Id == first.Id ? m with { Profile = kitted } : m), setup.Scenario));
        Assert.Contains("\"blesses\":true", Encoded(created), StringComparison.Ordinal);
        Assert.Equal(kitted, ((MatchCreated)RoundTrip(created)).Setup.Models[0].Profile);

        var (a, b) = (new ModelId("A1"), new ModelId("B1"));
        var roll = Enumerable.Range(1, 100).Select(seed => InjuryRoll.Roll(new SeededDice(seed))).First(r => r.Outcome == InjuryOutcome.MinorHit);
        var plain = new InjuryRolled(a, b, roll);
        Assert.DoesNotContain("infection", Encoded(plain), StringComparison.OrdinalIgnoreCase);
        Assert.True(((InjuryRolled)RoundTrip(plain with { Infection = true })).Infection);
        var check = SuccessRoll.Roll(new SeededDice(3), 0, 0, risky: true);
        var blessed = new SelfActionRolled(a, SelfActionKind.GodIsWithUs, check) { Target = b, Placed = 1 };
        Assert.Equal(1, ((SelfActionRolled)RoundTrip(blessed)).Placed);
        Assert.DoesNotContain("placed", Encoded(blessed with { Placed = 0 }), StringComparison.OrdinalIgnoreCase);
    }
}
