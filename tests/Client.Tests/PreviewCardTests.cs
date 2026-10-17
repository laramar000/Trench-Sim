using System.Globalization;
using Client.Core;
using Match.Protocol;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Client.Tests;

// QoL stage B: the preview card drawn on the battlefield repeats the rules' own facts (distance used and allowed, sight,
// cover, range) and adds nothing of its own: no odds, no rule. It always says it is a preview.
public class PreviewCardTests
{
    private static IEnumerable<MatchState> ActivationStates() =>
        PreviewAgreementTests.States.Value.Where(s => s.Phase == Phase.Activation && s.Pending is null && s.ActiveModel is { } a && s.FindModel(a)?.Position is not null);

    private static double Inches(string text) => double.Parse(text.TrimEnd('"'), CultureInfo.InvariantCulture);

    private static void NoOdds(IReadOnlyList<string> lines) =>
        Assert.All(lines, l => Assert.False(l.Contains('%') || l.Contains("chance", StringComparison.OrdinalIgnoreCase) || l.Contains("probab", StringComparison.OrdinalIgnoreCase), l));

    [Fact]
    public void AMoveCardShowsTheRulesDistanceUsedAndLeft()
    {
        var random = new Random(4);
        var (cards, dashes, refused) = (0, 0, 0);
        foreach (var state in ActivationStates().Take(400))
        {
            var active = state.FindModel(state.ActiveModel!.Value)!;
            var at = TablePoint.Of(active.Position!.Value);
            foreach (var kind in new[] { PreviewKind.Move, PreviewKind.Dash, PreviewKind.Retreat })
            {
                var path = new List<Point2>();
                var (x, y) = (at.X, at.Y);
                for (var leg = random.Next(0, 3); leg >= 0; leg--)
                {
                    (x, y) = (Math.Clamp(x + random.NextDouble() * 8 - 4, 0.5, 35.5), Math.Clamp(y + random.NextDouble() * 8 - 4, 0.5, 35.5));
                    path.Add(Previews.PointAt(x, y));
                }
                var preview = Previews.Path(state, active.Seat, kind, active.Id, path);
                var lines = PreviewCard.Lines(preview);
                Assert.Equal(preview.Tag, lines[0]);
                Assert.Contains(Preview.Caveat, lines[0]);
                NoOdds(lines);
                // The rules' own measurement: "Distance 3.5" of 6.0" allowed", when the preview got that far.
                if (preview.Facts.FirstOrDefault(f => f.StartsWith("Distance ", StringComparison.Ordinal)) is { } fact)
                {
                    var words = fact.Split(' ');
                    // The card's numbers read exactly as the rules' fact does, and "left" is the allowance minus the cost.
                    Assert.Equal(words[1], BoardScene.Inch(preview.Used!.Value));
                    Assert.Equal(words[3], BoardScene.Inch(preview.Allowance!.Value));
                    Assert.InRange(Math.Abs(Math.Max(0, Inches(words[3]) - Inches(words[1])) - preview.Left!.Value), 0, 0.1);
                    Assert.Equal(Math.Max(0, preview.Allowance.Value - preview.Used.Value), preview.Left.Value, 9);
                    Assert.Contains($"{BoardScene.Inch(preview.Used.Value)} used of {BoardScene.Inch(preview.Allowance.Value)} ({BoardScene.Inch(preview.Left.Value)} left)", lines);
                    // A path never costs less than the straight line to its end.
                    Assert.True(preview.Straight!.Value <= preview.Used.Value + 0.01, $"straight {preview.Straight} > used {preview.Used}");
                    Assert.Contains($"straight line {BoardScene.Inch(preview.Straight.Value)}", lines);
                    cards++;
                }
                if (kind == PreviewKind.Dash && preview.Legal)
                {
                    Assert.Contains(lines, l => l.StartsWith("RISKY", StringComparison.Ordinal));
                    dashes++;
                }
                if (preview.Refusal is { } r)
                {
                    Assert.Equal($"{r.Text} ({r.RuleRef})", lines[^1]);
                    refused++;
                }
            }
        }
        Assert.True(cards > 200 && dashes > 10 && refused > 10, $"{cards} cards, {dashes} legal dashes, {refused} refused");
    }

