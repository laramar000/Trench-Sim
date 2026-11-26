using Client.Core;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.State;
using Sim;

namespace Client.Tests;

// Roster stage R5a (U105): the Combat menu offers a BLAST weapon aimed at a point (keys 7, 8 and 9); the cursor is the
// point, and the preview's verdict and hits are the rules' own. A missed SCATTER attack's point may also be clicked
// anywhere on the battlefield: the click snaps to the scatter distance in its direction, and only a point the rules
// accept is sent. The log, the notes and the action breakdown read the new events.
public class PointBlastPreviewTests
{
    private static List<Warband> Blasting(WeaponKeywords keywords = WeaponKeywords.None) =>
        Content.Load(Content.DefaultPath()).Select(w => w with
        {
            Models = w.Models.Select(m => m with { Profile = m.Profile.HasRangedWeapon ? m.Profile with { RangedWeaponBlast = 2, RangedWeaponKeywords = keywords } : m.Profile }).ToList(),
        }).ToList();

    // States where a model with a BLAST weapon has just started its activation, nothing else happening.
    private static IEnumerable<MatchState> Activations(List<Warband> warbands, int seeds)
    {
        for (var seed = 1; seed <= seeds; seed++)
        {
            var state = MatchState.None;
            foreach (var e in Runner.Run(seed, warbands).Log)
            {
                state = Reducer.Apply(state, e);
                if (e is ActivationStarted && state.Pending is null && state.InProgress is null && state.ActiveModel is { } active
                    && Previews.BlastWeaponsOf(state, active).Length > 0)
                {
                    yield return state;
                }
            }
        }
    }

    [Fact]
    public void TheCombatMenuAimsABlastAtThePointUnderTheCursor()
    {
        var checkedPoints = 0;
        var sent = 0;
        foreach (var state in Activations(Blasting(), 4).Take(30))
        {
            var active = state.FindModel(state.ActiveModel!.Value)!;
            var interaction = new Interaction();
            interaction.Update(state, active.Seat);
            Assert.True(interaction.Start(InteractionMode.Combat));
            var entry = interaction.CombatEntries().Single(e => e.Mode == InteractionMode.Blast && e.Weapon == 0);
            Assert.Equal(("Blast a point", "7"), (entry.Label, entry.Key));
            Assert.Equal(Previews.ActionOpen(state, active.Seat, active.Id, PreviewKind.Point, 0) is null, entry.Enabled);
            if (interaction.ChooseCombat(InteractionMode.Blast, byKey: true) != CombatPick.Target)
            {
                continue;
            }
            Assert.Equal(InteractionMode.Blast, interaction.Mode);
            Assert.True(interaction.UsesCursor);
            var from = TablePoint.Of(active.Position!.Value);
            foreach (var enemy in state.Models.Where(m => m.Seat != active.Seat && m.Position is not null).Take(3))
            {
                var at = TablePoint.Of(enemy.Position!.Value);
                var (x, y) = (Math.Round(at.X, 1), Math.Round(at.Y + 0.3, 1));
                var point = Previews.PointAt(x, y);
                interaction.SetCursor(x, y);
                var preview = interaction.Preview!;
                Assert.Equal(PreviewKind.Point, preview.Kind);
                var command = Assert.IsType<ShootPoint>(preview.Command);
                Assert.Equal((active.Id, point, 0), (command.Attacker, command.At, command.Weapon));
                Assert.Equal(TurnSequence.Check(state, command), preview.Refusal);
                if (preview.Legal)
                {
                    var hits = TurnSequence.BlastHitsAt(state, active.Id, point);
                    Assert.Contains(preview.Facts, f => f.StartsWith("BLAST 2\": hits ", StringComparison.Ordinal) && hits.All(h => f.Contains(h.Value, StringComparison.Ordinal)));
                    Assert.Equal(TurnSequence.CheckPoint(state, active.Id, point).LongRange, preview.LongRange);
                    Assert.Contains("Blast", PreviewCard.Lines(preview)[0], StringComparison.OrdinalIgnoreCase);
                    Assert.Equal(ClickResult.Send, interaction.ClickAt(x, y));
                    var result = TurnSequence.Decide(state, command, new SeededDice(checkedPoints));
                    Assert.True(result.IsAccepted);
                    var resolution = new ResolutionLog();
                    var seq = 0;
                    foreach (var e in result.Events)
                    {
                        resolution.Add(++seq, e, state.Setup);
                        Assert.NotEqual("", EventText.Line(e).Trim());
                    }
                    Assert.StartsWith($"{active.Id.Value.ToUpperInvariant()} SHOOTS AT ", resolution.Actions.Single().Headline, StringComparison.Ordinal);
                    if (result.Events.OfType<PointBlastHit>().SingleOrDefault() is { } blast)
                    {
                        Assert.Equal(blast.Hit.ToArray(), UnitNotes.Of(blast).Select(n => n.Model).ToArray());
                        // In the setup's order (a decision to spend Blood Markers may stop the rolls part way).
                        var rolled = result.Events.OfType<InjuryRolled>().Select(i => i.Target).ToArray();
                        Assert.Equal(blast.Hit.Take(rolled.Length).ToArray(), rolled);
                        Assert.True(rolled.Length == blast.Hit.Length || result.State!.Pending is not null);
                        // No model was the target: no Critical Success dice on any of the Injury Rolls.
                        Assert.All(result.Events.OfType<InjuryRolled>(), i => Assert.Equal(0, i.Injury.NetDice));
                    }
                    sent++;
                }
                checkedPoints++;
            }
            Assert.Equal(from, TablePoint.Of(active.Position.Value));
            interaction.Cancel();
            Assert.Equal(InteractionMode.Combat, interaction.Mode);
        }
        Assert.True(checkedPoints > 20 && sent > 3, $"{checkedPoints} points, {sent} sent");
    }

