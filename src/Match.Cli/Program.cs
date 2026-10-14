using System.Globalization;
using Match.Cli;
using Match.Protocol;
using Rules.Core.Commands;
using Sim;

// trench-cli [--server ws://127.0.0.1:5080/match] create <scenario> [--bot <seed>]
// trench-cli [--server ws://127.0.0.1:5080/match] join <code> [--bot <seed>]
var server = new Uri("ws://127.0.0.1:5080/match");
string? action = null, argument = null;
int? botSeed = null;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--server" when i + 1 < args.Length:
            server = new Uri(args[++i]);
            break;
        case "--bot" when i + 1 < args.Length:
            botSeed = int.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        case "create" or "join" when i + 1 < args.Length && action is null:
            action = args[i];
            argument = args[++i];
            break;
        default:
            Console.Error.WriteLine("usage: trench-cli [--server ws://host:port/match] (create <ranged|melee> | join <code>) [--bot <seed>]");
            return 2;
    }
}
if (action is null)
{
    Console.Error.WriteLine("usage: trench-cli [--server ws://host:port/match] (create <ranged|melee> | join <code>) [--bot <seed>]");
    return 2;
}

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Cancel();
};

await using var session = await ClientSession.ConnectAsync(server, stop.Token);
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
        case MatchAborted a:
            Console.WriteLine($"** Match aborted ({a.Reason}{(a.Seat is { } s ? $", Seat {s}" : "")}). No winner. Final hash {a.Hash[..12]} at event {a.Events}.");
            break;
    }
};

var answer = action == "create" ? await session.CreateAsync(argument!, stop.Token) : await session.JoinAsync(argument!, stop.Token);
if (answer is not Welcome welcome)
{
    Console.Error.WriteLine($"Refused: {(answer as Rejected)?.Code} {(answer as Rejected)?.Text}");
    return 1;
}
Console.WriteLine($"Match {welcome.Code} ({welcome.Scenario}). You are Seat {welcome.Seat}." +
    (welcome.Seat == Rules.Core.State.Seat.One ? $" Give the code {welcome.Code} to your opponent." : ""));

try
{
    if (botSeed is { } seed)
    {
        await BotPlayer.PlayAsync(session, seed, stop.Token, Console.WriteLine);
        await session.WaitForAsync(s => s.Aborted is not null || s.Mirror.State.Phase == Rules.Core.State.Phase.Ended || s.IsClosed, stop.Token);
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
