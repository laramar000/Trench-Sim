using Client.Core;
using Rules.Core;
using Sim;
using Rules.Core.State;

namespace Client.Tests;

// Battlefield UI, stage K6: one Combat action for Shoot, Charge and Fight. For the chosen enemy the menu lists each attack
// the acting model may take now, as the very preview choosing that action on that target gives (the previews agree with
// the server: PreviewAgreementTests); what the rules refuse is greyed out with their reason and is never sent from the menu.
public class CombatMenuTests
{
    private static List<MatchState> States => PreviewAgreementTests.States.Value;

    private static IEnumerable<(MatchState State, Seat Seat)> Activations() =>
        States.Where(s => s.Phase == Phase.Activation && s.Pending is null && s.ActiveModel is { } a && s.FindModel(a)!.Position is not null)
            .Select(s => (s, s.FindModel(s.ActiveModel!.Value)!.Seat));

    [Fact]
    public void TheEntriesForEachEnemyAreThePerActionPreviews()
    {
        var entries = 0;
        var enabled = 0;
        var greyed = 0;
        foreach (var (state, seat) in Activations().Take(300))
        {
            var interaction = new Interaction();
            interaction.Update(state, seat);
            Assert.True(interaction.Start(InteractionMode.Combat));
            Assert.Null(interaction.Preview);
            var verdicts = interaction.TargetVerdicts().ToDictionary(v => v.Model, v => v.Refusal);
            foreach (var target in interaction.Targets())
            {
                interaction.SetTarget(target);
                var menu = interaction.CombatEntries();
                Assert.Equal([InteractionMode.Shoot, InteractionMode.Charge, InteractionMode.Fight], menu.Select(e => e.Mode));
                Assert.Equal(["1", "2", "3"], menu.Select(e => e.Key));
                foreach (var entry in menu)
                {
                    var kind = entry.Mode switch { InteractionMode.Shoot => PreviewKind.Shoot, InteractionMode.Charge => PreviewKind.Charge, _ => PreviewKind.Fight };
                    var own = Previews.Target(state, seat, kind, state.ActiveModel!.Value, target);
                    Assert.Equal(own.Command, entry.Preview.Command);
                    Assert.Equal(own.Legal, entry.Preview.Legal);
                    var open = Previews.ActionOpen(state, seat, state.ActiveModel!.Value, kind);
                    Assert.Equal(open is null && own.Legal, entry.Enabled);
                    if (entry.Enabled)
                    {
                        Assert.Null(entry.Why);
                        enabled++;
                    }
                    else
                    {
                        var why = open ?? own.Refusal!;
                        Assert.Equal($"{why.Text} ({why.RuleRef})", entry.Why);
                        greyed++;
                    }
                    entries++;
                }
                // The enemy is ringed as a valid choice exactly when one of its attacks is open.
                Assert.Equal(menu.Any(e => e.Enabled), verdicts[target] is null);
            }
        }
        Assert.True(entries > 200 && enabled > 10 && greyed > 10, $"{entries} entries, {enabled} open, {greyed} greyed");
    }

    [Fact]
    public void PickingAnOpenAttackChoosesItOnTheSameTargetAndAGreyedOneOnlyByKey()
    {
        var picked = new HashSet<CombatPick>();
        foreach (var (state, seat) in Activations().Take(300))
        {
            var interaction = new Interaction();
            interaction.Update(state, seat);
            interaction.Start(InteractionMode.Combat);
            foreach (var target in interaction.Targets())
            {
                foreach (var mode in new[] { InteractionMode.Shoot, InteractionMode.Charge, InteractionMode.Fight })
                {
                    interaction.Start(InteractionMode.Combat);
                    interaction.SetTarget(target);
                    var entry = interaction.CombatEntries().Single(e => e.Mode == mode);
                    var byClick = interaction.ChooseCombat(mode);
                    if (!entry.Enabled)
                    {
                        Assert.Equal(CombatPick.None, byClick);
                        Assert.Equal(InteractionMode.Combat, interaction.Mode);
                        Assert.Equal(CombatPick.Shown, interaction.ChooseCombat(mode, byKey: true));
                    }
                    else
                    {
                        Assert.Equal(CombatPick.Confirm, byClick);
                    }
                    picked.Add(entry.Enabled ? byClick : CombatPick.Shown);
                    Assert.Equal(mode, interaction.Mode);
                    Assert.Equal(target, interaction.Target);
                    Assert.Equal(entry.Preview.Command, interaction.Preview!.Command);
                    Assert.Null(interaction.Pending);
                    // Not in Combat any more: picking again does nothing.
                    Assert.Equal(CombatPick.None, interaction.ChooseCombat(mode));
                }
            }
        }
        Assert.Contains(CombatPick.Confirm, picked);
        Assert.Contains(CombatPick.Shown, picked);
    }

