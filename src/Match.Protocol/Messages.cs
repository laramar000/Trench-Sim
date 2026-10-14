using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.State;

namespace Match.Protocol;

// Protocol version 1. Every message is one JSON object, {"v":1,"type":"<class name>", ...fields}, sent as one WebSocket
// text message. Clients send commands only; dice, outcomes, events and hashes come from the server.

// ---- Client to server ----

public abstract record ClientMessage;

// Create a match of a named scenario. The sender takes Seat One and receives the match code to give the opponent.
public sealed record CreateMatch : ClientMessage
{
    public CreateMatch(string scenario)
    {
        Scenario = Ids.Check(scenario, nameof(scenario));
    }

    public string Scenario { get; }
}

// Join a match by its code. The sender takes the seat still free; nobody chooses a seat.
public sealed record JoinMatch : ClientMessage
{
    public JoinMatch(string code)
    {
        Code = Ids.Check(code, nameof(code));
    }

    public string Code { get; }
}

// A command for the sender's own seat. Seq is the seat's command number, one more than the last one the server counted.
// KnownEvents is how many events the client has applied: a command built on an older state is refused as stale. A
// resend with the same CommandId and the same seq and command gets the original answer again and is not re-applied.
public sealed record SubmitCommand : ClientMessage
{
    public SubmitCommand(string commandId, long seq, int knownEvents, Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        CommandId = Ids.Check(commandId, nameof(commandId));
        Seq = seq >= 1 ? seq : throw new ArgumentOutOfRangeException(nameof(seq), "Command numbers start at 1.");
        KnownEvents = knownEvents >= 0 ? knownEvents : throw new ArgumentOutOfRangeException(nameof(knownEvents));
        Command = command;
    }

    public string CommandId { get; }
    public long Seq { get; }
    public int KnownEvents { get; }
    public Command Command { get; }
}

// ---- Server to client ----

public abstract record ServerMessage;

// The sender is in the match, in this seat. The match's events so far follow, in order.
public sealed record Welcome(string Code, Seat Seat, string Scenario) : ServerMessage;

// One event of the match, numbered from 1, with the state hash after applying it. Sent to both seats in log order.
public sealed record EventPublished(int Seq, GameEvent Event, string Hash) : ServerMessage;

// The command was accepted; its events (FirstEvent to LastEvent) have already been sent. Duplicate marks a resend.
public sealed record Accepted(string CommandId, long Seq, int FirstEvent, int LastEvent, bool Duplicate) : ServerMessage;

// The message was refused. Nothing changed and no dice were rolled. A Rules refusal consumed its seq (the next command
// is NextSeq); a Protocol refusal did not. CommandId and Seq are absent when the message could not be read.
public sealed record Rejected(
    string? CommandId,
    long? Seq,
    RejectionKind Kind,
    string Code,
    string Text,
    string? RuleRef,
    int Events,
    long NextSeq,
    bool Duplicate) : ServerMessage;

public sealed record Notice(NoticeKind Kind) : ServerMessage;

public enum RejectionKind
{
    // The message broke the protocol (ProtocolError names why).
    Protocol,

    // The rules refused the command (Rules.Core ReasonCode names why).
    Rules,
}

public enum ProtocolError
{
    Malformed,
    InvalidValue,
    UnknownType,
    UnsupportedVersion,
    TooLarge,
    NotJoined,
    AlreadyJoined,
    UnknownScenario,
    UnknownMatch,
    MatchFull,
    ServerBusy,
    MatchNotStarted,
    MatchClosed,
    SeatMismatch,
    ServerOnlyCommand,
    IdReused,
    OutOfOrder,
    Stale,
    ShuttingDown,
}

public enum NoticeKind
{
    OpponentJoined,
    OpponentDisconnected,

    // The server stopped this match after an internal error. The log sent so far is still consistent.
    MatchFaulted,
    ServerShutdown,
}

// Codes, scenario names and command ids: 1 to 64 printable ASCII characters, no spaces.
public static class Ids
{
    public static string Check(string value, string name)
    {
        ArgumentNullException.ThrowIfNull(value, name);
        if (value.Length is < 1 or > ProtocolLimits.MaxIdLength || value.Any(c => c is < '!' or > '~'))
        {
            throw new ArgumentException($"{name} must be 1 to {ProtocolLimits.MaxIdLength} printable ASCII characters.", name);
        }
        return value;
    }
}
