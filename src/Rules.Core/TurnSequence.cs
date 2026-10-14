using System.Collections.Immutable;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.State;

namespace Rules.Core;

// The outcome of Decide: either the events the command caused (and the state after them), or a Rejection.
public sealed class DecideResult
{
    private DecideResult(ImmutableArray<GameEvent> events, MatchState? state, Rejection? rejection)
    {
        Events = events;
        State = state;
        Rejection = rejection;
    }

    public bool IsAccepted => Rejection is null;
    public ImmutableArray<GameEvent> Events { get; }
    public MatchState? State { get; }
    public Rejection? Rejection { get; }

    internal static DecideResult Accepted(IEnumerable<GameEvent> events, MatchState state) =>
        new(events.ToImmutableArray(), state, null);

    internal static DecideResult Rejected(Rejection rejection) => new([], null, rejection);
}

// The turn sequence (rulebook 1.0.2 pp. 32-33, 50-51, Core pp. 14, 20-21, Scenario I pp. 151-153): decides what a
// command does to a match. It checks the command against the state, rolls any dice the sequence needs
// (roll-offs, Morale Checks) from the injected source, and returns the events. It never changes a state itself:
// every change goes through Reducer.Apply. A rejected command rolls no dice and returns no events.
public static class TurnSequence
{
    private const string RefTurn = "1.0.2 p. 32";
    private const string RefActivation = "1.0.2 p. 33";
    private const string RefDeployment = "1.0.2 p. 152";
    private const string RefMorale = "1.0.2 p. 50";
    private const string RefWinning = "1.0.2 p. 51";
    private const int MaxRollOffRounds = 1000;

    public static Seat? NextActivator(MatchState state) => TurnRules.NextActivator(state);

    public static DecideResult Decide(MatchState state, Command command, IDiceSource dice)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(dice);

        if (Validate(state, command) is { } rejection)
        {
            return DecideResult.Rejected(rejection);
        }

