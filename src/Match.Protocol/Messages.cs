using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.State;

namespace Match.Protocol;

// Protocol version 2. Every message is one JSON object, {"v":2,"type":"<class name>", ...fields}, sent as one WebSocket
// text message. Clients send commands only; dice, outcomes, events and hashes come from the server.

// ---- Client to server ----

public abstract record ClientMessage;

// Create a match of a named scenario. The sender takes Seat One and receives the match code to give the opponent.
// Map (trench-maps stage S5, optional, left off when null): play the scenario's models on another of the server's
// maps instead of the one the name comes with. The map must be made for the scenario and fit what it needs.
public sealed record CreateMatch : ClientMessage
{
    private readonly string? _map;

    public CreateMatch(string scenario)
    {
        Scenario = Ids.Check(scenario, nameof(scenario));
    }

    public string Scenario { get; }

    public string? Map
    {
        get => _map;
        init => _map = value is null ? null : Ids.Check(value, nameof(Map));
    }

    // Roster stage 5c (optional, left off when null): the text of the sender's saved roster file, with the scenario
    // "roster". The server checks it itself and plays it as Seat One; the match is set up once the second player joins
    // with a roster of their own.
    public string? Roster
    {
        get => _roster;
        init => _roster = RosterText.Check(value, nameof(Roster));
    }

    private readonly string? _roster;
}

// Join a match by its code. The sender takes the seat still free; nobody chooses a seat.
// Roster (stage 5c, optional, left off when null): the sender's saved roster, needed to join a match made with one.
public sealed record JoinMatch : ClientMessage
{
    private readonly string? _roster;

    public JoinMatch(string code)
    {
        Code = Ids.Check(code, nameof(code));
    }

    public string Code { get; }

    public string? Roster
    {
        get => _roster;
        init => _roster = RosterText.Check(value, nameof(Roster));
    }
}

// A roster sent in CreateMatch or JoinMatch is the text of a roster file; the message size limit bounds it.
public static class RosterText
{
    public const string Scenario = "roster";

    public static string? Check(string? roster, string name) =>
        roster is null || roster.Length > 0 ? roster : throw new ArgumentException("A roster is the text of a roster file.", name);
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

// Takes a seat back with its seat token, after the connection was lost. KnownEvents and KnownHash say how far the
// client's copy got (0 and null for a fresh copy); the server sends only the events after that, if the copy is a true
// prefix of the match, and otherwise everything from event 1.
public sealed record Rejoin : ClientMessage
{
    public Rejoin(string code, string seatToken, int knownEvents, string? knownHash)
    {
        Code = Ids.Check(code, nameof(code));
        SeatToken = SeatTokens.Check(seatToken, nameof(seatToken));
        KnownEvents = knownEvents >= 0 ? knownEvents : throw new ArgumentOutOfRangeException(nameof(knownEvents));
        if ((knownEvents == 0) != (knownHash is null))
        {
            throw new ArgumentException("A known hash is given exactly when known events is above 0.", nameof(knownHash));
        }
        KnownHash = knownHash is null ? null : Ids.Check(knownHash, nameof(knownHash));
    }

    public string Code { get; }
    public string SeatToken { get; }
    public int KnownEvents { get; }
    public string? KnownHash { get; }
}

// The client has durably saved the seat token from its last Welcome. Only then does the server stop accepting the
// token before it. No answer is sent unless the message is refused.
public sealed record TokenSaved : ClientMessage;

// ---- Server to client ----

public abstract record ServerMessage;

// The sender holds this seat. The events from ReplayFrom + 1 to Events follow, in order; the client's copy is in step
// once it holds Events events and its hash is Hash. ReplayFrom 0 means the client must start again from an empty copy.
// SeatToken is the seat's new token (null on a read-only rejoin to a match that is over). NextSeq is the seat's next
// command number and LastAnswer its most recent answer, so a client that lost an answer learns what happened.
public sealed record Welcome : ServerMessage
{
    public Welcome(
        string code,
        string matchId,
        Seat seat,
        string scenario,
        string? seatToken,
        int replayFrom,
        int events,
        string hash,
        long nextSeq,
        ServerMessage? lastAnswer,
        MatchStatus status)
    {
        Code = Ids.Check(code, nameof(code));
        MatchId = Ids.Check(matchId, nameof(matchId));
        Seat = seat;
        Scenario = Ids.Check(scenario, nameof(scenario));
        SeatToken = seatToken is null ? null : SeatTokens.Check(seatToken, nameof(seatToken));
        Events = events >= 1 ? events : throw new ArgumentOutOfRangeException(nameof(events));
        ReplayFrom = replayFrom >= 0 && replayFrom <= events ? replayFrom : throw new ArgumentOutOfRangeException(nameof(replayFrom));
        Hash = Ids.Check(hash, nameof(hash));
        NextSeq = nextSeq >= 1 ? nextSeq : throw new ArgumentOutOfRangeException(nameof(nextSeq));
        LastAnswer = lastAnswer is null or Accepted or Rejected { Kind: RejectionKind.Rules }
            ? lastAnswer
            : throw new ArgumentException("The last answer is an Accepted or a rules Rejected.", nameof(lastAnswer));
        Status = status;
    }

