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
                    Assert.Equal(dice.TargetInjuryDice != 0, dice.Lines(m.Attacker, m.Target).Any(l => l.Contains("-1 DICE (target's battlekit)", StringComparison.Ordinal)));
                    previews++;
                }
                var setup = state.Setup;
                state = Reducer.Apply(state, e);
                log.Add(++seq, e, setup ?? state.Setup);
                if (e is InjuryRolled i && log.WhyOf(seq) is { } why)
                {
                    Assert.True(why.Explained, why.Text);
                    Assert.Equal(state.Setup!.ProfileOf(i.Target)!.InjuryDiceTaken != 0, why.Dice.Any(d => d.Label == "target's battlekit"));
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
