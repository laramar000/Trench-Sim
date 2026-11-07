using Client.Core;
using Rules.Core;
using Rules.Core.Events;
using Rules.Core.State;
using Sim;

namespace Client.Tests;

// Roster stage R5b (U106): the client shows FEAR's penalty die in the Fight preview and the action breakdown, TOUGH's
// Down instead of Out of Action in the log, the notes and the dice, LEADER's die on the Morale Check, and the traits on
// the unit card.
public class ModelTraitDisplayTests
{
    private static List<Warband> Traited() => Content.Load(Content.MeleePath()).Select(w => w with
    {
        Models = w.Models.Select((m, i) => m with
        {
            Profile = m.Profile with
            {
                Traits = (i % 2 == 0 ? ModelTraits.Fear | ModelTraits.Tough : ModelTraits.None) | (i == 0 ? ModelTraits.Leader : ModelTraits.None),
            },
        }).ToList(),
    }).ToList();

    [Fact]
    public void FearToughAndLeaderAreShown()
    {
        var (previews, fears, toughs, leaders) = (0, 0, 0, 0);
        for (var seed = 1; seed <= 12; seed++)
        {
            var run = Runner.Run(seed, Traited());
            var state = MatchState.None;
            var log = new ResolutionLog();
            var seq = 0;
            var feared = false;
            foreach (var e in run.Log)
            {
                if (e is MeleeAttackDeclared m && state.ActiveModel == m.Attacker && !m.FreeFight && state.Pending is null)
                {
                    var preview = Previews.Target(state, state.FindModel(m.Attacker)!.Seat, PreviewKind.Fight, m.Attacker, m.Target, m.Weapon);
                    if (preview.Legal && preview.Dice is { } dice)
                    {
                        Assert.Equal(m.Fear, dice.HitReasons.Contains("FEAR, p. 54"));
                        previews++;
                    }
                }
                if (e is MeleeAttackDeclared declared)
                {
                    feared = declared.Fear;
                }
                var setup = state.Setup;
                state = Reducer.Apply(state, e);
                log.Add(++seq, e, setup ?? state.Setup);
                switch (e)
                {
                    case MeleeSuccessRolled when log.Actions[^1].Steps.Count > 0:
                        var why = log.WhyOf(seq);
                        Assert.True(why is null || why.Explained, why?.Text);
                        Assert.Equal(feared, why?.Dice.Any(d => d.Label == "target has FEAR") ?? feared);
                        break;
                    case InjuryRolled { Tough: true } i:
                        Assert.Contains("TOUGH", EventText.Line(i), StringComparison.Ordinal);
                        Assert.Equal("TOUGH: DOWN +1 BLOOD", UnitNotes.Of(i).Single().Text);
                        Assert.Contains("(TOUGH, rolled Out of Action)", DiceRolls.Of(i)!.Result, StringComparison.Ordinal);
                        Assert.Equal(RowOutcome.Success, EventText.Outcome(i));
                        Assert.Equal("DOWNS", EventText.Compact(i).Verb);
                        toughs++;
                        break;
                    case MoraleCheckRolled { Check.NetDice: 1 } c:
                        Assert.Contains("+1 DICE from its LEADER", EventText.Line(c), StringComparison.Ordinal);
                        leaders++;
                        break;
                }
                if (e is MeleeAttackDeclared { Fear: true })
                {
                    fears++;
                }
            }
        }
        Assert.True(previews > 0 && fears > 0 && toughs > 0 && leaders > 0, $"previews {previews}, FEAR {fears}, TOUGH {toughs}, LEADER {leaders}");
    }

    // Roster stage R5c (U107): the target's -1 INJURY DICE and the Devotees' +INJURY MODIFIER are in the preview and in
    // every Injury Roll's breakdown, which adds up to the roll.
    [Fact]
    public void InjuryBattlekitIsExplained()
    {
        var warbands = Content.Load(Content.MeleePath()).Select(w => w with
        {
            Models = w.Models.Select((m, i) => m with { Profile = m.Profile with { InjuryDiceTaken = i % 2 == 0 ? -1 : 0, MeleeInjuryBonus = i % 2 == 1 ? 2 : 0 } }).ToList(),
        }).ToList();
        var (rolls, previews) = (0, 0);
        for (var seed = 1; seed <= 6; seed++)
        {
            var state = MatchState.None;
            var log = new ResolutionLog();
            var seq = 0;
            foreach (var e in Runner.Run(seed, warbands).Log)
            {
                if (e is MeleeAttackDeclared m && state.ActiveModel == m.Attacker && !m.FreeFight && state.Pending is null
                    && Previews.Target(state, state.FindModel(m.Attacker)!.Seat, PreviewKind.Fight, m.Attacker, m.Target, m.Weapon) is { Legal: true, Dice: { } dice })
                {
                    Assert.Equal(state.Setup!.ProfileOf(m.Target)!.InjuryDiceTaken, dice.TargetInjuryDice);
                    Assert.Equal(dice.TargetInjuryDice != 0, dice.Lines(m.Attacker, m.Target).Any(l => l.Contains("-1 DICE (target's battlekit and abilities)", StringComparison.Ordinal)));
                    previews++;
                }
                var setup = state.Setup;
                state = Reducer.Apply(state, e);
                log.Add(++seq, e, setup ?? state.Setup);
                if (e is InjuryRolled i && log.WhyOf(seq) is { } why)
                {
                    Assert.True(why.Explained, why.Text);
                    Assert.Equal(state.Setup!.ProfileOf(i.Target)!.InjuryDiceTaken != 0, why.Dice.Any(d => d.Label == "target's battlekit and abilities"));
                    rolls++;
                }
            }
        }
        Assert.True(rolls > 0 && previews > 0, $"{rolls} rolls, {previews} previews");
        var card = UnitPanel.WeaponWords(new ModelProfile(Rules.Core.Geometry.Length.Parse("6"), Rules.Core.Geometry.Length.Parse("0.5"), Rules.Core.Geometry.Length.Zero, hasMeleeWeapon: true) { InjuryDiceTaken = -1, MeleeInjuryBonus = 2 });
        Assert.Equal(" · hit for -1 INJURY DICE · Fight injury +2", card);
    }

