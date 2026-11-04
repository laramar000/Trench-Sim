using System.Globalization;
using System.Net;
using Match.Server;

// trench-server [--address 127.0.0.1] [--port 5080] [--join-timeout 30] [--rejoin-window 300] [--data <folder>] [--new-signing-key]
// [--warbands <file>]: roster stage 5b, also offer the scenario "roster" with the two warbands of that content file.
const string Usage = "usage: trench-server [--address 127.0.0.1] [--port 5080] [--join-timeout 30] [--rejoin-window 300] [--data <folder>] [--new-signing-key] [--warbands <file>]";
var address = IPAddress.Loopback;
var port = 5080;
var joinTimeout = TimeSpan.FromSeconds(30);
var rejoinWindow = Lobby.DefaultRejoinWindow;
var data = ServerData.DefaultDirectory();
var newKey = false;
string? warbands = null;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--address" when i + 1 < args.Length:
            address = IPAddress.Parse(args[++i]);
            break;
        case "--port" when i + 1 < args.Length:
            port = int.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        case "--join-timeout" when i + 1 < args.Length:
            joinTimeout = TimeSpan.FromSeconds(int.Parse(args[++i], CultureInfo.InvariantCulture));
            break;
        case "--rejoin-window" when i + 1 < args.Length:
            rejoinWindow = TimeSpan.FromSeconds(int.Parse(args[++i], CultureInfo.InvariantCulture));
            break;
        case "--data" when i + 1 < args.Length:
            data = args[++i];
            break;
        case "--new-signing-key":
            newKey = true;
            break;
        case "--warbands" when i + 1 < args.Length:
            warbands = args[++i];
            break;
        default:
            Console.Error.WriteLine(Usage);
            return 2;
    }
}

MatchServer server;
try
{
    server = await MatchServer.StartAsync(new MatchServerOptions
    {
        Address = address,
        Port = port,
        JoinTimeout = joinTimeout,
        RejoinWindow = rejoinWindow,
        DataDirectory = data,
        NewSigningKey = newKey,
        Scenarios = warbands is null ? null : ScenarioCatalog.WithRosterMatch(warbands),
        Log = line => Console.WriteLine($"[{DateTimeOffset.UtcNow:HH:mm:ss}] {line}"),
    });
}
catch (ServerDataException e)
{
    Console.Error.WriteLine($"Cannot start: {e.Message}");
    return 1;
}

await using (server)
{
    var key = server.SigningKey!;
    Console.WriteLine($"Records in {Path.GetFullPath(data)}. Signing key {key.KeyId}{(key.Created ? " (new)" : "")}.");
    if (key.Created)
    {
        Console.WriteLine("Give players this public key so they can verify records (trench-cli verify --key <file>):");
        Console.Write(key.PublicPem);
        Console.WriteLine();
    }
    var recovery = server.Recovery!;
    if (recovery.Unrecorded.Count + recovery.BadRecords.Count + recovery.TempFilesRemoved + recovery.BadMarkers > 0)
    {
        Console.WriteLine($"Recovered from an earlier stop: {recovery.Unrecorded.Count} match(es) recorded as Unrecorded, " +
            $"{recovery.BadRecords.Count} bad record(s) set aside, {recovery.TempFilesRemoved} temporary file(s) removed.");
    }
    Console.WriteLine($"Listening on {server.Endpoint} (protocol {MatchServer.SubProtocol}). Ctrl+C stops.");
    await server.WaitForShutdownAsync();
}
Console.WriteLine("Stopped.");
return 0;
