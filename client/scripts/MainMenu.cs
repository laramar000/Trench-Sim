using Client.Core;
using Godot;
using Match.Client;
using Rules.Core.Scenarios;

// Stage M1 (main menu, 2026-10-28): the screens before a match. The main menu (Play, the roster placeholder, Settings,
// Credits, Exit), the Play screen (hot-seat, host, join, rejoin), Settings, Credits, and a small corner button that
// opens the developer screen (the connect screen as it was, with every scenario and test roster). They only lay out
// the same calls the connect screen made; nothing about matches, the server or the rules changes.
public partial class Main
{
    public const string GameName = "Trenches";
    public const string BackText = "Back";
    public const string PlayText = "Play";
    public const string RosterText = "Build Your Own Roster · Work in progress";
    public const string RosterNoteText = "Building your own roster is planned. It is not available yet.";
    public const string SettingsText = "Settings";
    public const string CreditsText = "Credits";
    public const string ExitText = "Exit";
    public const string StartHotSeatText = "Start hot-seat";
    public const string HostText = "Host match";
    public const string DeveloperTip = "Developer screen: every scenario and test roster, server settings and saved seats.";

    // Stage M2: Message, with Cancel (while connecting) and Retry (after a failure that trying again can fix), is MessageRow.
    private sealed record ConnectForm(LineEdit Server, SpinBox Window, OptionButton Scenario, OptionButton Map, LineEdit Code, VBoxContainer Saved, Label Message,
        Button Cancel, Button Retry, HBoxContainer MessageRow);

    public const string CancelText = "Cancel";
    public const string RetryText = "Retry";

    // The last attempt of each screen, for its Retry.
    private readonly Dictionary<ConnectForm, Action> _retry = [];

    private ConnectForm _devForm = null!;
    private ConnectForm _playForm = null!;
    private VBoxContainer _home = null!;
    private VBoxContainer _play = null!;
    private VBoxContainer _settingsPage = null!;
    private VBoxContainer _credits = null!;
    private Button _playButton = null!;
    private Label _rosterNote = null!;
    private Button _developer = null!;
    private Button _playHotSeat = null!;
    private Control _advanced = null!;

    private static string VersionLine =>
        $"{GameName} client. Godot {Engine.GetVersionInfo()["string"]}, {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}. " +
        $"Rules version {ClaimNoMansLand.RulesVersion}; protocol {ClientSession.SubProtocol}.";

    // The connection fields of one screen; its scenario list refreshes its own map list.
    private ConnectForm NewForm(IEnumerable<string> scenarios)
    {
        var scenario = new OptionButton();
        foreach (var choice in scenarios)
        {
            scenario.AddItem(ScenarioLabel(choice));
            scenario.SetItemMetadata(scenario.ItemCount - 1, choice);
        }
        var form = new ConnectForm(
            new LineEdit { Text = "ws://127.0.0.1:5080/match", CustomMinimumSize = new Vector2(420, 0) },
            new SpinBox { MinValue = 10, MaxValue = 86_400, Step = 10, Value = ReconnectPolicy.DefaultGiveUpAfter.TotalSeconds },
            scenario,
            new OptionButton { Name = "MapChoice" },
            new LineEdit { PlaceholderText = "match code", CustomMinimumSize = new Vector2(160, 0) },
            new VBoxContainer(),
            Text(""),
            new Button { Text = CancelText, Name = "CancelConnecting", Visible = false },
            new Button { Text = RetryText, Name = "RetryConnecting", Visible = false },
            new HBoxContainer());
        form.Message.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        form.Message.CustomMinimumSize = new Vector2(420, 0);
        form.Message.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        form.MessageRow.AddThemeConstantOverride("separation", 8);
        form.MessageRow.AddChild(form.Message);
        form.MessageRow.AddChild(form.Cancel);
        form.MessageRow.AddChild(form.Retry);
        form.Cancel.Pressed += CancelConnecting;
        form.Retry.Pressed += () =>
        {
            if (_retry.TryGetValue(form, out var again))
            {
                again();
            }
        };
        scenario.ItemSelected += _ => Use(form, () => RefreshMaps(null))();
        var before = _form;
        _form = form;
        RefreshMaps(null);
        _form = before;
        return form;
    }

