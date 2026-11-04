using Client.Core;
using Rules.Core;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.Scenarios;
using Rules.Core.State;
using Sim;

namespace Client.Tests;

// Roster stage R4a (U101): with grenades, SHRAPNEL, GAS, FIRE and NEGATE, the attack previews agree with the server for
// the grenade too (weapon 2), the preview's extra BLOOD MARKERS are the server's, and the Combat menu offers the grenade
// on key 6.
public class GrenadePreviewTests
{
    private static ModelProfile Armed(ModelProfile p, int i) =>
        new(p.Movement, p.BaseRadius, p.RangedRange, p.RangedInjuryModifier, p.Armour, p.HasMeleeWeapon, p.MeleeInjuryModifier)
        {
            RangedAlsoMelee = p.RangedAlsoMelee,
            RangedWeaponKeywords = p.HasRangedWeapon && i % 2 == 0 ? WeaponKeywords.Shrapnel : WeaponKeywords.None,
            MeleeWeaponKeywords = p.HasMeleeWeapon && i % 2 == 1 ? WeaponKeywords.Fire : WeaponKeywords.None,
            Grenade = i % 3 != 2 ? new Weapon(Length.Parse("8"), keywords: WeaponKeywords.Gas | WeaponKeywords.IgnoreLongRange | WeaponKeywords.Assault) : null,
            Negates = i % 4 == 3 ? WeaponKeywords.Gas | WeaponKeywords.Fire : WeaponKeywords.None,
        };

    private static readonly Lazy<List<MatchState>> States = new(() =>
    {
        var all = new List<MatchState>();
        var warbands = Content.Load(Path.Combine(Repository.Root, "content", "melee-warbands.json"));
        for (var seed = 0; seed < 8; seed++)
        {
            var one = warbands[seed % 2];
            var two = warbands[(seed + 1) % 2];
            var models = one.Models.Select((m, i) => new SetupModel(new ModelId($"a{i + 1}"), Seat.One, Armed(m.Profile, i)))
                .Concat(two.Models.Select((m, i) => new SetupModel(new ModelId($"b{i + 1}"), Seat.Two, Armed(m.Profile, i + 1))));
            var state = Reducer.Apply(MatchState.None, new MatchCreated(ClaimNoMansLand.CreateSetup(models)));
            var bot = new Bot(new Random(seed));
            var dice = new SeededDice(seed * 17 + 3);
            for (var step = 0; step < 3000 && state.Phase != Phase.Ended; step++)
            {
                var next = bot.Propose(state).Select(c => TurnSequence.Decide(state, c, dice)).FirstOrDefault(r => r.IsAccepted);
                Assert.NotNull(next);
                state = next.State!;
                all.Add(state);
            }
            Assert.Equal(Phase.Ended, state.Phase);
        }
        return all;
    });

    private static IEnumerable<MatchState> ActivationStates() =>
        States.Value.Where(s => s.Phase == Phase.Activation && s.Pending is null && s.ActiveModel is { } a && s.FindModel(a)?.Position is not null);