    // Battlefield UI 2, stage N1: choosing an action is not confirming it. Starting any attack (or Combat) chooses no
    // target and sends nothing; a pick from the menu sends nothing; only Confirm (the button, Space or Enter) sends,
    // exactly once, and what it sends is the command the preview showed.
    [Fact]
    public async Task StartingOrPickingAnAttackNeverSendsOnlyConfirmDoesOnce()
    {
        var (starts, confirmed) = (0, 0);
        foreach (var (state, seat) in Activations().Take(300))
        {
            var sent = new List<Rules.Core.Commands.Command>();
            var interaction = new Interaction { Send = (command, _) => { sent.Add(command); return Task.FromResult(new Feedback(FeedbackKind.Accepted, "ok")); } };
            interaction.Update(state, seat);
            foreach (var mode in new[] { InteractionMode.Combat, InteractionMode.Shoot, InteractionMode.Charge, InteractionMode.Fight })
            {
                if (!interaction.Start(mode))
                {
                    continue;
                }
                starts++;
                Assert.Null(interaction.Target);
                Assert.Null(interaction.Pending);
                Assert.Null(interaction.Preview);
                // Confirm with no target chosen sends nothing.
                Assert.Null(await interaction.ConfirmAsync());
                Assert.Empty(sent);
            }
            if (!interaction.Start(InteractionMode.Combat))
            {
                continue;
            }
            foreach (var target in interaction.Targets())
            {
                interaction.Start(InteractionMode.Combat);
                interaction.SetTarget(target);
                if (interaction.CombatEntries().FirstOrDefault(e => e.Enabled) is not { } entry)
                {
                    continue;
                }
                Assert.Equal(CombatPick.Confirm, interaction.ChooseCombat(entry.Mode));
                Assert.Empty(sent);
                Assert.Null(interaction.Pending);
                var shown = interaction.Preview!.Command;
                Assert.Equal(entry.Preview.Command, shown);
                Assert.NotNull(await interaction.ConfirmAsync());
                Assert.Equal(shown, Assert.Single(sent));
                confirmed++;
                break;
            }
        }
        Assert.True(starts > 100 && confirmed > 10, $"{starts} starts, {confirmed} confirmed");
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

    // Stage K7: after the server accepts a charge, Combat opens on the charged enemy exactly when the rules let the
    // charger Fight it now (its Fight preview is legal and Fight is still open); otherwise nothing opens by itself.
    [Fact]
    public async Task AfterAnAcceptedChargeCombatOpensOnTheChargedEnemyWhenItMayFight()
    {
        var (opened, notOpened) = (0, 0);
        // Over every simulated activation (the melee content's chargers are later in the list), until enough have opened.
        foreach (var (state, seat) in Activations())
        {
            if (opened >= 20 && notOpened >= 20)
            {
                break;
            }
            var probe = new Interaction();
            probe.Update(state, seat);
            if (!probe.Start(InteractionMode.Combat))
            {
                continue;
            }
            foreach (var target in probe.Targets())
            {
                probe.Start(InteractionMode.Combat);
                probe.SetTarget(target);
                if (probe.CombatEntries().Single(e => e.Mode == InteractionMode.Charge) is not { Enabled: true } charge)
                {
                    continue;
                }
                var decided = TurnSequence.Decide(state, charge.Preview.Command, new SeededDice(1));
                if (!decided.IsAccepted)
                {
                    continue;
                }
                var after = decided.Events.Aggregate(state, Reducer.Apply);
                var interaction = new Interaction { Send = (_, _) => Task.FromResult(new Feedback(FeedbackKind.Accepted, "ok")) };
                interaction.Update(state, seat);
                interaction.Start(InteractionMode.Combat);
                interaction.SetTarget(target);
                Assert.Equal(CombatPick.Confirm, interaction.ChooseCombat(InteractionMode.Charge));
                await interaction.ConfirmAsync();
                interaction.Update(after, seat);
                var mayFight = after.Pending is null && after.ActiveModel == state.ActiveModel
                    && Previews.ActionOpen(after, seat, state.ActiveModel!.Value, PreviewKind.Fight) is null
                    && Previews.Target(after, seat, PreviewKind.Fight, state.ActiveModel!.Value, target).Legal;
                if (mayFight)
                {
                    Assert.Equal(InteractionMode.Combat, interaction.Mode);
                    Assert.Equal(target, interaction.Target);
                    Assert.True(interaction.CombatEntries().Single(e => e.Mode == InteractionMode.Fight).Enabled);
                    Assert.Equal(1, interaction.CombatAfterCharge);
                    Assert.Null(interaction.Pending);
                    opened++;
                }
                else
                {
                    Assert.NotEqual(InteractionMode.Combat, interaction.Mode);
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
