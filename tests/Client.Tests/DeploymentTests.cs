using Client.Core;
using Rules.Core.Commands;
using Rules.Core.State;

namespace Client.Tests;

// Battlefield UI, stage K4: on your deployment turn the battlefield is already placing. A click on a legal spot places
// the model shown; chips on the bar (or N and P) choose which model; a spot the rules refuse sends nothing and says why.
// Every state comes from simulated matches; nothing here talks to a server, and the server still decides every placement.
public class DeploymentTests
{
    private static List<MatchState> States => PreviewAgreementTests.States.Value;

    private static bool PlacingFor(MatchState s, Seat seat) =>
        s.Phase == Phase.Deployment && s.Pending is null && s.Setup?.Scenario is not null && MatchView.NextDeployer(s) == seat
        && s.Models.Any(m => m.Seat == seat && !m.Deployed);

    private static (int Index, Seat Seat) FirstDeploying() =>
        States.Select((s, i) => (s, i))
            .Where(p => MatchView.NextDeployer(p.s) is { } seat && PlacingFor(p.s, seat) && p.s.Models.Count(m => m.Seat == seat && !m.Deployed) >= 2)
            .Select(p => (p.i, MatchView.NextDeployer(p.s)!.Value))
            .First();

    private static List<ModelId> Undeployed(MatchState s, Seat seat) =>
        [.. s.Models.Where(m => m.Seat == seat && !m.Deployed).OrderBy(m => m.Id.Value, StringComparer.Ordinal).Select(m => m.Id)];

    [Fact]
    public void YourDeploymentTurnStartsPlacingWithNoButtonAndThePromptSaysWhere()
    {
        var (index, seat) = FirstDeploying();
        var state = States[index];
        var interaction = new Interaction();
        interaction.Update(state, seat);
        Assert.Equal(InteractionMode.Place, interaction.Mode);
        Assert.Equal(Undeployed(state, seat)[0], interaction.Actor);
        Assert.DoesNotContain(interaction.Bar(null).Items, i => i.Kind == BarKind.Start);
        Assert.Equal($"Place {interaction.Actor!.Value.Value} in ZONE {Words.SeatLetter(seat)}: click a spot", interaction.Prompt);
        interaction.NameOf = id => "Sharpshooter";
        Assert.Equal($"Place Sharpshooter ({interaction.Actor!.Value.Value}) in ZONE {Words.SeatLetter(seat)}: click a spot", interaction.Prompt);
        // The other seat is not placing: nothing starts, no prompt.
        var other = new Interaction();
        other.Update(state, seat.Opponent());
        Assert.Equal(InteractionMode.None, other.Mode);
        Assert.Null(other.Prompt);
    }

    [Fact]
    public void ChipsChooseTheModelToPlaceAndSendNothing()
    {
        var (index, seat) = FirstDeploying();
        var state = States[index];
        var sent = new List<Command>();
        var interaction = new Interaction { Send = (c, _) => { sent.Add(c); return Task.FromResult(new Feedback(FeedbackKind.Accepted, "ok")); } };
        interaction.Update(state, seat);
        var undeployed = Undeployed(state, seat);
        var chips = interaction.Bar(null).Items.Where(i => i.Kind == BarKind.Pick).ToList();
        Assert.Equal(undeployed, chips.Select(c => c.Model!.Value));
        Assert.Equal($"▶ {undeployed[0].Value}", chips[0].Label);
        Assert.Equal(undeployed[1].Value, chips[1].Label);
        interaction.PickToPlace(undeployed[1]);
        Assert.Equal(undeployed[1], interaction.Actor);
        Assert.Equal($"▶ {undeployed[1].Value}", interaction.Bar(null).Items.First(i => i.Kind == BarKind.Pick && i.Model == undeployed[1]).Label);
        Assert.Equal(undeployed[1], interaction.Preview!.Model);
        Assert.Empty(sent);
        // The choice is kept while that model is still to place (a later state from the server, Esc and back).
        interaction.Cancel();
        interaction.Start(InteractionMode.Place);
        Assert.Equal(undeployed[1], interaction.Actor);
    }

