using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.State;

namespace Rules.Tests;

// The turn sequence, rule by rule. Provisional readings are marked "provisional R<n>"; they are choices,
// not official rulings (see docs/rules-notes.md, "Open rules questions").
public class TurnSequenceTests
{
    // ---------- Deployment: Scenario I, rulebook 1.0.2 p. 152 ----------

    [Fact]
    [Trait("Rule", "1.0.2 Scenario I p.152")]
    public void StartMatchRollsOffForTheZoneAndAsksTheWinnerToChoose()
    {
        var play = new Play(Play.Setup(2, 2));

        var result = play.Do(new StartMatch(), 2, 5);

        var roll = Assert.Single(result.Events.OfType<RollOffResolved>());
        Assert.Equal(RollOffPurpose.DeploymentZone, roll.Purpose);
        Assert.Equal(Seat.Two, roll.Winner);
        Assert.Equal(Phase.Deployment, play.State.Phase);
        Assert.Equal(new PendingDecision(DecisionKind.ChooseDeploymentZone, Seat.Two), play.State.Pending);
    }

    [Fact]
    [Trait("Rule", "1.0.2 Rolling Off p.31")]
    public void ATiedRollOffIsRolledAgain()
    {
        var play = new Play(Play.Setup(2, 2));

        var result = play.Do(new StartMatch(), 3, 3, 6, 6, 1, 4);

        var roll = result.Events.OfType<RollOffResolved>().Single();
        Assert.Equal(3, roll.Rounds.Length);
        Assert.Equal(Seat.Two, roll.Winner);
    }

    [Fact]
    [Trait("Rule", "1.0.2 Scenario I p.152")]
    public void OnlyTheRollOffWinnerChoosesTheZoneAndTheOpponentGetsTheOther()
    {
        var play = new Play(Play.Setup(2, 3));
        play.Do(new StartMatch(), 6, 1);

        var wrong = play.Try(new ChooseDeploymentZone(Seat.Two, "north"));
        Assert.Equal(ReasonCode.NotYourTurn, wrong.Rejection!.Code);

        play.Do(new ChooseDeploymentZone(Seat.One, "south"));
        Assert.Equal("south", play.State.One.ZoneId);
        Assert.Equal("north", play.State.Two.ZoneId);
    }

    [Fact]
    [Trait("Rule", "1.0.2 Scenario I p.152")]
    public void AnUnknownZoneIsRejected()
    {
        var play = new Play(Play.Setup(2, 2));
        play.Do(new StartMatch(), 6, 1);

        var result = play.Try(new ChooseDeploymentZone(Seat.One, "middle"));

        Assert.Equal(ReasonCode.UnknownZone, result.Rejection!.Code);
    }

    [Theory]
    [Trait("Rule", "1.0.2 Scenario I p.152")]
    [InlineData(2, 3, Seat.Two)]
    [InlineData(4, 1, Seat.One)]
    public void TheSideWithMoreModelsDeploysFirst(int one, int two, Seat expectedFirst)
    {
        var play = new Play(Play.Setup(one, two));
        play.Do(new StartMatch(), 6, 1);

        play.Do(new ChooseDeploymentZone(Seat.One, "north"));

        Assert.Equal(expectedFirst, play.State.DeploymentFirst);
        Assert.All(TurnSequence.LegalCommands(play.State), c => Assert.Equal(expectedFirst, ((DeployModel)c).Seat));
    }

    [Fact]
    [Trait("Rule", "1.0.2 Scenario I p.152")]
    public void EqualSidesRollOffForWhoDeploysFirst()
    {
        var play = new Play(Play.Setup(3, 3));
        play.Do(new StartMatch(), 6, 1);

        var result = play.Do(new ChooseDeploymentZone(Seat.One, "north"), 1, 6); // provisional R7: the winner starts

        Assert.Equal(RollOffPurpose.DeploymentOrder, result.Events.OfType<RollOffResolved>().Single().Purpose);
        Assert.Equal(Seat.Two, play.State.DeploymentFirst);
    }

