using Match.Protocol;
using Rules.Core.State;

namespace Client.Core;

// Single-player (hot-seat) mode: one person plays both seats of a match from one window. It is two ordinary seats on
// the authoritative server: Seat One creates the match and Seat Two joins it, each through its own MatchController
// (its own connection, command numbers, saved state and reconnects). The screen shows one of them at a time, the
// active seat, and every command goes through that seat's controller, so the server checks it like any other. Nothing
// here decides anything: the server's state says whose move it is, and switching only changes which seat's screen is
// shown. Switching waits while a command is on its way, so an answer always lands on the seat that sent the command.
public sealed class HotSeat : IAsyncDisposable
{
    private readonly LocalServer? _server;
    private int _disposed;

    // The two screens' versions when the shown seat last changed: following the turn reacts only to news from the
    // server after that, so a switch by hand is not undone at once.
    private (long One, long Two) _seenAtSwitch = (-1, -1);

    private HotSeat(MatchController one, MatchController two, LocalServer? server)
    {
        One = one;
        Two = two;
        _server = server;
    }

    public MatchController One { get; }

    public MatchController Two { get; }

    public LocalServer? Server => _server;

    public Seat Active { get; private set; } = Seat.One;

    public MatchController Controller => Active == Seat.One ? One : Two;

    public MatchController Other => Active == Seat.One ? Two : One;

    public Seat OtherSeat => Active.Opponent();

    // Switch to the seat that must act when the shown one has nothing to do (default on).
    public bool FollowTurn { get; set; } = true;

    // Something else that is waiting for the server's answer to a command of the active seat (the battlefield's send).
    public Func<bool>? Busy { get; set; }

    // Raised when the active seat changes.
    public event Action? Switched;

    // Creates the match as Seat One and joins it as Seat Two. On failure everything started here is stopped again.
    public static async Task<HotSeat> StartAsync(Func<MatchController> newController, string scenario, LocalServer? server,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(newController);
        var one = newController();
        MatchController? two = null;
        try
        {
            if (await one.CreateAsync(scenario, cancellationToken) is not Welcome welcome)
            {
                throw new LocalServerException("The server did not create the match.");
            }
            two = newController();
            if (await two.JoinAsync(welcome.Code, cancellationToken) is not Welcome)
            {
                throw new LocalServerException("The second seat could not join the match.");
            }
            return new HotSeat(one, two, server);
        }
        catch
        {
            await one.DisposeAsync();
            if (two is not null)
            {
                await two.DisposeAsync();
            }
            if (server is not null)
            {
                await server.DisposeAsync();
            }
            throw;
        }
    }

    // Why the seat cannot be switched now, or null.
    public string? SwitchBlocked() =>
        Controller.Snapshot().Feedback?.Kind == FeedbackKind.Sending || Busy?.Invoke() == true
            ? "Waiting for the server's answer to the command just sent; the seat switches after it."
            : null;

    // Shows the other seat (or the given one). False when it is already shown or a command is on its way.
    public bool Switch(Seat? to = null)
    {
        var target = to ?? Active.Opponent();
        if (target == Active || SwitchBlocked() is not null)
        {
            return false;
        }
        Active = target;
        _seenAtSwitch = (One.Snapshot().Version, Two.Snapshot().Version);
        Switched?.Invoke();
        return true;
    }

    // With FollowTurn: when the shown seat has nothing to do and the other seat must act, show the other seat. Only
    // after something changed since the last switch (a switch by hand stays until the match moves on).
    public bool FollowIfNeeded()
    {
        if (!FollowTurn)
        {
            return false;
        }
        var shown = Controller.Snapshot();
        var other = Other.Snapshot();
        var versions = Active == Seat.One ? (shown.Version, other.Version) : (other.Version, shown.Version);
        if (versions == _seenAtSwitch)
        {
            return false;
        }
        return !shown.View.YourMove && other.View.YourMove && Switch();
    }

    // The hot-seat line for the top of the screen: which seat is controlled, whose move it is, and how to switch.
    public string Banner()
    {
        var shown = Controller.Snapshot();
        var other = Other.Snapshot();
        var whose = shown.View.YourMove ? $"{Words.Seat(Active)} to move."
            : other.View.YourMove ? $"{Words.Seat(Active.Opponent())} to move."
            : shown.View.WhoseMove;
        var follow = FollowTurn ? "the seat follows the turn" : "switching is by hand";
        return $"HOT-SEAT: you control {Words.Seat(Active)} [{Words.SeatLetter(Active)}]. {(whose.Length == 0 ? "" : whose + " ")}" +
            $"Switch to {Words.Seat(Active.Opponent())} with F2 ({follow}).";
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }
        await One.DisposeAsync();
        await Two.DisposeAsync();
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }
    }

    // For a window that is closing and cannot await: stops the local server at once (the connections then close).
    public void StopServerNow() => _server?.Stop();
}