    [Fact]
    public void GrenadePreviewsAgreeWithTheServer()
    {
        var seen = new Dictionary<string, int>();
        void Saw(string what) => seen[what] = seen.GetValueOrDefault(what) + 1;
        var seed = 0;
        foreach (var state in ActivationStates())
        {
            var active = state.FindModel(state.ActiveModel!.Value)!;
            foreach (var (kind, weapon) in new[] { (PreviewKind.Shoot, 0), (PreviewKind.Shoot, ModelProfile.GrenadeWeapon), (PreviewKind.Fight, 0) })
            {
                foreach (var target in state.Models.Where(m => m.Seat != active.Seat))
                {
                    var preview = Previews.Target(state, active.Seat, kind, active.Id, target.Id, weapon);
                    var decided = TurnSequence.Decide(state, preview.Command, new SeededDice(++seed));
                    Assert.True(decided.IsAccepted == preview.Legal, $"{preview.Command}: preview {(preview.Legal ? "legal" : preview.Refusal!.Text)}, server {(decided.IsAccepted ? "accepted" : decided.Rejection!.Text)}");
                    Assert.Equal(decided.Rejection, preview.Refusal);
                    if (!preview.Legal)
                    {
                        Saw($"{kind} {weapon} refused {preview.Refusal!.Code}");
                        continue;
                    }
                    Saw($"{kind} {weapon} legal");
                    if (kind == PreviewKind.Shoot)
                    {
                        Assert.Equal(weapon, decided.Events.OfType<RangedAttackDeclared>().Single().Weapon);
                    }
                    var intoMelee = decided.Events.OfType<RangedAttackDeclared>().SingleOrDefault()?.IntoMelee == true;
                    // A Blood Marker decision may come before the Injury Roll: the bot answers it.
                    var events = decided.Events.ToList();
                    var after = decided.State!;
                    var bot = new Bot(new Random(1));
                    var rolls = new SeededDice(9);
                    for (var k = 0; k < 4 && after.Pending is not null && !events.OfType<InjuryRolled>().Any(); k++)
                    {
                        var answered = bot.Propose(after).Select(c => TurnSequence.Decide(after, c, rolls)).First(r => r.IsAccepted);
                        events.AddRange(answered.Events);
                        after = answered.State!;
                    }
                    if (!intoMelee && events.OfType<InjuryRolled>().FirstOrDefault() is { } injury)
                    {
                        Assert.Equal(preview.Dice!.ExtraMarkers, injury.ExtraMarkers);
                        Assert.Equal(preview.Dice.InjuryModifier, injury.Injury.Modifier);
                        Saw($"extra markers {injury.ExtraMarkers}");
                    }
                }
            }
        }
        var summary = string.Join(", ", seen.OrderBy(p => p.Key).Select(p => $"{p.Key}: {p.Value}"));
        Assert.True(seen.GetValueOrDefault("Shoot 2 legal") > 0 && seen.GetValueOrDefault("Shoot 2 refused NoRangedWeapon") > 0
            && seen.GetValueOrDefault("extra markers 0") > 0 && seen.GetValueOrDefault("extra markers 1") > 0, summary);
    }

    [Fact]
    public void TheBotThrowsGrenades()
    {
        var thrown = States.Value.Zip(States.Value.Skip(1)).Count(p => p.Second.ShotWeapon == ModelProfile.GrenadeWeapon && p.First.ShotWeapon != ModelProfile.GrenadeWeapon);
        Assert.True(thrown > 0);
    }

    [Fact]
    public void TheCombatMenuOffersTheGrenade()
    {
        var found = false;
        foreach (var state in ActivationStates())
        {
            var active = state.FindModel(state.ActiveModel!.Value)!;
            var profile = state.Setup!.ProfileOf(active.Id)!;
            var interaction = new Interaction();
            interaction.Update(state, active.Seat);
            interaction.Start(InteractionMode.Combat);
            var entries = interaction.CombatEntries();
            if (profile.Grenade is null)
            {
                Assert.DoesNotContain(entries, e => e.Weapon == ModelProfile.GrenadeWeapon);
                continue;
            }
            Assert.Contains(entries, e => e is { Mode: InteractionMode.Shoot, Weapon: ModelProfile.GrenadeWeapon, Key: "6", Label: "Throw grenade" });
            if (entries.FirstOrDefault(e => e is { Mode: InteractionMode.Shoot, Weapon: ModelProfile.GrenadeWeapon, Enabled: true }) is not null)
            {
                Assert.Equal(CombatPick.Target, interaction.ChooseCombat(InteractionMode.Shoot, weapon: ModelProfile.GrenadeWeapon));
                Assert.Equal(ModelProfile.GrenadeWeapon, interaction.Weapon);
                Assert.Contains("(grenades)", interaction.Stats, StringComparison.Ordinal);
                found = true;
                break;
            }
        }
        Assert.True(found, "no state where the grenade could be thrown");
    }

    [Fact]
    public void TheUnitPanelAndTheNotesNameTheGrenade()
    {
        var p = new ModelProfile(Length.Parse("6"), Length.Parse("0.5"), Length.Parse("18")) { Grenade = new Weapon(Length.Parse("8")) };
        Assert.Contains($"Grenades range {BoardScene.Inch(8)}", UnitPanel.WeaponWords(p), StringComparison.Ordinal);
    }
}
