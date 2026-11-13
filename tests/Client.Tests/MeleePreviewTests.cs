using Client.Core;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Client.Tests;

// Proposal J2b: the melee previews take their answers from the rules: a Fight target can be chosen exactly when
// TurnSequence.Check (the check Decide makes) accepts it, the panel's "Engaged with" list is Engagement.Pairs, and a
// Retreat preview names exactly the free attackers Engagement.FreeAttackersIfRetreating gives.
public class MeleePreviewTests
{
    [Fact]
    public void AFightTargetCanBeChosenExactlyWhenTheRulesAcceptIt()
    {
        var (legal, refused) = (0, 0);
        foreach (var state in PreviewAgreementTests.States.Value.Where(s => s.ActiveModel is not null && s.Pending is null && s.Phase == Phase.Activation))
        {
            var active = state.FindModel(state.ActiveModel!.Value)!;
            foreach (var enemy in state.Models.Where(m => m.Seat != active.Seat))
            {
                var preview = Previews.Target(state, active.Seat, PreviewKind.Fight, active.Id, enemy.Id);
                Assert.Equal(TurnSequence.Check(state, new FightModel(active.Seat, active.Id, enemy.Id)), preview.Refusal);
                Assert.Equal(preview.Refusal is null, preview.Facts.Contains(Previews.EngagedFact));
                legal += preview.Refusal is null ? 1 : 0;
                refused += preview.Refusal is null ? 0 : 1;
            }
        }
        Assert.True(legal > 20 && refused > 200, $"{legal} legal, {refused} refused");
    }

    [Fact]
    public void ThePanelListsTheModelsTheRulesSayAreEngaged()
    {
        var engaged = 0;
        foreach (var state in PreviewAgreementTests.States.Value.Where(s => s.Setup?.Scenario is not null))
        {
            var pairs = Engagement.Pairs(state);
            foreach (var model in state.Models)
            {
                var line = UnitPanel.EngagedLine(state, model.Id);
                var wanted = pairs.Where(p => p.One == model.Id || p.Two == model.Id).Select(p => p.One == model.Id ? p.Two : p.One).ToList();
                Assert.Equal(wanted.Count == 0, line is null);
                if (line is not null)
                {
                    engaged++;
                    Assert.StartsWith(UnitPanel.EngagedPrefix, line, StringComparison.Ordinal);
                    Assert.All(wanted, e => Assert.Contains($"{UnitPanel.ShortId(e)} (", line, StringComparison.Ordinal));
                    Assert.Equal(wanted.Count, line.Split(", ").Length);
                }
            }
        }
        Assert.True(engaged > 20, $"{engaged} engaged models");
    }

    [Fact]
    public void ARetreatPreviewNamesTheRulesFreeAttackers()
    {
        var (named, previews) = (0, 0);
        foreach (var state in PreviewAgreementTests.States.Value.Where(s => s.ActiveModel is not null && s.Pending is null && s.Phase == Phase.Activation))
        {
            var active = state.FindModel(state.ActiveModel!.Value)!;
            if (Engagement.Pairs(state).All(p => p.One != active.Id && p.Two != active.Id)
                || (state.Actions & (ActionsTaken.Move | ActionsTaken.Charge | ActionsTaken.Retreat)) != 0 || active.Status != ModelStatus.Standing)
            {
                continue;
            }
            var at = active.Position!.Value;
            var preview = Previews.Path(state, active.Seat, PreviewKind.Retreat, active.Id, [new Point2(at.X, at.Y)]);
            var attackers = Engagement.FreeAttackersIfRetreating(state, active.Id);
            Assert.Contains(Previews.FreeAttackFact(attackers), preview.Facts);
            Assert.Contains(PreviewCard.Lines(preview), l => l == Previews.FreeAttackFact(attackers).TrimEnd('.'));
            previews++;
            named += attackers.Count;
        }
        Assert.True(previews > 5 && named > 0, $"{previews} retreat previews, {named} free attackers named");
    }
}
