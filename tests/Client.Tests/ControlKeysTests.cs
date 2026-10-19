using Client.Core;
using Rules.Core.State;

namespace Client.Tests;

// QoL H5: the keys shown on the action bar's buttons are the battlefield's own (none is new), every action has its own,
// and none of them is a camera key, so a camera key never starts or sends an action.
public class ControlKeysTests
{
    private static readonly InteractionMode[] Actions =
        [InteractionMode.Place, InteractionMode.Move, InteractionMode.Dash, InteractionMode.Retreat, InteractionMode.Shoot, InteractionMode.Charge, InteractionMode.Fight];

    // Arrows, + and -, Q E (turn), F (fit; Shift+F reset), T (top-down), Home (centre), N P (next model), J (next unit),
    // O (next objective).
    private static readonly string[] CameraKeys = ["Q", "E", "F", "T", "Home", "N", "P", "J", "O"];

    [Fact]
    public void EachActionHasItsOwnKeyAndNoneIsACameraKey()
    {
        var keys = Actions.Select(Interaction.Key).ToList();
        Assert.All(keys, k => Assert.False(string.IsNullOrEmpty(k)));
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.Empty(keys.Intersect(CameraKeys));
        Assert.DoesNotContain("A", keys);
    }

    [Fact]
    public void TheBarShowsTheKeyThatDoesTheSame()
    {
        foreach (var mode in Actions)
        {
            var item = new BarItem(BarKind.Start, Interaction.Words(mode), Interaction.Tip(mode), Mode: mode);
            Assert.Equal($"{Interaction.Words(mode)} · {Interaction.Key(mode)}", item.ButtonText);
            Assert.Contains($"(key {Interaction.Key(mode)})", Interaction.Tip(mode), StringComparison.Ordinal);
        }
        Assert.Equal("Activate · A", new BarItem(BarKind.Activate, "Activate", "").ButtonText);
        Assert.Equal("Confirm · Space", new BarItem(BarKind.Confirm, "Confirm", "").ButtonText);
        Assert.Equal("Cancel · Esc", new BarItem(BarKind.Cancel, "Cancel", "").ButtonText);
        Assert.Equal("Undo point · Backspace", new BarItem(BarKind.Undo, "Undo point", "").ButtonText);
        // Without a key the label stands alone.
        Assert.Equal("End activation", new BarItem(BarKind.EndActivation, "End activation", "").ButtonText);
        Assert.Equal("No Blood", new BarItem(BarKind.Decide, "No Blood", "").ButtonText);
    }

    [Fact]
    public void EveryBarInTheSampleShowsKeysOnlyWhereTheyExist()
    {
        var seen = 0;
        foreach (var state in PreviewAgreementTests.States.Value.Where(s => s.Phase == Phase.Activation && s.ActiveModel is not null).Take(300))
        {
            var seat = state.FindModel(state.ActiveModel!.Value)!.Seat;
            var interaction = new Interaction();
            interaction.Update(state, seat);
            foreach (var item in interaction.Bar(state.ActiveModel).Items)
            {
                Assert.Equal(item.Kind is BarKind.Start or BarKind.Activate or BarKind.Confirm or BarKind.Undo or BarKind.Cancel, item.Key is not null);
                Assert.StartsWith(item.Label, item.ButtonText, StringComparison.Ordinal);
                seen++;
            }
        }
        Assert.True(seen > 300, $"{seen} bar items");
    }
}