    [Fact]
    [Trait("Rule", "1.0.2 Scenario I p.152")]
    public void DeploymentAlternatesAndTheLargerSideFinishes()
    {
        var play = new Play(Play.Setup(1, 3));
        play.Do(new StartMatch(), 6, 1);
        play.Do(new ChooseDeploymentZone(Seat.One, "north"));
        var order = new List<Seat>();

        while (play.State.Phase == Phase.Deployment)
        {
            var deploy = (DeployModel)TurnSequence.LegalCommands(play.State)[0];
            order.Add(deploy.Seat);
            play.Do(deploy);
        }

        Assert.Equal([Seat.Two, Seat.One, Seat.Two, Seat.Two], order);
    }

    [Fact]
    [Trait("Rule", "1.0.2 Scenario I p.152")]
    public void FinishingDeploymentStartsTurnOneAndAsksTheInitiativeHolder()
    {
        var play = new Play(Play.Setup(2, 3));

        play.ToActivation().State.GetType();

        Assert.Equal(Phase.Activation, play.State.Phase);
        Assert.Equal(1, play.State.Turn);
        Assert.All(play.State.Models, m => Assert.True(m.Deployed));
    }

    [Fact]
    [Trait("Rule", "1.0.2 Scenario I p.152")]
    public void DeployingTheSameModelTwiceIsRejected()
    {
        var play = new Play(Play.Setup(3, 3));
        play.Do(new StartMatch(), 6, 1);
        play.Do(new ChooseDeploymentZone(Seat.One, "north"), 5, 2);
        play.Do(new DeployModel(Seat.One, Play.A(1)));
        play.Do(new DeployModel(Seat.Two, Play.B(1)));

        var result = play.Try(new DeployModel(Seat.One, Play.A(1)));

        Assert.Equal(ReasonCode.ModelAlreadyDeployed, result.Rejection!.Code);
    }

    [Fact]
    [Trait("Rule", "1.0.2 Scenario I p.152")]
    public void DeployingTheWrongSeatsOrAnUnknownModelIsRejected()
    {
        var play = new Play(Play.Setup(3, 3));
        play.Do(new StartMatch(), 6, 1);
        play.Do(new ChooseDeploymentZone(Seat.One, "north"), 5, 2);

        Assert.Equal(ReasonCode.NotYourTurn, play.Try(new DeployModel(Seat.Two, Play.B(1))).Rejection!.Code);
        Assert.Equal(ReasonCode.NotYourModel, play.Try(new DeployModel(Seat.One, Play.B(1))).Rejection!.Code);
        Assert.Equal(ReasonCode.UnknownModel, play.Try(new DeployModel(Seat.One, new ModelId("Z9"))).Rejection!.Code);
    }

    // ---------- Initiative: rulebook 1.0.2 p. 32 (Core p. 14) ----------

    [Theory]
    [Trait("Rule", "1.0.2 Initiative Phase p.32")]
    [InlineData(2, 3, Seat.One)]
    [InlineData(3, 2, Seat.Two)]
    public void TheSideWithFewerModelsHoldsTheInitiative(int one, int two, Seat expectedHolder)
    {
        var play = new Play(Play.Setup(one, two));
        play.Do(new StartMatch(), 6, 1);
        play.Do(new ChooseDeploymentZone(Seat.One, "north"));
        var lastDeployment = TurnSequence.LegalCommands(play.State);
        while (play.State.Phase == Phase.Deployment)
        {
            play.Do(TurnSequence.LegalCommands(play.State)[0]);
        }

        var initiative = play.Log.OfType<InitiativeDetermined>().Single();
        Assert.Equal(expectedHolder, initiative.Holder);
        Assert.Equal((one, two), (initiative.CountSeatOne, initiative.CountSeatTwo));
        Assert.Equal(new PendingDecision(DecisionKind.ChooseFirstActivation, expectedHolder), play.State.Pending);
        Assert.NotEmpty(lastDeployment);
    }

