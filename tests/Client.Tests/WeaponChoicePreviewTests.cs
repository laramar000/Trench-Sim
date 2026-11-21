using Client.Core;
using Rules.Core;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.Scenarios;
using Rules.Core.State;
using Sim;

namespace Client.Tests;

// Roster stage R3 (U100): with second weapons, CLEAVE, BLOCK and IMPERVIOUS, the attack previews agree with the server
// for either weapon, a Fight going on with its next attack previews that attack, and the Combat menu offers the second
// weapon on keys 4 and 5.
public class WeaponChoicePreviewTests
{
    private static ModelProfile Armed(ModelProfile p, int i) =>
        new(p.Movement, p.BaseRadius, p.RangedRange, p.RangedInjuryModifier, p.Armour == 0 ? -1 : p.Armour, p.HasMeleeWeapon, p.MeleeInjuryModifier)
        {
            RangedAlsoMelee = p.RangedAlsoMelee,
            MeleeWeaponCleave = p.HasMeleeWeapon && i >= 2 ? 2 : 0,
            ImperviousArmour = -1,
            SecondRangedWeapon = p.HasRangedWeapon ? new Weapon(Length.Parse("36"), injuryModifier: 1, dice: -1, keywords: WeaponKeywords.IgnoreArmour) : null,
            SecondMeleeWeapon = p.CanMakeMeleeAttack && i % 3 != 2 ? new Weapon(Length.Zero, injuryDice: 1, keywords: i % 3 == 0 ? WeaponKeywords.Block : WeaponKeywords.None) : null,
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
                .Concat(two.Models.Select((m, i) => new SetupModel(new ModelId($"b{i + 1}"), Seat.Two, Armed(m.Profile, i))));
            var state = Reducer.Apply(MatchState.None, new MatchCreated(ClaimNoMansLand.CreateSetup(models)));
            var bot = new Bot(new Random(seed));
            var dice = new SeededDice(seed * 31 + 5);
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
    public void AttackPreviewsAgreeWithTheServerForEitherWeapon()
    {
        var seen = new Dictionary<string, int>();
        void Saw(string what) => seen[what] = seen.GetValueOrDefault(what) + 1;
        var seed = 0; // fresh dice for each attack, so some hit and make their Injury Roll
        foreach (var state in ActivationStates())
        {
            var active = state.FindModel(state.ActiveModel!.Value)!;
            foreach (var (kind, weapon) in new[] { (PreviewKind.Shoot, 0), (PreviewKind.Shoot, 1), (PreviewKind.Fight, 0), (PreviewKind.Fight, 1) })
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
                    var dice = preview.Dice!;
                    if (kind == PreviewKind.Fight)
                    {
                        var declared = decided.Events.OfType<MeleeAttackDeclared>().Single();
                        Assert.Equal(dice.HitPenalty, declared.PenaltyDice);
                        Assert.Equal(declared.OffHand, dice.HitReasons.Any(r => r.StartsWith("Off-Hand", StringComparison.Ordinal)));
                        Assert.Equal(declared.Block, dice.HitReasons.Any(r => r.StartsWith("BLOCK", StringComparison.Ordinal)));
                        Saw(declared.OffHand ? "off-hand" : declared.Block ? "block" : TurnSequence.NextFightAttack(state, active.Id) is not null ? "cleave" : "first attack");
                    }
                    else
                    {
                        Assert.Equal(preview.Command is Rules.Core.Commands.Shoot { Weapon: 1 } ? 1 : 0, decided.Events.OfType<RangedAttackDeclared>().Single().Weapon);
                    }
                    var intoMelee = decided.Events.OfType<RangedAttackDeclared>().SingleOrDefault()?.IntoMelee == true;
                    if (!intoMelee && decided.Events.OfType<RangedSuccessRolled>().Select(r => r.Check).Concat(decided.Events.OfType<MeleeSuccessRolled>().Select(m => m.Check)).SingleOrDefault() is { } check)
                    {
                        Assert.Equal(dice.HitNetDice, check.NetDice);
                    }
                    if (!intoMelee && decided.Events.OfType<InjuryRolled>().SingleOrDefault() is { } injury)
                    {
                        Assert.Equal(dice.InjuryModifier, injury.Injury.Modifier);
                        Saw($"injury modifier {dice.InjuryModifier}");
                    }
                }
            }
        }
        var summary = string.Join(", ", seen.OrderBy(p => p.Key).Select(p => $"{p.Key}: {p.Value}"));
        Assert.True(seen.GetValueOrDefault("Shoot 1 legal") > 0 && seen.GetValueOrDefault("Fight 1 legal") > 0 && seen.GetValueOrDefault("off-hand") > 0
            && seen.GetValueOrDefault("cleave") > 0 && seen.GetValueOrDefault("Shoot 1 refused NoRangedWeapon") > 0
            && seen.Keys.Any(k => k.StartsWith("injury modifier", StringComparison.Ordinal)), summary);
    }

    [Fact]
    public void TheUnitPanelNamesTheWeapons()
    {
        var p = new ModelProfile(Length.Parse("6"), Length.Parse("0.5"), Length.Parse("18"), armour: -2, hasMeleeWeapon: true)
        {
            MeleeWeaponCleave = 2,
            ImperviousArmour = -1,
            MeleeWeaponKeywords = WeaponKeywords.Block,
            SecondRangedWeapon = new Weapon(Length.Parse("12")),
            SecondMeleeWeapon = new Weapon(Length.Zero),
        };
        Assert.Equal($" · 2nd gun range {BoardScene.Inch(12)} · 2nd Melee Weapon (Off-Hand -1 DICE) · CLEAVE 2 · BLOCK · IMPERVIOUS Armour -1", UnitPanel.WeaponWords(p));
        Assert.Equal("", UnitPanel.WeaponWords(new ModelProfile(Length.Parse("6"), Length.Parse("0.5"), Length.Parse("18"))));
    }

    [Fact]
    public void TheCombatMenuOffersTheSecondWeapons()
    {
        var found = false;
        foreach (var state in ActivationStates())
        {
            var active = state.FindModel(state.ActiveModel!.Value)!;
            var profile = state.Setup!.ProfileOf(active.Id)!;
            if (profile.Weapons(AttackKind.Ranged) < 2 || profile.Weapons(AttackKind.Melee) < 2 || TurnSequence.NextFightAttack(state, active.Id) is not null)
            {
                continue;
            }
            var interaction = new Interaction();
            interaction.Update(state, active.Seat);
            interaction.Start(InteractionMode.Combat);
            var entries = interaction.CombatEntries();
            Assert.Contains(entries, e => e is { Mode: InteractionMode.Shoot, Weapon: 1, Key: "4", Label: "Shoot, 2nd weapon" });
            Assert.Contains(entries, e => e is { Mode: InteractionMode.Fight, Weapon: 1, Key: "5", Label: "Fight, 2nd weapon" });
            if (entries.FirstOrDefault(e => e is { Mode: InteractionMode.Shoot, Weapon: 1, Enabled: true }) is { } shoot)
            {
                Assert.Equal(CombatPick.Target, interaction.ChooseCombat(InteractionMode.Shoot, weapon: 1));
                Assert.Equal(1, interaction.Weapon);
                Assert.Contains("(2nd weapon)", interaction.Stats, StringComparison.Ordinal);
                found = true;
                break;
            }
        }
        Assert.True(found, "no state where the second gun could shoot");
        // A model with one weapon of each kind sees only keys 1-3.
        var plain = PreviewAgreementTests.States.Value.First(s => s.Phase == Phase.Activation && s.Pending is null && s.ActiveModel is not null);
        var single = new Interaction();
        single.Update(plain, plain.FindModel(plain.ActiveModel!.Value)!.Seat);
        single.Start(InteractionMode.Combat);
        Assert.All(single.CombatEntries(), e => Assert.Equal(0, e.Weapon));
    }
}