    // Roster stage R5d (U108): the Dash preview names the Musical Instrument's die and the Crucifix; every Dash roll's
    // breakdown adds up with that die; the unit card names the four pieces.
    [Fact]
    public void RiskyAndCoverBattlekitIsExplained()
    {
        var (helped, previews, cloaks, crucifixes) = (0, 0, 0, 0);
        foreach (var (path, seed) in new[] { Content.MeleePath(), Content.DefaultPath() }.SelectMany(p => Enumerable.Range(1, 6).Select(s => (p, s))))
        {
            var warbands = Content.Load(path).Select(w => w with
            {
                Models = w.Models.Select((m, i) => m with { Profile = m.Profile with { Fanfare = i == 0, KeepsGoingAfterRisky = i % 2 == 1, BlendsIn = i % 2 == 0 } }).ToList(),
            }).ToList();
            var state = MatchState.None;
            var log = new ResolutionLog();
            var seq = 0;
            foreach (var e in Runner.Run(seed, warbands).Log)
            {
                if (e is DashDeclared d && state.ActiveModel == d.Model && state.Pending is null)
                {
                    var preview = Previews.Path(state, state.FindModel(d.Model)!.Seat, PreviewKind.Dash, d.Model, d.Path);
                    Assert.Equal(TurnSequence.DashDice(state, d.Model) > 0, preview.Facts.Contains("+1 DICE on the Dash roll: a Musical Instrument within 4\" (p. 85)."));
                    Assert.Equal(!TurnSequence.RiskyEnds(state, d.Model), preview.Facts.Contains("A failed Risky roll does not end this model's activation (its Crucifix)."));
                    previews++;
                }
                var setup = state.Setup;
                state = Reducer.Apply(state, e);
                log.Add(++seq, e, setup ?? state.Setup);
                if (e is DashRolled or RangedSuccessRolled && log.WhyOf(seq) is { } why)
                {
                    Assert.True(why.Explained, why.Text);
                    helped += why.Dice.Count(p => p.Label == "Musical Instrument within 4\"");
                    if (e is DashRolled { Check.RiskyFailure: true } failed)
                    {
                        var kept = state.Setup!.ProfileOf(failed.Model)!.KeepsGoingAfterRisky;
                        Assert.Contains(kept ? "RISKY: the Crucifix keeps the activation going" : "RISKY: the activation ends", log.Actions[^1].Outcome);
                        // The dice popup's rule line says the same.
                        Assert.EndsWith(kept ? "RISKY: a fail does not end the activation (Crucifix)" : "RISKY: a fail ends the activation", DiceRolls.Step(e, state.Setup)!.Roll!.Rule, StringComparison.Ordinal);
                        crucifixes += kept ? 1 : 0;
                    }
                    cloaks += why.Dice.Count(p => p.Label.EndsWith(", Cloak of the Mountain", StringComparison.Ordinal) && p.Value == -2);
                }
            }
        }
        Assert.True(helped > 0 && previews > 0 && cloaks > 0 && crucifixes > 0, $"{helped} helped, {previews} previews, {cloaks} cloaks, {crucifixes} crucifixes");
        var card = UnitPanel.WeaponWords(new ModelProfile(Rules.Core.Geometry.Length.Parse("6"), Rules.Core.Geometry.Length.Parse("0.5"), Rules.Core.Geometry.Length.Zero, hasMeleeWeapon: true)
        {
            KeepsGoingAfterRisky = true, ClimbDice = 1, Fanfare = true, BlendsIn = true,
        });
        Assert.Equal(" · Risky failure keeps going · climb +1 DICE · Dash +1 DICE within 4\" · Cover -2 DICE against it", card);
    }

    // Roster stage R5e (U109): the Shoot preview and the breakdown count the Urn's -1 DICE; the Lamb's ward shows in the
    // log, the notes and the dice.
    [Fact]
    public void UrnAndLambAreExplained()
    {
        var (urns, previews, wards) = (0, 0, 0);
        foreach (var (path, seed) in new[] { Content.DefaultPath(), Content.MeleePath() }.SelectMany(p => Enumerable.Range(1, 6).Select(s => (p, s))))
        {
            var warbands = Content.Load(path).Select(w => w with
            {
                Models = w.Models.Select((m, i) => m with { Profile = m.Profile with { SinisterWhispers = i == 0, WardedByBlood = i % 2 == 1 } }).ToList(),
            }).ToList();
            var state = MatchState.None;
            var log = new ResolutionLog();
            var seq = 0;
            foreach (var e in Runner.Run(seed, warbands).Log)
            {
                if (e is RangedAttackDeclared r && state.ActiveModel == r.Attacker && state.Pending is null
                    && Previews.Target(state, state.FindModel(r.Attacker)!.Seat, PreviewKind.Shoot, r.Attacker, r.Target, r.Weapon) is { Legal: true, Dice: { } dice })
                {
                    Assert.Equal(TurnSequence.Whispers(state, r.Target), dice.HitReasons.Contains("Urn of the Bitter Ashes within 3\", Warbands p. 143"));
                    previews++;
                }
                var setup = state.Setup;
                state = Reducer.Apply(state, e);
                log.Add(++seq, e, setup ?? state.Setup);
                if (e is RangedSuccessRolled && log.WhyOf(seq) is { } why)
                {
                    Assert.True(why.Explained, why.Text);
                    urns += why.Dice.Count(p => p.Label == "Urn of the Bitter Ashes within 3\"");
                }
                if (e is InjuryRolled { Warded: true } i)
                {
                    Assert.Contains("Sacrificial Lamb", EventText.Line(i), StringComparison.Ordinal);
                    Assert.Contains("1 Blood Marker kept off (Sacrificial Lamb)", DiceRolls.Of(i)!.Result, StringComparison.Ordinal);
                    Assert.Equal(i.PlacedMarkers > 0, UnitNotes.Of(i).Single().Text.Contains("BLOOD", StringComparison.Ordinal));
                    wards++;
                }
            }
        }
        Assert.True(urns > 0 && previews > 0 && wards > 0, $"{urns} urns, {previews} previews, {wards} wards");
        var card = UnitPanel.WeaponWords(new ModelProfile(Rules.Core.Geometry.Length.Parse("6"), Rules.Core.Geometry.Length.Parse("0.5"), Rules.Core.Geometry.Length.Zero, hasMeleeWeapon: true)
        {
            SinisterWhispers = true, WardedByBlood = true,
        });
        Assert.Equal(" · shots at it or within 3\" -1 DICE · first BLOOD MARKER ignored", card);
    }

