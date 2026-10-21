using Client.Core;
using Rules.Core.Commands;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Client.Tests;

// M7.4 stage 4b: choosing a placement point, a path or a target on the battlefield, and what the previews say about it.
// Every state comes from simulated matches (PreviewAgreementTests.States); nothing here talks to a server.
public class InteractionTests
{
    private static List<MatchState> States => PreviewAgreementTests.States.Value;

    private static (MatchState State, Seat Seat) Deploying() =>
        States.Where(s => s.Phase == Phase.Deployment && s.Pending is null && s.Setup?.Scenario is not null && MatchView.NextDeployer(s) is not null
                && s.Models.Count(m => m.Seat == MatchView.NextDeployer(s) && !m.Deployed) >= 2)
            .Select(s => (s, MatchView.NextDeployer(s)!.Value))
            .First();

    private static (MatchState State, Seat Seat) Activating(Func<MatchState, bool>? also = null) =>
        States.Where(s => s.Phase == Phase.Activation && s.Pending is null && s.Actions == ActionsTaken.None && s.ActiveModel is { } a
                && s.FindModel(a)!.Status == ModelStatus.Standing && (also?.Invoke(s) ?? true))
            .Select(s => (s, s.FindModel(s.ActiveModel!.Value)!.Seat))
            .First();

    [Fact]
    public void NothingIsOfferedWhenItIsNotYourMoment()
    {
        var (state, seat) = Activating();
        var interaction = new Interaction();
        interaction.Update(state, seat.Opponent());
        Assert.Empty(interaction.Available);
        Assert.False(interaction.Start(InteractionMode.Move));
        Assert.Equal(InteractionMode.None, interaction.Mode);
        Assert.Contains("Nothing to preview now", interaction.Notice);
        Assert.Null(interaction.Preview);
        Assert.Contains("No action to preview right now.", interaction.Lines());

        interaction.Update(state, null);
        Assert.Empty(interaction.Available);
    }

    [Fact]
    public void PlacingStartsInYourZoneMovesInHalfInchStepsAndCyclesTheModelsToPlace()
    {
        var (state, seat) = Deploying();
        var interaction = new Interaction();
        interaction.Update(state, seat);
        Assert.Equal([InteractionMode.Place], interaction.Available);
        Assert.False(interaction.Start(InteractionMode.Move));
        Assert.True(interaction.Start(InteractionMode.Place));
        var undeployed = state.Models.Where(m => m.Seat == seat && !m.Deployed).OrderBy(m => m.Id.Value, StringComparer.Ordinal).Select(m => m.Id).ToList();
        Assert.Equal(undeployed[0], interaction.Actor);

        var zone = TableRect.Of(state.Setup!.Scenario!.Battlefield.Zone(state.SeatFor(seat).ZoneId!)!.Area.Bounds);
        Assert.InRange(interaction.Cursor.X, zone.X0, zone.X1);
        Assert.InRange(interaction.Cursor.Y, zone.Y0, zone.Y1);

        var start = interaction.Cursor;
        interaction.MoveCursor(Interaction.Step, 0);
        interaction.MoveCursor(0, -Interaction.BigStep);
        Assert.Equal(new TablePoint(start.X + 0.5, start.Y - 2), interaction.Cursor);
        interaction.MoveCursor(-100, 100);
        Assert.Equal(new TablePoint(0, 36), interaction.Cursor);

        interaction.SetCursor(start.X, start.Y);
        var preview = interaction.Preview!;
        Assert.Equal(PreviewKind.Place, preview.Kind);
        Assert.Equal(new PlaceModel(seat, undeployed[0], Previews.PointAt(start.X, start.Y)), preview.Command);
        Assert.True(preview.Legal, preview.Headline);
        Assert.Contains(Preview.Caveat, preview.Headline);

        interaction.NextChoice();
        Assert.Equal(undeployed[1], interaction.Actor);
        interaction.NextChoice(forward: false);
        interaction.NextChoice(forward: false);
        Assert.Equal(undeployed[^1], interaction.Actor);
    }

    // Stands in for MatchController.SendAsync: records every command and answers when the test says so.
    private sealed class FakeSender
    {
        public List<(Command Command, string Label)> Sent { get; } = [];

