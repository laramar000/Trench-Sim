using Client.Core;
using Rules.Core;
using Rules.Core.State;
using Sim;

namespace Client.Tests;

// Battlefield UI 3, stage U1 (approved by Jason on 2026-10-22): Combat → the attack → the target → Confirm. The Combat menu
// lists the acting model's attacks before any target, each open exactly when the rules open that action and accept at
// least one enemy as its target (the previews agree with the server: PreviewAgreementTests); picking one chooses the
// attack with no target; the enemies are ringed or crossed by the rules' own verdicts; only Confirm sends, once, the
// command the preview showed, always for the server's active model. Replaces the K6 CombatMenuTests (enemy first).
public class CombatFlowTests
{
    private static List<MatchState> States => PreviewAgreementTests.States.Value;

    private static readonly InteractionMode[] Attacks = [InteractionMode.Shoot, InteractionMode.Charge, InteractionMode.Fight];

    private static IEnumerable<(MatchState State, Seat Seat)> Activations() =>
        States.Where(s => s.Phase == Phase.Activation && s.Pending is null && s.ActiveModel is { } a && s.FindModel(a)!.Position is not null)
            .Select(s => (s, s.FindModel(s.ActiveModel!.Value)!.Seat));

    private static PreviewKind Kind(InteractionMode mode) => mode switch
    {
        InteractionMode.Shoot => PreviewKind.Shoot,
        InteractionMode.Charge => PreviewKind.Charge,
        _ => PreviewKind.Fight,
    };

    [Fact]
    public void TheMenuListsEachAttackAsTheRulesOpenItBeforeAnyTarget()
    {
        var (entries, enabled, closed, noTarget) = (0, 0, 0, 0);
        foreach (var (state, seat) in Activations())
        {
            var actor = state.ActiveModel!.Value;
            var interaction = new Interaction();
            interaction.Update(state, seat);
            Assert.True(interaction.Start(InteractionMode.Combat));
            Assert.Null(interaction.Target);
            Assert.Null(interaction.Preview);
            Assert.Equal(actor, interaction.Actor);
            var menu = interaction.CombatEntries();
            Assert.Equal(Attacks, menu.Select(e => e.Mode));
            Assert.Equal(["1", "2", "3"], menu.Select(e => e.Key));
            var enemies = interaction.Targets();
            foreach (var entry in menu)
            {
                var verdicts = enemies.Select(t => Previews.Target(state, seat, Kind(entry.Mode), actor, t)).ToList();
                var open = Previews.ActionOpen(state, seat, actor, Kind(entry.Mode));
                var legal = open is null ? verdicts.Count(v => v.Legal) : 0;
                Assert.Equal(legal, entry.Targets);
                Assert.Equal(open is null && legal > 0, entry.Enabled);
                if (entry.Enabled)
                {
                    Assert.Null(entry.Why);
                    enabled++;
                }
                else if (open is not null)
                {
                    Assert.Equal($"{open.Text} ({open.RuleRef})", entry.Why);
                    closed++;
                }
                else
                {
                    var first = verdicts.Select(v => v.Refusal).First(r => r is not null)!;
                    Assert.Equal($"No enemy can be the target: {first.Text} ({first.RuleRef})", entry.Why);
                    noTarget++;
                }
                entries++;
            }
            // Combat itself chooses no target: a pick of an enemy is not possible until an attack is chosen.
            Assert.False(interaction.IsTarget);
            Assert.Empty(interaction.TargetVerdicts());
        }
        // Previews.ActionOpen already refuses an attack with no enemy in reach, so "no enemy can be the target" is rare.
        Assert.True(entries > 200 && enabled > 10 && closed > 10, $"{entries} entries, {enabled} open, {closed} closed, {noTarget} with no target");
    }