    [Theory]
    [Trait("Rule", "1.0.2 Initiative Phase p.32")]
    [InlineData(6, 1, Seat.One)]
    [InlineData(1, 6, Seat.Two)]
    public void EqualCountsRollOffForTheInitiative(int dieOne, int dieTwo, Seat expectedHolder)
    {
        var play = new Play(Play.Setup(3, 3));
        play.Do(new StartMatch(), 6, 1);
        play.Do(new ChooseDeploymentZone(Seat.One, "north"), 5, 2);

        play.DeployAll(dieOne, dieTwo);

        var roll = play.Log.OfType<RollOffResolved>().Last();
        Assert.Equal(RollOffPurpose.Initiative, roll.Purpose);
        Assert.Equal(expectedHolder, play.State.InitiativeHolder);
    }

    [Fact]
    [Trait("Rule", "1.0.2 Rolling Off p.31")]
    public void ATiedInitiativeRollOffIsRolledAgain()
    {
        var play = new Play(Play.Setup(2, 2));
        play.Do(new StartMatch(), 6, 1);
        play.Do(new ChooseDeploymentZone(Seat.One, "north"), 5, 2);

        play.DeployAll(4, 4, 2, 5);

        Assert.Equal(Seat.Two, play.State.InitiativeHolder);
        Assert.Equal(2, play.Log.OfType<RollOffResolved>().Last().Rounds.Length);
    }

    [Fact]
    [Trait("Rule", "1.0.2 Initiative Phase p.32")]
    public void TheHolderChoosesWhichPlayerActivatesFirst()
    {
        var play = new Play(Play.Setup(2, 3));
        play.Do(new StartMatch(), 6, 1);
        play.Do(new ChooseDeploymentZone(Seat.One, "north"));
        play.DeployAll();

        var notTheHolder = play.Try(new ChooseFirstActivation(Seat.Two, Seat.Two));
        Assert.Equal(ReasonCode.NotYourTurn, notTheHolder.Rejection!.Code);

        play.Do(new ChooseFirstActivation(Seat.One, Seat.Two));
        Assert.Equal(Phase.Activation, play.State.Phase);
        Assert.Equal(Seat.Two, TurnSequence.NextActivator(play.State));
    }

    [Fact]
    [Trait("Rule", "1.0.2 Initiative Phase p.32")]
    public void DownAndOutOfActionModelsAreNotCountedForInitiative()
    {
        var play = new Play(Play.Setup(4, 4)).ToActivation();
        play.SetStatus("A1", ModelStatus.Down); // 1 of 4 is below the Morale threshold, so no check

        play.RunActivations();

        var initiative = play.Log.OfType<InitiativeDetermined>().Last();
        Assert.Equal(2, play.State.Turn);
        Assert.Equal((3, 4, Seat.One), (initiative.CountSeatOne, initiative.CountSeatTwo, initiative.Holder));
    }

    [Fact]
    [Trait("Rule", "1.0.2 Initiative Phase p.32; Core Rules p.14")]
    public void TheCoreRulesReadingCountsDownModelsToo() // provisional R1
    {
        var profile = new RulesProfile(InitiativeCount: InitiativeCounting.CountAllNotOutOfAction);
        var play = new Play(Play.Setup(4, 4, profile)).ToActivation();
        play.SetStatus("A1", ModelStatus.Down);

        play.RunActivations(2, 5);

        var initiative = play.Log.OfType<InitiativeDetermined>().Last();
        Assert.Equal((4, 4, Seat.Two), (initiative.CountSeatOne, initiative.CountSeatTwo, initiative.Holder));
    }

    // ---------- Activation: rulebook 1.0.2 p. 33 ----------

