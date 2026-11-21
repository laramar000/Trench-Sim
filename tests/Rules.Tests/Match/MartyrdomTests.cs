using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.State;
using static Rules.Tests.Match.BoardPlay;
using static Rules.Tests.Match.MeleePlay;

namespace Rules.Tests.Match;

// Roster stage R5j (U115): a Martyrdom Device (Warbands p. 58): a Trigger Martyrdom Device ACTION, once per game, makes
// an Injury Roll for every model within 3" of the bearer, friend or foe, the bearer included; +1 INJURY DICE for the
// other models within 1"; the bearer's own roll is 4D6 added together.
public class MartyrdomTests
{
    private static readonly ModelProfile Bearer = Brawler with { Martyrdom = true };

    // A1 (the bearer, 10, 10) is activated. B1 is 0.1" away, B2 2" away, A2 (a friend) 1.5" away; A3 and B3 are far off.
    private static BoardPlay Near(ModelProfile? bearer = null)
    {
        var play = new BoardPlay(3, id => id == A(1) ? bearer ?? Bearer : Brawler).ToActivation();
        play.Put(A(1), "10", "10").Put(B(1), "11.1", "10").Put(B(2), "13", "10").Put(A(2), "10", "12.5")
            .Put(A(3), "2", "30").Put(B(3), "30", "30");
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    private static TriggerMartyrdom Trigger => new(Seat.One, A(1));

    [Fact]
    [Trait("Rule", "Martyrdom Device (Warbands p. 58): an Injury Roll for each model within 3\", +1 INJURY DICE within 1\", the bearer's 4D6 added together")]
    public void TheDeviceInjuresEveryModelWithinThreeInches()
    {
        var play = Near();
        var models = play.State.Models.Select(m => m.Id).ToList();
        // A2: 1, 1 (Minor Hit); B1 (+1 die): 6, 6, 1 (Out of Action); B2: 3, 3 (Minor Hit); A1: 1, 1, 1, 1 = 4 (Minor Hit).
        var expected = models.Where(id => id == A(2) || id == B(1) || id == B(2)).Append(A(1)).ToList();
        var dice = expected.SelectMany(id => id == A(2) ? new[] { 1, 1 } : id == B(1) ? new[] { 6, 6, 1 } : id == B(2) ? new[] { 3, 3 } : new[] { 1, 1, 1, 1 }).ToArray();
        var events = play.Do(Trigger, dice).Events;
        var triggered = events.OfType<MartyrdomTriggered>().Single();
        Assert.Equal(expected, triggered.Hit.ToList());
        Assert.Equal([B(1)], triggered.Close.ToArray());
        var injuries = events.OfType<InjuryRolled>().ToList();
        Assert.Equal(expected, injuries.Select(i => i.Target).ToList());
        Assert.All(injuries, i => Assert.Equal(A(1), i.Attacker));
        var self = injuries[^1].Injury;
        Assert.Equal((true, 4, 0, InjuryOutcome.MinorHit), (self.AllFour, self.Total, self.NetDice, self.Outcome));
        Assert.Equal((1, InjuryOutcome.OutOfAction), (injuries.Single(i => i.Target == B(1)).Injury.NetDice, injuries.Single(i => i.Target == B(1)).Result));
        Assert.All(injuries.Take(3), i => Assert.False(i.Injury.AllFour));
        Assert.Equal((ModelStatus.OutOfAction, 1, 1, 1), (play.M(B(1)).Status, play.M(B(2)).BloodMarkers, play.M(A(2)).BloodMarkers, play.M(A(1)).BloodMarkers));
        Assert.Equal((true, (ModelId?)A(1)), (play.M(A(1)).MartyrdomSpent, play.State.ActiveModel));
        Assert.Contains("martyrdom=spent", StateHash.Canonical(play.State), StringComparison.Ordinal);
        // Once per game: not again this activation, nor in a later one.
        play.Refused(Trigger, ReasonCode.ActionAlreadyTaken);
        play.Do(new MoveModel(Seat.One, A(1), Path(("10", "8"))));
    }

    [Fact]
    [Trait("Rule", "Martyrdom Device: the bearer taken Down or Out of Action by it ends its activation (p. 49)")]
    public void TheBearerMayFallToItsOwnDevice()
    {
        var play = Near();
        var models = play.State.Models.Select(m => m.Id).ToList();
        var order = models.Where(id => id == A(2) || id == B(1) || id == B(2)).ToList();
        var dice = order.SelectMany(id => id == B(1) ? new[] { 1, 1, 1 } : new[] { 1, 1 }).Concat([6, 6, 6, 6]).ToArray();
        var events = play.Do(Trigger, dice).Events;
        Assert.Equal((24, InjuryOutcome.OutOfAction), (events.OfType<InjuryRolled>().Last().Injury.Total, events.OfType<InjuryRolled>().Last().Result));
        Assert.Equal(ActivationEndReason.TakenDown, events.OfType<ActivationEnded>().Single().Reason);
        Assert.Equal((ModelStatus.OutOfAction, (ModelId?)null), (play.M(A(1)).Status, play.State.ActiveModel));
    }

    [Fact]
    [Trait("Rule", "Martyrdom Device: the bearer's markers are not spent on its own roll; enemies' markers may be spent on theirs")]
    public void MarkersAreOfferedOnEnemiesOnly()
    {
        var play = Near().Mark(B(2), 1).Mark(A(2), 1).Mark(A(1), 1);
        var models = play.State.Models.Select(m => m.Id).ToList();
        var order = models.Where(id => id == A(2) || id == B(1) || id == B(2)).ToList();
        // Up to B2's roll: A2 (friend, no offer), B1 if it comes first; then the offer for B2.
        var before = order.TakeWhile(id => id != B(2)).SelectMany(id => id == B(1) ? new[] { 1, 1, 1 } : new[] { 1, 1 }).ToArray();
        var asked = play.Do(Trigger, before).Events;
        Assert.Equal((DecisionKind.SpendMarkersOnInjuryRoll, Seat.One), (asked.OfType<DecisionRequested>().Single().Kind, asked.OfType<DecisionRequested>().Single().Seat));
        var after = order.SkipWhile(id => id != B(2)).Skip(1).SelectMany(id => id == B(1) ? new[] { 1, 1, 1 } : new[] { 1, 1 });
        var rest = play.Do(new SpendMarkers(Seat.One, 1), [1, 1, 1, .. after, 1, 1, 1, 1]).Events;
        Assert.Equal(1, rest.OfType<InjuryRolled>().First().Injury.NetDice);
        Assert.DoesNotContain(rest, e => e is DecisionRequested);
    }

    [Fact]
    [Trait("Rule", "Martyrdom Device: a target's Holy Smoke still takes its INJURY DICE; the bearer's own roll is always 4D6")]
    public void TakenInjuryDiceCount()
    {
        var play = new BoardPlay(3, id => id == A(1) ? Bearer with { InjuryDiceTaken = -1 } : id == B(1) ? Brawler with { InjuryDiceTaken = -1 } : Brawler).ToActivation();
        play.Put(A(1), "10", "10").Put(B(1), "11.1", "10").Put(B(2), "30", "20").Put(A(2), "2", "20").Put(A(3), "2", "30").Put(B(3), "30", "30");
        play.Do(new ActivateModel(Seat.One, A(1)));
        var injuries = play.Do(Trigger, 2, 2, 1, 1, 1, 1).Events.OfType<InjuryRolled>().ToList();
        Assert.Equal((B(1), 0, 2), (injuries[0].Target, injuries[0].Injury.NetDice, injuries[0].Injury.Dice.Rolled.Count));
        Assert.Equal((A(1), true, 4), (injuries[1].Target, injuries[1].Injury.AllFour, injuries[1].Injury.Total));
    }

    [Fact]
    [Trait("Rule", "Martyrdom Device: only its bearer, standing, and only once; forged detonations are refused")]
    public void TriggersThatCannotBeTakenAreRefused()
    {
        Near(Brawler).Refused(Trigger, ReasonCode.NoSuchAction);
        var spent = Near();
        spent.State = spent.State with { Models = [.. spent.State.Models.Select(m => m.Id == A(1) ? m with { MartyrdomSpent = true } : m)] };
        spent.Refused(Trigger, ReasonCode.ActionAlreadyTaken);

        var play = Near();
        var good = TurnSequence.Decide(play.State, Trigger, new SeededDice(1)).Events.OfType<MartyrdomTriggered>().Single();
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(play.State, new MartyrdomTriggered(A(1), [B(1), A(1)], [B(1)])));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(play.State, new MartyrdomTriggered(A(1), good.Hit, [])));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(Near(Brawler).State, good));
        var started = Reducer.Apply(play.State, good);
        var four = InjuryRoll.Roll(new FixedDice(1, 1, 1, 1), 0, 0, [0, 0], false, allFour: true);
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(started, new InjuryRolled(A(1), started.InProgress!.Target!.Value, four)));
        Assert.Throws<ArgumentException>(() => new MartyrdomTriggered(A(1), [B(1)], []));
        Assert.Throws<ArgumentException>(() => new MartyrdomTriggered(A(1), [B(1), A(1)], [A(1)]));
    }

    [Fact]
    [Trait("Rule", "Records: matches with Martyrdom Devices play to the end and replay; the field is hashed only when set")]
    public void SimulatedMatchesReplay()
    {
        Assert.DoesNotContain("martyrdom", StateHash.Canonical(Near(Brawler).State), StringComparison.Ordinal);
        Assert.Contains("martyrdom=yes", StateHash.Canonical(Near().State), StringComparison.Ordinal);
        var (devices, selves) = (0, 0);
        foreach (var path in new[] { Sim.Content.DefaultPath(), Sim.Content.MeleePath() })
        {
            var warbands = Sim.Content.Load(path).Select(w => w with
            {
                Models = w.Models.Select((m, i) => m with { Profile = m.Profile with { Martyrdom = i % 2 == 0 } }).ToList(),
            }).ToList();
            for (var seed = 1; seed <= 8; seed++)
            {
                var run = Sim.Runner.Run(seed, warbands);
                Assert.True(run.Terminal, run.Status);
                Assert.Equal(run.FinalHash, StateHash.Compute(Reducer.Replay(run.Log)));
                devices += run.Log.OfType<MartyrdomTriggered>().Count();
                selves += run.Log.OfType<InjuryRolled>().Count(i => i.Injury.AllFour);
            }
        }
        Assert.True(devices > 0 && selves > 0, $"{devices} devices, {selves} bearers' rolls");
    }
}