    [Fact]
    public void PickingAnAttackChoosesItWithNoTargetAndTheRingsAreTheRulesVerdicts()
    {
        var (picked, greyedByKey) = (0, 0);
        foreach (var (state, seat) in Activations().Take(300))
        {
            var actor = state.ActiveModel!.Value;
            foreach (var mode in Attacks)
            {
                var interaction = new Interaction();
                interaction.Update(state, seat);
                interaction.Start(InteractionMode.Combat);
                var entry = interaction.CombatEntries().Single(e => e.Mode == mode);
                var byClick = interaction.ChooseCombat(mode);
                if (!entry.Enabled)
                {
                    // A greyed-out attack is not chosen by a click; by its key it is (Space twice still sends it anyway).
                    Assert.Equal(CombatPick.None, byClick);
                    Assert.Equal(InteractionMode.Combat, interaction.Mode);
                    Assert.Equal(CombatPick.Target, interaction.ChooseCombat(mode, byKey: true));
                    greyedByKey++;
                }
                else
                {
                    Assert.Equal(CombatPick.Target, byClick);
                    picked++;
                }
                Assert.Equal(mode, interaction.Mode);
                Assert.True(interaction.FromCombat);
                Assert.Equal(actor, interaction.Actor);
                Assert.Null(interaction.Target);
                Assert.Null(interaction.Preview);
                Assert.Null(interaction.Pending);
                // Every enemy is ringed or crossed by the rules' own check of that attack on it.
                var verdicts = interaction.TargetVerdicts();
                Assert.Equal(interaction.Targets(), verdicts.Select(v => v.Model));
                foreach (var (model, refusal) in verdicts)
                {
                    Assert.Equal(Previews.Target(state, seat, Kind(mode), actor, model).Refusal?.Code, refusal?.Code);
                }
                Assert.Equal(entry.Targets, entry.Enabled ? verdicts.Count(v => v.Refusal is null) : entry.Targets);
                // Not in Combat any more: picking again does nothing.
                Assert.Equal(CombatPick.None, interaction.ChooseCombat(mode));
            }
        }
        Assert.True(picked > 10 && greyedByKey > 10, $"{picked} picked, {greyedByKey} greyed chosen by key");
    }

    // Choosing is not confirming: Combat, the attack and the target send nothing; only Confirm (the button, Space or
    // Enter) sends, exactly once, the command the preview showed, for the server's active model.
    [Fact]
    public async Task OnlyConfirmSendsOnceAndAlwaysForTheActiveModel()
    {
        var (starts, confirmed) = (0, 0);
        foreach (var (state, seat) in Activations().Take(300))
        {
            var sent = new List<Rules.Core.Commands.Command>();
            var interaction = new Interaction { Send = (command, _) => { sent.Add(command); return Task.FromResult(new Feedback(FeedbackKind.Accepted, "ok")); } };
            interaction.Update(state, seat);
            foreach (var mode in Attacks.Prepend(InteractionMode.Combat))
            {
                if (!interaction.Start(mode))
                {
                    continue;
                }
                starts++;
                Assert.Null(interaction.Target);
                Assert.Null(interaction.Preview);
                Assert.Null(await interaction.ConfirmAsync());
                Assert.Empty(sent);
            }
            interaction.Start(InteractionMode.Combat);
            if (interaction.CombatEntries().FirstOrDefault(e => e.Enabled) is not { } entry)
            {
                continue;
            }
            Assert.Equal(CombatPick.Target, interaction.ChooseCombat(entry.Mode));
            Assert.Null(await interaction.ConfirmAsync());
            var target = interaction.TargetVerdicts().First(v => v.Refusal is null).Model;
            interaction.SetTarget(target);
            Assert.Empty(sent);
            Assert.Null(interaction.Pending);
            var shown = interaction.Preview!;
            Assert.True(shown.Legal);
            Assert.Equal(state.ActiveModel, shown.Model);
            Assert.Equal(target, shown.Target);
            Assert.Equal(Previews.Target(state, seat, Kind(entry.Mode), state.ActiveModel!.Value, target).Command, shown.Command);
            Assert.NotNull(await interaction.ConfirmAsync());
            Assert.Equal(shown.Command, Assert.Single(sent));
            confirmed++;
        }
        Assert.True(starts > 100 && confirmed > 10, $"{starts} starts, {confirmed} confirmed");
    }

    // Esc (or a right-click) takes an attack picked from the menu back to the menu, then out; the card's Cancel leaves at
    // once; an attack started by its own key (S, X) leaves at once too. Nothing is sent.
    [Fact]
    public void EscStepsBackToTheMenuThenOut()
    {
        var (state, seat) = Activations().First(p => new Interaction() is var i && Update(i, p) && i.Start(InteractionMode.Combat) && i.CombatEntries().Any(e => e.Enabled));
        var interaction = new Interaction();
        interaction.Update(state, seat);
        interaction.Start(InteractionMode.Combat);
        var mode = interaction.CombatEntries().First(e => e.Enabled).Mode;
        interaction.ChooseCombat(mode);
        interaction.Cancel();
        Assert.Equal(InteractionMode.Combat, interaction.Mode);
        Assert.Equal(state.ActiveModel, interaction.Actor);
        Assert.False(interaction.FromCombat);
        interaction.Cancel();
        Assert.Equal(InteractionMode.None, interaction.Mode);

        interaction.Start(InteractionMode.Combat);
        interaction.ChooseCombat(mode);
        interaction.Cancel(all: true);
        Assert.Equal(InteractionMode.None, interaction.Mode);

        interaction.Start(mode);
        Assert.False(interaction.FromCombat);
        interaction.Cancel();
        Assert.Equal(InteractionMode.None, interaction.Mode);
        Assert.Null(interaction.Pending);
    }