    [Theory]
    [Trait("Rule", "1.0.2 Activation Phase p.33")]
    [InlineData(2, 2, Seat.One, "One,Two,One,Two")]
    [InlineData(2, 2, Seat.Two, "Two,One,Two,One")]
    [InlineData(2, 5, Seat.One, "One,Two,One,Two,Two,Two,Two")]
    [InlineData(5, 2, Seat.Two, "Two,One,Two,One,One,One,One")]
    public void PlayersAlternateAndTheLargerSideFinishesItsModels(int one, int two, Seat first, string expected)
    {
        var play = new Play(Play.Setup(one, two)).ToActivation(first);
        var order = new List<Seat>();

        while (play.State.Phase == Phase.Activation)
        {
            var seat = TurnSequence.NextActivator(play.State)!.Value;
            order.Add(seat);
            play.Do(TurnSequence.LegalCommands(play.State)[0]);
            var last = play.State.Models.Count(m => !m.ActivatedThisTurn) == 0;
            // Ending the last activation starts turn 2, whose Initiative roll-off is needed when the sides are equal.
            play.Do(new EndActivation(seat), last && one == two ? [5, 2] : []);
        }

        Assert.Equal(expected, string.Join(",", order));
    }

    [Fact]
    [Trait("Rule", "1.0.2 Activation Phase p.33")]
    public void ActivatingAModelTwiceInOneTurnIsRejected()
    {
        var play = new Play(Play.Setup(2, 2)).ToActivation();
        play.Do(new ActivateModel(Seat.One, Play.A(1)));
        play.Do(new EndActivation(Seat.One));
        play.Do(new ActivateModel(Seat.Two, Play.B(1)));
        play.Do(new EndActivation(Seat.Two));

        var result = play.Try(new ActivateModel(Seat.One, Play.A(1)));

        Assert.Equal(ReasonCode.ModelAlreadyActivated, result.Rejection!.Code);
        Assert.Contains("already", result.Rejection.Text);
    }

    [Fact]
    [Trait("Rule", "1.0.2 Activation Phase p.33")]
    public void ActivationRejectionsCarryTheRightReason()
    {
        var play = new Play(Play.Setup(2, 2)).ToActivation();
        play.SetStatus("A2", ModelStatus.OutOfAction);

        Assert.Equal(ReasonCode.NotYourTurn, play.Try(new ActivateModel(Seat.Two, Play.B(1))).Rejection!.Code);
        Assert.Equal(ReasonCode.NotYourModel, play.Try(new ActivateModel(Seat.One, Play.B(1))).Rejection!.Code);
        Assert.Equal(ReasonCode.UnknownModel, play.Try(new ActivateModel(Seat.One, new ModelId("Z9"))).Rejection!.Code);
        Assert.Equal(ReasonCode.ModelOutOfAction, play.Try(new ActivateModel(Seat.One, Play.A(2))).Rejection!.Code);

        play.Do(new ActivateModel(Seat.One, Play.A(1)));
        Assert.Equal(ReasonCode.ActivationInProgress, play.Try(new ActivateModel(Seat.One, Play.A(2))).Rejection!.Code);
    }

    [Fact]
    [Trait("Rule", "1.0.2 Activation Phase p.33; Core Rules p.20")]
    public void ADownModelCanBeActivated()
    {
        var play = new Play(Play.Setup(2, 2)).ToActivation();
        play.SetStatus("A1", ModelStatus.Down);

        var result = play.Try(new ActivateModel(Seat.One, Play.A(1)));

        Assert.True(result.IsAccepted);
    }

    [Fact]
    [Trait("Rule", "1.0.2 Activation Phase p.33")]
    public void EndingNothingOrSomeoneElsesActivationIsRejected()
    {
        var play = new Play(Play.Setup(2, 2)).ToActivation();

        Assert.Equal(ReasonCode.NoActivationInProgress, play.Try(new EndActivation(Seat.One)).Rejection!.Code);

        play.Do(new ActivateModel(Seat.One, Play.A(1)));
        Assert.Equal(ReasonCode.NotYourTurn, play.Try(new EndActivation(Seat.Two)).Rejection!.Code);
    }

