using Client.Core;
using Rules.Core.State;

namespace Client.Tests;

// Battlefield UI 3, stage U4: the compact line beside the action bar says only what the rules' own previews say: the
// Move, Dash and Retreat allowance the path previews use, the weapon range a Shoot preview states, and the charge
// reach a Charge preview states.
public class ActionStatsTests
{
    private static List<MatchState> States => PreviewAgreementTests.States.Value;

    private static string Number(string text, string after)
    {
        var at = text.IndexOf(after, StringComparison.Ordinal) + after.Length;
        return text[at..text.IndexOf('"', at)];
    }

    [Fact]
    public void EachLineIsWhatTheRulesPreviewsSay()
    {
        var (moves, shots, charges, fights) = (0, 0, 0, 0);
        foreach (var state in States.Where(s => s.ActiveModel is not null).Take(3000))
        {
            var active = state.FindModel(state.ActiveModel!.Value)!;
            var seat = active.Seat;
            foreach (var (mode, kind) in new[] { (InteractionMode.Move, PreviewKind.Move), (InteractionMode.Dash, PreviewKind.Dash), (InteractionMode.Retreat, PreviewKind.Retreat) })
            {
                var stats = ActionStats.For(state, seat, active.Id, mode);
                var preview = Previews.Path(state, seat, kind, active.Id, []);
                if (preview.Allowance is { } allowance && Previews.Reach(state, seat, active.Id) is not null)
                {
                    Assert.NotNull(stats);
                    Assert.Equal(BoardScene.Inch(allowance), Number(stats!, "up to ") + "\"");
                    Assert.Equal(state.MovementHalved, stats!.EndsWith("(halved after standing up)", StringComparison.Ordinal));
                    moves++;
                }
            }
            var enemy = state.Models.FirstOrDefault(m => m.Seat != seat && m.Position is not null);
            if (enemy is null)
            {
                continue;
            }
            var shoot = ActionStats.For(state, seat, active.Id, InteractionMode.Shoot)!;
            var shot = Previews.Target(state, seat, PreviewKind.Shoot, active.Id, enemy.Id);
            if (shot.Facts.FirstOrDefault(f => f.Contains("weapon range ", StringComparison.Ordinal)) is { } range)
            {
                Assert.Equal(Number(range, "weapon range "), Number(shoot, "weapon range "));
                shots++;
            }
            var charge = ActionStats.For(state, seat, active.Id, InteractionMode.Charge)!;
            var charged = Previews.Target(state, seat, PreviewKind.Charge, active.Id, enemy.Id);
            if (charged.Facts.FirstOrDefault(f => f.StartsWith("Charge reach: ", StringComparison.Ordinal)) is { } reach)
            {
                // "Charge reach: Movement 6.0" plus a D6, at most 12"[, halved after standing up]." in the preview.
                Assert.Equal(reach["Charge reach: ".Length..].TrimEnd('.').Replace(", halved", " (halved", StringComparison.Ordinal) + (state.MovementHalved ? ")" : ""),
                    charge["Charge: reach ".Length..]);
                charges++;
            }
            var fight = ActionStats.For(state, seat, active.Id, InteractionMode.Fight)!;
            Assert.Equal(state.Setup!.ProfileOf(active.Id)!.HasMeleeWeapon, !fight.Contains("no Melee Weapon", StringComparison.Ordinal));
            fights++;
        }
        Assert.True(moves > 300 && shots > 50 && charges > 50 && fights > 100, $"{moves} moves, {shots} shots, {charges} charges, {fights} fights");
    }

    [Fact]
    public void NoLineOutsideAnActionThatHasNumbers()
    {
        var state = States.First(s => s.ActiveModel is not null);
        var active = state.FindModel(state.ActiveModel!.Value)!;
        Assert.Null(ActionStats.For(state, active.Seat, active.Id, InteractionMode.None));
        Assert.Null(ActionStats.For(state, active.Seat, active.Id, InteractionMode.Combat));
    }
}