    [Fact]
    public void TheBarHasOneCombatButtonAndTheKeysAreCThenOneTwoThree()
    {
        var (state, seat) = Activations().First(p => new Interaction() is var i && Update(i, p) && i.Offered.Any(Interaction.IsAttack));
        var interaction = new Interaction();
        interaction.Update(state, seat);
        var starts = interaction.Bar(null).Items.Where(i => i.Kind == BarKind.Start).ToList();
        var combat = Assert.Single(starts, i => i.Mode == InteractionMode.Combat);
        Assert.Equal("Combat · C", combat.ButtonText);
        Assert.DoesNotContain(starts, i => Interaction.IsAttack(i.Mode));
        Assert.Equal("C", Interaction.Key(InteractionMode.Combat));
        Assert.Equal("2", Interaction.Key(InteractionMode.Charge));
        Assert.Equal("S", Interaction.Key(InteractionMode.Shoot));
        Assert.Equal("X", Interaction.Key(InteractionMode.Fight));
    }

    // Stage K7, kept in U1: after the server accepts a charge, Fight is chosen on the charged enemy (Confirm fight beside
    // it, nothing sent) exactly when the rules let the charger Fight it now; otherwise nothing opens by itself.
    [Fact]
    public async Task AfterAnAcceptedChargeFightIsChosenOnTheChargedEnemyWhenItMayFight()
    {
        var (opened, notOpened) = (0, 0);
        foreach (var (state, seat) in Activations())
        {
            if (opened >= 20 && notOpened >= 20)
            {
                break;
            }
            var actor = state.ActiveModel!.Value;
            if (Previews.ActionOpen(state, seat, actor, PreviewKind.Charge) is not null)
            {
                continue;
            }
            var probe = new Interaction();
            probe.Update(state, seat);
            foreach (var target in probe.Start(InteractionMode.Combat) ? probe.Targets() : [])
            {
                var charge = Previews.Target(state, seat, PreviewKind.Charge, actor, target);
                if (!charge.Legal)
                {
                    continue;
                }
                var decided = TurnSequence.Decide(state, charge.Command, new SeededDice(1));
                if (!decided.IsAccepted)
                {
                    continue;
                }
                var after = decided.Events.Aggregate(state, Reducer.Apply);
                var sent = new List<Rules.Core.Commands.Command>();
                var interaction = new Interaction { Send = (c, _) => { sent.Add(c); return Task.FromResult(new Feedback(FeedbackKind.Accepted, "ok")); } };
                interaction.Update(state, seat);
                interaction.Start(InteractionMode.Combat);
                Assert.Equal(CombatPick.Target, interaction.ChooseCombat(InteractionMode.Charge));
                interaction.SetTarget(target);
                await interaction.ConfirmAsync();
                Assert.Equal(charge.Command, Assert.Single(sent));
                interaction.Update(after, seat);
                var mayFight = after.Pending is null && after.ActiveModel == actor
                    && Previews.ActionOpen(after, seat, actor, PreviewKind.Fight) is null
                    && Previews.Target(after, seat, PreviewKind.Fight, actor, target).Legal;
                if (mayFight)
                {
                    Assert.Equal(InteractionMode.Fight, interaction.Mode);
                    Assert.Equal(target, interaction.Target);
                    Assert.Equal(actor, interaction.Actor);
                    Assert.True(interaction.Preview is { Legal: true });
                    Assert.Equal(1, interaction.CombatAfterCharge);
                    Assert.Null(interaction.Pending);
                    Assert.Single(sent);
                    opened++;
                }
                else
                {
                    Assert.NotEqual(InteractionMode.Fight, interaction.Mode);
                    Assert.Equal(0, interaction.CombatAfterCharge);
                    notOpened++;
                }
                break;
            }
        }
        Assert.True(opened >= 20 && notOpened >= 20, $"{opened} opened, {notOpened} not");
    }

    private static bool Update(Interaction interaction, (MatchState State, Seat Seat) p)
    {
        interaction.Update(p.State, p.Seat);
        return true;
    }
}
