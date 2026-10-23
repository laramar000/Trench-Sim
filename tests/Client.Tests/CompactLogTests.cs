using Client.Core;
using Rules.Core.Dice;
using Rules.Core.Events;

namespace Client.Tests;

// Proposal G, step G4: the combat log's compact rows ("A4 FIGHTS B3"). Each row comes from its event alone: the actor
// and any target are the event's own models (short ids), the verb is one of a fixed list, injuries name the rules'
// result, and an event without an actor keeps the narrator's line. No odds.
public class CompactLogTests
{
    private static readonly HashSet<string> Verbs =
    [
        "DEPLOYS", "ACTIVATES", "ENDS", "STANDS UP", "MOVES", "DASHES", "DECLARES", "ROLLS", "SHOOTS", "CHARGES", "CANNOT MOVE",
        "REACHES", "FALLS SHORT", "RETREATS", "STAYS", "FIGHTS", "ATTACKS", "MUST SHOOT", "KILLS", "DOWNS", "HITS",
    ];

    private static IEnumerable<GameEvent> Events => ClientCoreTests.Matches.Value.SelectMany(m => m.Log);

    [Fact]
    public void EveryRowIsShortAndNamesTheEventsOwnModels()
    {
        var (withVerb, without) = (0, 0);
        foreach (var e in Events)
        {
            var row = EventText.Compact(e);
            Assert.False(string.IsNullOrWhiteSpace(row.Text));
            Assert.DoesNotContain("%", row.Text, StringComparison.Ordinal);
            if (row.Verb is null)
            {
                // No actor: the narrator's own line.
                Assert.Null(row.Actor);
                Assert.Equal(EventText.Line(e).Trim(), row.Rest);
                without++;
                continue;
            }
            Assert.Contains(row.Verb, Verbs);
            Assert.Matches("^[A-Z][0-9]+$", row.Actor!);
            // The actor is a model the narrator's line names (in lower case); the lines of success and injury rolls do not
            // name the attacker, so those rows take the attacker and target from the event.
            if (e switch
            {
                RangedSuccessRolled r => (r.Attacker, r.Target),
                MeleeSuccessRolled m => (m.Attacker, m.Target),
                InjuryRolled i => (i.Attacker, i.Target),
                _ => ((Rules.Core.State.ModelId, Rules.Core.State.ModelId)?)null,
            } is var (attacker, target))
            {
                Assert.Equal(attacker.Value.ToUpperInvariant(), row.Actor);
                Assert.Contains(target.Value.ToUpperInvariant(), row.Rest, StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains(row.Actor!.ToLowerInvariant(), EventText.Line(e), StringComparison.Ordinal);
                Assert.True(row.Text.Length < EventText.Line(e).Trim().Length + 12, $"{row.Text} vs {EventText.Line(e)}");
            }
            withVerb++;
        }
        Assert.True(withVerb > 5_000 && without > 1_000, $"{withVerb} rows with a verb, {without} without");
    }

    [Fact]
    public void InjuriesSayTheRulesResult()
    {
        var seen = new HashSet<InjuryOutcome>();
        foreach (var injury in Events.OfType<InjuryRolled>())
        {
            var row = EventText.Compact(injury);
            Assert.Equal(injury.Attacker.Value.ToUpperInvariant(), row.Actor);
            var target = injury.Target.Value.ToUpperInvariant();
            Assert.Equal(injury.Injury.Outcome switch
            {
                InjuryOutcome.OutOfAction => $"KILLS {target} (Out of Action)",
                InjuryOutcome.Down => $"DOWNS {target}",
                InjuryOutcome.MinorHit => $"HITS {target} (Minor Hit)",
                _ => $"HITS {target} (No Effect)",
            }, $"{row.Verb} {row.Rest}");
            seen.Add(injury.Injury.Outcome);
        }
        Assert.Contains(InjuryOutcome.OutOfAction, seen);
        Assert.Contains(InjuryOutcome.Down, seen);
    }

    [Fact]
    public void AttacksNameAttackerAndTarget()
    {
        var fights = Events.OfType<MeleeAttackDeclared>().Where(m => !m.FreeFight).Take(200).ToList();
        Assert.NotEmpty(fights);
        Assert.All(fights, f => Assert.StartsWith($"{f.Attacker.Value.ToUpperInvariant()} FIGHTS {f.Target.Value.ToUpperInvariant()}", EventText.Compact(f).Text));
        var shots = Events.OfType<RangedAttackDeclared>().Take(200).ToList();
        Assert.NotEmpty(shots);
        Assert.All(shots, s => Assert.StartsWith($"{s.Attacker.Value.ToUpperInvariant()} SHOOTS {s.Target.Value.ToUpperInvariant()}", EventText.Compact(s).Text));
        var charges = Events.OfType<ChargeDeclared>().Take(200).ToList();
        Assert.NotEmpty(charges);
        Assert.All(charges, c => Assert.Equal($"{c.Model.Value.ToUpperInvariant()} CHARGES → {c.Target.Value.ToUpperInvariant()}", EventText.Compact(c).Text));
    }
}
