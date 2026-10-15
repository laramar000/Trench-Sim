using Godot;
using Match.Client;
using Match.Protocol;
using Rules.Core.Scenarios;

// M7.2: the client loads our .NET 10 libraries inside Godot and can reach a server through Match.Client. Later stages
// replace this screen with the connect screen, battlefield and HUD.
// Headless check: godot --headless --path client -- --smoke-server ws://127.0.0.1:5080/match
public partial class Main : Control
{
    private Label _text = null!;

    public override async void _Ready()
    {
        _text = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _text.AddThemeFontSizeOverride("font_size", 20);
        _text.SetAnchorsPreset(LayoutPreset.FullRect);
        _text.OffsetLeft = 24;
        _text.OffsetTop = 24;
        AddChild(_text);
        Show(
            "Trench Sim client (M7.2: shared client library)",
            $"Godot {Engine.GetVersionInfo()["string"]}, {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}",
            $"Rules version {ClaimNoMansLand.RulesVersion}; protocol {ClientSession.SubProtocol}",
            "Nothing to play yet: the match screen arrives in M7.3.");

        var args = OS.GetCmdlineUserArgs();
        var at = Array.IndexOf(args, "--smoke-server");
        if (at >= 0 && at + 1 < args.Length)
        {
            Show(await Smoke(new Uri(args[at + 1])));
            GetTree().Quit();
        }
    }

    // Creates a match on the server and reports the Welcome. The session's receive loop runs on its own task; awaits here
    // resume on Godot's main thread, so only the main thread touches the scene.
    private static async Task<string> Smoke(Uri server)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var session = await ClientSession.ConnectAsync(server, timeout.Token);
            var answer = await session.CreateAsync("ranged", timeout.Token);
            if (answer is not Welcome welcome)
            {
                return $"SMOKE FAILED: {answer.GetType().Name}";
            }
            await session.WaitForAsync(s => s.Synchronized, timeout.Token);
            return $"SMOKE OK: Welcome seat={welcome.Seat} status={welcome.Status} events={session.Mirror.Events} hash={session.Mirror.Hash[..12]} synchronized={session.Synchronized}";
        }
        catch (Exception e)
        {
            return $"SMOKE FAILED: {e.GetType().Name}: {e.Message}";
        }
    }

    private void Show(params string[] lines)
    {
        var text = string.Join("\n", lines);
        GD.Print(text);
        _text.Text = _text.Text.Length == 0 ? text : _text.Text + "\n" + text;
    }
}
