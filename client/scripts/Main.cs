using Godot;
using Match.Protocol;
using Rules.Core.Scenarios;

// M7.1 bootstrap: proves the client project loads our .NET 10 libraries inside Godot. Later stages replace this
// screen with the connect screen, battlefield and HUD.
public partial class Main : Control
{
    public override void _Ready()
    {
        var text = new Label
        {
            Text = string.Join("\n",
                "Trench Sim client (M7.1 bootstrap)",
                $"Godot {Engine.GetVersionInfo()["string"]}, {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}",
                $"Rules version {ClaimNoMansLand.RulesVersion}; {typeof(Codec).Assembly.GetName().Name} loaded",
                "Nothing to play yet: connecting to a server arrives in M7.2."),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        text.AddThemeFontSizeOverride("font_size", 20);
        text.SetAnchorsPreset(LayoutPreset.FullRect);
        text.OffsetLeft = 24;
        text.OffsetTop = 24;
        AddChild(text);
    }
}
