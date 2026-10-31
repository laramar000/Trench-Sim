using Client.Core;
using Rules.Core;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.State;
using Sim;

namespace Client.Tests;

// Proposal J, stage J1a: the named reasons for each roll's dice and Injury modifier add up exactly to the server's own
// numbers in every roll of the simulated ranged and melee matches, and each action's outcome repeats the server's
// results. The reasons come from the events and the rules' own functions; nothing is rolled or decided here.
public class ActionResolutionTests
{
    private static readonly Lazy<List<(IReadOnlyList<GameEvent> Log, MatchSetup Setup)>> Matches = new(() =>
    {
        var all = new List<(IReadOnlyList<GameEvent>, MatchSetup)>();
        foreach (var path in new[] { Content.DefaultPath(), Content.MeleePath() })
        {
            var warbands = Content.Load(path);
            for (var seed = 1; seed <= 30; seed++)
            {
                var log = Runner.Run(seed, warbands).Log;
                all.Add((log, ((MatchCreated)log[0]).Setup));
            }
            // Roster stage R1: the same warbands with the models' own dice (Ranged, Melee, weapon DICE and INJURY DICE).
            for (var seed = 1; seed <= 10; seed++)
            {
                var diced = warbands.Select(w => w with
                {
                    Models = w.Models.Select(m => m with { Profile = PreviewAgreementTests.WithDice(m.Profile, seed % 3 - 1) }).ToList(),
                }).ToList();
                var log = Runner.Run(seed, diced).Log;
                all.Add((log, ((MatchCreated)log[0]).Setup));
            }
        }
        return all;
    });

    [Fact]
    public void EveryRollIsExplainedByPartsThatAddUpToTheServersNumbers()
    {
        var seen = new Dictionary<string, int>();
        void Saw(string what) => seen[what] = seen.GetValueOrDefault(what) + 1;
        foreach (var (log, setup) in Matches.Value)
        {
            var resolutions = new ResolutionLog();
            for (var seq = 0; seq < log.Count; seq++)
            {
                var e = log[seq];
                resolutions.Add(seq, e, setup);
                var why = resolutions.WhyOf(seq);
                switch (e)
                {
                    case RangedSuccessRolled or MeleeSuccessRolled or DashRolled:
                        var check = e switch { RangedSuccessRolled r => r.Check, MeleeSuccessRolled m => m.Check, _ => ((DashRolled)e).Check };
                        Assert.NotNull(why);
                        Assert.True(why!.Explained, $"seq {seq} {e.GetType().Name}: {why.Text} vs {check.NetDice}");
                        Assert.Equal(check.NetDice, why.Dice.Sum(p => p.Value));
                        Assert.Equal(check.NetDice, why.NetDice);
                        Assert.Empty(why.Modifiers);
                        Saw(e.GetType().Name);
                        foreach (var part in why.Dice)
                        {
                            Saw(part.Label.Contains("Blood Marker", StringComparison.Ordinal) ? "markers" : part.Label.Split(' ')[0]);
                        }
                        break;
                    case InjuryRolled i:
                        Assert.NotNull(why);
                        Assert.True(why!.Explained, $"seq {seq} injury: {why.Text} vs dice {i.Injury.NetDice}, modifier {i.Injury.Modifier}");
                        Assert.Equal(i.Injury.NetDice, why.Dice.Sum(p => p.Value));
                        Assert.Equal(i.Injury.Modifier, why.Modifier);
                        Assert.Equal(i.Injury.Modifier, InjuryRoll.CombineModifiers(why.Modifiers.Select(p => p.Value)));
                        Saw("InjuryRolled");
                        foreach (var part in why.Dice)
                        {
                            Saw(part.Label.Contains("Blood Marker", StringComparison.Ordinal) ? "markers" : part.Label.Split(' ')[0]);
                        }
                        if (why.Modifiers.Any(p => p.Value != 0))
                        {
                            Saw("modifier");
                        }
                        break;
                    default:
                        Assert.Null(why);
                        break;
                }
            }
        }
        // The matches cover each kind of reason, so none of them is untested.
        foreach (var kind in new[] { "RangedSuccessRolled", "MeleeSuccessRolled", "DashRolled", "InjuryRolled", "Long", "Cover", "critical", "modifier", "markers", "Defended", "Melee", "Ranged", "ranged", "melee" })
        {
            Assert.True(seen.GetValueOrDefault(kind) > 0, $"no {kind} in the simulated matches: {string.Join(", ", seen.Select(kv => $"{kv.Key} {kv.Value}"))}");
        }
    }

