using System.Globalization;
using System.Net;
using Match.Server;

// trench-server [--address 127.0.0.1] [--port 5080] [--join-timeout 30]
var address = IPAddress.Loopback;
var port = 5080;
var joinTimeout = TimeSpan.FromSeconds(30);
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
        default:
            Console.Error.WriteLine("usage: trench-server [--address 127.0.0.1] [--port 5080] [--join-timeout 30]");
            return 2;
    }
}

await using var server = await MatchServer.StartAsync(new MatchServerOptions { Address = address, Port = port, JoinTimeout = joinTimeout });
Console.WriteLine($"Listening on {server.Endpoint} (protocol {MatchServer.SubProtocol}). Ctrl+C stops.");
await server.WaitForShutdownAsync();
Console.WriteLine("Stopped.");
return 0;
