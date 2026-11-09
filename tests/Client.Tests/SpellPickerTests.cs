using System.Collections.Immutable;
using Client.Core;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.State;
using Sim;

namespace Client.Tests;

// October 2026 Review, stage C6: the spell menu on the action bar.
public class SpellPickerTests
{
    private const SpellKind Known = SpellKind.CharmOfAcedia | SpellKind.ExquisitePain | SpellKind.UncaringGluttony | SpellKind.RainingBlood;

    // A state in the middle of a model's activation, every model DEMONIC and knowing the spells, the model's seat holding SIN MARKERS.
    private static MatchState Activating(int sin)
    {
        var warbands = Content.Load(Content.DefaultPath()).Select(w => w with
        {
            Models = w.Models.Select(m => m with { Profile = m.Profile with { Traits = ModelTraits.Demonic, Spells = Known } }).ToList(),
        }).ToList();
        var state = MatchState.None;
        foreach (var e in Runner.Run(3, warbands).Log)
        {
            state = Reducer.Apply(state, e);
            if (state.Phase == Phase.Activation && state.ActiveModel is { } active && state.Pending is null && state.FindModel(active) is { } model && state.Actions == ActionsTaken.None)
            {
                var owner = model.Seat;
                return state with { Models = [.. state.Models.Select(m => m.Seat == owner ? m with { SinMarkers = sin } : m)] };
            }
        }
        throw new InvalidOperationException("No activation found.");
    }

    [Fact]
    public void TheMenuListsTheSpellsThenTheirCastsAndEveryCastIsLegal()
    {
        var state = Activating(3);
        var seat = state.FindModel(state.ActiveModel!.Value)!.Seat;
        var choices = SpellPicker.Choices(state, seat);
        Assert.Contains(choices, c => c.Spell == SpellKind.CharmOfAcedia);
        Assert.Contains(choices, c => c.Spell == SpellKind.ExquisitePain);
        Assert.DoesNotContain(choices, c => c.Spell == SpellKind.RainingBlood);   // needs a list of targets: CLI only
        Assert.All(choices.SelectMany(c => c.Casts), cast => Assert.Null(TurnSequence.Check(state, cast)));

        var interaction = new Interaction();
        interaction.Update(state, seat);
        var bar = interaction.Bar(null);
        Assert.Contains(bar.Items, i => i.Kind == BarKind.Spell && i.Spell == SpellKind.None);
        interaction.OpenSpells();
        var menu = interaction.Bar(null).Items;
        Assert.Contains(menu, i => i.Kind == BarKind.Spell && i.Spell == SpellKind.CharmOfAcedia);
        Assert.Equal(BarKind.SpellBack, menu[^1].Kind);
        interaction.PickSpell(SpellKind.CharmOfAcedia);
        var casts = interaction.Bar(null).Items.Where(i => i.Kind == BarKind.Decide).ToList();
        var charm = Assert.Single(casts);
        Assert.IsType<CastSpell>(charm.Command);
        interaction.SpellBack();
        Assert.Contains(interaction.Bar(null).Items, i => i.Kind == BarKind.Spell && i.Spell == SpellKind.ExquisitePain);
        interaction.SpellBack();
        Assert.False(interaction.SpellsOpen);
    }

    [Fact]
    public void NoMarkersNoMenu()
    {
        var state = Activating(0);
        var seat = state.FindModel(state.ActiveModel!.Value)!.Seat;
        Assert.Empty(SpellPicker.Choices(state, seat));
        var interaction = new Interaction();
        interaction.Update(state, seat);
        Assert.DoesNotContain(interaction.Bar(null).Items, i => i.Kind == BarKind.Spell);
        interaction.OpenSpells();
        Assert.False(interaction.SpellsOpen);
    }
}