    [Fact]
    [Trait("Rule", "1.0.2 Activation Phase p.33")]
    public void AModelThatIsOutOfActionMidTurnIsSkipped()
    {
        var play = new Play(Play.Setup(3, 3)).ToActivation();
        play.Do(new ActivateModel(Seat.One, Play.A(1)));
        play.Do(new EndActivation(Seat.One));
        play.SetStatus("B2", ModelStatus.OutOfAction); // 1 of 3 is below the Morale threshold
        var activatedB = new List<string>();

        while (play.State.Phase == Phase.Activation)
        {
            var cmd = TurnSequence.LegalCommands(play.State)[0];
            if (cmd is ActivateModel { Seat: Seat.Two } a)
            {
                activatedB.Add(a.Model.Value);
            }
            var isLast = cmd is EndActivation && play.State.Models.Count(m => m.Status != ModelStatus.OutOfAction && !m.ActivatedThisTurn) == 0;
            play.Do(cmd, isLast ? [] : []);
        }

        Assert.Equal(["B1", "B3"], activatedB);
        Assert.Equal(2, play.State.Turn);
    }

    [Fact]
    [Trait("Rule", "1.0.2 Activation Phase p.33; Core Rules p.14")]
    public void AfterTheLastActivationTheNextTurnStartsAndMarksAreCleared()
    {
        var play = new Play(Play.Setup(2, 3)).ToActivation();

        play.RunActivations();

        Assert.Equal(Phase.Initiative, play.State.Phase);
        Assert.Equal(2, play.State.Turn);
        Assert.All(play.State.Models, m => Assert.False(m.ActivatedThisTurn));
        Assert.Equal(new PendingDecision(DecisionKind.ChooseFirstActivation, Seat.One), play.State.Pending);
    }

    [Fact]
    [Trait("Rule", "1.0.2 Activation Phase p.33")]
    public void ActivatingBeforeTheFirstActivatorIsChosenIsRejectedWithTheDecisionPending()
    {
        var play = new Play(Play.Setup(2, 3));
        play.Do(new StartMatch(), 6, 1);
        play.Do(new ChooseDeploymentZone(Seat.One, "north"));
        play.DeployAll();

        var result = play.Try(new ActivateModel(Seat.One, Play.A(1)));

        Assert.Equal(ReasonCode.DecisionPending, result.Rejection!.Code);
    }

    // ---------- Morale Phase: rulebook 1.0.2 p. 50 (Core p. 21) ----------

    [Theory]
    [Trait("Rule", "1.0.2 Morale Phase p.50")]
    [InlineData(5, 2, 0, false)] // under half
    [InlineData(5, 3, 0, true)]  // half of 5 is 2.5, rounded up to 3 (the rulebook's own example)
    [InlineData(5, 2, 1, true)]  // Down and Out of Action both count
    [InlineData(4, 1, 0, false)]
    [InlineData(4, 2, 0, true)]  // exactly half
    [InlineData(4, 1, 1, true)]
    [InlineData(3, 1, 0, false)]
    [InlineData(3, 2, 0, true)]
    [InlineData(2, 1, 0, true)]
    [InlineData(1, 1, 0, true)]
    public void AMoraleCheckIsNeededAtHalfTheWarbandRoundedUp(int size, int down, int outOfAction, bool needed)
    {
        var play = new Play(Play.Setup(size, size + 1)).ToActivation();
        for (var i = 1; i <= down; i++)
        {
            play.SetStatus($"A{i}", ModelStatus.Down);
        }
        for (var i = 0; i < outOfAction; i++)
        {
            play.SetStatus($"A{size - i}", ModelStatus.OutOfAction);
        }

        play.RunActivations(needed ? [4, 3] : []);

        Assert.Equal(needed, play.Log.OfType<MoraleCheckRolled>().Any());
        if (needed)
        {
            var check = play.Log.OfType<MoraleCheckRolled>().Single();
            Assert.Equal((size, down + outOfAction), (check.WarbandSize, check.ModelsDownOrOut));
        }
    }