    [Fact]
    public void EscDropsPlacingUntilTheNextStateOrL()
    {
        var (index, seat) = FirstDeploying();
        var state = States[index];
        var interaction = new Interaction();
        interaction.Update(state, seat);
        interaction.Cancel();
        Assert.Equal(InteractionMode.None, interaction.Mode);
        Assert.Equal("Place a model", Assert.Single(interaction.Bar(null).Items).Label);
        // The same state again changes nothing; a new one from the server places again.
        interaction.Update(state, seat);
        Assert.Equal(InteractionMode.None, interaction.Mode);
        interaction.Update(state with { }, seat);
        Assert.Equal(InteractionMode.Place, interaction.Mode);
        interaction.Cancel();
        Assert.True(interaction.Start(InteractionMode.Place));
        Assert.Equal(InteractionMode.Place, interaction.Mode);
    }

    // Through every simulated deployment: whenever a state comes from the server, the battlefield is placing exactly when
    // the rules let this seat place a model now, with a model of its own still to place.
    [Fact]
    public void ThroughEveryDeploymentPlacingFollowsTheRules()
    {
        var checkedStates = 0;
        foreach (var seat in new[] { Seat.One, Seat.Two })
        {
            var interaction = new Interaction();
            foreach (var state in States.Where(s => s.Phase == Phase.Deployment))
            {
                interaction.Update(state, seat);
                var placing = PlacingFor(state, seat);
                Assert.Equal(placing ? InteractionMode.Place : InteractionMode.None, interaction.Mode);
                if (placing)
                {
                    Assert.Contains(interaction.Actor!.Value, Undeployed(state, seat));
                    checkedStates++;
                }
            }
        }
        Assert.True(checkedStates > 20, $"{checkedStates} placing states");
    }

    [Fact]
    public async Task OnePlacementAcceptedThenTheNextModelIsReadyWithTheAnswerKept()
    {
        var (index, seat) = FirstDeploying();
        var state = States[index];
        var interaction = new Interaction { Send = (_, _) => Task.FromResult(new Feedback(FeedbackKind.Accepted, "ok")) };
        interaction.Update(state, seat);
        Assert.True(interaction.Preview!.Legal, interaction.Preview.Headline);
        var first = interaction.Actor!.Value;
        var centre = interaction.Cursor;
        Assert.Equal(ClickResult.Send, interaction.ClickAt(centre.X, centre.Y));
        Assert.Equal(FeedbackKind.Accepted, (await interaction.ConfirmAsync())!.Kind);
        // The server's events have not come yet: nothing starts for a model that is still shown undeployed.
        Assert.Equal(InteractionMode.None, interaction.Mode);
        // The next state in which this seat places again: the next model, the answer still shown.
        var next = States.Skip(index + 1).First(s => PlacingFor(s, seat) || s.Phase != Phase.Deployment);
        interaction.Update(next, seat);
        if (next.Phase == Phase.Deployment)
        {
            Assert.Equal(InteractionMode.Place, interaction.Mode);
            Assert.Contains(interaction.Actor!.Value, Undeployed(next, seat));
        }
        Assert.StartsWith("DONE: ", interaction.Notice);
        Assert.Equal(FeedbackKind.Accepted, interaction.Answer!.Kind);
    }

    [Fact]
    public void ASpotTheRulesRefuseSendsNothingAndCarriesTheRulesReason()
    {
        var (index, seat) = FirstDeploying();
        var state = States[index];
        var sent = 0;
        var interaction = new Interaction { Send = (_, _) => { sent++; return Task.FromResult(new Feedback(FeedbackKind.Accepted, "ok")); } };
        interaction.Update(state, seat);
        var centre = interaction.Cursor;
        var outside = new TablePoint(centre.X < 18 ? 35 : 1, centre.Y);
        interaction.SetCursor(outside.X, outside.Y);
        var preview = interaction.Preview!;
        var rules = Previews.Place(state, seat, interaction.Actor!.Value, Previews.PointAt(outside.X, outside.Y));
        Assert.False(preview.Legal);
        Assert.Equal(rules.Refusal!.Text, preview.Refusal!.Text);
        Assert.Equal(ClickResult.Refused, interaction.ClickAt(outside.X, outside.Y));
        Assert.Equal(0, sent);
        Assert.Null(interaction.Pending);
        Assert.Contains(rules.Refusal.Text, interaction.Notice);
    }
}
