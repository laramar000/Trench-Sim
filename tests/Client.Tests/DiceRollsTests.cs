using Client.Core;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Client.Tests;

// Visible dice rolls (QoL stage F1): every roll shown is the server's own event read back. The faces are the event's
// dice in order (kept and dropped as recorded), the result is the rules' own Outcome/Classify, and events without
// dice show no dice at all.
public class DiceRollsTests
{
    private static readonly ModelId A1 = new("a1");
    private static readonly ModelId B2 = new("b2");

    [Fact]
    public void EveryEventWithDiceShowsExactlyTheServersDiceAndNoOtherEventShowsAny()
    {
        var seen = new Dictionary<string, HashSet<RollVerdict>>();
        var count = 0;
        foreach (var e in ClientCoreTests.Matches.Value.SelectMany(m => m.Log))
        {
            var roll = DiceRolls.Of(e);
            // The combat log lists dice for exactly the events that carry dice; the dice display matches it.
            Assert.Equal(EventText.Dice(e) is not null, roll is not null);
            if (roll is null)
            {
                continue;
            }
            count++;
            var (rolled, kept) = ServerDice(e);
            Assert.Equal(rolled, roll.Faces);
            Assert.Equal(kept.Order(), roll.Dice.Where(d => d.Kept).Select(d => d.Value).Order());
            Assert.Equal(ExpectedVerdict(e), roll.Verdict);
            Assert.Matches("^(✓✓|✓|✗|•) ", roll.Result);
            Assert.DoesNotContain("%", roll.Rule + roll.Result, StringComparison.Ordinal);
            Assert.Equal(ExpectedModel(e), roll.Model);
            var kind = e.GetType().Name;
            seen.TryAdd(kind, []);
            seen[kind].Add(roll.Verdict);
        }
        Assert.True(count > 1000, $"{count} rolls");
        // Every kind of roll the rules make turns up in the simulated matches; the rarer results are covered below.
        foreach (var kind in new[] { "DashRolled", "RangedSuccessRolled", "MeleeSuccessRolled", "MoraleCheckRolled", "InjuryRolled", "ChargeRolled", "RollOffResolved" })
        {
            Assert.True(seen.ContainsKey(kind), $"no {kind} in the simulated matches: {string.Join(", ", seen.Keys)}");
        }
        foreach (var kind in new[] { "RangedSuccessRolled", "MeleeSuccessRolled" })
        {
            Assert.Contains(RollVerdict.Pass, seen[kind]);
            Assert.Contains(RollVerdict.Fail, seen[kind]);
        }
    }

    [Theory]
    [InlineData(new[] { 4, 2 }, new[] { 4, 2 }, 0, "✗ MISSED 6", RollVerdict.Fail)]
    [InlineData(new[] { 4, 3 }, new[] { 4, 3 }, 0, "✓ HIT 7", RollVerdict.Pass)]
    [InlineData(new[] { 6, 6 }, new[] { 6, 6 }, 0, "✓✓ CRITICAL HIT 12", RollVerdict.Critical)]
    [InlineData(new[] { 6, 1, 5 }, new[] { 6, 5 }, 1, "✓ HIT 11", RollVerdict.Pass)]
    [InlineData(new[] { 6, 1, 5 }, new[] { 1, 5 }, -1, "✗ MISSED 6", RollVerdict.Fail)]
    public void AHitRollShowsEveryDieWhichCountedAndTheRulesResult(int[] rolled, int[] kept, int net, string result, RollVerdict verdict)
    {
        var check = new SuccessRollResult(new RollRecord(rolled, kept), net, risky: false);
        var roll = DiceRolls.Of(new RangedSuccessRolled(A1, B2, check))!;
        Assert.Equal(rolled, roll.Faces);
        Assert.Equal(kept.Order(), roll.Dice.Where(d => d.Kept).Select(d => d.Value).Order());
        Assert.Equal(rolled.Length - kept.Length, roll.Dice.Count(d => !d.Kept));
        Assert.Equal(result, roll.Result);
        Assert.Equal(verdict, roll.Verdict);
        Assert.Equal(A1, roll.Model);
        Assert.Equal("A1 SHOOTS B2: hit roll", roll.Title);
        Assert.Contains($"{SuccessRoll.SuccessFrom}+ passes, {SuccessRoll.CriticalFrom}+ critical", roll.Rule, StringComparison.Ordinal);
        Assert.Contains(net switch { > 0 => "kept the highest 2", < 0 => "kept the lowest 2", _ => $"{rolled.Length} dice" }, roll.Rule, StringComparison.Ordinal);
    }

