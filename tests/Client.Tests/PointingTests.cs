using Client.Core;
using Rules.Core.State;

namespace Client.Tests;

// QoL H1: the panel's "POINTING AT" lines for the model under the pointer. In Shoot, Charge and Fight, pointing at an
// enemy shows what choosing it would give, from the same rules check as a click: the same facts and the same refusal.
// Pointing chooses nothing and changes nothing. Outside target modes it gives the model's state and the rules' distance.
public class PointingTests
{
    private static IEnumerable<MatchState> ActivationStates() =>
        PreviewAgreementTests.States.Value.Where(s => s.Phase == Phase.Activation && s.Pending is null && s.Outcome is null && s.ActiveModel is not null);

    [Fact]
    public void PointingAtATargetShowsWhatAClickOnItWouldAndChangesNothing()
    {
        var (pointed, refused) = (0, 0);
        foreach (var state in ActivationStates().Take(400))
        {
            var actor = state.FindModel(state.ActiveModel!.Value)!;
            var scene = BoardScene.Of(state, actor.Seat, ContentNames.None)!;
            foreach (var mode in new[] { InteractionMode.Shoot, InteractionMode.Charge, InteractionMode.Fight })
            {
                var interaction = new Interaction();
                interaction.Update(state, actor.Seat);
                if (!interaction.Start(mode))
                {
                    continue;
                }
                var chosen = interaction.Target;
                foreach (var other in interaction.Targets().Where(t => t != chosen))
                {
                    var version = interaction.Version;
                    var hover = interaction.PreviewOf(other)!;
                    var panel = UnitPanel.Of(state, scene, interaction, actor.Seat, scene.Find(actor.Id), scene.Find(other));
                    // Nothing was chosen by pointing.
                    Assert.Equal(chosen, interaction.Target);
                    Assert.Equal(version, interaction.Version);

                    // The same preview as a click on it.
                    var clicked = new Interaction();
                    clicked.Update(state, actor.Seat);
                    clicked.Start(mode);
                    clicked.SetTarget(other);
                    var click = clicked.Preview!;
                    Assert.Equal(click.Refusal, hover.Refusal);
                    Assert.Equal(click.Facts, hover.Facts);

                    Assert.Equal(UnitPanelView.PointingPrefix + UnitPanel.Heading(scene.Find(other)!), panel.Pointing[0]);
                    foreach (var fact in click.Facts.Where(PreviewCard.IsTargetFact))
                    {
                        Assert.Contains(fact.TrimEnd('.'), panel.Pointing);
                    }
                    Assert.Equal(click.Refusal is { } r ? $"✕ {r.Text}" : "✓ can be the target: click to choose it", panel.Pointing[^1]);
                    Assert.All(panel.Pointing, l => Assert.False(l.Contains('%') || l.Contains("chance", StringComparison.OrdinalIgnoreCase), l));
                    pointed++;
                    refused += click.Refusal is null ? 0 : 1;
                }
                // The target already chosen and the actor itself add nothing.
                if (chosen is { } c)
                {
                    Assert.Empty(UnitPanel.PointingAt(state, interaction, scene.Find(actor.Id), scene.Find(c)));
                }
                Assert.Empty(UnitPanel.PointingAt(state, interaction, scene.Find(actor.Id), scene.Find(actor.Id)));
            }
        }
        Assert.True(pointed > 50 && refused > 10 && pointed - refused > 10, $"{pointed} pointed at, {refused} refused");
    }

    [Fact]
    public void OutsideTargetModesPointingGivesTheModelsStateAndTheRulesDistance()
    {
        var checkedLines = 0;
        foreach (var state in ActivationStates().Take(100))
        {
            var actor = state.FindModel(state.ActiveModel!.Value)!;
            var scene = BoardScene.Of(state, actor.Seat, ContentNames.None)!;
            var interaction = new Interaction();
            interaction.Update(state, actor.Seat);
            var unit = scene.Find(actor.Id)!;
            foreach (var other in scene.Models.Where(m => m.Id != actor.Id))
            {
                var lines = UnitPanel.PointingAt(state, interaction, unit, other);
                Assert.Equal(UnitPanelView.PointingPrefix + UnitPanel.Heading(other), lines[0]);
                Assert.StartsWith($"{Words.Seat(other.Seat)} [{other.SeatLetter}]", lines[1], StringComparison.Ordinal);
                Assert.Contains(Words.Status(other.Status), lines[1], StringComparison.Ordinal);
                var markers = state.FindModel(other.Id)!.BloodMarkers;
                Assert.Equal(markers > 0, lines[1].Contains("Blood Marker", StringComparison.Ordinal));
                Assert.Equal($"{Previews.Gap(state, actor.Id, other.Id)} from {UnitPanel.ShortId(actor.Id)} (base to base)", lines[2]);
                checkedLines++;
            }
            Assert.Empty(UnitPanel.PointingAt(state, interaction, unit, null));
        }
        Assert.True(checkedLines > 200, $"{checkedLines} models pointed at");
    }

    [Fact]
    public void TheDistanceIsTheOneTheAttackPreviewsMeasure()
    {
        var compared = 0;
        foreach (var state in ActivationStates().Take(300))
        {
            var actor = state.FindModel(state.ActiveModel!.Value)!;
            var interaction = new Interaction();
            interaction.Update(state, actor.Seat);
            if (!interaction.Start(InteractionMode.Shoot))
            {
                continue;
            }
            foreach (var target in interaction.Targets())
            {
                var preview = interaction.PreviewOf(target) ?? interaction.Preview!;
                if (preview.Facts.FirstOrDefault(f => f.StartsWith("Distance ", StringComparison.Ordinal)) is { } fact)
                {
                    Assert.StartsWith($"Distance {Previews.Gap(state, actor.Id, target)},", fact, StringComparison.Ordinal);
                    compared++;
                }
            }
        }
        Assert.True(compared > 20, $"{compared} distances compared");
    }
}
