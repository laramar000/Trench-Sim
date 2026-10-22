using Client.Core;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.State;

namespace Client.Tests;

// Battlefield UI 2, stage N3: the HUD's words split between the battlefield's top-left (where the match stands) and its
// top-centre (what happens now), losing and adding nothing; and the choices that belong to no model, shown on a card.
public class BattlefieldLayoutTests
{
    private static List<MatchState> States => PreviewAgreementTests.States.Value;
    private static readonly ContentNames Names = ContentNames.None;

    [Fact]
    public void TheTwoHudPlacesTogetherSayExactlyTheWholeLine()
    {
        var checkedLines = 0;
        foreach (var state in States.Where(s => s.Setup?.Scenario is not null).Take(3000))
        {
            foreach (var seat in new Seat?[] { Seat.One, Seat.Two, null })
            {
                foreach (var waiting in new[] { false, true })
                {
                    var view = MatchView.Of(state, seat, Names);
                    var scene = BoardScene.Of(state, seat, Names);
                    var whole = TopHud.Of(view, scene, [], seat, waiting, "a prompt");
                    var (left, centre) = TopHud.Split(view, scene, [], seat, waiting, "a prompt");
                    var pieces = (left.Length == 0 ? [] : left.Split(" · ")).Concat(centre.Length == 0 ? [] : centre.Split(" · ")).Order(StringComparer.Ordinal);
                    Assert.Equal(whole.Split(" · ").Order(StringComparer.Ordinal), pieces);
                    // The seat, turn and phase on the left; whose move, the prompt and a waiting command at the centre.
                    Assert.StartsWith(seat is { } s ? $"[{Words.SeatLetter(s)}] " : "WATCHING", left, StringComparison.Ordinal);
                    Assert.Contains(view.Phase, left.Split(" · "));
                    Assert.Contains("a prompt", centre.Split(" · "));
                    Assert.Equal(waiting, centre.EndsWith(TopHud.Waiting, StringComparison.Ordinal));
                    checkedLines++;
                }
            }
        }
        Assert.True(checkedLines > 1000, $"{checkedLines} lines");
    }

    [Fact]
    public void OnlyChoicesWithNoModelOnTheBattlefieldGoOnTheDecisionCard()
    {
        var decisions = new HashSet<Type>();
        var others = new HashSet<Type>();
        foreach (var state in States)
        {
            foreach (var seat in new[] { Seat.One, Seat.Two })
            {
                var hints = ActionHints.For(state, seat, Names);
                foreach (var hint in hints)
                {
                    (ActionHints.IsDecision(hint.Command) ? decisions : others).Add(hint.Command.GetType());
                }
                var cards = hints.Where(h => ActionHints.IsDecision(h.Command)).Select(h => h.Command).ToList();
                if (cards.Count > 0)
                {
                    Assert.StartsWith("YOUR CHOICE", ActionHints.DecisionTitle(cards), StringComparison.Ordinal);
                }
            }
        }
        // Activating a unit, ending an activation and the decisions about a model (Blood Markers, free attacks, a shot
        // into melee) are on the battlefield (a unit's click and its bar), never on the card.
        Assert.Contains(typeof(ChooseDeploymentZone), decisions);
        Assert.Contains(typeof(ChooseFirstActivation), decisions);
        Assert.DoesNotContain(typeof(ActivateModel), decisions);
        Assert.DoesNotContain(typeof(EndActivation), decisions);
        foreach (var model in new Command[] { new SpendMarkers(Seat.One, 0), new FreeFight(Seat.One, new ModelId("a1")), new DeclineFreeFights(Seat.One), new ActivateModel(Seat.One, new ModelId("a1")), new EndActivation(Seat.One) })
        {
            Assert.False(ActionHints.IsDecision(model), model.ToString());
        }
        Assert.Equal("YOUR CHOICE: which deployment zone you take", ActionHints.DecisionTitle([new EndActivation(Seat.One), new ChooseDeploymentZone(Seat.One, "north")]));
    }
}