    [Fact]
    public void ADroppedDieEqualToAKeptOneIsMarkedOnce()
    {
        var check = new SuccessRollResult(new RollRecord([3, 3, 3], [3, 3]), -1, risky: false);
        var roll = DiceRolls.Of(new MeleeSuccessRolled(A1, B2, check))!;
        Assert.Equal([true, true, false], roll.Dice.Select(d => d.Kept));
        Assert.Equal("✗ MISSED 6", roll.Result);
        Assert.Equal("A1 FIGHTS B2: hit roll", roll.Title);
    }

    [Fact]
    public void ADashIsRiskyAndSaysWhatAFailDoes()
    {
        var failed = DiceRolls.Of(new DashRolled(A1, new SuccessRollResult(new RollRecord([1, 2], [1, 2]), 0, risky: true)))!;
        Assert.Equal("✗ DASH FAILED 3", failed.Result);
        Assert.Equal(RollVerdict.Fail, failed.Verdict);
        Assert.Contains("RISKY: a fail ends the activation", failed.Rule, StringComparison.Ordinal);
        var passed = DiceRolls.Of(new DashRolled(A1, new SuccessRollResult(new RollRecord([5, 2], [5, 2]), 0, risky: true)))!;
        Assert.Equal("✓ DASH 7", passed.Result);
    }

    [Fact]
    public void AMoraleCheckBelongsToTheWarbandNotAModel()
    {
        var roll = DiceRolls.Of(new MoraleCheckRolled(Seat.Two, new SuccessRollResult(new RollRecord([2, 3], [2, 3]), 0, risky: false), 2, 4, false))!;
        Assert.Null(roll.Model);
        Assert.Equal("Seat Two: morale check", roll.Title);
        Assert.Equal("✗ FAILS 5", roll.Result);
        var held = DiceRolls.Of(new MoraleCheckRolled(Seat.One, new SuccessRollResult(new RollRecord([4, 4], [4, 4]), 0, risky: false), 2, 4, false))!;
        Assert.Equal("✓ HOLDS 8", held.Result);
    }

    [Fact]
    public void TheInjuryBandsAreTheRulesOwn()
    {
        Assert.Equal("2+ Minor Hit, 7+ Down, 9+ Out of Action", DiceRolls.InjuryBands());
        // Each band starts exactly where Classify changes its answer.
        Assert.Equal(InjuryOutcome.NoEffect, InjuryRoll.Classify(1));
        Assert.Equal(InjuryOutcome.MinorHit, InjuryRoll.Classify(2));
        Assert.Equal(InjuryOutcome.MinorHit, InjuryRoll.Classify(6));
        Assert.Equal(InjuryOutcome.Down, InjuryRoll.Classify(7));
        Assert.Equal(InjuryOutcome.Down, InjuryRoll.Classify(8));
        Assert.Equal(InjuryOutcome.OutOfAction, InjuryRoll.Classify(9));
    }

    [Theory]
    [InlineData(new[] { 1, 1 }, new[] { 1, 1 }, 0, -1, false, "• NO EFFECT 1 (2-1)", RollVerdict.Neutral)]
    [InlineData(new[] { 2, 3 }, new[] { 2, 3 }, 0, 0, false, "• MINOR HIT 5, +1 Blood Marker", RollVerdict.Neutral)]
    [InlineData(new[] { 6, 1, 3 }, new[] { 6, 3 }, 1, -1, false, "✓ DOWN 8 (9-1), +1 Blood Marker", RollVerdict.Pass)]
    [InlineData(new[] { 4, 3 }, new[] { 4, 3 }, 0, 0, true, "✓ DOWN 7, +2 Blood Markers", RollVerdict.Pass)]
    [InlineData(new[] { 5, 6 }, new[] { 5, 6 }, 0, 2, false, "✓ OUT OF ACTION 13 (11+2)", RollVerdict.Pass)]
    public void AnInjuryRollShowsItsDiceModifierBandAndBloodMarkers(int[] rolled, int[] kept, int net, int modifier, bool alreadyDown, string result, RollVerdict verdict)
    {
        var injury = new InjuryRollResult(new RollRecord(rolled, kept), net, modifier, alreadyDown);
        var roll = DiceRolls.Of(new InjuryRolled(A1, B2, injury))!;
        Assert.Equal(rolled, roll.Faces);
        Assert.Equal(result, roll.Result);
        Assert.Equal(verdict, roll.Verdict);
        // The target's injury: shown at the target.
        Assert.Equal(B2, roll.Model);
        Assert.Equal("A1 INJURES B2: injury roll", roll.Title);
        Assert.EndsWith(DiceRolls.InjuryBands(), roll.Rule, StringComparison.Ordinal);
        Assert.Equal(modifier != 0, roll.Rule.Contains($"modifier {modifier:+0;-0}", StringComparison.Ordinal));
    }