        public TaskCompletionSource<Feedback>? Answer { get; private set; }

        public Task<Feedback> Send(Command command, string label)
        {
            Sent.Add((command, label));
            Answer = new TaskCompletionSource<Feedback>(TaskCreationOptions.RunContinuationsAsynchronously);
            return Answer.Task;
        }
    }

    private static Interaction Placing(out MatchState state, out Seat seat, out FakeSender sender)
    {
        (state, seat) = Deploying();
        sender = new FakeSender();
        var interaction = new Interaction { Send = sender.Send };
        interaction.Update(state, seat);
        interaction.Start(InteractionMode.Place);
        return interaction;
    }

    [Fact]
    public async Task ConfirmSendsTheCommandOnceAndOnlyTheServersAcceptanceIsDone()
    {
        var interaction = Placing(out _, out var seat, out var sender);
        var preview = interaction.Preview!;
        Assert.True(preview.Legal);

        var confirming = interaction.ConfirmAsync();
        Assert.Equal(preview.Command, interaction.Pending);
        Assert.StartsWith($"SENT: Place {preview.Model.Value} at ", interaction.Notice);
        Assert.Null(interaction.Answer);
        Assert.Equal(InteractionMode.Place, interaction.Mode);

        // Pressing again, or starting something else, while the answer is on its way sends nothing more.
        Assert.Null(await interaction.ConfirmAsync());
        Assert.False(interaction.Start(InteractionMode.Place));
        Assert.Null(await interaction.ActivateAsync(preview.Model));
        Assert.Contains("nothing else was sent", interaction.Notice);
        Assert.Single(sender.Sent);
        Assert.Equal(new PlaceModel(seat, preview.Model, Previews.PointAt(interaction.Cursor.X, interaction.Cursor.Y)), sender.Sent[0].Command);

        sender.Answer!.SetResult(new Feedback(FeedbackKind.Accepted, "Done: placed."));
        var answer = await confirming;
        Assert.Equal(FeedbackKind.Accepted, answer!.Kind);
        Assert.Null(interaction.Pending);
        Assert.Equal(InteractionMode.None, interaction.Mode);
        Assert.StartsWith("DONE: Place ", interaction.Notice);
        Assert.Single(sender.Sent);
    }

    [Fact]
    public async Task ALegalPreviewIsNeverSuccessWithoutTheServer()
    {
        var (state, seat) = Deploying();
        var interaction = new Interaction();
        interaction.Update(state, seat);
        interaction.Start(InteractionMode.Place);
        Assert.True(interaction.Preview!.Legal);
        Assert.Null(await interaction.ConfirmAsync());
        Assert.Equal("Not connected to a match: nothing was sent.", interaction.Notice);
        Assert.Null(interaction.Answer);
        Assert.Equal(InteractionMode.Place, interaction.Mode);
    }

    [Fact]
    public async Task ARefusingPreviewIsSentOnlyOnASecondConfirmationAndTheServersRefusalIsShown()
    {
        var interaction = Placing(out var state, out var seat, out var sender);
        var zone = TableRect.Of(state.Setup!.Scenario!.Battlefield.Zone(state.SeatFor(seat).ZoneId!)!.Area.Bounds);
        interaction.SetCursor(36 - zone.CentreX, 36 - zone.CentreY);
        var refusal = interaction.Preview!.Refusal!;

        Assert.Null(await interaction.ConfirmAsync());
        Assert.StartsWith("Not sent: the preview says the server would refuse.", interaction.Notice);
        Assert.Contains("Confirm again to send it anyway; the server decides.", interaction.Notice);
        // Changing the choice disarms it: a single confirmation still sends nothing.
        interaction.MoveCursor(0, Interaction.Step);
        Assert.Null(await interaction.ConfirmAsync());
        Assert.Empty(sender.Sent);

        var confirming = interaction.ConfirmAsync();
        Assert.Single(sender.Sent);
        sender.Answer!.SetResult(new Feedback(FeedbackKind.RulesRefused, $"Refused by the rules: {refusal.Text}", refusal.RuleRef));
        Assert.Equal(FeedbackKind.RulesRefused, (await confirming)!.Kind);
        Assert.Equal($"REFUSED BY THE RULES: Refused by the rules: {refusal.Text} ({refusal.RuleRef})", interaction.Notice);
        Assert.Equal(InteractionMode.Place, interaction.Mode);
        Assert.Contains(interaction.Notice, interaction.Lines());

        // After a refusal the next send needs the full confirmation again.
        Assert.Null(await interaction.ConfirmAsync());
        Assert.Single(sender.Sent);
    }