    [Fact]
    [Trait("Rule", "1.0.2 Morale Phase p.50")]
    public void NoDiceAreRolledWhenNoCheckIsNeeded()
    {
        var play = new Play(Play.Setup(3, 4)).ToActivation();
        while (play.State.Phase == Phase.Activation && TurnSequence.LegalCommands(play.State)[0] is { } c && !(c is EndActivation && play.State.Models.Count(m => !m.ActivatedThisTurn) == 0))
        {
            play.Do(c);
        }
        var dice = new FixedDice(6, 6, 6, 6);

        var result = TurnSequence.Decide(play.State, new EndActivation(play.State.ActiveModel is { } id ? play.State.FindModel(id)!.Seat : Seat.One), dice);

        Assert.True(result.IsAccepted);
        Assert.Equal(4, dice.Remaining);
        Assert.Empty(result.Events.OfType<MoraleCheckRolled>());
    }

    [Fact]
    [Trait("Rule", "1.0.2 Morale Phase p.50")]
    public void APassedCheckLetsTheWarbandCarryOn()
    {
        var play = new Play(Play.Setup(5, 6)).ToActivation();
        foreach (var id in new[] { "A1", "A2", "A3" })
        {
            play.SetStatus(id, ModelStatus.Down);
        }

        play.RunActivations(4, 3);

        Assert.False(play.State.One.Shaken);
        Assert.Equal(2, play.State.Turn);
        Assert.Null(play.State.Outcome);
    }

    [Fact]
    [Trait("Rule", "1.0.2 Morale Phase p.50")]
    public void AFailedCheckAsksTheWarbandHowToAnswerAndShakenIsTheOneChoice()
    {
        var play = new Play(Play.Setup(5, 6)).ToActivation();
        foreach (var id in new[] { "A1", "A2", "A3" })
        {
            play.SetStatus(id, ModelStatus.Down);
        }

        play.RunActivations(1, 2);
        Assert.Equal(new PendingDecision(DecisionKind.FailedMorale, Seat.One), play.State.Pending);
        Assert.False(play.State.One.Shaken);
        Assert.Equal(ReasonCode.DecisionPending, play.Try(new EndActivation(Seat.One)).Rejection!.Code);

        play.Do(new ResolveFailedMorale(Seat.One, MoraleFailureChoice.BecomeShaken));

        Assert.True(play.State.IsShaken(Seat.One));
        Assert.Equal(2, play.State.Turn);
    }

    [Fact]
    [Trait("Rule", "1.0.2 Sounding the Retreat p.50")]
    public void SoundingTheRetreatMakesTheWarbandFleeAndLoseAtOnce()
    {
        var play = new Play(Play.Setup(5, 6)).ToActivation();
        foreach (var id in new[] { "A1", "A2", "A3" })
        {
            play.SetStatus(id, ModelStatus.Down);
        }
        play.RunActivations(1, 2);

        play.Do(new ResolveFailedMorale(Seat.One, MoraleFailureChoice.SoundRetreat));

        Assert.Equal(Phase.Ended, play.State.Phase);
        Assert.Equal(new MatchOutcome(Seat.Two, OutcomeReason.OpponentFled), play.State.Outcome);
        Assert.True(play.State.One.Fled);
    }

    [Fact]
    [Trait("Rule", "1.0.2 Shaken Warbands p.50")]
    public void AShakenWarbandChecksNextTurnEvenBelowTheThresholdAndPassingRecovers()
    {
        var play = new Play(Play.Setup(5, 6)).ToActivation();
        foreach (var id in new[] { "A1", "A2", "A3" })
        {
            play.SetStatus(id, ModelStatus.Down);
        }
        play.RunActivations(1, 2);
        play.Do(new ResolveFailedMorale(Seat.One, MoraleFailureChoice.BecomeShaken));
        foreach (var id in new[] { "A1", "A2", "A3" })
        {
            play.SetStatus(id, ModelStatus.Standing); // below the threshold now
        }
        play.Do(new ChooseFirstActivation(Seat.One, Seat.One));

        play.RunActivations(5, 5);

        var check = play.Log.OfType<MoraleCheckRolled>().Last();
        Assert.True(check.WasShaken);
        Assert.Equal(0, check.ModelsDownOrOut);
        Assert.False(play.State.One.Shaken);
        Assert.Contains(play.Log, e => e is WarbandRecovered { Seat: Seat.One });
        Assert.Equal(3, play.State.Turn);
    }