    [Fact]
    public void AScatterClickSnapsToTheDistanceAndOnlyALegalPointIsSent()
    {
        var decisions = 0;
        foreach (var start in Activations(Blasting(WeaponKeywords.Scatter), 6))
        {
            var active = start.FindModel(start.ActiveModel!.Value)!;
            if (TurnSequence.Check(start, new ShootPoint(active.Seat, active.Id, active.Position!.Value)) is not null)
            {
                continue;
            }
            // Aim at the attacker's own spot; find dice that miss, so the attack scatters from the point.
            var state = Enumerable.Range(1, 40).Select(s => TurnSequence.Decide(start, new ShootPoint(active.Seat, active.Id, active.Position.Value), new SeededDice(s)))
                .Select(r => r.State!).FirstOrDefault(s => s.Pending is { Kind: DecisionKind.ChooseScatterPoint });
            if (state is null)
            {
                continue;
            }
            var opponent = active.Seat.Opponent();
            var interaction = new Interaction();
            interaction.Update(state, opponent);
            var circle = interaction.ScatterCircle()!.Value;
            Assert.Equal(TablePoint.Of(active.Position.Value), circle.Centre);
            Assert.Equal(state.InProgress!.ScatterInches, circle.Radius);
            // The bar sits beside the attacker, as there is no target, and offers the rules' points.
            var bar = interaction.Bar(null);
            Assert.Equal(active.Id, bar.Anchor);
            Assert.Equal(TurnSequence.LegalCommands(state).OfType<ScatterAttack>(), bar.Items.Select(b => b.Command));
            Assert.Contains("of the aimed point", bar.Items[0].Tip, StringComparison.Ordinal);
            for (var degree = 0; degree < 360; degree += 15)
            {
                var r = degree * Math.PI / 180;
                var click = (X: circle.Centre.X + 7.3 * Math.Cos(r), Y: circle.Centre.Y + 7.3 * Math.Sin(r));
                var landing = interaction.ScatterAt(click.X, click.Y);
                var expected = Previews.PointAt(circle.Centre.X + circle.Radius * Math.Cos(r), circle.Centre.Y + circle.Radius * Math.Sin(r));
                var accepted = TurnSequence.Check(state, new ScatterAttack(opponent, expected)) is null;
                Assert.Equal(accepted, landing is not null);
                if (landing is not null)
                {
                    Assert.Equal(expected, landing.At);
                    var scattered = TurnSequence.Decide(state, landing, new SeededDice(degree)).Events.OfType<AttackScattered>().Single();
                    Assert.Null(scattered.Target);
                    Assert.DoesNotContain("the target", ScatterWords.Where(state, landing.At), StringComparison.Ordinal);
                }
            }
            // Not the attacker's seat to answer: no circle, no click.
            var mine = new Interaction();
            mine.Update(state, active.Seat);
            Assert.Null(mine.ScatterCircle());
            Assert.Null(mine.ScatterAt(circle.Centre.X + 3, circle.Centre.Y));
            decisions++;
            if (decisions >= 6)
            {
                break;
            }
        }
        Assert.True(decisions >= 3, $"{decisions} scatter decisions");
    }
}
