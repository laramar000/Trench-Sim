using Client.Core;
using Rules.Core;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Client.Tests;

// Proposal M1: a ranged weapon with "Melee" under its Range (a Pistol, 12"/Melee; 1.0.2 pp. 56, 68, 70) can make
// Melee Attacks. The client's Fight preview, the action line and the unit card follow the rules' own answer.
public class DualPurposeMeleeTests
{
    private static MatchState WithProfile(MatchState state, ModelId id, ModelProfile profile)
    {
        var setup = state.Setup!;
        return state with
        {
            Setup = new MatchSetup(setup.RulesVersion, setup.ScenarioId, setup.TurnLimit, setup.Profile, setup.ZoneIds,
                setup.Models.Select(m => m.Id == id ? m with { Profile = profile } : m), setup.Scenario),
        };
    }

    [Fact]
    public void AFightThePreviewAllowsWithAMeleeWeaponIsAllowedWithAPistolAndRefusedWithNeither()
    {
        var checkedStates = 0;
        foreach (var state in PreviewAgreementTests.States.Value.Where(s => s.ActiveModel is not null))
        {
            var active = state.FindModel(state.ActiveModel!.Value)!;
            var profile = state.Setup!.ProfileOf(active.Id)!;
            if (!profile.HasMeleeWeapon || profile.MeleeInjuryModifier != 0)
            {
                continue;
            }
            var enemy = state.Models.FirstOrDefault(m => m.Seat != active.Seat && Previews.Target(state, active.Seat, PreviewKind.Fight, active.Id, m.Id).Legal);
            if (enemy is null)
            {
                continue;
            }
            var range = profile.HasRangedWeapon ? profile.RangedRange : Length.FromInches(12);
            var pistol = WithProfile(state, active.Id, new ModelProfile(profile.Movement, profile.BaseRadius, range, 0, profile.Armour, rangedAlsoMelee: true));
            Assert.True(Previews.Target(pistol, active.Seat, PreviewKind.Fight, active.Id, enemy.Id).Legal);
            Assert.Null(TurnSequence.Check(pistol, new Rules.Core.Commands.FightModel(active.Seat, active.Id, enemy.Id)));
            Assert.Equal("Fight: its ranged weapon (also Melee), against an engaged enemy", ActionStats.For(pistol, active.Seat, active.Id, InteractionMode.Fight));

            var none = WithProfile(state, active.Id, new ModelProfile(profile.Movement, profile.BaseRadius, range, 0, profile.Armour));
            Assert.Equal(ReasonCode.NoMeleeWeapon, Previews.Target(none, active.Seat, PreviewKind.Fight, active.Id, enemy.Id).Refusal?.Code);
            Assert.Equal(ReasonCode.NoMeleeWeapon, TurnSequence.Check(none, new Rules.Core.Commands.FightModel(active.Seat, active.Id, enemy.Id))?.Code);
            Assert.Equal("Fight: no Melee Weapon", ActionStats.For(none, active.Seat, active.Id, InteractionMode.Fight));
            if (++checkedStates >= 25)
            {
                break;
            }
        }
        Assert.True(checkedStates >= 10, $"{checkedStates} states");
    }

    [Fact]
    public void ContentReadsTheMeleeMarkerInBothReaders()
    {
        const string json = """
            { "warbands": [ { "id": "w", "name": "W", "models": [
              { "name": "P", "movementInches": "6", "baseMm": 25, "rangeInches": "12", "weaponInjuryModifier": 0, "armour": 0, "rangedAlsoMelee": true },
              { "name": "R", "movementInches": "6", "baseMm": 25, "rangeInches": "12", "weaponInjuryModifier": 0, "armour": 0 } ] } ] }
            """;
        var ours = ContentNames.Parse(json).Single().Models;
        var sims = Sim.Content.Parse(json).Single().Models;
        Assert.True(ours[0].Profile.RangedAlsoMelee);
        Assert.False(ours[1].Profile.RangedAlsoMelee);
        Assert.Equal(sims.Select(m => m.Profile), ours.Select(m => m.Profile));
        Assert.Equal("Ranged weapon also Melee", UnitPanel.MeleeWords(ours[0].Profile));
        Assert.Equal("No melee weapon", UnitPanel.MeleeWords(ours[1].Profile));
    }
}