    public string Code { get; }
    public string MatchId { get; }
    public Seat Seat { get; }
    public string Scenario { get; }
    public string? SeatToken { get; }
    public int ReplayFrom { get; }
    public int Events { get; }
    public string Hash { get; }
    public long NextSeq { get; }
    public ServerMessage? LastAnswer { get; }
    public MatchStatus Status { get; }

    // Roster stage 5c (optional, left off when null): the names of a match played with rosters sent online, in the
    // warband content format (both warbands, Seat One first), so each player sees the models' names. Names only: the
    // models' profiles are in the setup, and a client uses the names only if they match it.
    public string? Warbands { get; init; }
}

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

// A seat's connection was lost. The match is paused: nothing can happen until that seat rejoins, and if it has not by
// RejoinBy the match is aborted (RejoinExpired).
public sealed record MatchPaused(Seat Seat, DateTimeOffset RejoinBy) : ServerMessage;

// The seat rejoined and no seat is away: play goes on.
public sealed record MatchResumed(Seat Seat) : ServerMessage;

// The match stopped before the rules ended it. This is not a rules result: the match has no winner and no draw, and its
// log ends without MatchEnded. Events and Hash are the log length and state hash at the stop, for the client to confirm
// its copy. Seat is the seat that disconnected, when that is the reason.
public sealed record MatchAborted(AbortReason Reason, Seat? Seat, int Events, string Hash) : ServerMessage;

public enum AbortReason
{
    // A player's connection closed or failed, and the server does not wait for a rejoin.
    Disconnected,

    // A player's connection was lost and the seat was not taken back before its rejoin deadline.
    RejoinExpired,

    // The server stopped the match after an internal error.
    Faulted,
    ServerShutdown,
}

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
    MatchAborted,
    SeatMismatch,
    ServerOnlyCommand,
    IdReused,
    OutOfOrder,
    Stale,
    ShuttingDown,
    JoinTimeout,
    InvalidToken,
    MatchPaused,
    MatchFinished,
    SeatTakenOver,

    // Trench-maps stage S5: no map with that id on this server, or it does not fit the scenario.
    UnknownMap,
    MapDoesNotFit,

    // Roster stage 5c: the roster sent cannot be played (the text says why), a match made with rosters was joined
    // without one, or one made with the practice warbands was joined with one.
    RosterRefused,
}

// Where a match is, as Welcome reports it.
public enum MatchStatus
{
    // Created, waiting for the second player.
    Waiting,
    Playing,

    // A seat's connection was lost; nothing happens until it rejoins or its deadline passes.
    Paused,

    // The rules ended the match (MatchEnded). Nothing more can happen.
    Finished,

    // Stopped before the rules ended it: no winner, no draw, and the log ends without MatchEnded.
    Aborted,
}

public enum NoticeKind
{
    OpponentJoined,

    // The opponent left a match that had already ended by the rules; the result stands.
    OpponentDisconnected,

    // The server is stopping. A match in progress gets MatchAborted instead.
    ServerShutdown,
}

public static class SeatTokens
{
    public static string Check(string value, string name)
    {
        ArgumentNullException.ThrowIfNull(value, name);
        if (value.Length != ProtocolLimits.SeatTokenLength || !value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            throw new ArgumentException($"{name} is {ProtocolLimits.SeatTokenLength} base64url characters.", name);
        }
        return value;
    }
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
