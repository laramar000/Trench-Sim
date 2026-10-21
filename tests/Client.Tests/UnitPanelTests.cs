using System.Text.RegularExpressions;
using Client.Core;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.State;

namespace Client.Tests;

// Proposal G (step G1): the selected-unit and action panel beside the battlefield. It shows who the unit is, its state,
// why it cannot act when it cannot (agreeing with the rules' own offer of activations), and what the chosen action would
// do, using only the preview's facts: no odds, no rule of its own. Only tests call the rules' TurnSequence.
public partial class UnitPanelTests
{
    private static List<MatchState> States => PreviewAgreementTests.States.Value;

    private static IEnumerable<MatchState> ActivationStates() =>
        States.Where(s => s.Phase == Phase.Activation && s.Pending is null && s.Outcome is null && s.Setup?.Scenario is not null);

    private static void NoOdds(IEnumerable<string> lines) =>
        Assert.All(lines, l => Assert.False(l.Contains('%') || l.Contains("chance", StringComparison.OrdinalIgnoreCase) || l.Contains("probab", StringComparison.OrdinalIgnoreCase), l));

    [GeneratedRegex("^[A-Z][0-9]+$")]
    private static partial Regex ShortIdPattern();

    [Fact]
    public void ShortIdsAreTheSeatLetterAndNumber()
    {
        var scene = BoardScene.Of(ActivationStates().First(), Seat.One, ContentNames.None)!;
        Assert.NotEmpty(scene.Models);
        foreach (var model in States.Where(s => s.Setup?.Scenario is not null).Take(50).SelectMany(s => BoardScene.Of(s, Seat.One, ContentNames.None)!.Models))
        {
            var id = UnitPanel.ShortId(model.Id);
            Assert.Matches(ShortIdPattern(), id);
            Assert.StartsWith(model.SeatLetter, id, StringComparison.Ordinal);
        }
    }

    // A unit the panel calls available is one the rules offer to activate (or already acting); a unit it gives a reason
    // for is not offered.
    [Fact]
    public void TheUnitPartAgreesWithTheRulesOffer()
    {
        var (available, unavailable, activated, enemy) = (0, 0, 0, 0);
        foreach (var state in ActivationStates().Take(600))
        {
            if ((state.ActiveModel is { } a ? state.FindModel(a)!.Seat : TurnSequence.NextActivator(state)) is not { } seat)
            {
                continue;
            }
            var scene = BoardScene.Of(state, seat, ContentNames.None)!;
            var interaction = new Interaction();
            interaction.Update(state, seat);
            var offered = TurnSequence.LegalCommands(state).OfType<ActivateModel>().Select(c => c.Model).ToHashSet();
            foreach (var unit in scene.Models)
            {
                var panel = UnitPanel.Of(state, scene, interaction, seat, unit);
                Assert.Equal(UnitPanel.Heading(unit), panel.Title);
                Assert.DoesNotContain($"({unit.Id.Value})", panel.Title, StringComparison.Ordinal);
                Assert.Contains(panel.Unit, l => l.Contains(Words.Status(unit.Status), StringComparison.Ordinal));
                Assert.Null(panel.ActionTitle);
                Assert.False(panel.CanConfirm);
                Assert.False(panel.CanCancel);
                if (!unit.Yours)
                {
                    Assert.StartsWith("— Opponent's unit", panel.Unavailable);
                    Assert.DoesNotContain(panel.Unit, l => l.StartsWith("Actions:", StringComparison.Ordinal));
                    enemy++;
                }
                else if (panel.Unavailable is null)
                {
                    Assert.True(unit.Active || offered.Contains(unit.Id), $"{unit.Id} shown as available but not offered");
                    available++;
                }
                else
                {
                    Assert.DoesNotContain(unit.Id, offered);
                    Assert.False(unit.Active);
                    activated += unit.Activated ? 1 : 0;
                    if (unit.Activated)
                    {
                        Assert.Equal("— Already activated this turn.", panel.Unavailable);
                        Assert.Contains("✓ Activated this turn", panel.Unit);
                    }
                    unavailable++;
                }
            }
        }
        Assert.True(available > 200 && unavailable > 200 && activated > 100 && enemy > 200, $"{available} available, {unavailable} unavailable ({activated} activated), {enemy} enemy");
    }