    [Fact]
    [Trait("Rule", "1.0.2 Shaken Warbands p.50; Winning the Game p.51")]
    public void AShakenWarbandThatFailsAgainFleesWithNoChoiceAndLoses()
    {
        var play = new Play(Play.Setup(5, 6)).ToActivation();
        foreach (var id in new[] { "A1", "A2", "A3" })
        {
            play.SetStatus(id, ModelStatus.Down);
        }
        play.RunActivations(1, 2);
        play.Do(new ResolveFailedMorale(Seat.One, MoraleFailureChoice.BecomeShaken));
        play.Do(new ChooseFirstActivation(Seat.One, Seat.One));

        play.RunActivations(2, 2);

        Assert.Contains(play.Log, e => e is WarbandFled { Seat: Seat.One, Reason: FledReason.FailedWhileShaken });
        Assert.Equal(new MatchOutcome(Seat.Two, OutcomeReason.OpponentFled), play.State.Outcome);
    }

    [Theory]
    [Trait("Rule", "1.0.2 Morale Phase p.50")]
    [InlineData(Seat.One, "4,3,1,1", Seat.Two)]
    [InlineData(Seat.Two, "4,3,1,1", Seat.One)]
    public void WhenBothMustCheckTheInitiativeHolderChoosesTheOrder(Seat first, string dice, Seat failing) // provisional R5
    {
        var play = new Play(Play.Setup(3, 3)).ToActivation();
        foreach (var id in new[] { "A1", "A2", "B1", "B2" })
        {
            play.SetStatus(id, ModelStatus.Down);
        }
        play.RunActivations();
        Assert.Equal(new PendingDecision(DecisionKind.ChooseMoraleOrder, Seat.One), play.State.Pending);
        Assert.Equal(ReasonCode.NotYourTurn, play.Try(new ChooseMoraleOrder(Seat.Two, Seat.Two)).Rejection!.Code);

        play.Do(new ChooseMoraleOrder(Seat.One, first), dice.Split(',').Select(int.Parse).ToArray());

        Assert.Equal(first, play.Log.OfType<MoraleCheckRolled>().First().Seat);
        Assert.Equal(new PendingDecision(DecisionKind.FailedMorale, failing), play.State.Pending);
    }

    [Fact]
    [Trait("Rule", "1.0.2 Morale Phase p.50")]
    public void TheFixedOrderProfilePutsTheInitiativeHolderFirstWithoutAsking() // provisional R5
    {
        var profile = new RulesProfile(MoraleOrder: MoraleOrderRule.InitiativeHolderFirst);
        var play = new Play(Play.Setup(3, 3, profile)).ToActivation();
        foreach (var id in new[] { "A1", "A2", "B1", "B2" })
        {
            play.SetStatus(id, ModelStatus.Down);
        }

        play.RunActivations(4, 3, 4, 3, 5, 2);  // two passing Morale Checks, then the turn-2 Initiative roll-off

        Assert.Equal([Seat.One, Seat.Two], play.Log.OfType<MoraleCheckRolled>().Select(c => c.Seat));
        Assert.Contains(play.Log, e => e is MoraleOrderSet { First: Seat.One, Chosen: false });
        Assert.Equal(2, play.State.Turn);
    }

    [Fact]
    [Trait("Rule", "1.0.2 Winning the Game p.51")]
    public void AFleeingWarbandEndsTheMatchBeforeTheOtherChecks()
    {
        var play = new Play(Play.Setup(3, 3)).ToActivation();
        foreach (var id in new[] { "A1", "A2", "B1", "B2" })
        {
            play.SetStatus(id, ModelStatus.Down);
        }
        play.State = play.State with { Two = play.State.Two with { Shaken = true } };
        play.RunActivations();

        var dice = new FixedDice(1, 1, 6, 6);
        var result = TurnSequence.Decide(play.State, new ChooseMoraleOrder(Seat.One, Seat.Two), dice);

        Assert.Equal(2, dice.Remaining);
        Assert.Equal(Seat.One, result.State!.Outcome!.Winner);
        Assert.Single(result.Events.OfType<MoraleCheckRolled>());
    }

