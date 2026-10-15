using System.Globalization;
using Match.Cli;
using Match.Protocol;
using Rules.Core.Commands;
using Sim;

// trench-cli [--server ws://127.0.0.1:5080/match] create <scenario> [--bot <seed>]
// trench-cli [--server ws://127.0.0.1:5080/match] join <code> [--bot <seed>]
// trench-cli [--server ws://127.0.0.1:5080/match] rejoin <code> [--seat One|Two] [--bot <seed>]
// trench-cli verify <record.json> [--key <public.pem>]... [--content <folder>]
const string Usage = "usage: trench-cli [--server ws://host:port/match] (create <ranged|melee> | join <code> | rejoin <code> [--seat One|Two]) [--bot <seed>]\n" +
    "       trench-cli verify <record.json> [--key <public.pem>]... [--content <folder>]";
if (args.Length > 0 && args[0] == "verify")
{
    return Verify.Run(args[1..], Console.Out, Console.Error);
}

var server = new Uri("ws://127.0.0.1:5080/match");
string? action = null, argument = null;
int? botSeed = null;
Rules.Core.State.Seat? seatChoice = null;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--server" when i + 1 < args.Length:
            server = new Uri(args[++i]);
            break;
        case "--seat" when i + 1 < args.Length && Enum.TryParse<Rules.Core.State.Seat>(args[i + 1], out var chosen) && Enum.IsDefined(chosen):
            seatChoice = chosen;
            i++;
            break;
        case "--bot" when i + 1 < args.Length:
            botSeed = int.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        case "create" or "join" or "rejoin" when i + 1 < args.Length && action is null:
            action = args[i];
            argument = args[++i];
            break;
        default:
            Console.Error.WriteLine(Usage);
            return 2;
    }
}
if (action is null)
{
    Console.Error.WriteLine(Usage);
    return 2;
}

var store = new SessionStore(SessionStore.DefaultDirectory());
SavedSeat? saved = null;
if (action == "rejoin")
{
    var lookup = store.Find(server.ToString(), argument!, seatChoice);
    switch (lookup.Status)
    {
        case SavedSeatStatus.Missing:
            Console.Error.WriteLine($"No saved seat for match {argument} on {server} in {store.Directory}.");
            return 1;
        case SavedSeatStatus.Corrupt:
            Console.Error.WriteLine($"The saved seat {lookup.Path} cannot be read; it was left as it is.");
            return 1;
        case SavedSeatStatus.Ambiguous:
            Console.Error.WriteLine($"Both seats of match {argument} are saved here; add --seat One or --seat Two.");
            return 1;
        case SavedSeatStatus.Insecure:
            Console.Error.WriteLine($"{lookup.Path} can be read by other users; fix its permissions (chmod 600, folder 700) before using it.");
            return 1;
    }
    saved = lookup.Seat!;
}

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Cancel();
};

await using var session = await ClientSession.ConnectAsync(server, stop.Token);
// Each new token is saved before it is confirmed. If it cannot be saved, it is not confirmed, and the server keeps the
// previous one valid; the player is warned that a crash would then lose the seat.
session.SaveToken = welcome =>
{
    if (store.TrySave(new SavedSeat(server.ToString(), welcome.Code, welcome.MatchId, welcome.Seat, welcome.SeatToken!), out var problem))
    {
        return true;
    }
    Console.Error.WriteLine($"Warning: {problem} You cannot rejoin this match after a crash.");
    return false;
};
session.Received += message =>
{
    switch (message)
    {
        case EventPublished e when Narrator.Describe(e.Event) is { } line:
            Console.WriteLine($"  [{e.Seq}] {line}");
            break;
        case Notice n:
            Console.WriteLine($"** {n.Kind}");
            break;
        case MatchPaused p:
            Console.WriteLine($"** Seat {p.Seat} lost its connection. The match is paused until it rejoins (by {p.RejoinBy:HH:mm:ss} UTC).");
            break;
        case MatchResumed r:
            Console.WriteLine($"** Seat {r.Seat} is back. Play goes on.");
            break;
        case MatchAborted a:
            Console.WriteLine($"** Match aborted ({a.Reason}{(a.Seat is { } s ? $", Seat {s}" : "")}). No winner. Final hash {a.Hash[..12]} at event {a.Events}.");
            break;
    }
};