    // While a Move, Dash or Retreat is chosen, the panel shows the card's distances (used, allowance, left, straight
    // line), says it is a preview, and gives a refusal in one line with its rule page under Details.
    [Fact]
    public void APathActionShowsItsDistancesAndAnyRefusal()
    {
        var random = new Random(7);
        var (shown, refused) = (0, 0);
        foreach (var state in ActivationStates().Where(s => s.ActiveModel is not null).Take(300))
        {
            var actor = state.FindModel(state.ActiveModel!.Value)!;
            var scene = BoardScene.Of(state, actor.Seat, ContentNames.None)!;
            foreach (var mode in new[] { InteractionMode.Move, InteractionMode.Dash, InteractionMode.Retreat })
            {
                var interaction = new Interaction();
                interaction.Update(state, actor.Seat);
                if (!interaction.Start(mode))
                {
                    continue;
                }
                var at = TablePoint.Of(actor.Position!.Value);
                interaction.SetCursor(at.X + random.NextDouble() * 10 - 5, at.Y + random.NextDouble() * 10 - 5);
                var panel = UnitPanel.Of(state, scene, interaction, actor.Seat, scene.Find(actor.Id));
                Assert.Equal(UnitPanel.Heading(scene.Find(actor.Id)!), panel.Title);
                if (interaction.Preview is not { } preview)
                {
                    continue;
                }
                Assert.Equal($"{Interaction.Words(mode).ToUpperInvariant()} → {preview.Path[^1].At}", panel.ActionTitle);
                Assert.Equal(UnitPanelView.PreviewLine, panel.Action[0]);
                NoOdds(panel.Shown());
                foreach (var line in PreviewCard.Lines(preview).Skip(1).Where(l => preview.Refusal is not { } r || l != $"{r.Text} ({r.RuleRef})"))
                {
                    Assert.Contains(line, panel.Action);
                }
                Assert.True(panel.CanCancel);
                if (preview.Refusal is { } refusal)
                {
                    Assert.Equal($"REFUSED: {refusal.Text}", panel.Refusal);
                    Assert.Contains($"Rule: {refusal.RuleRef}", panel.Details);
                    // The page is under Details, not in the short line.
                    Assert.DoesNotContain(refusal.RuleRef, panel.Refusal, StringComparison.Ordinal);
                    refused++;
                }
                else
                {
                    Assert.Null(panel.Refusal);
                    Assert.True(panel.CanConfirm);
                }
                shown++;
            }
        }
        Assert.True(shown > 200 && refused > 20, $"{shown} path previews, {refused} refused");
    }