        var run = new Run(state);
        Execute(run, command, dice);
        while (Advance(run, dice))
        {
        }
        return DecideResult.Accepted(run.Events, run.State);
    }

    // Every command that would be accepted in this state, in a fixed order.
    public static IReadOnlyList<Command> LegalCommands(MatchState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var commands = new List<Command>();
        switch (state.Phase)
        {
            case Phase.PreGame:
                commands.Add(new StartMatch());
                break;
            case Phase.None or Phase.Ended:
                break;
            default:
                if (state.Pending is { } pending)
                {
                    AddDecisionCommands(state, pending, commands);
                }
                else if (state.Phase == Phase.Deployment && TurnRules.NextDeployer(state) is { } deployer)
                {
                    commands.AddRange(TurnRules.ModelsOf(state, deployer).Where(m => !m.Deployed).Select(m => new DeployModel(deployer, m.Id)));
                }
                else if (state.Phase == Phase.Activation)
                {
                    if (state.ActiveModel is { } active)
                    {
                        commands.Add(new EndActivation(state.FindModel(active)!.Seat));
                    }
                    else if (TurnRules.NextActivator(state) is { } next)
                    {
                        commands.AddRange(TurnRules.ModelsOf(state, next).Where(TurnRules.CanActivate).Select(m => new ActivateModel(next, m.Id)));
                    }
                }
                break;
        }
        return commands;
    }

    private static void AddDecisionCommands(MatchState state, PendingDecision pending, List<Command> commands)
    {
        switch (pending.Kind)
        {
            case DecisionKind.ChooseDeploymentZone:
                commands.AddRange(state.Setup!.ZoneIds.Select(z => new ChooseDeploymentZone(pending.Seat, z)));
                break;
            case DecisionKind.ChooseFirstActivation:
                commands.Add(new ChooseFirstActivation(pending.Seat, Seat.One));
                commands.Add(new ChooseFirstActivation(pending.Seat, Seat.Two));
                break;
            case DecisionKind.ChooseMoraleOrder:
                commands.Add(new ChooseMoraleOrder(pending.Seat, Seat.One));
                commands.Add(new ChooseMoraleOrder(pending.Seat, Seat.Two));
                break;
            case DecisionKind.FailedMorale:
                commands.Add(new ResolveFailedMorale(pending.Seat, MoraleFailureChoice.BecomeShaken));
                commands.Add(new ResolveFailedMorale(pending.Seat, MoraleFailureChoice.SoundRetreat));
                break;
        }
    }

    // ---- validation: reasons a player can read; nothing here rolls dice or changes anything ----

    private static Rejection? Validate(MatchState state, Command command)
    {
        if (state.Phase == Phase.None)
        {
            return new(ReasonCode.MatchNotCreated, "No match has been created.", RefTurn);
        }
        if (state.Phase == Phase.Ended)
        {
            return new(ReasonCode.MatchEnded, "The match has ended.", RefWinning);
        }
        if (command is StartMatch)
        {
            return state.Phase == Phase.PreGame ? null : new(ReasonCode.WrongPhase, "The match has already started.", RefTurn);
        }
        if (state.Phase == Phase.PreGame)
        {
            return new(ReasonCode.MatchNotStarted, "The match has not been started.", RefTurn);
        }

        var expected = ExpectedDecision(command);
        if (expected is { } kind)
        {
            if (state.Pending is not { } pending)
            {
                return new(ReasonCode.NoDecisionPending, "There is no decision to make right now.", RefTurn);
            }
            if (pending.Kind != kind)
            {
                return new(ReasonCode.WrongDecision, $"A different decision is pending ({pending.Kind}).", RefTurn);
            }
            if (pending.Seat != SeatOf(command))
            {
                return new(ReasonCode.NotYourTurn, "That decision belongs to the other player.", RefTurn);
            }
            return command is ChooseDeploymentZone z && !state.Setup!.ZoneIds.Contains(z.ZoneId)
                ? new(ReasonCode.UnknownZone, "That is not one of the scenario's deployment zones.", RefDeployment)
                : null;
        }

        if (state.Pending is { } waiting)
        {
            return new(ReasonCode.DecisionPending, $"A decision must be made first ({waiting.Kind}).", RefTurn);
        }

        return command switch
        {
            DeployModel c => ValidateDeploy(state, c),
            ActivateModel c => ValidateActivate(state, c),
            EndActivation c => ValidateEnd(state, c),
            _ => throw new ArgumentException($"Unknown command {command.GetType().Name}.", nameof(command)),
        };
    }

    private static Rejection? ValidateDeploy(MatchState state, DeployModel c)
    {
        if (state.Phase != Phase.Deployment)
        {
            return new(ReasonCode.WrongPhase, "Models are deployed only during Deployment.", RefDeployment);
        }
        if (TurnRules.NextDeployer(state) != c.Seat)
        {
            return new(ReasonCode.NotYourTurn, "It is the other player's turn to deploy.", RefDeployment);
        }
        var model = state.FindModel(c.Model);
        if (model is null)
        {
            return new(ReasonCode.UnknownModel, "There is no such model.", RefDeployment);
        }
        if (model.Seat != c.Seat)
        {
            return new(ReasonCode.NotYourModel, "That model belongs to the other player.", RefDeployment);
        }
        return model.Deployed ? new(ReasonCode.ModelAlreadyDeployed, "That model is already deployed.", RefDeployment) : null;
    }

    private static Rejection? ValidateActivate(MatchState state, ActivateModel c)
    {
        if (state.Phase != Phase.Activation)
        {
            return new(ReasonCode.WrongPhase, "Models activate only during the Activation Phase.", RefActivation);
        }
        if (state.ActiveModel is not null)
        {
            return new(ReasonCode.ActivationInProgress, "A model is already activating; end its activation first.", RefActivation);
        }
        if (TurnRules.NextActivator(state) != c.Seat)
        {
            return new(ReasonCode.NotYourTurn, "It is the other player's turn to activate.", RefActivation);
        }
        var model = state.FindModel(c.Model);
        if (model is null)
        {
            return new(ReasonCode.UnknownModel, "There is no such model.", RefActivation);
        }
        if (model.Seat != c.Seat)
        {
            return new(ReasonCode.NotYourModel, "That model belongs to the other player.", RefActivation);
        }
        if (model.Status == ModelStatus.OutOfAction)
        {
            return new(ReasonCode.ModelOutOfAction, "That model is Out of Action.", RefActivation);
        }
        return model.ActivatedThisTurn
            ? new(ReasonCode.ModelAlreadyActivated, "That model has already activated this turn.", RefActivation)
            : null;
    }

    private static Rejection? ValidateEnd(MatchState state, EndActivation c)
    {
        if (state.Phase != Phase.Activation)
        {
            return new(ReasonCode.WrongPhase, "Activations happen only during the Activation Phase.", RefActivation);
        }
        if (state.ActiveModel is not { } active)
        {
            return new(ReasonCode.NoActivationInProgress, "No model is activating.", RefActivation);
        }
        return state.FindModel(active)!.Seat == c.Seat
            ? null
            : new(ReasonCode.NotYourTurn, "That is the other player's activation.", RefActivation);
    }

    private static DecisionKind? ExpectedDecision(Command command) => command switch
    {
        ChooseDeploymentZone => DecisionKind.ChooseDeploymentZone,
        ChooseFirstActivation => DecisionKind.ChooseFirstActivation,
        ChooseMoraleOrder => DecisionKind.ChooseMoraleOrder,
        ResolveFailedMorale => DecisionKind.FailedMorale,
        _ => null,
    };

    private static Seat SeatOf(Command command) => command switch
    {
        ChooseDeploymentZone c => c.Seat,
        ChooseFirstActivation c => c.Seat,
        ChooseMoraleOrder c => c.Seat,
        ResolveFailedMorale c => c.Seat,
        _ => throw new ArgumentException("Command has no seat."),
    };

    // ---- executing an accepted command ----

    private static void Execute(Run run, Command command, IDiceSource dice)
    {
        switch (command)
        {
            case StartMatch:
                var zoneRoll = RollOff(dice, RollOffPurpose.DeploymentZone);
                run.Emit(zoneRoll);
                run.Emit(new PhaseEntered(Phase.Deployment));
                run.Emit(new DecisionRequested(DecisionKind.ChooseDeploymentZone, zoneRoll.Winner));
                break;
            case ChooseDeploymentZone c:
                run.Emit(new DeploymentZoneChosen(c.Seat, c.ZoneId));
                break;
            case DeployModel c:
                run.Emit(new ModelDeployed(c.Seat, c.Model));
                break;
            case ChooseFirstActivation c:
                run.Emit(new FirstActivationChosen(c.First));
                break;
            case ActivateModel c:
                run.Emit(new ActivationStarted(c.Seat, c.Model));
                break;
            case EndActivation:
                run.Emit(new ActivationEnded(run.State.ActiveModel!.Value, ActivationEndReason.Voluntary));
                break;
            case ChooseMoraleOrder c:
                run.Emit(new MoraleOrderSet(c.First, Chosen: true));
                break;
            case ResolveFailedMorale { Choice: MoraleFailureChoice.BecomeShaken } c:
                run.Emit(new WarbandShaken(c.Seat));
                break;
            case ResolveFailedMorale c:
                run.Emit(new WarbandFled(c.Seat, FledReason.SoundedRetreat));
                run.Emit(new MatchEnded(c.Seat.Opponent(), OutcomeReason.OpponentFled));
                break;
        }
    }

    // ---- automatic steps: run until a player has to act, a decision is pending, or the match ends ----

    private static bool Advance(Run run, IDiceSource dice)
    {
        var s = run.State;
        if (s.Pending is not null)
        {
            return false;
        }

        switch (s.Phase)
        {
            case Phase.Deployment:
                return AdvanceDeployment(run, s, dice);
            case Phase.Initiative:
                return AdvanceInitiative(run, s, dice);
            case Phase.Activation:
                if (s.ActiveModel is not null || TurnRules.NextActivator(s) is not null)
                {
                    return false;
                }
                run.Emit(new PhaseEntered(Phase.Morale));
                return true;
            case Phase.Morale:
                return AdvanceMorale(run, s, dice);
            default:
                return false;
        }
    }

    private static bool AdvanceDeployment(Run run, MatchState s, IDiceSource dice)
    {
        if (s.DeploymentFirst is null)
        {
            // More models deploys first; equal counts roll off (p. 152). Provisional rule R7: the roll-off winner starts.
            var one = TurnRules.WarbandSize(s, Seat.One);
            var two = TurnRules.WarbandSize(s, Seat.Two);
            if (one != two)
            {
                run.Emit(new DeploymentOrderSet(one > two ? Seat.One : Seat.Two));
            }
            else
            {
                var roll = RollOff(dice, RollOffPurpose.DeploymentOrder);
                run.Emit(roll);
                run.Emit(new DeploymentOrderSet(roll.Winner));
            }
            return true;
        }
        if (s.Models.All(m => m.Deployed))
        {
            run.Emit(new TurnStarted(1));
            return true;
        }
        return false;
    }

    private static bool AdvanceInitiative(Run run, MatchState s, IDiceSource dice)
    {
        if (s.InitiativeHolder is null)
        {
            var one = TurnRules.InitiativeCount(s, Seat.One);
            var two = TurnRules.InitiativeCount(s, Seat.Two);
            Seat holder;
            if (one != two)
            {
                holder = one < two ? Seat.One : Seat.Two;
            }
            else
            {
                var roll = RollOff(dice, RollOffPurpose.Initiative);
                run.Emit(roll);
                holder = roll.Winner;
            }
            run.Emit(new InitiativeDetermined(one, two, holder));
            run.Emit(new DecisionRequested(DecisionKind.ChooseFirstActivation, holder));
            return true;
        }
        if (s.FirstActivator is not null)
        {
            run.Emit(new PhaseEntered(Phase.Activation));
            return true;
        }
        return false;
    }

    private static bool AdvanceMorale(Run run, MatchState s, IDiceSource dice)
    {
        if (s.TurnEnded)
        {
            if (s.Turn >= s.Setup!.TurnLimit)
            {
                // The winner comes from the scenario's Victory Points (pp. 152-153), which M4 will score.
                run.Emit(new MatchEnded(null, OutcomeReason.FinalTurnComplete));
            }
            else
            {
                run.Emit(new TurnStarted(s.Turn + 1));
            }
            return true;
        }

        var remaining = TurnRules.MoraleRemaining(s);
        if (remaining.Count == 0)
        {
            // End-of-turn tasks would happen here (p. 50); M2 has none.
            run.Emit(new TurnEnded(s.Turn));
            return true;
        }
        if (remaining.Count == 2 && s.MoraleFirst is null)
        {
            if (s.Setup!.Profile.MoraleOrder == MoraleOrderRule.InitiativeHolderChooses)
            {
                run.Emit(new DecisionRequested(DecisionKind.ChooseMoraleOrder, s.InitiativeHolder!.Value));
            }
            else
            {
                run.Emit(new MoraleOrderSet(s.InitiativeHolder!.Value, Chosen: false));
            }
            return true;
        }

        var seat = remaining.Count == 2 ? s.MoraleFirst!.Value : remaining[0];
        var wasShaken = s.SeatFor(seat).Shaken;
        // Provisional rule R3: a Morale Check is a plain Success Roll with no modifiers (pp. 21, 50).
        var check = SuccessRoll.Roll(dice);
        run.Emit(new MoraleCheckRolled(seat, check, TurnRules.DownOrOut(s, seat), TurnRules.WarbandSize(s, seat), wasShaken));
        if (check.Outcome != SuccessOutcome.Failure)
        {
            if (wasShaken)
            {
                run.Emit(new WarbandRecovered(seat));
            }
        }
        else if (wasShaken)
        {
            run.Emit(new WarbandFled(seat, FledReason.FailedWhileShaken));
            run.Emit(new MatchEnded(seat.Opponent(), OutcomeReason.OpponentFled));
        }
        else
        {
            // Sounding the Retreat is the player's choice (Comprehensive p. 50 box).
            run.Emit(new DecisionRequested(DecisionKind.FailedMorale, seat));
        }
        return true;
    }

    // Each round: seat One rolls, then seat Two. A tie is rolled again (p. 31).
    private static RollOffResolved RollOff(IDiceSource dice, RollOffPurpose purpose)
    {
        var rounds = new List<RollOffRound>();
        for (var i = 0; i < MaxRollOffRounds; i++)
        {
            var round = new RollOffRound(Roll(dice), Roll(dice));
            rounds.Add(round);
            if (!round.IsTie)
            {
                return new RollOffResolved(purpose, rounds, round.SeatOne > round.SeatTwo ? Seat.One : Seat.Two);
            }
        }
        throw new InvalidOperationException("The dice source kept rolling ties.");
    }

    private static int Roll(IDiceSource dice)
    {
        var value = dice.RollD6();
        return value is < 1 or > 6 ? throw new InvalidOperationException($"Dice source returned {value}; a D6 must be 1 to 6.") : value;
    }

    private sealed class Run(MatchState state)
    {
        public MatchState State { get; private set; } = state;
        public List<GameEvent> Events { get; } = [];

        public void Emit(GameEvent e)
        {
            State = Reducer.Apply(State, e);
            Events.Add(e);
        }
    }
}