    [Theory]
    [InlineData(FeedbackKind.Stale, "OUT OF DATE: ", true)]
    [InlineData(FeedbackKind.ProtocolRefused, "NOT TAKEN: ", true)]
    [InlineData(FeedbackKind.Unknown, "UNKNOWN: ", false)]
    public async Task EveryOtherAnswerIsShownAndALostAnswerClearsTheChoice(FeedbackKind kind, string word, bool kept)
    {
        var interaction = Placing(out _, out _, out var sender);
        var confirming = interaction.ConfirmAsync();
        sender.Answer!.SetResult(new Feedback(kind, "the words"));
        Assert.Equal(kind, (await confirming)!.Kind);
        Assert.StartsWith(word + "the words", interaction.Notice);
        Assert.Equal(kept, interaction.Mode == InteractionMode.Place);
        Assert.Equal(kind, interaction.Answer!.Kind);
        Assert.Single(sender.Sent);
    }

    [Fact]
    public async Task ASendThatThrowsIsReportedAsUnknownAndNotRepeated()
    {
        var (state, seat) = Deploying();
        var calls = 0;
        var interaction = new Interaction { Send = (_, _) => { calls++; throw new InvalidOperationException("gone"); } };
        interaction.Update(state, seat);
        interaction.Start(InteractionMode.Place);
        Assert.Equal(FeedbackKind.Unknown, (await interaction.ConfirmAsync())!.Kind);
        Assert.Equal(1, calls);
        Assert.Equal(InteractionMode.None, interaction.Mode);
        Assert.Null(await interaction.ConfirmAsync());
        Assert.Equal(1, calls);
    }

    // Hot-seat: when the window shows the other seat, a choice made for the previous seat is cleared, never carried over.
    [Fact]
    public void ShowingAnotherSeatClearsTheChoice()
    {
        var interaction = Placing(out var state, out var seat, out var sender);
        Assert.Equal(InteractionMode.Place, interaction.Mode);
        interaction.Update(state, seat.Opponent());
        Assert.Equal(InteractionMode.None, interaction.Mode);
        Assert.Null(interaction.Preview);
        Assert.Equal("Another seat is shown now, so the preview was cleared.", interaction.Notice);
        Assert.Empty(sender.Sent);
    }

    // Review fix: a cancelled send (OperationCanceledException) used to escape and leave the battlefield waiting forever.
    [Fact]
    public async Task ACancelledSendIsReportedAsUnknownAndNeverLeavesTheBattlefieldWaiting()
    {
        var (state, seat) = Deploying();
        var calls = 0;
        var interaction = new Interaction { Send = (_, _) => { calls++; return Task.FromCanceled<Feedback>(new CancellationToken(true)); } };
        interaction.Update(state, seat);
        interaction.Start(InteractionMode.Place);
        Assert.Equal(FeedbackKind.Unknown, (await interaction.ConfirmAsync())!.Kind);
        Assert.Equal(1, calls);
        Assert.Null(interaction.Pending);
        Assert.StartsWith("UNKNOWN: ", interaction.Notice);
        Assert.True(interaction.Start(InteractionMode.Place));
    }

