using System.Globalization;
using Client.Core;
using Rules.Core;
using Rules.Core.State;

namespace Client.Tests;

// QoL H3: the panel's unit status, checked against the server's state in every simulated match state: the marks the
// battlefield uses (↓ Down, •N Blood Markers, ✓ activated, ● activating, ○ can activate, — unavailable), the unit's
// values from its profile (Movement, ranged range, Armour, melee weapon; no wounds), and the warband's Shaken or Fled.
public class UnitStatusTests
{
    private static string In(Rules.Core.Geometry.Length length) => (length.Thousandths / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "\"";

    [Fact]
    public void ThePanelSaysWhatTheServersStateSaysForEveryUnit()
    {
        var (down, marked, shaken, fled, halved, activated) = (0, 0, 0, 0, 0, 0);
        foreach (var state in PreviewAgreementTests.States.Value.Where(s => s.Phase == Phase.Activation && s.Setup?.Scenario is not null))
        {
            var seat = state.ActiveModel is { } a ? state.FindModel(a)!.Seat : TurnSequence.NextActivator(state) ?? Seat.One;
            var scene = BoardScene.Of(state, seat, ContentNames.None)!;
            var interaction = new Interaction();
            interaction.Update(state, seat);
            foreach (var unit in scene.Models)
            {
                var model = state.FindModel(unit.Id)!;
                var profile = state.Setup!.ProfileOf(unit.Id)!;
                var panel = UnitPanel.Of(state, scene, interaction, seat, unit);
                var status = panel.Unit[0];
                Assert.Equal(model.Status == ModelStatus.Down, status.Contains("↓ Down", StringComparison.Ordinal));
                Assert.Equal(model.BloodMarkers > 0, status.Contains($"•{model.BloodMarkers} Blood Marker", StringComparison.Ordinal));
                var halfNow = state.ActiveModel == unit.Id && state.MovementHalved;
                var move = $"Move {In(profile.Movement)}" + (halfNow ? $" ({(profile.Movement.Thousandths / 2000.0).ToString("0.0", CultureInfo.InvariantCulture)}\" now, halved after standing up)" : "");
                var range = profile.RangedRange.Thousandths > 0 ? $"Range {In(profile.RangedRange)}" : "No ranged weapon";
                Assert.Contains($"{move} · {range} · Armour {profile.Armour} · {(profile.HasMeleeWeapon ? "Melee weapon" : "No melee weapon")}", panel.Unit);
                var warband = state.SeatFor(unit.Seat);
                Assert.Equal(warband.Fled ? "Warband: Fled" : warband.Shaken ? "Warband: Shaken" : null, panel.Unit.FirstOrDefault(l => l.StartsWith("Warband: ", StringComparison.Ordinal)));
                Assert.Equal(model.ActivatedThisTurn && state.ActiveModel != unit.Id, panel.Unit.Contains("✓ Activated this turn"));
                Assert.Equal(state.ActiveModel == unit.Id, panel.Unit.Contains("● Activating now"));
                Assert.True(panel.Unavailable is null || panel.Unavailable.StartsWith("— ", StringComparison.Ordinal), panel.Unavailable);
                Assert.DoesNotContain(panel.Unit, l => l.Contains("wound", StringComparison.OrdinalIgnoreCase));
                down += model.Status == ModelStatus.Down ? 1 : 0;
                marked += model.BloodMarkers > 0 ? 1 : 0;
                shaken += warband.Shaken && !warband.Fled ? 1 : 0;
                fled += warband.Fled ? 1 : 0;
                halved += halfNow ? 1 : 0;
                activated += model.ActivatedThisTurn ? 1 : 0;
            }
        }
        Assert.True(down > 20 && marked > 20 && activated > 100 && halved > 5, $"{down} down, {marked} with Blood Markers, {shaken} shaken, {fled} fled, {halved} halved, {activated} activated");
    }

    // The sample matches have no Shaken warband in activation, so the warband line is checked on a copy of a state.
    [Fact]
    public void AShakenOrFledWarbandIsSaidOnItsUnitsNotOnTheOpponents()
    {
        var state = PreviewAgreementTests.States.Value.First(s => s.Phase == Phase.Activation && s.Setup?.Scenario is not null);
        var scene = BoardScene.Of(state, Seat.One, ContentNames.None)!;
        var interaction = new Interaction();
        interaction.Update(state, Seat.One);
        var mine = scene.Models.First(m => m.Seat == Seat.One);
        var theirs = scene.Models.First(m => m.Seat == Seat.Two);
        foreach (var (shakenState, words) in new[] { (state with { One = state.One with { Shaken = true } }, "Warband: Shaken"), (state with { One = state.One with { Shaken = true, Fled = true } }, "Warband: Fled") })
        {
            Assert.Contains(words, UnitPanel.Of(shakenState, scene, interaction, Seat.One, mine).Unit);
            Assert.DoesNotContain(UnitPanel.Of(shakenState, scene, interaction, Seat.One, theirs).Unit, l => l.StartsWith("Warband: ", StringComparison.Ordinal));
        }
        Assert.DoesNotContain(UnitPanel.Of(state, scene, interaction, Seat.One, mine).Unit, l => l.StartsWith("Warband: ", StringComparison.Ordinal));
    }
}
