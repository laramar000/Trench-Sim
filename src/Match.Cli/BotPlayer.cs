using Match.Protocol;
using Rules.Core.Commands;
using Rules.Core.State;
using Sim;

namespace Match.Cli;

// Plays one seat with the simulator's bot. It only ever proposes; the server decides. When none of its commands for its
// own seat is accepted (it is the other player's turn, or its view was stale) it waits for the next event.
public static class BotPlayer
{
    public static async Task PlayAsync(ClientSession session, int seed, CancellationToken cancellationToken = default, Action<string>? log = null)
    {
        var bot = new Bot(new Random(seed));
        var seat = session.Seat ?? throw new InvalidOperationException("Create or join a match first.");
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (session.Aborted is not null || session.Failure is not null || session.IsClosed || session.Mirror.State.Phase == Phase.Ended)
            {
                return;
            }
            if (session.IsPaused)
            {
                await session.WaitForAsync(s => !s.IsPaused || s.Aborted is not null || s.IsClosed, cancellationToken);
                continue;
            }
            var seen = session.Mirror.Events;
            var accepted = false;
            foreach (var command in Propose(bot, session.Mirror.State).Where(c => CommandSeats.Of(c) == seat))
            {
                var answer = await session.SubmitAsync(command, cancellationToken);
                if (answer is Accepted)
                {
                    log?.Invoke($"{seat}: {Describe(command)}");
                    accepted = true;
                    break;
                }
                if (answer is Rejected { Kind: RejectionKind.Protocol, Code: nameof(ProtocolError.MatchPaused) })
                {
                    // Nothing can happen until the other seat rejoins (or the match is aborted).
                    await session.WaitForAsync(s => !s.IsPaused || s.Aborted is not null || s.IsClosed, cancellationToken);
                    accepted = true;
                    break;
                }
                if (answer is Rejected { Kind: RejectionKind.Protocol, Code: nameof(ProtocolError.SeatTakenOver) or nameof(ProtocolError.MatchFinished) or nameof(ProtocolError.MatchAborted) })
                {
                    return;
                }
                if (answer is Rejected { Kind: RejectionKind.Protocol } || session.Mirror.Events != seen)
                {
                    // Out of date or refused by the protocol: look again at the newer state.
                    accepted = true;
                    break;
                }
            }
            if (!accepted)
            {
                await session.WaitForAsync(s => s.Mirror.Events != seen || s.Aborted is not null || s.IsClosed, cancellationToken);
            }
        }
    }

    private static List<Command> Propose(Bot bot, MatchState state)
    {
        try
        {
            return bot.Propose(state).ToList();
        }
        catch (InvalidOperationException)
        {
            return [];
        }
    }

    public static string Describe(Command command) => command.GetType().Name + " " + string.Join(" ",
        command.GetType().GetProperties().Where(p => p.Name is not ("Seat" or "EqualityContract"))
            .Select(p => $"{p.Name}={Format(p.GetValue(command))}"));

    private static string Format(object? value) => value switch
    {
        System.Collections.IEnumerable list and not string => "[" + string.Join(" ", list.Cast<object>()) + "]",
        _ => value?.ToString() ?? "-",
    };
}
