using Client.Core;
using Rules.Core.Dice;
using Rules.Core.Events;

namespace Client.Tests;

// Battlefield UI 3, stage U5: a log row's success or failure mark is exactly the event's own result; every row
// without a pass or fail is unmarked, and the row's words do not change.
public class LogOutcomeTests
{
    private static RowOutcome Expected(GameEvent e) => e switch
    {
        DashRolled d => d.Check.Outcome == SuccessOutcome.Failure ? RowOutcome.Failure : RowOutcome.Success,
        SideClimbRolled c => c.Check.Outcome == SuccessOutcome.Failure ? RowOutcome.Failure : RowOutcome.Success,
        RangedSuccessRolled r => r.Check.Outcome == SuccessOutcome.Failure ? RowOutcome.Failure : RowOutcome.Success,
        MeleeSuccessRolled m => m.Check.Outcome == SuccessOutcome.Failure ? RowOutcome.Failure : RowOutcome.Success,
        MoraleCheckRolled m => m.Check.Outcome == SuccessOutcome.Failure ? RowOutcome.Failure : RowOutcome.Success,
        InjuryRolled i => i.Injury.Outcome is InjuryOutcome.Down or InjuryOutcome.OutOfAction ? RowOutcome.Success
            : i.Injury.Outcome == InjuryOutcome.MinorHit ? RowOutcome.None : RowOutcome.Failure,
        ChargeMoved c => c.Reached && !c.Path.IsEmpty ? RowOutcome.Success : RowOutcome.Failure,
        _ => RowOutcome.None,
    };

    [Fact]
    public void EveryEventOfTheSimulatedMatchesIsMarkedByItsOwnResult()
    {
        var counts = new Dictionary<RowOutcome, int>();
        var kinds = new HashSet<Type>();
        foreach (var (_, log) in ClientCoreTests.Matches.Value)
        {
            foreach (var e in log)
            {
                var outcome = EventText.Outcome(e);
                Assert.Equal(Expected(e), outcome);
                counts[outcome] = counts.GetValueOrDefault(outcome) + 1;
                if (outcome != RowOutcome.None)
                {
                    kinds.Add(e.GetType());
                }
                // Moves, activations and "within range" lines carry no mark.
                if (e is ModelMoved or ActivationStarted or RangedAttackDeclared or ChargeDeclared or ChargeRolled)
                {
                    Assert.Equal(RowOutcome.None, outcome);
                }
            }
        }
        Assert.True(counts.GetValueOrDefault(RowOutcome.Success) > 100 && counts.GetValueOrDefault(RowOutcome.Failure) > 100,
            string.Join(", ", counts.Select(c => $"{c.Key} {c.Value}")));
        Assert.Contains(typeof(RangedSuccessRolled), kinds);
        Assert.Contains(typeof(InjuryRolled), kinds);
        Assert.Contains(typeof(ChargeMoved), kinds);
    }

    [Fact]
    public void TheMarkIsAWordSymbolNotOnlyAColour()
    {
        Assert.Equal("✓", EventText.Mark(RowOutcome.Success));
        Assert.Equal("✕", EventText.Mark(RowOutcome.Failure));
        Assert.Equal("", EventText.Mark(RowOutcome.None));
    }
}