    // Review fix: while a command is on its way, the choice stays exactly the command that was sent: cursor moves,
    // waypoints and target or model changes are ignored until the server answers.
    [Fact]
    public async Task WhileACommandIsOnItsWayTheChoiceCannotBeChanged()
    {
        var index = States.FindIndex(s => s.Phase == Phase.Activation && s.Pending is null && s.ActiveModel is not null);
        var state = States[index];
        var seat = state.FindModel(state.ActiveModel!.Value)!.Seat;
        var sender = new FakeSender();
        var interaction = new Interaction { Send = sender.Send };
        interaction.Update(state, seat);
        Assert.True(interaction.Start(InteractionMode.Move));
        interaction.MoveCursor(0.5, 0);
        Assert.True(interaction.AddPoint());
        interaction.MoveCursor(0.5, 0);
        var sent = interaction.Preview!.Command;
        var cursor = interaction.Cursor;
        var confirming = interaction.ConfirmAsync();
        Assert.NotNull(interaction.Pending);

        interaction.MoveCursor(2, 2);
        interaction.SetCursor(1, 1);
        Assert.False(interaction.AddPoint());
        Assert.False(interaction.RemovePoint());
        Assert.False(interaction.Start(InteractionMode.Dash));
        interaction.Cancel();
        Assert.Equal(cursor, interaction.Cursor);
        Assert.Equal(InteractionMode.Move, interaction.Mode);
        Assert.True(Same(sent, interaction.Preview!.Command));
        Assert.StartsWith("Waiting for the server's answer", interaction.Notice);
        Assert.Null(await interaction.ConfirmAsync());
        Assert.Single(sender.Sent);

        sender.Answer!.SetResult(new Feedback(FeedbackKind.RulesRefused, "Refused by the rules: no.", "p. 1"));
        await confirming;
        // After the answer the choice can be changed again.
        interaction.MoveCursor(0.5, 0);
        Assert.NotEqual(cursor, interaction.Cursor);
    }

    // Review fix: the same for targets.
    [Fact]
    public async Task WhileAShotIsOnItsWayTheTargetCannotBeChanged()
    {
        var state = States.First(s => s.Phase == Phase.Activation && s.Pending is null && s.ActiveModel is { } a
            && s.Models.Count(m => m.Seat != s.FindModel(a)!.Seat && m.Position is not null) >= 2);
        var seat = state.FindModel(state.ActiveModel!.Value)!.Seat;
        var sender = new FakeSender();
        var interaction = new Interaction { Send = sender.Send };
        interaction.Update(state, seat);
        Assert.True(interaction.Start(InteractionMode.Shoot));
        interaction.NextChoice();
        var target = interaction.Target;
        Assert.NotNull(target);
        var confirming = interaction.ConfirmAsync() is var first && interaction.Pending is null ? interaction.ConfirmAsync() : first;
        Assert.NotNull(interaction.Pending);
        interaction.NextChoice();
        interaction.SetTarget(state.Models.First(m => m.Id != target).Id);
        Assert.Equal(target, interaction.Target);
        Assert.Single(sender.Sent);
        sender.Answer!.SetResult(new Feedback(FeedbackKind.Accepted, "Done."));
        await confirming;
        Assert.Equal(InteractionMode.None, interaction.Mode);
    }

    private static bool Same(Command a, Command b) => (a, b) switch
    {
        (MoveModel x, MoveModel y) => x with { Path = default } == y with { Path = default } && x.Path.SequenceEqual(y.Path),
        _ => a == b,
    };

    [Fact]
    public async Task ANewStateWhileACommandIsOnItsWayKeepsTheChoiceUntilTheAnswer()
    {
        var index = States.FindIndex(s => s.Phase == Phase.Activation && s.Pending is null && s.ActiveModel is not null);
        var state = States[index];
        var seat = state.FindModel(state.ActiveModel!.Value)!.Seat;
        var later = States.Skip(index).First(s => s.ActiveModel is { } a && a != state.ActiveModel);
        var sender = new FakeSender();
        var interaction = new Interaction { Send = sender.Send };
        interaction.Update(state, seat);
        interaction.Start(InteractionMode.Move);
        interaction.MoveCursor(0.5, 0);
        var confirming = interaction.ConfirmAsync();
        interaction.Update(later, seat);
        Assert.Equal(InteractionMode.Move, interaction.Mode);
        sender.Answer!.SetResult(new Feedback(FeedbackKind.RulesRefused, "Refused by the rules: no.", "p. 1"));
        await confirming;
        // The match has moved on, so the choice is cleared, but the refusal stays on screen.
        Assert.Equal(InteractionMode.None, interaction.Mode);
        Assert.Equal("REFUSED BY THE RULES: Refused by the rules: no. (p. 1)", interaction.Notice);
        Assert.Contains(interaction.Notice, interaction.Lines());
    }

