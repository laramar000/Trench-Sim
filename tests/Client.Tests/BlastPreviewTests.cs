using Client.Core;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.State;
using Sim;

namespace Client.Tests;

// Roster stage R4b (U102): the attack preview names the models a BLAST would hit, and they are the server's; the event's
// line and the units' notes name them too. Roster stage R4c (U103): a missed SCATTER attack's point is the opponent's
// decision on the bar beside the target, one answer per point the rules offer, each naming who it would hit.
public class BlastPreviewTests
{
    [Fact]
    public void ThePreviewsBlastHitsAreTheServers()
    {
        var warbands = Content.Load(Content.DefaultPath());
        var compared = 0;
        var others = 0;
        for (var seed = 1; seed <= 10; seed++)
        {
            var blasting = warbands.Select(w => w with
            {
                Models = w.Models.Select((m, i) => m with { Profile = m.Profile.HasRangedWeapon ? m.Profile with { RangedWeaponBlast = 1 + (seed + i) % 3 } : m.Profile }).ToList(),
            }).ToList();
            var log = Runner.Run(seed, blasting).Log;
            var state = MatchState.None;
            MatchState? before = null;
            RangedAttackDeclared? shot = null;
            foreach (var e in log)
            {
                if (e is RangedAttackDeclared declared)
                {
                    (before, shot) = (state, declared);
                }
                if (e is BlastHit hit)
                {
                    Assert.NotNull(shot);
                    var attacker = before!.FindModel(shot!.Attacker)!;
                    var preview = Previews.Target(before, attacker.Seat, PreviewKind.Shoot, shot.Attacker, shot.Target, shot.Weapon);
                    Assert.True(preview.Legal);
                    if (hit.Target == shot.Target)
                    {
                        Assert.Equal(hit.Hit.ToArray(), preview.Dice!.BlastHits.ToArray());
                        Assert.Contains(preview.Dice.KeywordNotes, n => n.StartsWith("BLAST ", StringComparison.Ordinal));
                        compared++;
                        others += hit.Hit.Length > 1 ? 1 : 0;
                    }
                    Assert.Contains("BLAST HITS", EventText.Compact(hit).ToString(), StringComparison.Ordinal);
                    Assert.Equal(hit.Hit.ToArray(), UnitNotes.Of(hit).Select(n => n.Model).ToArray());
                }
                state = Reducer.Apply(state, e);
            }
        }
        Assert.True(compared > 20 && others > 0, $"{compared} blasts compared, {others} hitting others");
    }

    [Fact]
    public void TheScatterPointsAreOnTheBarBesideTheTargetAndNameWhoTheyHit()
    {
        var warbands = Content.Load(Content.DefaultPath());
        var bars = 0;
        for (var seed = 1; seed <= 12 && bars < 10; seed++)
        {
            var scattering = warbands.Select(w => w with
            {
                Models = w.Models.Select(m => m with { Profile = m.Profile.HasRangedWeapon ? m.Profile with { RangedWeaponBlast = 2, RangedWeaponKeywords = WeaponKeywords.Scatter } : m.Profile }).ToList(),
            }).ToList();
            var log = Runner.Run(seed, scattering).Log;
            var state = MatchState.None;
            for (var i = 0; i < log.Count; i++)
            {
                state = Reducer.Apply(state, log[i]);
                if (state.Pending is not { Kind: DecisionKind.ChooseScatterPoint } pending)
                {
                    continue;
                }
                var interaction = new Interaction();
                interaction.Update(state, pending.Seat);
                var bar = interaction.Bar(null);
                Assert.Equal(state.InProgress!.Target, bar.Anchor);
                var legal = TurnSequence.LegalCommands(state).OfType<ScatterAttack>().ToList();
                Assert.NotEmpty(legal);
                Assert.Equal(legal, bar.Items.Select(b => b.Command));
                Assert.All(bar.Items, b => Assert.StartsWith("Land ", b.Label, StringComparison.Ordinal));
                Assert.Equal(bar.Items.Count, bar.Items.Select(b => b.Label).Distinct().Count());
                foreach (var answer in legal)
                {
                    var scattered = TurnSequence.Decide(state, answer, new SeededDice(seed)).Events.OfType<AttackScattered>().Single();
                    var hits = ScatterWords.Hits(state, answer.At, id => id.Value);
                    Assert.Equal(scattered.Hit.IsEmpty ? "hits nobody" : "hits " + string.Join(", ", scattered.Hit.Select(h => h.Value + (state.FindModel(h)!.Seat == pending.Seat ? "" : " (the attacker's)"))), hits);
                }
                // What the bot answered is in the log next.
                Assert.IsType<AttackScattered>(log[i + 1]);
                bars++;
            }
        }
        Assert.True(bars >= 10, $"{bars} scatter decisions");
        Assert.Equal("east", ScatterDirectionAt(1, 0));
        Assert.Equal("north-west", ScatterDirectionAt(-1, -1));
        Assert.Equal("about south", ScatterDirectionAt(0.1, 3));
    }

    private static string ScatterDirectionAt(double dx, double dy)
    {
        var state = Runner.Run(1, Content.Load(Content.DefaultPath())).Log.Aggregate(MatchState.None, Reducer.Apply);
        var target = state.Models.First(m => m.Position is not null);
        var at = target.Position!.Value;
        var scattering = state with { InProgress = new RollInProgress(RollStage.Scatter, state.Models[^1].Id, target.Id) with { ScatterInches = 3 } };
        return ScatterWords.Direction(scattering, Previews.PointAt(TableRect.Inches(at.X) + dx, TableRect.Inches(at.Y) + dy));
    }
}