    [Fact]
    public void EachActionsOutcomeRepeatsTheServersResults()
    {
        var actions = 0;
        foreach (var (log, setup) in Matches.Value)
        {
            var resolutions = new ResolutionLog();
            for (var seq = 0; seq < log.Count; seq++)
            {
                resolutions.Add(seq, log[seq], setup);
            }
            foreach (var action in resolutions.Actions)
            {
                actions++;
                var events = Enumerable.Range(action.FirstSeq, log.Count - action.FirstSeq).Where(s => resolutions.ActionAt(s)?.FirstSeq == action.FirstSeq).Select(s => log[s]).ToList();
                Assert.Same(log[action.FirstSeq], events[0]);
                foreach (var e in events)
                {
                    switch (e)
                    {
                        case RangedSuccessRolled or MeleeSuccessRolled or DashRolled:
                            var check = e switch { RangedSuccessRolled r => r.Check, MeleeSuccessRolled m => m.Check, _ => ((DashRolled)e).Check };
                            Assert.Contains($"{(check.Outcome == SuccessOutcome.Failure ? "✗" : "✓")} {check.Dice.Total}", action.Outcome, StringComparison.Ordinal);
                            break;
                        case InjuryRolled i:
                            Assert.Contains($"injury {DiceRolls.Of(i)!.Result.TrimStart('✓', '•', ' ')}", action.Outcome, StringComparison.Ordinal);
                            break;
                        case ChargeMoved m:
                            Assert.EndsWith(m.Reached ? "REACHED ✓" : "STOPS SHORT ✗", action.Outcome, StringComparison.Ordinal);
                            break;
                    }
                }
                // Nothing outside its own events: an action never runs past the end of an activation or turn.
                Assert.DoesNotContain(events.Skip(1), e => e is ActivationEnded or TurnEnded or MatchEnded or RangedAttackDeclared or MeleeAttackDeclared or ChargeDeclared or DashDeclared);
                Assert.Equal(events.Count, action.Steps.Count + events.Count(e => e is not (ShootingTargetRolled or FriendlyTargetChosen or MarkersSpent or DashRolled or RangedSuccessRolled or MeleeSuccessRolled or InjuryRolled or ChargeRolled or ChargeMoved or ModelMoved { Kind: MoveKind.Dash })));
            }
        }
        Assert.True(actions > 500, $"{actions} actions");
    }

    [Fact]
    public void ABreakdownThatDoesNotAddUpShowsOnlyTheServersTotals()
    {
        var why = new RollWhy([new("Long Range", -1)], -2, [], null, Explained: false);
        Assert.Equal($"-2 DICE · {RollWhy.NotAvailable}", why.Text);
        Assert.DoesNotContain("Long Range", why.Text, StringComparison.Ordinal);
    }
    // A decision to spend no Blood Markers is a step in words, never "0 DICE" or "0 Blood Markers".
    [Fact]
    public void SpendingNoMarkersIsSaidInWords()
    {
        var none = 0;
        foreach (var (log, setup) in Matches.Value)
        {
            var resolutions = new ResolutionLog();
            for (var seq = 0; seq < log.Count; seq++)
            {
                resolutions.Add(seq, log[seq], setup);
                none += log[seq] is MarkersSpent { Count: 0 } ? 1 : 0;
            }
            foreach (var action in resolutions.Actions)
            {
                Assert.DoesNotContain(action.Steps, s => s.Contains(" 0 Blood Marker", StringComparison.Ordinal) || s.Contains(" 0 DICE", StringComparison.Ordinal));
            }
        }
        Assert.True(none > 0, "no match spent zero markers");
    }
}

// Proposal J1b: the dice display carries each roll's "why" and its action's outcome exactly as ResolutionLog has them.
public class DiceFeedResolutionTests
{
    [Fact]
    public void BoxesCarryTheWhyOfEachRollAndTheOutcomeOfTheirAction()
    {
        var (whys, outcomes) = (0, 0);
        foreach (var path in new[] { Content.DefaultPath(), Content.MeleePath() })
        {
            var warbands = Content.Load(path);
            for (var seed = 1; seed <= 6; seed++)
            {
                var log = Runner.Run(seed, warbands).Log;
                var setup = ((MatchCreated)log[0]).Setup;
                var resolutions = new ResolutionLog();
                var feed = new DiceFeed { Seconds = 1e9 };
                for (var seq = 0; seq < log.Count; seq++)
                {
                    resolutions.Add(seq, log[seq], setup);
                    var line = new LogLine(seq, "", null, Step: DiceRolls.Step(log[seq]))
                    {
                        Why = resolutions.WhyOf(seq)?.Text is { Length: > 0 } why ? why : null,
                        Action = resolutions.ActionAt(seq),
                    };
                    feed.Add(line, 0);
                    foreach (var box in feed.Boxes.Where(b => b.ActionStart is not null))
                    {
                        Assert.Equal(resolutions.Actions.First(a => a.FirstSeq == box.ActionStart).Outcome, box.Outcome);
                        outcomes += box.OutcomeShown is null ? 0 : 1;
                    }
                }
                foreach (var row in feed.Shown)
                {
                    Assert.Equal(resolutions.WhyOf(row.Seq)?.Text is { Length: > 0 } why ? why : null, row.Why);
                    whys += row.Why is null ? 0 : 1;
                }
            }
        }
        Assert.True(whys > 20 && outcomes > 20, $"{whys} rows with a why, {outcomes} outcome lines");
    }

}