    // Roster stage R5g (U112): the event lines, the unit notes and the unit card name the burns.
    [Fact]
    public void CrownAndPactAreExplained()
    {
        var burns = 0;
        foreach (var (path, seed) in new[] { Content.DefaultPath(), Content.MeleePath() }.SelectMany(p => Enumerable.Range(1, 6).Select(s => (p, s))))
        {
            var warbands = Content.Load(path).Select(w => w with
            {
                Models = w.Models.Select((m, i) => m with { Profile = m.Profile with { BurnsAtActivationEnd = i % 2 == 0, BurnsWhenTakenOut = i % 2 == 1 } }).ToList(),
            }).ToList();
            foreach (var placed in Runner.Run(seed, warbands).Log.OfType<BloodMarkersPlaced>())
            {
                Assert.StartsWith($"  Flames from {placed.Source} put 1 Blood Marker on each enemy within 1\": ", EventText.Line(placed), StringComparison.Ordinal);
                Assert.Equal(EventKind.Injury, EventText.Kind(placed));
                Assert.Equal(placed.Targets.ToArray(), UnitNotes.Of(placed).Select(n => n.Model).ToArray());
                Assert.Equal("BURNS", EventText.Compact(placed).Verb);
                burns++;
            }
        }
        Assert.True(burns > 0, $"{burns} burns");
        var card = UnitPanel.WeaponWords(new ModelProfile(Rules.Core.Geometry.Length.Parse("6"), Rules.Core.Geometry.Length.Parse("0.5"), Rules.Core.Geometry.Length.Zero, hasMeleeWeapon: true)
        {
            BurnsAtActivationEnd = true, BurnsWhenTakenOut = true,
        });
        Assert.Equal(" · +1 BLOOD MARKER to enemies within 1\" when it ends an activation · +1 BLOOD MARKER to enemies within 1\" when taken Out of Action", card);
    }

    // Roster stage R5h (U113): a Treat is previewed, logged, noted and shown in the dice popup; the card names it.
    [Fact]
    public void TreatIsExplained()
    {
        var (treats, previews) = (0, 0);
        foreach (var (path, seed) in new[] { Content.DefaultPath(), Content.MeleePath() }.SelectMany(p => Enumerable.Range(1, 6).Select(s => (p, s))))
        {
            var warbands = Content.Load(path).Select(w => w with
            {
                Models = w.Models.Select((m, i) => m with { Profile = m.Profile with { Treats = true, HealingWaters = i % 2 == 0 } }).ToList(),
            }).ToList();
            var state = MatchState.None;
            foreach (var e in Runner.Run(seed, warbands).Log)
            {
                if (e is TreatDeclared d && state.FindModel(d.Model) is { } medic)
                {
                    var preview = Previews.Target(state, medic.Seat, PreviewKind.Treat, d.Model, d.Target);
                    Assert.Null(preview.Refusal);
                    Assert.Equal(new Rules.Core.Commands.TreatModel(medic.Seat, d.Model, d.Target, d.Choice), preview.Command);
                    Assert.Contains(Previews.TreatFact, preview.Facts);
                    var interaction = new Interaction();
                    interaction.Update(state, medic.Seat);
                    Assert.Contains(InteractionMode.Treat, interaction.Available);
                    Assert.Equal(state.Setup!.ProfileOf(d.Model)!.HealingWaters, preview.Facts.Contains(Previews.HolyWaterFact));
                    previews++;
                }
                if (e is TreatRolled t)
                {
                    Assert.StartsWith($"  {t.Model} Treat roll {t.Check.Dice.Total}: ", EventText.Line(t), StringComparison.Ordinal);
                    Assert.Equal(EventKind.Status, EventText.Kind(t));
                    Assert.NotEmpty(UnitNotes.Of(t));
                    Assert.Contains($"rolled {string.Join(", ", t.Check.Dice.Rolled)};", EventText.Dice(t));
                    Assert.NotNull(DiceRolls.Step(t, state.Setup));
                    treats++;
                }
                state = Reducer.Apply(state, e);
            }
        }
        Assert.True(treats > 0 && previews == treats, $"{treats} treats, {previews} previews");
        var medicCard = new ModelProfile(Rules.Core.Geometry.Length.Parse("6"), Rules.Core.Geometry.Length.Parse("0.5"), Rules.Core.Geometry.Length.Zero, hasMeleeWeapon: true) { Treats = true, HealingWaters = true };
        Assert.Contains("Treat ACTION (H), +1 DICE", UnitPanel.WeaponWords(medicCard), StringComparison.Ordinal);
    }