    // ---------- Ending the turn and the match: pp. 50-51, Scenario I pp. 152-153 ----------

    [Fact]
    [Trait("Rule", "1.0.2 Ending The Turn p.50")]
    public void EarlierTurnsStartTheNextOne()
    {
        var play = new Play(Play.Setup(2, 2)).ToActivation();

        play.RunActivations(6, 1); // turn two's Initiative roll-off (equal counts)

        Assert.Contains(play.Log, e => e is TurnEnded { Turn: 1 });
        Assert.Equal(2, play.State.Turn);
        Assert.Equal(Phase.Initiative, play.State.Phase);
    }

    [Fact]
    [Trait("Rule", "1.0.2 Winning the Game p.51; Scenario I p.152")]
    public void TheFinalTurnEndsTheMatchWithTheWinnerLeftToTheScenarioScoring()
    {
        var play = new Play(Play.Setup(2, 3, turnLimit: 1)).ToActivation();

        play.RunActivations();

        Assert.Equal(Phase.Ended, play.State.Phase);
        Assert.Equal(new MatchOutcome(null, OutcomeReason.FinalTurnComplete), play.State.Outcome);
    }

    [Fact]
    [Trait("Rule", "1.0.2 Winning the Game p.51")]
    public void EveryCommandIsRejectedAfterTheMatchEnds()
    {
        var play = new Play(Play.Setup(2, 3, turnLimit: 1)).ToActivation();
        play.RunActivations();
        Command[] commands =
        [
            new StartMatch(),
            new ChooseDeploymentZone(Seat.One, "north"),
            new DeployModel(Seat.One, Play.A(1)),
            new ChooseFirstActivation(Seat.One, Seat.One),
            new ActivateModel(Seat.One, Play.A(1)),
            new EndActivation(Seat.One),
            new ResolveFailedMorale(Seat.One, MoraleFailureChoice.BecomeShaken),
            new ChooseMoraleOrder(Seat.One, Seat.One),
        ];

        Assert.All(commands, c => Assert.Equal(ReasonCode.MatchEnded, play.Try(c).Rejection!.Code));
        Assert.Empty(TurnSequence.LegalCommands(play.State));
    }

    // ---------- Match lifecycle ----------

    [Fact]
    [Trait("Rule", "1.0.2 The Sequence of Play p.32")]
    public void CommandsBeforeTheMatchIsCreatedOrStartedAreRejected()
    {
        Assert.Equal(ReasonCode.MatchNotCreated,
            TurnSequence.Decide(MatchState.None, new StartMatch(), new FixedDice()).Rejection!.Code);

        var play = new Play(Play.Setup(1, 1));
        Assert.Equal(ReasonCode.MatchNotStarted, play.Try(new ActivateModel(Seat.One, Play.A(1))).Rejection!.Code);

        play.Do(new StartMatch(), 6, 1);
        Assert.Equal(ReasonCode.WrongPhase, play.Try(new StartMatch(), 6, 1).Rejection!.Code);
    }

    [Fact]
    [Trait("Rule", "1.0.2 The Sequence of Play p.32")]
    public void AnAcceptedCommandReturnsTheEventsAndTheStateTheyProduce()
    {
        var play = new Play(Play.Setup(2, 2));
        var before = StateHash.Compute(play.State);

        var result = TurnSequence.Decide(play.State, new StartMatch(), new FixedDice(6, 1));

        Assert.Equal(before, StateHash.Compute(play.State));
        Assert.Equal(StateHash.Compute(result.State!), StateHash.Compute(Replay(play.State, result.Events)));
    }

    private static MatchState Replay(MatchState state, IEnumerable<GameEvent> events) =>
        events.Aggregate(state, Reducer.Apply);
}