    [Fact]
    public void AChargeShowsItsBonusThenWhetherItReached()
    {
        var roll = DiceRolls.Of(new ChargeRolled(A1, B2, 4))!;
        Assert.Equal([4], roll.Faces);
        Assert.Equal("• +4\"", roll.Result);
        Assert.Equal(RollVerdict.Neutral, roll.Verdict);
        Assert.Equal(("✓ REACHED", RollVerdict.Pass), DiceRolls.FollowUp(new ChargeMoved(A1, B2, [new(Length.Inch, Length.Inch)], Length.Inch, true)));
        Assert.Equal(("✗ FAILED", RollVerdict.Fail), DiceRolls.FollowUp(new ChargeMoved(A1, B2, [], Length.Zero, false)));
        Assert.Null(DiceRolls.FollowUp(new ChargeDeclared(A1, B2)));
    }

    [Theory]
    [InlineData(3, "✗ FRIENDLY FIRE 3", RollVerdict.Fail)]
    [InlineData(4, "✓ ENEMY TARGET 4", RollVerdict.Pass)]
    public void ShootingIntoMeleeShowsWhichTargetTheDieGave(int die, string result, RollVerdict verdict)
    {
        var roll = DiceRolls.Of(new ShootingTargetRolled(A1, B2, die))!;
        Assert.Equal([die], roll.Faces);
        Assert.Equal(result, roll.Result);
        Assert.Equal(verdict, roll.Verdict);
    }

    [Fact]
    public void ARollOffShowsEveryRoundAndOnlyTheLastCounts()
    {
        var roll = DiceRolls.Of(new RollOffResolved(RollOffPurpose.Initiative, [new RollOffRound(3, 3), new RollOffRound(2, 5)], Seat.Two))!;
        Assert.Null(roll.Model);
        Assert.Equal("Roll-off: initiative", roll.Title);
        Assert.Equal([3, 3, 2, 5], roll.Faces);
        Assert.Equal([false, false, true, true], roll.Dice.Select(d => d.Kept));
        Assert.Contains("1 tie rolled again", roll.Rule, StringComparison.Ordinal);
        Assert.Equal("• SEAT TWO WINS 5-2", roll.Result);
    }

    [Fact]
    public void EventsWithoutDiceShowNoDice()
    {
        Assert.Null(DiceRolls.Of(new ChargeDeclared(A1, B2)));
        Assert.Null(DiceRolls.Of(new TurnStarted(1)));
        Assert.Null(DiceRolls.Of(new MarkersSpent(Seat.Two, A1, 1, MarkerSpendPurpose.ReduceSuccessRoll)));
    }

    private static (IReadOnlyList<int> Rolled, IReadOnlyList<int> Kept) ServerDice(GameEvent e) => e switch
    {
        DashRolled d => (d.Check.Dice.Rolled, d.Check.Dice.Kept),
        RangedSuccessRolled r => (r.Check.Dice.Rolled, r.Check.Dice.Kept),
        MeleeSuccessRolled m => (m.Check.Dice.Rolled, m.Check.Dice.Kept),
        MoraleCheckRolled m => (m.Check.Dice.Rolled, m.Check.Dice.Kept),
        InjuryRolled i => (i.Injury.Dice.Rolled, i.Injury.Dice.Kept),
        ChargeRolled c => ([c.Die], [c.Die]),
        ShootingTargetRolled t => ([t.Die], [t.Die]),
        RollOffResolved r => (r.Rounds.SelectMany(x => new[] { x.SeatOne, x.SeatTwo }).ToList(), new[] { r.Rounds[^1].SeatOne, r.Rounds[^1].SeatTwo }),
        _ => throw new InvalidOperationException(e.GetType().Name),
    };

    private static RollVerdict ExpectedVerdict(GameEvent e)
    {
        static RollVerdict Of(SuccessRollResult c) => c.Outcome switch
        {
            SuccessOutcome.CriticalSuccess => RollVerdict.Critical,
            SuccessOutcome.Success => RollVerdict.Pass,
            _ => RollVerdict.Fail,
        };
        return e switch
        {
            DashRolled d => Of(d.Check),
            RangedSuccessRolled r => Of(r.Check),
            MeleeSuccessRolled m => Of(m.Check),
            MoraleCheckRolled m => Of(m.Check),
            InjuryRolled i => i.Injury.Outcome is InjuryOutcome.Down or InjuryOutcome.OutOfAction ? RollVerdict.Pass : RollVerdict.Neutral,
            ShootingTargetRolled t => t.HitsFriend ? RollVerdict.Fail : RollVerdict.Pass,
            _ => RollVerdict.Neutral,
        };
    }

    private static ModelId? ExpectedModel(GameEvent e) => e switch
    {
        DashRolled d => d.Model,
        RangedSuccessRolled r => r.Attacker,
        MeleeSuccessRolled m => m.Attacker,
        InjuryRolled i => i.Target,
        ChargeRolled c => c.Model,
        ShootingTargetRolled t => t.Attacker,
        _ => null,
    };
}