    [Fact]
    public void ATargetCardShowsTheTargetAndTheRulesSightAndCover()
    {
        var (sight, cover, legal) = (0, 0, 0);
        foreach (var state in ActivationStates().Take(400))
        {
            var active = state.FindModel(state.ActiveModel!.Value)!;
            var scene = BoardScene.Of(state, active.Seat, ContentNames.None)!;
            foreach (var target in state.Models.Where(m => m.Seat != active.Seat && m.Position is not null))
            {
                foreach (var kind in new[] { PreviewKind.Shoot, PreviewKind.Charge, PreviewKind.Fight })
                {
                    var preview = Previews.Target(state, active.Seat, kind, active.Id, target.Id);
                    var shape = scene.Find(target.Id)!;
                    var lines = PreviewCard.Lines(preview, shape);
                    Assert.Equal(preview.Tag, lines[0]);
                    Assert.Equal($"target {shape.Label}", lines[1]);
                    NoOdds(lines);
                    // Every fact about distance, sight and cover is the rules' own, word for word.
                    foreach (var fact in preview.Facts.Where(f => f.StartsWith("Distance", StringComparison.Ordinal) || f.StartsWith("Line of sight", StringComparison.Ordinal) || f.StartsWith("Target ", StringComparison.Ordinal)))
                    {
                        Assert.Contains(fact.TrimEnd('.'), lines);
                    }
                    sight += preview.Facts.Any(f => f.StartsWith("Line of sight", StringComparison.Ordinal)) ? 1 : 0;
                    cover += preview.Facts.Any(f => f.StartsWith("Target in cover", StringComparison.Ordinal)) ? 1 : 0;
                    // Only a preview that looks legal says the dice are the server's; a refused one ends with the reason.
                    Assert.Equal(preview.Legal, lines.Contains("dice: rolled by the server"));
                    if (preview.Refusal is { } r)
                    {
                        Assert.Equal($"{r.Text} ({r.RuleRef})", lines[^1]);
                    }
                    legal += preview.Legal ? 1 : 0;
                }
            }
        }
        Assert.True(sight > 100 && cover > 0 && legal > 20, $"{sight} with sight, {cover} in cover, {legal} legal");
    }

    // A refusal by the server shows a note above the model, with the rule the server cited; an accepted command does not.
    [Fact]
    public async Task AServerRefusalShowsANoteAboveTheModel()
    {
        var state = ActivationStates().First();
        var active = state.FindModel(state.ActiveModel!.Value)!;
        var answer = new Feedback(FeedbackKind.RulesRefused, "Refused by the rules: no.", "1.0.2 p. 34");
        var interaction = new Interaction { Send = (_, _) => Task.FromResult(answer) };
        interaction.Update(state, active.Seat);
        await interaction.SendCommandAsync(new EndActivation(active.Seat), "End");
        Assert.Equal(0, interaction.ServerNotes);
        var move = new MoveModel(active.Seat, active.Id, [active.Position!.Value]);
        await interaction.SendCommandAsync(move, "Move");
        Assert.Equal(1, interaction.ServerNotes);
        // A short word on the battlefield; the reason and its page are in the notice the panel shows.
        Assert.Equal(new UnitNote(active.Id, "REFUSED", NoteTone.Failure), interaction.ServerNote);
        Assert.Equal("REFUSED BY THE RULES: Refused by the rules: no. (1.0.2 p. 34)", interaction.Notice);
        answer = new Feedback(FeedbackKind.Accepted, "Done.");
        await interaction.SendCommandAsync(move, "Move");
        Assert.Equal(1, interaction.ServerNotes);
    }
}
