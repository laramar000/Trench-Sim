using System.Net;
using System.Net.Sockets;
using System.Text;
using Client.Core;
using Match.Client;
using Match.Protocol;

namespace Client.Tests;

// Main menu stage M2: the plain words for a connection that does not work, checked against real failures where a test
// can make them (a closed port, something that is not a WebSocket server, a server that never answers).
public class ConnectWordsTests
{
    [Theory]
    [InlineData("", "Type the server address")]
    [InlineData("   ", "Type the server address")]
    [InlineData("127.0.0.1:5080", "is not a server address. It looks like ws://127.0.0.1:5080/match.")]
    [InlineData("hello there", "is not a server address")]
    [InlineData("http://127.0.0.1:5080/match", "must start with ws:// (or wss://), not http://. For example ws://127.0.0.1:5080/match.")]
    public void ABadAddressIsExplained(string text, string expected)
    {
        Assert.Contains(expected, ConnectWords.AddressProblem(text, out var endpoint), StringComparison.Ordinal);
        Assert.Null(endpoint);
    }

    [Theory]
    [InlineData("ws://127.0.0.1:5080/match")]
    [InlineData(" wss://example.org/match ")]
    public void AGoodAddressIsTaken(string text)
    {
        Assert.Null(ConnectWords.AddressProblem(text, out var endpoint));
        Assert.Equal(text.Trim(), endpoint!.ToString());
    }

    [Fact]
    public void EveryRefusalAPlayerCanMeetHasItsOwnWords()
    {
        foreach (var code in new[]
        {
            ProtocolError.UnknownMatch, ProtocolError.MatchFull, ProtocolError.MatchFinished, ProtocolError.MatchAborted,
            ProtocolError.ServerBusy, ProtocolError.ShuttingDown, ProtocolError.JoinTimeout, ProtocolError.UnknownScenario,
            ProtocolError.UnknownMap, ProtocolError.MapDoesNotFit, ProtocolError.UnsupportedVersion, ProtocolError.InvalidToken,
            ProtocolError.SeatMismatch, ProtocolError.SeatTakenOver,
        })
        {
            var words = ConnectWords.Refusal(Refusal(code, "server text"));
            Assert.DoesNotContain("server text", words, StringComparison.Ordinal);
            Assert.DoesNotContain(code.ToString(), words, StringComparison.Ordinal);
        }
        Assert.Equal("No match with that code on this server. Check the code and the server address; a match that ended or a server that restarted no longer has it.",
            ConnectWords.Refusal(Refusal(ProtocolError.UnknownMatch, "no such match")));
    }

    [Fact]
    public void ARefusedRosterSaysWhatTheServerFoundWrong()
    {
        Assert.Equal("Your roster was not accepted: This match is played with rosters: join with one of your saved rosters.",
            ConnectWords.Refusal(Refusal(ProtocolError.RosterRefused, "This match is played with rosters: join with one of your saved rosters.")));
    }

    [Fact]
    public void AnUnexpectedRefusalKeepsTheServersWords()
    {
        Assert.Equal("The server refused (Malformed): bad json", ConnectWords.Refusal(Refusal(ProtocolError.Malformed, "bad json")));
        Assert.Equal("The server refused: odd", ConnectWords.Refusal(Rejected("NotACode", "odd")));
    }

    [Fact]
    public async Task AClosedPortSaysNothingAnswered()
    {
        var error = await FailureOf(new Uri($"ws://127.0.0.1:{ClosedPort()}/match"));
        Assert.Equal("Nothing answered at that address. Is the server running, and is the port right?", ConnectWords.Failure(error));
    }

    [Fact]
    public async Task SomethingThatIsNotATrenchesServerIsSaid()
    {
        // A plain web server: it answers the handshake with 404.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var serving = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var buffer = new byte[4096];
            _ = await stream.ReadAsync(buffer);
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
        });
        var error = await FailureOf(new Uri($"ws://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/match"));
        await serving;
        Assert.StartsWith("Something answered at that address, but not a Trenches server", ConnectWords.Failure(error), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AServerThatNeverAnswersCanBeCancelledOrTimedOut()
    {
        await using var silent = new FakeServer(_ => FakeReply.NoHandshake);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ClientSession.ConnectAsync(silent.Endpoint, cancel.Token));
        Assert.StartsWith("The server did not answer within 30 seconds.", ConnectWords.Failure(new OperationCanceledException(), ConnectWords.Timeout), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownFailureKeepsItsMessage()
    {
        Assert.Equal("Could not connect. Check the address, then retry. (boom)", ConnectWords.Failure(new IOException("boom")));
    }

    private static Rejected Refusal(ProtocolError code, string text) => Rejected(code.ToString(), text);

    private static Rejected Rejected(string code, string text) => new(null, null, RejectionKind.Protocol, code, text, null, 0, 0, false);

    private static int ClosedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<Exception> FailureOf(Uri endpoint)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var error = await Record.ExceptionAsync(() => ClientSession.ConnectAsync(endpoint, timeout.Token));
        Assert.NotNull(error);
        return error;
    }
}