    // Runs an action with that screen's fields as the ones in use (what Create, Join, Rejoin and hot-seat read).
    private Action Use(ConnectForm form, Action act) => () =>
    {
        _form = form;
        act();
    };

    private void BuildMenuScreens()
    {
        // ---- main menu ----
        _home = new VBoxContainer { Name = "MainMenu", SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        _home.AddThemeConstantOverride("separation", 12);
        var title = Text(GameName.ToUpperInvariant());
        title.Name = "SectionTitle";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        _headings.Add(title);
        _home.AddChild(title);
        _home.AddChild(new HSeparator());
        Button MenuItem(string text, Action pressed)
        {
            var button = Button(text, pressed);
            button.CustomMinimumSize = new Vector2(340, 0);
            return button;
        }
        _playButton = MenuItem(PlayText, ShowPlay);
        _playButton.Name = "PlayButton";
        _home.AddChild(_playButton);
        // Jason's choice 1 (greyed item): it looks unavailable, and pressing it only says so.
        var roster = MenuItem(RosterText, () => _rosterNote.Visible = true);
        roster.Name = "RosterButton";
        roster.Modulate = new Color(1, 1, 1, 0.55f);
        roster.TooltipText = RosterNoteText;
        _home.AddChild(roster);
        _rosterNote = Text(RosterNoteText);
        _rosterNote.Name = "RosterNote";
        _rosterNote.CustomMinimumSize = new Vector2(340, 0);
        _rosterNote.Visible = false;
        _home.AddChild(_rosterNote);
        _home.AddChild(MenuItem(SettingsText, ShowSettings));
        _home.AddChild(MenuItem(CreditsText, ShowCredits));
        _home.AddChild(MenuItem(ExitText, () => GetTree().Quit()));
        _root.AddChild(_home);

        // The developer screen's door: a small plain button in the window's bottom-right corner, on the main menu only.
        _developer = new Button { Name = "DeveloperButton", Text = "·", Flat = true, FocusMode = FocusModeEnum.All, TooltipText = DeveloperTip };
        _developer.Pressed += ShowConnect;
        _developer.SetAnchorsPreset(LayoutPreset.BottomRight);
        _developer.GrowHorizontal = GrowDirection.Begin;
        _developer.GrowVertical = GrowDirection.Begin;
        _developer.OffsetRight = -4;
        _developer.OffsetBottom = -4;
        AddChild(_developer);

        // ---- Play ----
        var normal = ContentNames.Choices.Where(c => !c.Contains("v1", StringComparison.Ordinal) && !c.Contains("v2", StringComparison.Ordinal)
            && !c.Contains("v3", StringComparison.Ordinal));
        var form = NewForm(normal);
        _playForm = form;
        _play = new VBoxContainer { Name = "PlayScreen" };
        _play.AddThemeConstantOverride("separation", 8);
        _play.AddChild(Row(Button(BackText, ShowHome)));
        _play.AddChild(Section("Play", Row(Text("Scenario:"), form.Scenario, Text("Map:"), form.Map)));
        _playHotSeat = Button(StartHotSeatText, Use(form, StartHotSeat));
        var hotNote = Text("Play both sides in this window. A local server starts on this PC; F2 switches the side you control.");
        if (LocalServer.FindRepository(ProjectSettings.GlobalizePath("res://")) is null)
        {
            // Hot-seat runs the server from the Trench Sim source folder; without it the button cannot work.
            _playHotSeat.Disabled = true;
            hotNote.Text = "Not available here: hot-seat starts the server from the Trench Sim source folder (TrenchSim.sln above client/), which was not found.";
        }
        _play.AddChild(Section("This PC (hot-seat)", Column(Row(_playHotSeat), hotNote)));
        _play.AddChild(Section("Host or join a match", Column(
            Text("Needs a running Trench Sim server that both players can reach."),
            Row(Text("Server address:"), form.Server),
            Row(Button(HostText, Use(form, () => Create(ChosenScenario()))), Text("Uses the scenario and map above; give your opponent the match code shown next.")),
            Row(Text("Match code:"), form.Code, Button("Join match", Use(form, Join))))));
        _play.AddChild(Section("Rejoin", Column(
            Text("Your saved seats on this server:"),
            form.Saved,
            Button("Refresh saved seats", Use(form, RefreshSaved)))));
        var advancedToggle = new CheckButton { Text = "Advanced", Name = "AdvancedToggle" };
        _advanced = Column(Row(Text("Server rejoin window (seconds):"), form.Window),
            Text("Set this to the server's --rejoin-window (300 by default). It decides how long reconnecting keeps trying before offering Rejoin now."));
        _advanced.Visible = false;
        advancedToggle.Toggled += on => _advanced.Visible = on;
        _play.AddChild(advancedToggle);
        _play.AddChild(_advanced);
        _play.AddChild(form.MessageRow);
        _root.AddChild(_play);

        // ---- Settings (the same controls as the in-match Menu, moved here while shown) ----
        _settingsPage = new VBoxContainer { Name = "SettingsScreen" };
        _settingsPage.AddThemeConstantOverride("separation", 8);
        _settingsPage.AddChild(Row(Button(BackText, ShowHome)));
        _settingsPage.AddChild(Section("Settings", Text("Saved on this PC and used in every match.")));
        _root.AddChild(_settingsPage);

        // ---- Credits ----
        _credits = new VBoxContainer { Name = "CreditsScreen" };
        _credits.AddThemeConstantOverride("separation", 8);
        _credits.AddChild(Row(Button(BackText, ShowHome)));
        var lines = Column();
        foreach (var (heading, text) in Credits.Entries)
        {
            lines.AddChild(Text($"{heading}: {text}"));
        }
        lines.AddChild(Text(VersionLine));
        _credits.AddChild(Section("Credits", lines));
        _root.AddChild(_credits);
    }

    private static VBoxContainer Column(params Control[] controls)
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        foreach (var control in controls)
        {
            column.AddChild(control);
        }
        return column;
    }

