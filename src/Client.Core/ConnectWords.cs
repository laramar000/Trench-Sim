using System.Net.Sockets;
using System.Net.WebSockets;
using Match.Protocol;

namespace Client.Core;

// Main menu stage M2 (2026-10-28): what the Play and developer screens say when starting, joining or rejoining a match
// does not work. The server's refusals and the common connection failures are described in plain words, with the server's
// own text kept only where nothing better is known.
public static class ConnectWords
{
    // How long connecting and the server's first answer may take before the attempt is given up.
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public const string Connecting = "Connecting…";
    public const string Cancelled = "Cancelled. Nothing was joined.";

    // Null when the address can be used; otherwise what is wrong with it.
    public static string? AddressProblem(string text, out Uri? endpoint)
    {
        endpoint = null;
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return "Type the server address first, for example ws://127.0.0.1:5080/match.";
        }
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || uri.Host.Length == 0)
        {
            return $"\"{trimmed}\" is not a server address. It looks like ws://127.0.0.1:5080/match.";
        }
        if (uri.Scheme is not ("ws" or "wss"))
        {
            return $"The server address must start with ws:// (or wss://), not {uri.Scheme}://. For example ws://{uri.Authority}/match.";
        }
        endpoint = uri;
        return null;
    }

    // The server answered but did not seat this player.
    public static string Refusal(ServerMessage answer) => answer switch
    {
        Rejected r => Enum.TryParse<ProtocolError>(r.Code, out var code) ? Refusal(code, r.Text) : $"The server refused: {r.Text}",
        _ => $"The server sent an unexpected answer ({answer.GetType().Name}). Nothing was joined.",
    };

    public static string Refusal(ProtocolError code, string serverText) => code switch
    {
        ProtocolError.UnknownMatch => "No match with that code on this server. Check the code and the server address; a match that ended or a server that restarted no longer has it.",
        ProtocolError.MatchFull => "That match already has two players.",
        ProtocolError.MatchFinished => "That match has already ended.",
        ProtocolError.MatchAborted => "That match was stopped and can no longer be joined.",
        ProtocolError.ServerBusy => "The server has as many matches as it allows right now. Try again later.",
        ProtocolError.ShuttingDown => "The server is shutting down. Start it again, then retry.",
        ProtocolError.JoinTimeout => "The server stopped waiting for this window. Retry.",
        ProtocolError.UnknownScenario => "This server does not have that scenario. It may be running another version of Trenches.",
        ProtocolError.UnknownMap or ProtocolError.MapDoesNotFit => "This server does not have that map for this scenario. It may be running another version of Trenches.",
        ProtocolError.UnsupportedVersion => "This server runs another version of Trenches. Both players and the server need the same version.",
        // Roster stage 5c: the server's own words say what is wrong with the roster or which kind of match this is.
        ProtocolError.RosterRefused => $"Your roster was not accepted: {serverText}",
        ProtocolError.InvalidToken or ProtocolError.SeatMismatch or ProtocolError.SeatTakenOver =>
            "That saved seat is no longer valid on this server.",
        _ => $"The server refused ({code}): {serverText}",
    };

    // Connecting, or waiting for the first answer, failed. timedOutAfter: the attempt was given up after that long.
    public static string Failure(Exception error, TimeSpan? timedOutAfter = null)
    {
        if (timedOutAfter is { } after)
        {
            return $"The server did not answer within {after.TotalSeconds:0} seconds. Check that it is running and that the address is right, then retry.";
        }
        if (Find<SocketException>(error) is { } socket)
        {
            return socket.SocketErrorCode switch
            {
                SocketError.ConnectionRefused => "Nothing answered at that address. Is the server running, and is the port right?",
                SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain => "That server name was not found. Check the address.",
                SocketError.TimedOut => "The server could not be reached in time. Check the address and the network, then retry.",
                SocketError.NetworkUnreachable or SocketError.HostUnreachable => "That address cannot be reached from this PC. Check the address and the network.",
                SocketError.ConnectionReset or SocketError.ConnectionAborted => "The server closed the connection. Retry, or check that it is a Trenches server.",
                _ => $"Could not connect ({socket.SocketErrorCode}). Check the address, then retry.",
            };
        }
        if (Find<WebSocketException>(error) is { } web)
        {
            return web.WebSocketErrorCode switch
            {
                WebSocketError.NotAWebSocket or WebSocketError.UnsupportedProtocol =>
                    "Something answered at that address, but not a Trenches server of this version. Check the port, the /match path and the version.",
                WebSocketError.ConnectionClosedPrematurely => "The server closed the connection. Retry, or check that it is a Trenches server.",
                _ => $"Could not connect. Check the address, then retry. ({web.Message})",
            };
        }
        return $"Could not connect. Check the address, then retry. ({error.Message})";
    }

    private static T? Find<T>(Exception? error) where T : Exception
    {
        for (var e = error; e is not null; e = e.InnerException)
        {
            if (e is T found)
            {
                return found;
            }
        }
        return null;
    }
}