    [Fact]
    public async Task ActivateSendsTheSameCommandAsTheHintButton()
    {
        var index = States.FindIndex(s => s.Phase == Phase.Activation && s.Pending is null && s.ActiveModel is null);
        var state = States[index];
        var sender = new FakeSender();
        var interaction = new Interaction { Send = sender.Send };
        interaction.Update(state, Seat.Two);
        var model = state.Models.First(m => m.Seat == Seat.Two).Id;
        var activating = interaction.ActivateAsync(model);
        Assert.Equal(new ActivateModel(Seat.Two, model), interaction.Pending);
        sender.Answer!.SetResult(new Feedback(FeedbackKind.Accepted, "Done."));
        await activating;
        Assert.Equal((new ActivateModel(Seat.Two, model), $"Activate {model.Value}"), sender.Sent.Single());
        Assert.StartsWith($"DONE: Activate {model.Value}.", interaction.Notice);
    }

    [Fact]
    public void EachCommandIsDescribedInWords()
    {
        var (state, seat) = Activating();
        var actor = state.ActiveModel!.Value;
        var enemy = state.Models.First(m => m.Seat != seat).Id;
        var to = Previews.PointAt(3, 4);
        Assert.Equal($"Move {actor.Value} to (3.0\", 4.0\")", Interaction.Describe(Previews.Path(state, seat, PreviewKind.Move, actor, [to])));
        Assert.Equal($"Dash {actor.Value} to (3.0\", 4.0\")", Interaction.Describe(Previews.Path(state, seat, PreviewKind.Dash, actor, [to])));
        Assert.Equal($"Retreat {actor.Value} to (3.0\", 4.0\")", Interaction.Describe(Previews.Path(state, seat, PreviewKind.Retreat, actor, [to])));
        Assert.Equal($"Shoot {enemy.Value} with {actor.Value}", Interaction.Describe(Previews.Target(state, seat, PreviewKind.Shoot, actor, enemy)));
        Assert.Equal($"Charge {enemy.Value} with {actor.Value}", Interaction.Describe(Previews.Target(state, seat, PreviewKind.Charge, actor, enemy)));
        Assert.Equal($"Fight {enemy.Value} with {actor.Value}", Interaction.Describe(Previews.Target(state, seat, PreviewKind.Fight, actor, enemy)));
        Assert.Equal($"Place {actor.Value} at (3.0\", 4.0\")", Interaction.Describe(Previews.Place(state, seat, actor, to)));
    }

    [Fact]
    public void APathGrowsByWaypointsWithTheCursorAsTheNextPoint()
    {
        var (state, seat) = Activating();
        var interaction = new Interaction();
        interaction.Update(state, seat);
        // The six actions and (stage K6) Combat, which chooses among Shoot, Charge and Fight.
        Assert.Equal(7, interaction.Available.Count);
        Assert.True(interaction.Start(InteractionMode.Move));
        var model = state.FindModel(state.ActiveModel!.Value)!;
        Assert.Equal(TablePoint.Of(model.Position!.Value), interaction.Cursor);
        Assert.Null(interaction.Preview);
        Assert.Contains("Move the cursor to where the model should go.", interaction.Lines());

        interaction.MoveCursor(1, 0);
        Assert.Single(interaction.PathPoints());
        var preview = interaction.Preview!;
        Assert.Equal(PreviewKind.Move, preview.Kind);
        Assert.Equal(2, preview.Path.Count);
        Assert.Equal(0, preview.Path[0].Inches);

        Assert.True(interaction.AddPoint());
        Assert.False(interaction.AddPoint());
        interaction.MoveCursor(0, 1);
        Assert.Equal(2, interaction.PathPoints().Count);
        var path = (MoveModel)interaction.Preview!.Command;
        Assert.Equal(2, path.Path.Length);
        Assert.Equal(3, interaction.Preview!.Path.Count);
        Assert.True(interaction.Preview.Path[2].Inches >= interaction.Preview.Path[1].Inches);

        Assert.True(interaction.RemovePoint());
        Assert.False(interaction.RemovePoint());
        Assert.Single(interaction.PathPoints());

        interaction.Cancel();
        Assert.Equal(InteractionMode.None, interaction.Mode);
        Assert.Empty(interaction.Waypoints);
        Assert.Null(interaction.Preview);
    }

