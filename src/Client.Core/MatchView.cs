using Rules.Core;
using Rules.Core.State;

namespace Client.Core;

public sealed record SeatView(Seat Seat, string Label, string Warband, int VictoryPoints, bool Shaken, bool Fled, string? Zone, bool IsYou)
{
    // Every fact as words, so nothing depends on colour.
    public string Text => $"{Label}{(IsYou ? " (you)" : "")}: {Warband}. VP {VictoryPoints}" +
        $"{(Shaken ? ", Shaken" : "")}{(Fled ? ", Fled" : "")}{(Zone is null ? "" : $", zone {Zone}")}.";
}

public sealed record ModelView(
    ModelId Id,
    string Name,
    Seat Seat,
    string SeatLetter,
    ModelStatus Status,
    bool Deployed,
    bool Activated,
    bool Active,
    int BloodMarkers)
{
    public string Text => $"[{SeatLetter}] {Name}: {Words.Status(Status)}" +
        $"{(Active ? ", ACTIVE" : Activated ? ", activated this turn" : "")}" +
        $"{(Deployed ? "" : ", not deployed")}" +
        $"{(BloodMarkers > 0 ? $", {BloodMarkers} Blood Marker{(BloodMarkers == 1 ? "" : "s")}" : "")}";
}

// Everything the match screen shows, built only from the server's state as the copy of the match holds it after
// hash-checked events. Nothing here is predicted; rebuilt after every message.
public sealed record MatchView(
    string Status,
    string Turn,
    string Phase,
    string WhoseMove,
    bool YourMove,
    IReadOnlyList<SeatView> Seats,
    IReadOnlyList<ModelView> Models,
    string? Activation,
    string? Pending,
    string? Roll,
    string? Outcome,
    string? NamesProblem)
{
    public static MatchView Of(MatchState state, Seat? you, ContentNames names)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(names);
        var setup = state.Setup;
        var turn = setup is null ? "" : state.Turn == 0 ? $"Turn limit {setup.TurnLimit}" : $"Turn {state.Turn} of {setup.TurnLimit}";
        var (whose, yours) = Whose(state, you);
        var seats = new[] { Seat.One, Seat.Two }.Select(s =>
        {
            var seat = state.SeatFor(s);
            return new SeatView(s, $"{Words.Seat(s)} [{Words.SeatLetter(s)}]", names.WarbandOf(s), seat.VictoryPoints, seat.Shaken, seat.Fled, seat.ZoneId, s == you);
        }).ToList();
        var models = state.Models.Select(m => new ModelView(
            m.Id, names.Of(m.Id), m.Seat, Words.SeatLetter(m.Seat), m.Status, m.Deployed, m.ActivatedThisTurn, state.ActiveModel == m.Id, m.BloodMarkers)).ToList();
        var activation = state.ActiveModel is { } active
            ? $"{names.Of(active)} is activating. Done so far: {Words.Actions(state.Actions)}{(state.MovementHalved ? "; Movement halved" : "")}."
            : null;
        var pending = state.Pending is { } p ? $"{(p.Seat == you ? "You" : Words.Seat(p.Seat))} must {Words.Decision(p.Kind)}." : null;
        var roll = state.InProgress is { } r
            ? $"Roll in progress: {r.Stage} by {names.Of(r.Actor)}{(r.Target is { } t ? $" against {names.Of(t)}" : "")}" +
              $"{(r.BonusDice > 0 ? $", +{r.BonusDice} dice" : "")}{(r.PenaltyDice > 0 ? $", -{r.PenaltyDice} dice" : "")}."
            : null;
        var outcome = state.Outcome is { } o ? Words.Outcome(o, you) : null;
        var status = state.Phase switch
        {
            Rules.Core.State.Phase.None => "No match yet.",
            Rules.Core.State.Phase.Ended => outcome ?? "The match has ended.",
            _ => $"{Words.Phase(state.Phase)}. {whose}",
        };
        return new MatchView(status, turn, Words.Phase(state.Phase), whose, yours, seats, models, activation, pending, roll, outcome, names.Problem);
    }

    // Whose move it is, in words: the seat that must decide, else the seat of the active model, else the seat that
    // deploys or activates next, else the server is resolving (for example between phases).
    private static (string Text, bool Yours) Whose(MatchState state, Seat? you)
    {
        Seat? seat = state.Phase switch
        {
            Rules.Core.State.Phase.None or Rules.Core.State.Phase.Ended => null,
            _ when state.Pending is { } p => p.Seat,
            _ when state.ActiveModel is { } a => state.FindModel(a)?.Seat,
            Rules.Core.State.Phase.Deployment => NextDeployer(state),
            Rules.Core.State.Phase.Activation => TurnSequence.NextActivator(state),
            _ => null,
        };
        if (seat is not { } s)
        {
            return (state.Phase switch
            {
                Rules.Core.State.Phase.None => "",
                Rules.Core.State.Phase.Ended => "Nobody: the match has ended.",
                Rules.Core.State.Phase.PreGame => "Waiting for the server to start the match.",
                _ => "Waiting for the server.",
            }, false);
        }
        return s == you ? ("Your move.", true) : ($"Opponent's move ({Words.Seat(s)}).", false);
    }

    // Who places the next model, for display only: deployment alternates from the seat that deploys first, and a side
    // with nothing left lets the other continue (rulebook p. 152). Rules.Core keeps its own version internal; a test
    // checks this one against the order of ModelDeployed events in simulated matches. Never used to decide anything.
    public static Seat? NextDeployer(MatchState state)
    {
        bool Undeployed(Seat s) => state.Models.Any(m => m.Seat == s && !m.Deployed);
        if (state.DeploymentFirst is not { } first)
        {
            return null;
        }
        if (state.LastDeployer is not { } last)
        {
            return Undeployed(first) ? first : Undeployed(first.Opponent()) ? first.Opponent() : null;
        }
        return Undeployed(last.Opponent()) ? last.Opponent() : Undeployed(last) ? last : null;
    }

    public IEnumerable<string> Lines()
    {
        yield return Status;
        if (Turn.Length > 0)
        {
            yield return $"{Turn}. Phase: {Phase}.";
        }
        foreach (var seat in Seats)
        {
            yield return seat.Text;
        }
        if (Pending is not null)
        {
            yield return Pending;
        }
        if (Activation is not null)
        {
            yield return Activation;
        }
        if (Roll is not null)
        {
            yield return Roll;
        }
        if (NamesProblem is not null)
        {
            yield return NamesProblem;
        }
    }
}