    // Roster stage R5i (U114): Grind to Dust is key 0 of the Combat menu, previewed, logged and named on the card.
    [Fact]
    public void GrindIsExplained()
    {
        var (grinds, menus) = (0, 0);
        foreach (var (path, seed) in new[] { Content.DefaultPath(), Content.MeleePath() }.SelectMany(p => Enumerable.Range(1, 6).Select(s => (p, s))))
        {
            var warbands = Content.Load(path).Select(w => w with
            {
                Models = w.Models.Select(m => m with { Profile = m.Profile with { GrindsToDust = true } }).ToList(),
            }).ToList();
            var state = MatchState.None;
            foreach (var e in Runner.Run(seed, warbands).Log)
            {
                if (e is MeleeAttackDeclared { Weapon: ModelProfile.GrindWeapon } m && state.FindModel(m.Attacker) is { } grinder)
                {
                    var preview = Previews.Target(state, grinder.Seat, PreviewKind.Fight, m.Attacker, m.Target, ModelProfile.GrindWeapon);
                    Assert.Null(preview.Refusal);
                    Assert.Equal(new Rules.Core.Commands.FightModel(grinder.Seat, m.Attacker, m.Target) { Weapon = ModelProfile.GrindWeapon }, preview.Command);
                    Assert.Contains(Previews.GrindFact, preview.Facts);
                    Assert.Equal($"Grind {m.Target.Value} to dust with {m.Attacker.Value}", Interaction.Describe(preview));
                    var interaction = new Interaction();
                    interaction.Update(state, grinder.Seat);
                    if (interaction.Start(InteractionMode.Combat))
                    {
                        var entry = interaction.CombatEntries().Single(c => c.Weapon == ModelProfile.GrindWeapon);
                        Assert.Equal((InteractionMode.Fight, "Grind to Dust", "0", true), (entry.Mode, entry.Label, entry.Key, entry.Enabled));
                        menus++;
                    }
                    Assert.StartsWith($"  {m.Attacker} grinds to dust {m.Target}", EventText.Line(m), StringComparison.Ordinal);
                    Assert.Equal("GRINDS", EventText.Compact(m).Verb);
                    grinds++;
                }
                state = Reducer.Apply(state, e);
            }
        }
        Assert.True(grinds > 0 && menus == grinds, $"{grinds} grinds, {menus} menus");
        var card = new ModelProfile(Rules.Core.Geometry.Length.Parse("6"), Rules.Core.Geometry.Length.Parse("0.5"), Rules.Core.Geometry.Length.Zero, hasMeleeWeapon: true) { GrindsToDust = true };
        Assert.Contains("Grind to Dust ACTION (Combat 0)", UnitPanel.WeaponWords(card), StringComparison.Ordinal);
        Assert.DoesNotContain("Grind", UnitPanel.WeaponWords(card with { GrindsToDust = false }), StringComparison.Ordinal);
    }

