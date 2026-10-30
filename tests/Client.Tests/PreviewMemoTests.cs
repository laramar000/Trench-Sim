using Client.Core;
using Rules.Core.State;

namespace Client.Tests;

// Performance (2026-10-30): Interaction works out each target preview (Previews.Target) once per state and keeps it
// while the state object is the same, because opening Combat, choosing the attack, pointing at an enemy and clicking it
// all ask for the same previews. These tests hold the memo to the rules: every preview it gives is the one
// Previews.Target gives on the same state, nothing is worked out twice for one state, and a new state (or a unit
// chosen, which previews on the state as if activated) is never answered from the old one.
public class PreviewMemoTests
{
    private static List<MatchState> States => PreviewAgreementTests.States.Value;

    private static readonly InteractionMode[] Attacks = [InteractionMode.Shoot, InteractionMode.Charge, InteractionMode.Fight];

    private static PreviewKind Kind(InteractionMode mode) => mode switch
    {
        InteractionMode.Shoot => PreviewKind.Shoot,
        InteractionMode.Charge => PreviewKind.Charge,
        InteractionMode.Move => PreviewKind.Move,
        InteractionMode.Dash => PreviewKind.Dash,
        InteractionMode.Retreat => PreviewKind.Retreat,
        _ => PreviewKind.Fight,
    };

    private static IEnumerable<(MatchState State, Seat Seat)> Activations() =>
        States.Where(s => s.Phase == Phase.Activation && s.Pending is null && s.ActiveModel is { } a && s.FindModel(a)!.Position is not null)
            .Select(s => (s, s.FindModel(s.ActiveModel!.Value)!.Seat));

    // Everything a preview says, for comparing two previews field by field (the lists by their items).
    private static string Canon(Preview? p) => p is null ? "null" : string.Join(" | ",
        p.Kind, p.Model, p.Command, p.Refusal, string.Join(";", p.Facts), string.Join(";", p.Path), p.Radius, p.Target,
        string.Join(";", p.Rays), string.Join(";", p.ChargePath), p.ChargeRollNeeded, p.Allowance, p.LongRange,
        p.Dice is { } d ? $"{d with { HitReasons = [] }} {string.Join(";", d.HitReasons)}" : "no dice");

    [Fact]
    public void EveryRememberedPreviewIsTheRulesPreviewOnTheSameState()
    {
        var (checkedTargets, charges) = (0, 0);
        foreach (var (state, seat) in Activations())
        {
            var actor = state.ActiveModel!.Value;
            var interaction = new Interaction();
            interaction.Update(state, seat);
            if (!interaction.Start(InteractionMode.Combat))
            {
                continue;
            }
            var menu = interaction.CombatEntries();
            var enemies = interaction.Targets();
            foreach (var entry in menu.Where(e => e.Enabled))
            {
                Assert.Equal(CombatPick.Target, interaction.ChooseCombat(entry.Mode, byKey: true));
                var fresh = enemies.ToDictionary(t => t, t => Previews.Target(state, seat, Kind(entry.Mode), actor, t));
                Assert.Equal(enemies.Select(t => (t, fresh[t].Refusal)), interaction.TargetVerdicts());
                foreach (var t in enemies)
                {
                    Assert.Equal(Canon(fresh[t]), Canon(interaction.PreviewOf(t)));
                    checkedTargets++;
                }
                var first = enemies.First(t => fresh[t].Legal);
                interaction.SetTarget(first);
                Assert.Equal(Canon(fresh[first]), Canon(interaction.Preview));
                charges += entry.Mode == InteractionMode.Charge ? 1 : 0;
                Assert.True(interaction.Start(InteractionMode.Combat));
            }
        }
        Assert.True(checkedTargets > 100 && charges > 10, $"{checkedTargets} targets, {charges} charges checked");
    }

