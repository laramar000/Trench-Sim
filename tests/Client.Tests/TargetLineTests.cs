using Client.Core;
using Rules.Core.State;

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
}