    [Fact]
    public void AMoveHasAtMostSixteenWaypoints()
    {
        var (state, seat) = Activating();
        var interaction = new Interaction();
        interaction.Update(state, seat);
        interaction.Start(InteractionMode.Dash);
        for (var i = 0; i < 20; i++)
        {
            interaction.MoveCursor(i % 2 == 0 ? 0.5 : -0.5, 0.5);
            interaction.AddPoint();
        }
        Assert.Equal(16, interaction.Waypoints.Count);
    }

    [Fact]
    public void TargetsStartWithTheNearestEnemyAndCycleBothWays()
    {
        var (state, seat) = Activating(s => s.Models.Count(m => m.Position is not null && m.Seat != s.FindModel(s.ActiveModel!.Value)!.Seat) >= 3
            && s.Setup!.ProfileOf(s.ActiveModel!.Value)!.HasRangedWeapon);
        var interaction = new Interaction();
        interaction.Update(state, seat);
        interaction.Start(InteractionMode.Shoot);
        var targets = interaction.Targets();
        Assert.True(targets.Count >= 3);
        Assert.All(targets, t => Assert.NotEqual(seat, state.FindModel(t)!.Seat));
        Assert.Equal(targets[0], interaction.Target);
        interaction.NextChoice();
        Assert.Equal(targets[1], interaction.Target);
        interaction.NextChoice(forward: false);
        interaction.NextChoice(forward: false);
        Assert.Equal(targets[^1], interaction.Target);

        // A click may pick any model; the preview then says the server would refuse a friend.
        var friend = state.Models.First(m => m.Seat == seat && m.Id != interaction.Actor);
        interaction.SetTarget(friend.Id);
        Assert.Equal(Rules.Core.ReasonCode.TargetNotEnemy, interaction.Preview!.Refusal?.Code);
        Assert.False(interaction.UsesCursor);
        var before = interaction.Version;
        interaction.MoveCursor(1, 1);
        Assert.Equal(before, interaction.Version);
    }

    [Fact]
    public void ThePreviewIsWorkedOutOncePerChange()
    {
        var (state, seat) = Activating();
        var interaction = new Interaction();
        interaction.Update(state, seat);
        interaction.Start(InteractionMode.Charge);
        var first = interaction.Preview;
        Assert.Same(first, interaction.Preview);
        var version = interaction.Version;
        interaction.Update(state, seat);
        Assert.Equal(version, interaction.Version);
        Assert.Same(first, interaction.Preview);
        interaction.NextChoice();
        Assert.True(interaction.Version > version);
    }

    [Fact]
    public void ANewStateFromTheServerClearsAChoiceThatNoLongerFits()
    {
        var index = States.FindIndex(s => s.Phase == Phase.Activation && s.Pending is null && s.ActiveModel is not null);
        var state = States[index];
        var seat = state.FindModel(state.ActiveModel!.Value)!.Seat;
        var later = States.Skip(index).First(s => s.ActiveModel is { } a && a != state.ActiveModel);
        var interaction = new Interaction();
        interaction.Update(state, seat);
        Assert.True(interaction.Start(InteractionMode.Shoot));
        interaction.Update(later, seat);
        Assert.Equal(InteractionMode.None, interaction.Mode);
        Assert.Equal("The match moved on, so the preview was cleared.", interaction.Notice);
    }