    [Fact]
    public void OpeningCombatWorksOutEachPreviewOnceAndTheRestReuseIt()
    {
        var checkedStates = 0;
        foreach (var (state, seat) in Activations())
        {
            var interaction = new Interaction();
            interaction.Update(state, seat);
            if (!interaction.Start(InteractionMode.Combat) || interaction.CombatEntries().FirstOrDefault(e => e.Mode == InteractionMode.Charge && e.Enabled) is null)
            {
                continue;
            }
            var afterMenu = interaction.TargetPreviewsWorkedOut;
            Assert.True(afterMenu > 0);
            interaction.ChooseCombat(InteractionMode.Charge, byKey: true);
            _ = interaction.TargetVerdicts();
            var targets = interaction.Targets();
            foreach (var t in targets)
            {
                _ = interaction.PreviewOf(t);
            }
            interaction.SetTarget(targets.First(t => interaction.TargetVerdicts().Any(v => v.Model == t && v.Refusal is null)));
            _ = interaction.Preview;
            _ = interaction.TargetVerdicts();
            // Back to the menu and into Charge again: still nothing new.
            interaction.Start(InteractionMode.Combat);
            _ = interaction.CombatEntries();
            Assert.Equal(afterMenu, interaction.TargetPreviewsWorkedOut);
            // The same state again from the server (the same object) keeps them.
            interaction.Update(state, seat);
            _ = interaction.CombatEntries();
            Assert.Equal(afterMenu, interaction.TargetPreviewsWorkedOut);
            checkedStates++;
        }
        Assert.True(checkedStates > 10, $"{checkedStates} states");
    }

    [Fact]
    public void ANewStateIsNeverAnsweredFromTheOldOne()
    {
        var checkedStates = 0;
        var list = Activations().ToList();
        for (var i = 0; i + 1 < list.Count; i++)
        {
            var (state, seat) = list[i];
            var (next, nextSeat) = list[i + 1];
            var interaction = new Interaction();
            interaction.Update(state, seat);
            if (!interaction.Start(InteractionMode.Combat))
            {
                continue;
            }
            _ = interaction.CombatEntries();
            var before = interaction.TargetPreviewsWorkedOut;
            if (before == 0)
            {
                continue;   // every attack closed before any target is looked at: nothing was worked out to keep
            }
            // A copy of the state, equal in every value but another object, is a new state too: the memo is keyed on the
            // object, not on a comparison that could miss a change.
            interaction.Update(state with { }, seat);
            interaction.Start(InteractionMode.Combat);
            _ = interaction.CombatEntries();
            Assert.Equal(2 * before, interaction.TargetPreviewsWorkedOut);
            before = interaction.TargetPreviewsWorkedOut;
            interaction.Update(next, nextSeat);
            if (!interaction.Start(InteractionMode.Combat) || interaction.Actor is not { } actor)
            {
                continue;
            }
            var menu = interaction.CombatEntries();
            Assert.True(interaction.TargetPreviewsWorkedOut > before || menu.All(e => !e.Enabled && e.Targets == 0),
                "the new state's menu was answered without working anything out");
            foreach (var entry in menu.Where(e => e.Enabled))
            {
                interaction.ChooseCombat(entry.Mode, byKey: true);
                foreach (var t in interaction.Targets())
                {
                    Assert.Equal(Canon(Previews.Target(next, nextSeat, Kind(entry.Mode), actor, t)), Canon(interaction.PreviewOf(t)));
                }
                interaction.Start(InteractionMode.Combat);
            }
            checkedStates++;
        }
        Assert.True(checkedStates > 10, $"{checkedStates} states");
    }

    [Fact]
    public void TheBarsRememberedChecksAreTheRulesOwnOnEachState()
    {
        var checkedStates = 0;
        var interaction = new Interaction();
        foreach (var (state, seat) in Activations())
        {
            // One Interaction through the whole match, as the client has: each new state must be checked afresh.
            interaction.Update(state, seat);
            var actor = state.ActiveModel!.Value;
            var available = interaction.Available;
            var offered = available.Where(m => m != InteractionMode.Combat && Previews.ActionOpen(state, seat, actor, Kind(m)) is null).ToList();
            Assert.Equal(offered, interaction.Offered);
            foreach (var mode in available.Where(m => m != InteractionMode.Combat))
            {
                Assert.Equal(Previews.ActionOpen(state, seat, actor, Kind(mode))?.Text, interaction.WhyNot(mode));
            }
            var bar = interaction.Bar(actor);
            var again = interaction.Bar(actor);
            Assert.Equal(bar.Anchor, again.Anchor);
            Assert.Equal(bar.Items, again.Items);
            var legal = Rules.Core.TurnSequence.LegalCommands(state).OfType<Rules.Core.Commands.EndActivation>().Any();
            Assert.Equal(legal, bar.Items.Any(i => i.Kind == BarKind.EndActivation));
            checkedStates++;
        }
        Assert.True(checkedStates > 50, $"{checkedStates} states");
    }
}
