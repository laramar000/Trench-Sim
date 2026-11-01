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