    [Fact]
    public void ShootPreviewsDrawTheRulesOwnSightRaysAndSayTheFactsInInches()
    {
        var checkedShots = 0;
        foreach (var state in States.Where(s => s.Phase == Phase.Activation && s.Pending is null && s.ActiveModel is not null))
        {
            var actor = state.FindModel(state.ActiveModel!.Value)!;
            foreach (var target in state.Models.Where(m => m.Seat != actor.Seat && m.Position is not null))
            {
                var preview = Previews.Target(state, actor.Seat, PreviewKind.Shoot, actor.Id, target.Id);
                if (!preview.Legal)
                {
                    continue;
                }
                var from = Body(state, actor);
                var to = Body(state, target);
                var cover = Cover.Of(state.Setup!.Scenario!.Battlefield, from, to);
                Assert.Equal(cover.Sight.Total, preview.Rays.Count);
                Assert.Equal(cover.Sight.Visible, preview.Rays.Count(r => r.Clear));
                Assert.Equal(TablePoint.Of(from.Centre), preview.Rays[0].From);
                Assert.Contains(preview.Facts, f => f.StartsWith($"Distance {BoardScene.Inch(Measure.Gap(from, to).Thousandths / 1000.0)}, weapon range ", StringComparison.Ordinal));
                Assert.Contains(preview.Facts, f => f.StartsWith("Line of sight: ", StringComparison.Ordinal) && f.Contains($"{cover.Sight.Visible} of {cover.Sight.Total} sight rays clear", StringComparison.Ordinal));
                Assert.Contains(cover.InCover ? $"Target in cover ({cover.TerrainId})." : "Target not in cover.", preview.Facts);
                Assert.Equal("Shoot: looks legal (preview; the server decides)", preview.Headline);
                checkedShots++;
            }
        }
        Assert.True(checkedShots > 100, $"only {checkedShots} legal shots checked");
    }

    [Fact]
    public void ChargePreviewsSayWhichRollReachesAndDrawThePlannedPath()
    {
        var checkedCharges = 0;
        foreach (var state in States.Where(s => s.Phase == Phase.Activation && s.Pending is null && s.ActiveModel is not null))
        {
            var actor = state.FindModel(state.ActiveModel!.Value)!;
            foreach (var target in state.Models.Where(m => m.Seat != actor.Seat && m.Position is not null))
            {
                var preview = Previews.Target(state, actor.Seat, PreviewKind.Charge, actor.Id, target.Id);
                if (!preview.Legal)
                {
                    continue;
                }
                Assert.NotEmpty(preview.ChargePath);
                Assert.Equal(TablePoint.Of(actor.Position!.Value), preview.ChargePath[0].At);
                Assert.Contains(preview.Facts, f => f.StartsWith("Charge reach: Movement ", StringComparison.Ordinal));
                if (preview.ChargeRollNeeded is { } n)
                {
                    Assert.InRange(n, 1, 6);
                }
                else
                {
                    Assert.Contains("Would not reach base contact even on a 6; the model would stop short.", preview.Facts);
                }
                checkedCharges++;
            }
        }
        Assert.True(checkedCharges > 50, $"only {checkedCharges} legal charges checked");
    }

    [Fact]
    public void PathMarksCountTheDistanceUsedAndEndAtTheReportedTotal()
    {
        var (state, seat) = Activating();
        var model = state.FindModel(state.ActiveModel!.Value)!;
        var at = TablePoint.Of(model.Position!.Value);
        var preview = Previews.Path(state, seat, PreviewKind.Move, model.Id, [Previews.PointAt(at.X + 1, at.Y), Previews.PointAt(at.X + 1, at.Y + 1)]);
        Assert.Equal(3, preview.Path.Count);
        Assert.Equal(1.0, preview.Path[1].Inches, 3);
        Assert.Equal(2.0, preview.Path[2].Inches, 1);
        Assert.Contains(preview.Facts, f => f.StartsWith($"Distance {BoardScene.Inch(preview.Path[2].Inches)} of ", StringComparison.Ordinal));
    }

    [Fact]
    public void TheHoverThrottlePassesTheNewestPointAtMostOncePerInterval()
    {
        var throttle = new HoverThrottle(0.1);
        Assert.Null(throttle.Take(0));
        throttle.Offer(new TablePoint(1, 1));
        Assert.Equal(new TablePoint(1, 1), throttle.Take(0));
        throttle.Offer(new TablePoint(2, 2));
        throttle.Offer(new TablePoint(3, 3));
        Assert.Null(throttle.Take(0.05));
        Assert.Equal(new TablePoint(3, 3), throttle.Take(0.1));
        Assert.Null(throttle.Take(0.5));
    }

    // At the elevation the rules give it (a tower's platform or the ground).
    private static ModelBody Body(MatchState state, ModelState model) =>
        new(model.Id.Value, model.Position!.Value, state.Setup!.ProfileOf(model.Id)!.BaseRadius, Towers.ElevationAt(state.Setup.Scenario!.Battlefield, model.Position!.Value));
}