var answer = action switch
{
    "create" => await session.CreateAsync(argument!, stop.Token),
    "join" => await session.JoinAsync(argument!, stop.Token),
    _ => await session.RejoinAsync(saved!.Code, saved.Token, stop.Token),
};
if (answer is not Welcome welcome)
{
    Console.Error.WriteLine($"Refused: {(answer as Rejected)?.Code} {(answer as Rejected)?.Text}");
    if (saved is not null && answer is Rejected { Code: nameof(ProtocolError.UnknownMatch) })
    {
        // The match is gone (it ended, or the server restarted): the saved seat is no use any more.
        store.Delete(saved.MatchId, saved.Seat);
    }
    return 1;
}
if (welcome.LastAnswer is { } last)
{
    Console.WriteLine($"Your last command before the drop: {(last is Accepted ? "accepted" : "refused")}. Next command number {welcome.NextSeq}.");
}
Console.WriteLine($"Match {welcome.Code} ({welcome.Scenario}). You are Seat {welcome.Seat}." +
    (welcome.Seat == Rules.Core.State.Seat.One ? $" Give the code {welcome.Code} to your opponent." : ""));

try
{
    if (botSeed is { } seed)
    {
        await BotPlayer.PlayAsync(session, seed, stop.Token, Console.WriteLine);
        await session.WaitForAsync(s => s.Aborted is not null || (s.Synchronized && s.Mirror.State.Phase == Rules.Core.State.Phase.Ended) || s.IsClosed, stop.Token);
    }
    else
    {
        await Interactive(session, stop.Token);
    }
}
catch (OperationCanceledException)
{
}
catch (InvalidOperationException e)
{
    Console.Error.WriteLine(e.Message);
}

Console.WriteLine(Board.Render(session.Mirror.State, session.Seat));
Console.WriteLine($"{session.Mirror.Events} events, every one matched the server's hash. Final hash {session.Mirror.Hash}.");
if (session.Aborted is not null || session.Mirror.State.Phase == Rules.Core.State.Phase.Ended)
{
    // The match is over: the saved seat has nothing left to rejoin.
    store.Delete(welcome.MatchId, welcome.Seat);
}
if (session.Failure is not null)
{
    Console.Error.WriteLine($"Replay failed: {session.Failure}");
    return 1;
}
await session.CloseAsync(CancellationToken.None);
return 0;

static async Task Interactive(ClientSession session, CancellationToken cancellationToken)
{
    Console.WriteLine("Type 'help' for commands, 'board' for the table, 'quit' to leave.");
    while (!cancellationToken.IsCancellationRequested && !session.IsClosed && session.Aborted is null)
    {
        Console.Write("> ");
        var line = await Task.Run(Console.ReadLine, cancellationToken);
        if (line is null || line.Trim() == "quit")
        {
            return;
        }
        switch (line.Trim())
        {
            case "":
                continue;
            case "help":
                Console.WriteLine(CommandParser.Help);
                continue;
            case "board":
                Console.WriteLine(Board.Render(session.Mirror.State, session.Seat));
                continue;
            case "legal":
                foreach (var legal in Rules.Core.TurnSequence.LegalCommands(session.Mirror.State).Where(c => CommandSeats.Of(c) == session.Seat))
                {
                    Console.WriteLine("  " + BotPlayer.Describe(legal));
                }
                continue;
        }
        Command command;
        try
        {
            command = CommandParser.Parse(line, session.Seat!.Value);
        }
        catch (Exception e) when (e is FormatException or ArgumentException or OverflowException)
        {
            Console.WriteLine(e.Message);
            continue;
        }
        var answer = await session.SubmitAsync(command, cancellationToken);
        Console.WriteLine(answer switch
        {
            Accepted a => $"ok (events {a.FirstEvent}-{a.LastEvent})",
            Rejected r => $"refused: {r.Code}: {r.Text}{(r.RuleRef is null ? "" : $" ({r.RuleRef})")}",
            _ => answer.ToString(),
        });
    }
}