    // Shoot, Charge and Fight: the target by short id and name, the rules' sight, cover and distance facts, and for a
    // Charge the roll the rules say it needs.
    [Fact]
    public void ATargetActionShowsTheTargetAndTheRulesFacts()
    {
        var (shoot, charge, fight, rolls) = (0, 0, 0, 0);
        foreach (var state in ActivationStates().Where(s => s.ActiveModel is not null).Take(500))
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
                // Stage N1: starting chooses no target; N chooses the nearest.
                interaction.NextChoice();
                if (interaction.Preview is not { Target: { } targetId } preview)
                {
                    continue;
                }
                var target = scene.Find(targetId)!;
                var panel = UnitPanel.Of(state, scene, interaction, actor.Seat, scene.Find(actor.Id));
                Assert.Equal($"{Interaction.Words(mode).ToUpperInvariant()} → {UnitPanel.Heading(target)}", panel.ActionTitle);
                NoOdds(panel.Shown());
                foreach (var line in PreviewCard.Lines(preview, target).Skip(1)
                    .Where(l => !l.StartsWith("target ", StringComparison.Ordinal) && (preview.Refusal is not { } r || l != $"{r.Text} ({r.RuleRef})")))
                {
                    Assert.Contains(line, panel.Action);
                }
                if (preview.Refusal is { } refusal)
                {
                    Assert.Equal($"REFUSED: {refusal.Text}", panel.Refusal);
                }
                if (mode == InteractionMode.Shoot && preview.Rays.Count > 0)
                {
                    Assert.Contains(panel.Action, l => l.StartsWith("Line of sight", StringComparison.Ordinal));
                    shoot++;
                }
                if (mode == InteractionMode.Charge)
                {
                    if (preview.ChargePath.Count > 1)
                    {
                        Assert.Contains(panel.Action, l => l.Contains("Charge roll", StringComparison.Ordinal) || l == "stops short of the target");
                        rolls++;
                    }
                    charge++;
                }
                fight += mode == InteractionMode.Fight ? 1 : 0;
            }
        }
        Assert.True(shoot > 20 && charge > 20 && fight > 5 && rolls > 5, $"{shoot} shots with sight, {charge} charges ({rolls} with a path), {fight} fights");
    }

    [Fact]
    public void APlacementShowsTheModelAndWhere()
    {
        var placed = 0;
        foreach (var state in States.Where(s => s.Phase == Phase.Deployment && s.Pending is null && s.Setup?.Scenario is not null).Take(40))
        {
            if (MatchView.NextDeployer(state) is not { } seat)
            {
                continue;
            }
            var interaction = new Interaction();
            interaction.Update(state, seat);
            if (!interaction.Start(InteractionMode.Place) || interaction.Preview is not { } preview)
            {
                continue;
            }
            var panel = UnitPanel.Of(state, BoardScene.Of(state, seat, ContentNames.None), interaction, seat, null);
            Assert.Equal($"PLACE {UnitPanel.ShortId(preview.Model)} → {preview.Path[0].At}", panel.ActionTitle);
            Assert.Contains("Placing a model in your deployment zone.", panel.Unit);
            Assert.Equal(UnitPanelView.PreviewLine, panel.Action[0]);
            placed++;
        }
        Assert.True(placed > 5, $"{placed} placements");
    }

    // The selected unit's state this turn (it moved here from the old status strip): chosen but not activated, can
    // activate, activated this turn.
    [Fact]
    public void TheSelectedUnitsStateIsInWords()
    {
        var state = States.First(s => s.Phase == Phase.Activation && s.ActiveModel is null && s.Pending is null && s.Setup?.Scenario is not null
            && TurnSequence.NextActivator(s) is { } next && s.Models.Any(m => m.Seat == next && m.ActivatedThisTurn));
        var seat = TurnSequence.NextActivator(state)!.Value;
        var scene = BoardScene.Of(state, seat, ContentNames.None)!;
        var eligible = scene.Models.First(m => m.Yours && m.Eligible);
        var done = scene.Models.First(m => m.Yours && m.Activated);
        var interaction = new Interaction();
        interaction.Update(state, seat);
        Assert.Contains("○ Can activate now", UnitPanel.Of(state, scene, interaction, seat, eligible).Unit);
        Assert.Contains("✓ Activated this turn", UnitPanel.Of(state, scene, interaction, seat, done).Unit);
        Assert.True(interaction.Choose(eligible.Id));
        Assert.Contains("◌ Chosen: not activated yet (nothing sent)", UnitPanel.Of(state, scene, interaction, seat, eligible).Unit);
    }

    [Fact]
    public void WithNothingSelectedThePanelSaysSo()
    {
        var state = ActivationStates().First();
        var scene = BoardScene.Of(state, Seat.One, ContentNames.None)!;
        var interaction = new Interaction();
        interaction.Update(state, Seat.One);
        var panel = UnitPanel.Of(state, scene, interaction, Seat.One, null);
        Assert.Equal("No unit selected", panel.Title);
        Assert.False(panel.CanConfirm);
        Assert.False(panel.CanCancel);
        // Only the battlefield legend: no unit text, no rule.
        Assert.All(panel.Details, d => Assert.True(d.StartsWith("Terrain on this table: ", StringComparison.Ordinal) || d.StartsWith("Battlefield marks: ", StringComparison.Ordinal), d));
        Assert.Contains(panel.Details, d => d.StartsWith("Battlefield marks: ", StringComparison.Ordinal));
    }

    // Every terrain piece's kind is in the legend, since the battlefield draws terrain without words.
    [Fact]
    public void TheLegendNamesEveryKindOfTerrain()
    {
        foreach (var state in ActivationStates().Take(20))
        {
            var scene = BoardScene.Of(state, Seat.One, ContentNames.None)!;
            var interaction = new Interaction();
            interaction.Update(state, Seat.One);
            var legend = UnitPanel.Of(state, scene, interaction, Seat.One, null).Details.Single(d => d.StartsWith("Terrain on this table: ", StringComparison.Ordinal));
            Assert.All(scene.Terrain, t => Assert.Contains(t.Label.Replace(", Objective", "", StringComparison.Ordinal), legend, StringComparison.Ordinal));
        }
    }

    // While a target action is chosen, the enemies it cannot take are named with a short reason.
    [Fact]
    public void InvalidTargetsAreNamedWithAShortReason()
    {
        var named = 0;
        foreach (var state in ActivationStates().Where(s => s.ActiveModel is not null).Take(300))
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
                var invalid = interaction.TargetVerdicts().Where(v => v.Refusal is not null && v.Model != interaction.Target).ToList();
                var panel = UnitPanel.Of(state, scene, interaction, actor.Seat, scene.Find(actor.Id));
                var line = panel.Action.SingleOrDefault(l => l.StartsWith("✕ not valid: ", StringComparison.Ordinal));
                if (invalid.Count == 0)
                {
                    Assert.Null(line);
                    continue;
                }
                Assert.NotNull(line);
                foreach (var (model, refusal) in invalid)
                {
                    Assert.Contains($"{UnitPanel.ShortId(model)} ({UnitPanel.ShortReason(refusal!.Code)})", line, StringComparison.Ordinal);
                }
                named++;
            }
        }
        Assert.True(named > 20, $"{named} target choices with invalid targets");
    }
}