    // Every screen hidden (a match, or before one is shown).
    private void HideScreens()
    {
        foreach (var screen in new Control[] { _home, _play, _settingsPage, _credits, _connect })
        {
            screen.Visible = false;
        }
        _developer.Visible = false;
    }

    private void ShowScreen(Control screen)
    {
        // Stage M2: leaving a screen gives up its attempt to connect, so a match never opens behind another screen.
        CancelConnecting();
        HideScreens();
        _match.Visible = false;
        screen.Visible = true;
        _developer.Visible = screen == _home;
        _page.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
        _page.ScrollVertical = 0;
        MoveMenu(_root, 0, false);
        _status.Text = "";
    }

    public void ShowHome()
    {
        ShowScreen(_home);
        _rosterNote.Visible = false;
        _playButton.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void ShowPlay()
    {
        _form = _playForm;
        ShowScreen(_play);
        RefreshSaved();
        (_playHotSeat.Disabled ? (Control)_playForm.Server : _playHotSeat).CallDeferred(Control.MethodName.GrabFocus);
    }

    private void ShowSettings()
    {
        ShowScreen(_settingsPage);
        MoveMenu(_settingsPage, 2, true);
        _settingsPage.GetChild<Control>(0).GetChild<Control>(0).CallDeferred(Control.MethodName.GrabFocus);
    }

    private void ShowCredits()
    {
        ShowScreen(_credits);
        _credits.GetChild<Control>(0).GetChild<Control>(0).CallDeferred(Control.MethodName.GrabFocus);
    }

    // For the UI check: which pre-match screen is shown ("menu", "play", "settings", "credits", "developer"), or null.
    public string? ScreenShown =>
        _home.Visible ? "menu" : _play.Visible ? "play" : _settingsPage.Visible ? "settings" : _credits.Visible ? "credits" : _connect.Visible ? "developer" : null;

    public Button DeveloperButton => _developer;

    // For the UI check (stage M2): the shown screen's connection message, and whether its Cancel and Retry are shown.
    public string ConnectMessageText => _form.Message.Text;

    public bool CancelShown => _form.Cancel.IsVisibleInTree();

    public bool RetryShown => _form.Retry.IsVisibleInTree();

    public bool RosterNoteShown => _rosterNote.IsVisibleInTree();

    public bool SettingsShownOnSettingsScreen => _settingsPage.IsAncestorOf(_menuBox) && _menuBox.IsVisibleInTree();
}