    // Roster stage R5j (U115): a Martyrdom Device is previewed, logged, broken down and named on the card.
    // Roster stage R6a (U117): New Antioch abilities on the unit card, Shock Charge's two dice in the log.
    [Fact]
    public void NewAntiochAbilitiesAreExplained()
    {
        var profile = new ModelProfile(Rules.Core.Geometry.Length.Parse("6"), Rules.Core.Geometry.Length.Parse("0.5"), Rules.Core.Geometry.Length.Zero, hasMeleeWeapon: true);
        var card = UnitPanel.WeaponWords(profile with { ShotsIgnoreMarkers = true, ExpertMedic = true, FinishesTheFallen = true, ExtraChargeDie = true, InspiresFaithful = true });
        foreach (var words in new[] { "no BLOOD MARKERS spent on its shots", "Treat +2 DICE", "+1 INJURY DICE in melee at a Down model", "Charge Bonus: highest of 2D6", "PRINCIPALITY models within 8\" have NEGATE FEAR" })
        {
            Assert.Contains(words, card, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("2D6", UnitPanel.WeaponWords(profile), StringComparison.Ordinal);
        var (a, b) = (new ModelId("a1"), new ModelId("b1"));
        var shock = new ChargeRolled(a, b, 5) { Dropped = 2 };
        Assert.Equal("  a1 Charge Bonus 5 (Shock Charge, 2 dropped).", EventText.Line(shock));
        Assert.Equal(EventText.Describe(shock), Narrator.Describe(shock));
        Assert.Equal("  a1 Charge Bonus 5.", EventText.Line(new ChargeRolled(a, b, 5)));
    }

    // Roster stage R5k (U116): after a failed Risky roll the player of a model with a Talisman gets two answers on a bar
    // over the model, and the log says when it is used.
    [Fact]
    public void TalismanIsExplained()
    {
        var (asked, used) = (0, 0);
        foreach (var (path, seed) in new[] { Content.DefaultPath(), Content.MeleePath() }.SelectMany(p => Enumerable.Range(1, 6).Select(s => (p, s))))
        {
            var warbands = Content.Load(path).Select(w => w with
            {
                Models = w.Models.Select(m => m with { Profile = m.Profile with { Talismans = 1 } }).ToList(),
            }).ToList();
            var state = MatchState.None;
            foreach (var e in Runner.Run(seed, warbands).Log)
            {
                state = Reducer.Apply(state, e);
                if (e is DecisionRequested { Kind: DecisionKind.UseTalisman } d)
                {
                    var interaction = new Interaction();
                    interaction.Update(state, d.Seat);
                    var bar = interaction.Bar(null);
                    Assert.Equal(state.ActiveModel, bar.Anchor);
                    Assert.Equal(["Use Talisman", "Keep Talisman"], bar.Items.Where(i => i.Kind == BarKind.Decide).Select(i => i.Label));
                    Assert.Equal(TurnSequence.LegalCommands(state), bar.Items.Where(i => i.Kind == BarKind.Decide).Select(i => i.Command!));
                    Assert.Contains("use a Talisman", EventText.Line(d), StringComparison.Ordinal);
                    asked++;
                }
                if (e is TalismanUsed t)
                {
                    Assert.Equal($"  {t.Model} uses a Talisman: the failed Risky roll does not end its activation.", EventText.Line(t));
                    Assert.Equal(("USES A TALISMAN", EventKind.Activation), (EventText.Compact(t).Verb, EventText.Kind(t)));
                    Assert.Equal(EventText.Describe(t), Narrator.Describe(t));
                    used++;
                }
            }
        }
        Assert.True(asked > 0 && used > 0, $"{asked} asked, {used} used");
        var profile = new ModelProfile(Rules.Core.Geometry.Length.Parse("6"), Rules.Core.Geometry.Length.Parse("0.5"), Rules.Core.Geometry.Length.Zero, hasMeleeWeapon: true);
        Assert.Contains("Talisman: once per game", UnitPanel.WeaponWords(profile with { Talismans = 1 }), StringComparison.Ordinal);
        Assert.Contains("2 Talismans", UnitPanel.WeaponWords(profile with { Talismans = 2 }), StringComparison.Ordinal);
        Assert.DoesNotContain("Talisman", UnitPanel.WeaponWords(profile), StringComparison.Ordinal);
    }

    [Fact]
    public void MartyrdomIsExplained()
    {
        var (devices, selves, explained) = (0, 0, 0);
        foreach (var (path, seed) in new[] { Content.DefaultPath(), Content.MeleePath() }.SelectMany(p => Enumerable.Range(1, 6).Select(s => (p, s))))
        {
            var warbands = Content.Load(path).Select(w => w with
            {
                Models = w.Models.Select((m, i) => m with { Profile = m.Profile with { Martyrdom = i % 2 == 0 } }).ToList(),
            }).ToList();
            var state = MatchState.None;
            var log = new ResolutionLog();
            var seq = 0;
            var blasting = false;
            foreach (var e in Runner.Run(seed, warbands).Log)
            {
                if (e is MartyrdomTriggered t && state.FindModel(t.Model) is { } bearer)
                {
                    var preview = Previews.Target(state, bearer.Seat, PreviewKind.Detonate, t.Model, t.Model);
                    Assert.Null(preview.Refusal);
                    Assert.Equal(new Rules.Core.Commands.TriggerMartyrdom(bearer.Seat, t.Model), preview.Command);
                    Assert.Contains(Previews.MartyrdomFact, preview.Facts);
                    Assert.Contains($"Hits {string.Join(", ", t.Hit.Select(h => h.Value))}.", preview.Facts);
                    var interaction = new Interaction();
                    interaction.Update(state, bearer.Seat);
                    Assert.Contains(InteractionMode.Detonate, interaction.Available);
                    Assert.StartsWith($"  {t.Model} detonates its Martyrdom Device: Injury Rolls for ", EventText.Line(t), StringComparison.Ordinal);
                    Assert.Equal(("DETONATES", EventKind.Injury), (EventText.Compact(t).Verb, EventText.Kind(t)));
                    Assert.Equal(EventText.Describe(t), Narrator.Describe(t));
                    devices++;
                    blasting = true;
                }
                if (e is ActivationEnded or ActivationStarted)
                {
                    blasting = false;
                }
                var setup = state.Setup;
                state = Reducer.Apply(state, e);
                log.Add(++seq, e, setup ?? state.Setup);
                if (e is InjuryRolled i && blasting)
                {
                    var why = log.WhyOf(seq);
                    Assert.True(why is { Explained: true }, why?.Text);
                    explained++;
                    if (i.Injury.AllFour)
                    {
                        Assert.Contains("4D6 added together", EventText.Line(i), StringComparison.Ordinal);
                        Assert.Contains("CAUGHT BY ITS OWN DEVICE", DiceRolls.Of(i)!.Title, StringComparison.Ordinal);
                        selves++;
                        blasting = false;
                    }
                }
            }
        }
        Assert.True(devices > 0 && selves > 0 && explained > selves, $"{devices} devices, {selves} bearers, {explained} rolls");
        var card = new ModelProfile(Rules.Core.Geometry.Length.Parse("6"), Rules.Core.Geometry.Length.Parse("0.5"), Rules.Core.Geometry.Length.Zero, hasMeleeWeapon: true) { Martyrdom = true };
        Assert.Contains("Martyrdom Device (B), once per game", UnitPanel.WeaponWords(card), StringComparison.Ordinal);
    }

    // Roster stage R6c (U119): the shot and fight previews' dice match the server's with these abilities, and every
    // Success and Injury Roll's breakdown adds up and names them.
    [Fact]
    public void NumberAbilitiesAreExplained()
    {
        var (shots, fights, injuries, spared) = (0, 0, 0, 0);
        foreach (var (path, seed) in new[] { Content.DefaultPath(), Content.MeleePath() }.SelectMany(p => Enumerable.Range(1, 6).Select(s => (p, s))))
        {
            var warbands = Content.Load(path).Select(w => w with
            {
                Models = w.Models.Select((m, i) => m with
                {
                    Profile = m.Profile with
                    {
                        Agile = i % 2 == 0, SwiftDash = i % 3 == 0, QuickToRise = i % 2 == 1, UndeadFortitude = i % 2 == 0, AbioticLife = i % 3 == 1,
                        Stealthy = i % 2 == 1, HuntsTheTough = true, CounterCharge = true, MementoMori = i % 3 == 0,
                        Traits = m.Profile.Traits | (i % 3 == 2 ? ModelTraits.Tough : 0),
                    },
                }).ToList(),
            }).ToList();
            var state = MatchState.None;
            var log = new ResolutionLog();
            var seq = 0;
            DicePreview? pending = null;
            foreach (var e in Runner.Run(seed, warbands).Log)
            {
                if (e is RangedAttackDeclared r && state.Pending is null
                    && Previews.Target(state, state.FindModel(r.Attacker)!.Seat, PreviewKind.Shoot, r.Attacker, r.Target, r.Weapon) is { Legal: true, Dice: { } shot })
                {
                    pending = shot;
                    Assert.Equal(state.Setup!.ProfileOf(r.Target)!.Stealthy, shot.HitReasons.Contains("Stealth Generator, Warbands p. 110"));
                    shots++;
                }
                if (e is MeleeAttackDeclared m && state.ActiveModel == m.Attacker && !m.FreeFight && state.Pending is null
                    && Previews.Target(state, state.FindModel(m.Attacker)!.Seat, PreviewKind.Fight, m.Attacker, m.Target, m.Weapon) is { Legal: true, Dice: { } fight })
                {
                    pending = fight;
                    Assert.Equal(TurnSequence.CounterDice(state, m.Attacker), fight.CounterDice);
                    fights++;
                }
                // The preview's dice are before any Blood Markers are spent, and for the target it was made for.
                if (e is MarkersSpent { Count: > 0 } or FriendlyTargetChosen)
                {
                    pending = null;
                }
                if (e is RangedSuccessRolled or MeleeSuccessRolled && pending is { } hit)
                {
                    var net = e is RangedSuccessRolled rs ? rs.Check.NetDice : ((MeleeSuccessRolled)e).Check.NetDice;
                    Assert.True(hit.HitNetDice == net, $"{e} preview {hit.HitNetDice} server {net}: {string.Join("; ", hit.HitReasons)} own {hit.OwnHitDice} bonus {hit.HitBonus} aim {hit.AimDice} counter {hit.CounterDice} penalty {hit.HitPenalty} progress {state.InProgress}");
                    pending = null;
                }
                if (e is InjuryRolled ir && ir.Spared)
                {
                    Assert.Contains("Memento Mori", EventText.Line(ir), StringComparison.Ordinal);
                    Assert.Equal(EventText.Describe(ir), Narrator.Describe(ir));
                    spared++;
                }
                if (e is ModelStoodUp up)
                {
                    Assert.Equal(EventText.Describe(up), Narrator.Describe(up));
                    Assert.Equal(up.Quick, EventText.Line(up).Contains("Frightening Speed", StringComparison.Ordinal));
                }
                var setup = state.Setup;
                state = Reducer.Apply(state, e);
                log.Add(++seq, e, setup ?? state.Setup);
                if (e is DashRolled or SideClimbRolled or RangedSuccessRolled or MeleeSuccessRolled or InjuryRolled && log.WhyOf(seq) is { } why)
                {
                    Assert.True(why.Explained, why.Text);
                    injuries += e is InjuryRolled ? 1 : 0;
                }
                if (e is ActivationEnded or InjuryRolled)
                {
                    pending = null;
                }
            }
        }
        Assert.True(shots > 0 && fights > 0 && injuries > 0 && spared > 0, $"{shots} shots, {fights} fights, {injuries} injuries, {spared} spared");
        var card = new ModelProfile(Rules.Core.Geometry.Length.Parse("6"), Rules.Core.Geometry.Length.Parse("0.5"), Rules.Core.Geometry.Length.Zero, hasMeleeWeapon: true) { Agile = true, Stealthy = true, MementoMori = true };
        Assert.Contains("Agile: +1 DICE to Dash and climb rolls", UnitPanel.WeaponWords(card), StringComparison.Ordinal);
        Assert.Contains("first Out of Action counts as No Effect", UnitPanel.WeaponWords(card), StringComparison.Ordinal);
    }

    // Roster stage R6d (U120): Trample is in the Combat menu (key 0) and previewed; a fight's preview dice match the
    // server's with Overwhelming Horde; every Melee Attack's breakdown adds up and names the Horde's dice.
    [Fact]
    public void TrampleAndHordeAreExplained()
    {
        var (tramples, hordes) = (0, 0);
        foreach (var (path, seed) in new[] { Content.DefaultPath(), Content.MeleePath() }.SelectMany(p => Enumerable.Range(1, 6).Select(s => (p, s))))
        {
            var warbands = Content.Load(path).Select(w => w with
            {
                Models = w.Models.Select((m, i) => m with { Profile = m.Profile with { Tramples = i % 2 == 0, Horde = i % 2 == 1 } }).ToList(),
            }).ToList();
            var state = MatchState.None;
            var log = new ResolutionLog();
            var seq = 0;
            DicePreview? pending = null;
            foreach (var e in Runner.Run(seed, warbands).Log)
            {
                if (e is MeleeAttackDeclared m && state.ActiveModel == m.Attacker && !m.FreeFight && state.Pending is null
                    && state.FindModel(m.Attacker) is { } attacker)
                {
                    var preview = Previews.Target(state, attacker.Seat, PreviewKind.Fight, m.Attacker, m.Target, m.Weapon);
                    Assert.True(preview.Legal, preview.Refusal?.Text);
                    pending = preview.Dice;
                    if (m.Weapon == ModelProfile.TrampleWeapon)
                    {
                        Assert.Contains(Previews.TrampleFact, preview.Facts);
                        var interaction = new Interaction();
                        interaction.Update(state, attacker.Seat);
                        Assert.True(interaction.Start(InteractionMode.Combat));
                        Assert.Contains(interaction.CombatEntries(), c => c.Weapon == ModelProfile.TrampleWeapon && c.Key == "0" && c.Enabled);
                        Assert.Equal(ModelProfile.TrampleWeapon, interaction.ZeroKeyWeapon);
                        Assert.Equal(("TRAMPLES", EventText.Describe(m)), (EventText.Compact(m).Verb, Narrator.Describe(m)));
                        tramples++;
                    }
                    hordes += preview.Dice?.HordeDice > 0 ? 1 : 0;
                }
                if (e is MarkersSpent { Count: > 0 })
                {
                    pending = null;
                }
                if (e is MeleeSuccessRolled hit && pending is { } dice)
                {
                    Assert.Equal(dice.HitNetDice, hit.Check.NetDice);
                    pending = null;
                }
                var setup = state.Setup;
                state = Reducer.Apply(state, e);
                log.Add(++seq, e, setup ?? state.Setup);
                if (e is MeleeSuccessRolled or InjuryRolled && log.WhyOf(seq) is { } why)
                {
                    Assert.True(why.Explained, why.Text);
                }
            }
        }
        Assert.True(tramples > 0 && hordes > 0, $"{tramples} tramples, {hordes} horde previews");
        var card = new ModelProfile(Rules.Core.Geometry.Length.Parse("6"), Rules.Core.Geometry.Length.Parse("0.5"), Rules.Core.Geometry.Length.Zero, hasMeleeWeapon: true) { Tramples = true, Horde = true };
        Assert.Contains("Trample ACTION (Combat 0)", UnitPanel.WeaponWords(card), StringComparison.Ordinal);
        Assert.Contains("Overwhelming Horde", UnitPanel.WeaponWords(card), StringComparison.Ordinal);
    }

    // Roster stage R6e (U121): Enforced Orthodoxy and Laying on of Hands are on the action bar, previewed, logged, in the
    // dice popup and broken down; the notes name who stood up and the markers removed.
    [Fact]
    public void HelpingActionsAreExplained()
    {
        var (declared, stood, removed) = (0, 0, 0);
        foreach (var (path, seed) in new[] { Content.DefaultPath(), Content.MeleePath() }.SelectMany(p => Enumerable.Range(1, 6).Select(s => (p, s))))
        {
            var warbands = Content.Load(path).Select(w => w with
            {
                Models = w.Models.Select((m, i) => m with { Profile = m.Profile with { EnforcesOrthodoxy = i % 2 == 0, LaysOnHands = i % 2 == 1, HealingWaters = i % 4 == 1 } }).ToList(),
            }).ToList();
            var state = MatchState.None;
            var log = new ResolutionLog();
            var seq = 0;
            foreach (var e in Runner.Run(seed, warbands).Log)
            {
                if (e is SelfActionDeclared d && state.FindModel(d.Model) is { } model)
                {
                    var kind = Previews.KindOf(d.Action);
                    var preview = Previews.Target(state, model.Seat, kind, d.Model, d.Target ?? d.Model);
                    Assert.True(preview.Legal, preview.Refusal?.Text);
                    Assert.Equal(new Rules.Core.Commands.TakeSelfAction(model.Seat, d.Model, d.Action) { Target = d.Target }, preview.Command);
                    var interaction = new Interaction();
                    interaction.Update(state, model.Seat);
                    Assert.Contains(d.Action == SelfActionKind.EnforcedOrthodoxy ? InteractionMode.Orthodoxy : InteractionMode.LayHands, interaction.BarOffered);
                    Assert.Equal($"  {d.Model} declares {Words.SelfActionWithArticle(d.Action)} ACTION{(d.Target is { } t ? $" on {t}" : "")}.", EventText.Line(d));
                    declared++;
                }
                if (e is SelfActionRolled r)
                {
                    Assert.Equal(EventText.Describe(r), Narrator.Describe(r));
                    var (pass, fail) = Words.SelfActionVerdicts(r.Action);
                    Assert.Contains(r.Succeeded ? pass : fail, DiceRolls.Of(r)!.Result, StringComparison.Ordinal);
                    var notes = UnitNotes.Of(r);
                    Assert.Equal(r.StoodUp.Length, notes.Count(n => n.Text == "STANDS UP"));
                    stood += r.StoodUp.Length;
                    removed += r.Removed;
                }
                var setup = state.Setup;
                state = Reducer.Apply(state, e);
                log.Add(++seq, e, setup ?? state.Setup);
                if (e is SelfActionRolled && log.WhyOf(seq) is { } why)
                {
                    Assert.True(why.Explained, why.Text);
                }
            }
        }
        Assert.True(declared > 0 && stood > 0 && removed > 0, $"{declared} declared, {stood} stood up, {removed} removed");
        var card = new ModelProfile(Rules.Core.Geometry.Length.Parse("6"), Rules.Core.Geometry.Length.Parse("0.5"), Rules.Core.Geometry.Length.Zero, hasMeleeWeapon: true) { EnforcesOrthodoxy = true, LaysOnHands = true };
        Assert.Contains("Enforced Orthodoxy ACTION (Y)", UnitPanel.WeaponWords(card), StringComparison.Ordinal);
        Assert.Contains("Laying on of Hands ACTION (Z)", UnitPanel.WeaponWords(card), StringComparison.Ordinal);
        Assert.Equal("Enforced Orthodoxy, Laying on of Hands", Words.Actions(ActionsTaken.Orthodoxy | ActionsTaken.LayingOnHands));
    }

    // Roster stage R10 (U135): God is With Us! is on the action bar of a model that has it, previewed on itself and a friend, and
    // its result is worded.
    [Fact]
    public void GodIsWithUsIsOnTheBarAndPreviewed()
    {
        var offered = 0;
        var warbands = Content.Load(Content.DefaultPath()).Select(w => w with
        {
            Models = w.Models.Select(m => m with { Profile = m.Profile with { Blesses = true } }).ToList(),
        }).ToList();
        var state = MatchState.None;
        foreach (var e in Runner.Run(1, warbands).Log)
        {
            state = Reducer.Apply(state, e);
            if (e is ActivationStarted started && state.FindModel(started.Model) is { } model)
            {
                var preview = Previews.Target(state, model.Seat, PreviewKind.Bless, model.Id, model.Id);
                Assert.True(preview.Legal, preview.Refusal?.Text);
                Assert.Equal(new Rules.Core.Commands.TakeSelfAction(model.Seat, model.Id, SelfActionKind.GodIsWithUs) { Target = model.Id }, preview.Command);
                Assert.Contains(Previews.BlessFact, preview.Facts);
                var interaction = new Interaction();
                interaction.Update(state, model.Seat);
                Assert.Contains(InteractionMode.Bless, interaction.BarOffered);
                offered++;
                break;
            }
        }
        Assert.Equal(1, offered);
        var rolled = new SelfActionRolled(new ModelId("a1"), SelfActionKind.GodIsWithUs, Rules.Core.Dice.SuccessRoll.Roll(new SeededDice(1), 0, 0, risky: true)) { Target = new ModelId("a2"), Placed = 1 };
        Assert.Equal(EventText.Describe(rolled), Narrator.Describe(rolled));
        Assert.Contains("1 BLESSING MARKER on a2", Words.SelfActionResult(rolled), StringComparison.Ordinal);
    }

    // Roster stage R6b (U118): Aim and Fortify are on the action bar, previewed, logged and broken down; an aimed shot's
    // breakdown names the Aim's +2 DICE.
    [Fact]
    public void SelfActionsAreExplained()
    {
        var (declared, aimedShots) = (0, 0);
        foreach (var (path, seed) in new[] { Content.DefaultPath(), Content.MeleePath() }.SelectMany(p => Enumerable.Range(1, 6).Select(s => (p, s))))
        {
            var warbands = Content.Load(path).Select(w => w with
            {
                Models = w.Models.Select((m, i) => m with { Profile = m.Profile with { Aims = i % 2 == 0, Fortifies = i % 2 == 1 } }).ToList(),
            }).ToList();
            var state = MatchState.None;
            var log = new ResolutionLog();
            var seq = 0;
            foreach (var e in Runner.Run(seed, warbands).Log)
            {
                if (e is SelfActionDeclared d && state.FindModel(d.Model) is { } model)
                {
                    var (kind, mode) = d.Action == SelfActionKind.Aim ? (PreviewKind.Aim, InteractionMode.Aim) : (PreviewKind.Fortify, InteractionMode.Fortify);
                    var preview = Previews.Target(state, model.Seat, kind, d.Model, d.Model);
                    Assert.Null(preview.Refusal);
                    Assert.Equal(new Rules.Core.Commands.TakeSelfAction(model.Seat, d.Model, d.Action), preview.Command);
                    Assert.Contains(d.Action == SelfActionKind.Aim ? Previews.AimFact : Previews.FortifyFact, preview.Facts);
                    var interaction = new Interaction();
                    interaction.Update(state, model.Seat);
                    Assert.Contains(mode, interaction.BarOffered);
                    Assert.Contains(interaction.Bar(null).Items, b => b.Kind == BarKind.Start && b.Mode == mode);
                    Assert.DoesNotContain(d.Action == SelfActionKind.Aim ? InteractionMode.Fortify : InteractionMode.Aim, interaction.Available);
                    Assert.Equal(EventKind.Status, EventText.Kind(d));
                    declared++;
                }
                if (e is SelfActionRolled r)
                {
                    Assert.Equal(EventText.Describe(r), Narrator.Describe(r));
                    Assert.Contains(r.Succeeded ? (r.Action == SelfActionKind.Aim ? "AIMED" : "FORTIFIED") : (r.Action == SelfActionKind.Aim ? "AIM FAILED" : "FORTIFY FAILED"), DiceRolls.Of(r)!.Result, StringComparison.Ordinal);
                }
                var aiming = e is RangedSuccessRolled shot && state.Aiming && state.ActiveModel == shot.Attacker;
                var setup = state.Setup;
                state = Reducer.Apply(state, e);
                log.Add(++seq, e, setup ?? state.Setup);
                if (e is SelfActionRolled or RangedSuccessRolled && log.WhyOf(seq) is { } why)
                {
                    Assert.True(why.Explained, why.Text);
                    if (aiming)
                    {
                        Assert.Contains("Aim ACTION", why.Text, StringComparison.Ordinal);
                        aimedShots++;
                    }
                }
            }
        }
        Assert.True(declared > 0 && aimedShots > 0, $"{declared} declared, {aimedShots} aimed shots");
        var card = new ModelProfile(Rules.Core.Geometry.Length.Parse("6"), Rules.Core.Geometry.Length.Parse("0.5"), Rules.Core.Geometry.Length.Zero, hasMeleeWeapon: true) { Aims = true, Fortifies = true };
        Assert.Contains("Aim ACTION (I)", UnitPanel.WeaponWords(card), StringComparison.Ordinal);
        Assert.Contains("Fortify ACTION (V)", UnitPanel.WeaponWords(card), StringComparison.Ordinal);
    }

    // Roster stage R5f (U110): the previews and the breakdown name the COVER keyword's die.
    [Fact]
    public void CoverKeywordIsExplained()
    {
        var (shots, fights, previews) = (0, 0, 0);
        foreach (var (path, seed) in new[] { Content.DefaultPath(), Content.MeleePath() }.SelectMany(p => Enumerable.Range(1, 6).Select(s => (p, s))))
        {
            var warbands = Content.Load(path).Select(w => w with
            {
                Models = w.Models.Select((m, i) => m with { Profile = m.Profile with { HasCover = i == 0, DigsIn = i % 2 == 1 } }).ToList(),
            }).ToList();
            var state = MatchState.None;
            var log = new ResolutionLog();
            var seq = 0;
            foreach (var e in Runner.Run(seed, warbands).Log)
            {
                if (e is RangedAttackDeclared r && state.ActiveModel == r.Attacker && state.Pending is null
                    && Previews.Target(state, state.FindModel(r.Attacker)!.Seat, PreviewKind.Shoot, r.Attacker, r.Target, r.Weapon) is { Legal: true, Dice: { } dice })
                {
                    Assert.Equal(r.CoverTerrainId == TurnSequence.CoverKeyword, dice.HitReasons.Contains("Cover from its COVER keyword, p. 54"));
                    previews++;
                }
                if (e is MeleeAttackDeclared m && state.ActiveModel == m.Attacker && !m.FreeFight && state.Pending is null
                    && Previews.Target(state, state.FindModel(m.Attacker)!.Seat, PreviewKind.Fight, m.Attacker, m.Target, m.Weapon) is { Legal: true, Dice: { } fight })
                {
                    Assert.Equal(m.DefendedObstacleId == TurnSequence.CoverKeyword, fight.HitReasons.Contains("Defended Obstacle from its COVER keyword, p. 54"));
                    previews++;
                }
                var setup = state.Setup;
                state = Reducer.Apply(state, e);
                log.Add(++seq, e, setup ?? state.Setup);
                if (e is RangedSuccessRolled or MeleeSuccessRolled && log.WhyOf(seq) is { } why)
                {
                    Assert.True(why.Explained, why.Text);
                    shots += why.Dice.Count(p => p.Label == "Cover (its COVER keyword)");
                    fights += why.Dice.Count(p => p.Label == "Defended obstacle (its COVER keyword)");
                }
            }
        }
        Assert.True(shots > 0 && fights > 0 && previews > 0, $"{shots} shots, {fights} fights, {previews} previews");
        var card = UnitPanel.WeaponWords(new ModelProfile(Rules.Core.Geometry.Length.Parse("6"), Rules.Core.Geometry.Length.Parse("0.5"), Rules.Core.Geometry.Length.Zero, hasMeleeWeapon: true)
        {
            HasCover = true, DigsIn = true,
        });
        Assert.Equal(" · COVER · COVER while dug in", card);
    }

    [Fact]
    public void TheUnitCardNamesTheTraits()
    {
        var profile = new ModelProfile(Rules.Core.Geometry.Length.Parse("6"), Rules.Core.Geometry.Length.Parse("0.5"), Rules.Core.Geometry.Length.Zero, hasMeleeWeapon: true)
        {
            Traits = ModelTraits.Fear | ModelTraits.Tough | ModelTraits.Leader,
        };
        Assert.Equal(" · FEAR · TOUGH · LEADER", UnitPanel.WeaponWords(profile));
        Assert.Equal("", UnitPanel.WeaponWords(profile with { Traits = ModelTraits.None }));
    }
}
