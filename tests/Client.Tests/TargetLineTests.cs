using Client.Core;
using Rules.Core;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;
using Sim;

namespace Client.Tests;

// Battlefield UI, stage K5: Shoot, Charge and Fight draw one line from the attacker's base edge to the target's base edge,
// toned by the preview: refused when the preview refuses, cover when it says the target is in cover, else clear. Checked
// over the simulated matches against every enemy of each activating model.
public class TargetLineTests
{
    private static List<MatchState> States => PreviewAgreementTests.States.Value;

    private static double Dist(TablePoint a, TablePoint b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    [Fact]
    public void OneLineFromBaseEdgeToBaseEdgeToneByThePreview()
    {
        var tones = new HashSet<TargetTone>();
        var lines = 0;
        foreach (var state in States.Where(s => s.Phase == Phase.Activation && s.Pending is null && s.ActiveModel is not null).Take(400))
        {
            var attacker = state.FindModel(state.ActiveModel!.Value)!;
            if (attacker.Position is not { } a)
            {
                continue;
            }
            foreach (var enemy in state.Models.Where(m => m.Seat != attacker.Seat && m.Position is not null && m.Status != ModelStatus.OutOfAction))
            {
                foreach (var kind in new[] { PreviewKind.Shoot, PreviewKind.Charge, PreviewKind.Fight })
                {
                    var preview = Previews.Target(state, attacker.Seat, kind, attacker.Id, enemy.Id);
                    var line = TargetLine.Of(state, preview);
                    Assert.NotNull(line);
                    var tone = !preview.Legal ? TargetTone.Refused
                        : preview.Facts.Any(f => f.StartsWith("Target in cover (", StringComparison.Ordinal)) ? TargetTone.Cover : TargetTone.Clear;
                    Assert.Equal(tone, line!.Tone);
                    Assert.Equal(attacker.Id, line.Attacker);
                    Assert.Equal(enemy.Id, line.Target);
                    var (ac, tc) = (TablePoint.Of(a), TablePoint.Of(enemy.Position!.Value));
                    var ra = TableRect.Inches(state.Setup!.ProfileOf(attacker.Id)!.BaseRadius);
                    var rt = TableRect.Inches(state.Setup.ProfileOf(enemy.Id)!.BaseRadius);
                    var apart = Dist(ac, tc);
                    if (apart > ra + rt + 1e-6)
                    {
                        Assert.Equal(ra, Dist(line.From, ac), 6);
                        Assert.Equal(rt, Dist(line.To, tc), 6);
                        Assert.Equal(apart - ra - rt, Dist(line.From, line.To), 6);
                    }
                    else
                    {
                        Assert.Equal(ac, line.From);
                        Assert.Equal(tc, line.To);
                    }
                    // Long range only on a shot the preview accepts, and exactly when the panel says "Long range".
                    Assert.Equal(kind == PreviewKind.Shoot && preview.Legal && preview.Facts.Any(f => f.StartsWith("Long range", StringComparison.Ordinal)), line.LongRange);
                    tones.Add(tone);
                    lines++;
                }
            }
        }
        Assert.True(lines > 100, $"{lines} lines");
        Assert.Contains(TargetTone.Clear, tones);
        Assert.Contains(TargetTone.Refused, tones);
    }

    [Fact]
    public void MovesAndPlacementsHaveNoTargetLine()
    {
        var state = States.First(s => s.Phase == Phase.Deployment && s.Pending is null && s.Setup?.Scenario is not null && MatchView.NextDeployer(s) is not null);
        var seat = MatchView.NextDeployer(state)!.Value;
        var interaction = new Interaction();
        interaction.Update(state, seat);
        Assert.Null(TargetLine.Of(state, interaction.Preview));
        Assert.Null(TargetLine.Of(state, null));
    }

    // Long-range indicator (2026-10-30): the line is long range exactly when the rules declare Long Range. Over the
    // simulated matches, every Shoot the preview accepts is decided by the rules and compared, and both kinds are seen.
    [Fact]
    public void TheLongRangeLineAgreesWithTheRulesDeclaration()
    {
        var seen = new HashSet<bool>();
        foreach (var state in States.Where(s => s.Phase == Phase.Activation && s.Pending is null && s.ActiveModel is not null))
        {
            var attacker = state.FindModel(state.ActiveModel!.Value)!;
            foreach (var enemy in state.Models.Where(m => m.Seat != attacker.Seat && m.Position is not null))
            {
                var preview = Previews.Target(state, attacker.Seat, PreviewKind.Shoot, attacker.Id, enemy.Id);
                var line = TargetLine.Of(state, preview)!;
                if (!preview.Legal)
                {
                    Assert.Null(preview.LongRange);
                    Assert.False(line.LongRange);
                    continue;
                }
                var declared = TurnSequence.Decide(state, preview.Command, new SeededDice(1)).Events.OfType<RangedAttackDeclared>().Single();
                Assert.Equal(declared.LongRange, preview.LongRange);
                Assert.Equal(declared.LongRange, line.LongRange);
                Assert.Equal(declared.LongRange, preview.Dice!.Lines(attacker.Id, enemy.Id).Any(l => l.Contains("Long Range, p. 43", StringComparison.Ordinal)));
                seen.Add(line.LongRange);
            }
        }
        Assert.Contains(true, seen);
        Assert.Contains(false, seen);
    }

    // Exactly half the weapon's range is Short Range; a thousandth of an inch more is Long Range (p. 43); within range
    // but further is Long; beyond the range the shot is refused and the line is the refused one, not long range. Each
    // case agrees with the panel's words, the dice and the rules' own decision of the command.
    [Theory]
    [InlineData("short", false, true)]
    [InlineData("half", false, true)]
    [InlineData("justBeyondHalf", true, true)]
    [InlineData("long", true, true)]
    [InlineData("fullRange", true, true)]
    [InlineData("beyondRange", false, false)]
    public void ShortAndLongRangeAtTheEdges(string where, bool longRange, bool legal)
    {
        var (state, attacker, target) = Duel(where);
        var preview = Previews.Target(state, attacker.Seat, PreviewKind.Shoot, attacker.Id, target);
        Assert.True(legal == preview.Legal, preview.Headline + " | " + string.Join(" | ", preview.Facts));
        var line = TargetLine.Of(state, preview)!;
        Assert.Equal(longRange, line.LongRange);
        var decided = TurnSequence.Decide(state, preview.Command, new SeededDice(1));
        Assert.Equal(legal, decided.IsAccepted);
        if (!legal)
        {
            Assert.Equal(TargetTone.Refused, line.Tone);
            Assert.Null(preview.LongRange);
            Assert.Equal(ReasonCode.TargetOutOfRange, preview.Refusal!.Code);
            return;
        }
        Assert.Equal(longRange, preview.LongRange);
        Assert.Equal(TargetTone.Clear, line.Tone);
        Assert.Contains(preview.Facts, f => f.StartsWith(longRange ? "Long range: beyond half" : "Short range: within half", StringComparison.Ordinal));
        Assert.Equal(longRange ? 1 : 0, preview.Dice!.HitPenalty);
        var declared = decided.Events.OfType<RangedAttackDeclared>().Single();
        Assert.Equal(longRange, declared.LongRange);
        Assert.Equal(longRange ? 1 : 0, declared.PenaltyDice);
    }

    // A model with a Ranged Weapon at the start of its activation on a map's open ground, and one enemy in line with it at
    // the named gap (edge to edge): "half" is exactly half the weapon's range. Every other model is taken off the table.
    private static (MatchState State, ModelState Attacker, ModelId Target) Duel(string where)
    {
        foreach (var state in States.Where(s => s.Phase == Phase.Activation && s.Pending is null && s.Actions == ActionsTaken.None && s.ActiveModel is not null))
        {
            var attacker = state.FindModel(state.ActiveModel!.Value)!;
            var profile = state.Setup!.ProfileOf(attacker.Id)!;
            if (attacker.Status != ModelStatus.Standing || !profile.HasRangedWeapon
                || state.Models.FirstOrDefault(m => m.Seat != attacker.Seat) is not { } enemy)
            {
                continue;
            }
            var range = profile.RangedRange.Thousandths;
            var gap = where switch
            {
                "short" => range / 4,
                "half" => range / 2,
                "justBeyondHalf" => range / 2 + 1,
                "long" => range * 3 / 4,
                "fullRange" => range,
                _ => range + 1,
            };
            var reach = profile.BaseRadius.Thousandths + state.Setup.ProfileOf(enemy.Id)!.BaseRadius.Thousandths + gap;
            // Open ground: a row where the furthest case (just beyond the range) is seen with no cover.
            var far = range + 1 + profile.BaseRadius.Thousandths + state.Setup.ProfileOf(enemy.Id)!.BaseRadius.Thousandths;
            for (long y = 1000; y < 40_000; y += 500)
            {
                var placed = Place(state, attacker.Id, enemy.Id, 1000, y, far);
                if (placed is null)
                {
                    continue;
                }
                var probe = Previews.Target(placed, attacker.Seat, PreviewKind.Shoot, attacker.Id, enemy.Id);
                if (probe.Refusal?.Code != ReasonCode.TargetOutOfRange || probe.Facts.Any(f => f.StartsWith(Previews.CoverFact, StringComparison.Ordinal))
                    || Previews.Target(Place(state, attacker.Id, enemy.Id, 1000, y, far - 2)!, attacker.Seat, PreviewKind.Shoot, attacker.Id, enemy.Id) is not { Legal: true } edge
                    || edge.Facts.Any(f => f.StartsWith(Previews.CoverFact, StringComparison.Ordinal)))
                {
                    continue;
                }
                return (Place(state, attacker.Id, enemy.Id, 1000, y, reach)!, attacker, enemy.Id);
            }
        }
        throw new InvalidOperationException("No open row for a duel.");
    }

    // The state with the attacker at (x, y), the target the given distance (centre to centre, thousandths) east of it, and
    // every other model off the table; null when that is off the table.
    private static MatchState? Place(MatchState state, ModelId attacker, ModelId target, long x, long y, long apart)
    {
        var width = state.Setup!.Scenario!.Battlefield.Width.Thousandths;
        var depth = state.Setup.Scenario.Battlefield.Height.Thousandths;
        if (x + apart > width - 1000 || y > depth - 1000)
        {
            return null;
        }
        Point2 At(long px) => new(Length.FromThousandths(px), Length.FromThousandths(y));
        return state with
        {
            Models = [.. state.Models.Select(m => m.Id == attacker ? m with { Position = At(x) }
                : m.Id == target ? m with { Position = At(x + apart) }
                : m with { Position = null, Status = ModelStatus.OutOfAction })],
        };
    }
}
