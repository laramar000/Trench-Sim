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
        form.Message.CustomMinimumSize = new Vector2(200, 0);
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

    // Stage M3: each screen before a match is a centred column on a weathered brown panel (MenuScreen); the screen is
    // what is shown and hidden, the body is what holds its controls.
    private readonly List<Control> _menuScreens = [];
    private VBoxContainer _settingsBody = null!;
    private Button _settingsBack = null!;
    private Button _creditsBack = null!;
    private Button _roster = null!;
    private Theme? _menuTheme;
    private readonly List<(PanelContainer Panel, int Width)> _menuPanels = [];

    // Stage M3: a panel is as wide as it asks, or the window less a margin when that is narrower (a large interface
    // scale makes the window's content narrow).
    private void FitMenuPanels()
    {
        var room = GetViewportRect().Size.X - 32;
        foreach (var (panel, width) in _menuPanels)
        {
            panel.CustomMinimumSize = new Vector2(Math.Max(200, Math.Min(width, room)), 0);
        }
    }

    // A labelled field on its own line: the label never wraps, and the field takes the rest of the line (and can shrink).
    private static HBoxContainer Field(string label, Control field, params Control[] after)
    {
        var name = new Label { Text = label, AutowrapMode = TextServer.AutowrapMode.Off, VerticalAlignment = VerticalAlignment.Center };
        field.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        field.CustomMinimumSize = new Vector2(Math.Min(field.CustomMinimumSize.X, 160), field.CustomMinimumSize.Y);
        if (field is OptionButton option)
        {
            option.FitToLongestItem = false;
            option.ClipText = true;
        }
        return Row([name, field, .. after]);
    }

    // centred (Jason 2026-10-29, "center the menu"): the panel sits in the middle of the window, up and down as well as
    // across, with growing space above and below it (none when the window is too short, so it never scrolls further).
    private (VBoxContainer Screen, VBoxContainer Body) MenuScreen(string name, int width, int top, bool centred = false)
    {
        var screen = new VBoxContainer { Name = name, SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        if (centred)
        {
            screen.SizeFlagsVertical = SizeFlags.ExpandFill;
            screen.AddChild(new Control { Name = "SpaceAbove", SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        }
        else if (top > 0)
        {
            screen.AddChild(new Control { CustomMinimumSize = new Vector2(0, top), MouseFilter = MouseFilterEnum.Ignore });
        }
        var panel = new PanelContainer { Name = "MenuPanel", ThemeTypeVariation = "MenuPanel", CustomMinimumSize = new Vector2(width, 0) };
        _menuPanels.Add((panel, width));
        var body = new VBoxContainer { Name = "MenuBody" };
        body.AddThemeConstantOverride("separation", 10);
        panel.AddChild(body);
        screen.AddChild(panel);
        if (centred)
        {
            screen.AddChild(new Control { Name = "SpaceBelow", SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        }
        _menuScreens.Add(screen);
        _root.AddChild(screen);
        return (screen, body);
    }

    private void BuildMenuScreens()
    {
        GetViewport().SizeChanged += FitMenuPanels;
        // ---- main menu ----
        var (home, menu) = MenuScreen("MainMenu", 380, 0, centred: true);
        _home = home;
        var title = Text(GameName.ToUpperInvariant());
        // Its own name, not "SectionTitle": it is the game's name in bone and larger, not a gold section title.
        title.Name = "GameTitle";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        _headings.Add(title);
        _menuTitle = title;
        menu.AddChild(title);
        menu.AddChild(new HSeparator());
        Button MenuItem(string text, Action pressed, string? look = null)
        {
            var button = Button(text, pressed);
            button.CustomMinimumSize = new Vector2(340, 44);
            if (look is not null)
            {
                button.ThemeTypeVariation = look;
            }
            return button;
        }
        _playButton = MenuItem(PlayText, ShowPlay, "MenuPrimary");
        _playButton.Name = "PlayButton";
        menu.AddChild(_playButton);
        // Jason's choice 1 (greyed item): it looks unavailable, and pressing it only says so.
        _roster = MenuItem(RosterText, () => _rosterNote.Visible = true, "MenuMuted");
        _roster.Name = "RosterButton";
        _roster.TooltipText = RosterNoteText;
        menu.AddChild(_roster);
        _rosterNote = Text(RosterNoteText);
        _rosterNote.Name = "RosterNote";
        _rosterNote.CustomMinimumSize = new Vector2(340, 0);
        _rosterNote.Visible = false;
        menu.AddChild(_rosterNote);
        menu.AddChild(MenuItem(SettingsText, ShowSettings));
        menu.AddChild(MenuItem(CreditsText, ShowCredits));
        menu.AddChild(new HSeparator());
        menu.AddChild(MenuItem(ExitText, () => GetTree().Quit(), "MenuDanger"));

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
        var (playScreen, play) = MenuScreen("PlayScreen", 760, 8);
        _play = playScreen;
        play.AddChild(Row(Button(BackText, ShowHome)));
        play.AddChild(Section("Play", Column(Field("Scenario:", form.Scenario), Field("Map:", form.Map))));
        _playHotSeat = Button(StartHotSeatText, Use(form, StartHotSeat));
        _playHotSeat.ThemeTypeVariation = "MenuPrimary";
        var hotNote = Text("Play both sides in this window. A local server starts on this PC; F2 switches the side you control.");
        if (LocalServer.FindRepository(ProjectSettings.GlobalizePath("res://")) is null)
        {
            // Hot-seat runs the server from the source folder; without it the button cannot work.
            _playHotSeat.Disabled = true;
            hotNote.Text = "Not available here: hot-seat starts the server from the Trenches source folder (TrenchSim.sln above client/), which was not found.";
        }
        play.AddChild(Section("This PC (hot-seat)", Column(Row(_playHotSeat), hotNote)));
        var host = Button(HostText, Use(form, () => Create(ChosenScenario())));
        host.ThemeTypeVariation = "MenuPrimary";
        var join = Button("Join match", Use(form, Join));
        join.ThemeTypeVariation = "MenuPrimary";
        play.AddChild(Section("Host or join a match", Column(
            Text("Needs a running Trenches server that both players can reach."),
            Field("Server address:", form.Server),
            Row(host),
            Text("Host uses the scenario and map above; give your opponent the match code shown next."),
            Field("Match code:", form.Code, join))));
        play.AddChild(Section("Rejoin", Column(
            Text("Your saved seats on this server:"),
            form.Saved,
            Button("Refresh saved seats", Use(form, RefreshSaved)))));
        var advancedToggle = new CheckButton { Text = "Advanced", Name = "AdvancedToggle" };
        _advanced = Column(Field("Server rejoin window (seconds):", form.Window),
            Text("Set this to the server's --rejoin-window (300 by default). It decides how long reconnecting keeps trying before offering Rejoin now."));
        _advanced.Visible = false;
        advancedToggle.Toggled += on => _advanced.Visible = on;
        play.AddChild(advancedToggle);
        play.AddChild(_advanced);
        play.AddChild(form.MessageRow);

        // ---- Settings (the same controls as the in-match Menu, moved here while shown) ----
        var (settingsScreen, settings) = MenuScreen("SettingsScreen", 760, 8);
        _settingsPage = settingsScreen;
        _settingsBody = settings;
        _settingsBack = Button(BackText, ShowHome);
        settings.AddChild(Row(_settingsBack));
        settings.AddChild(Section("Settings", Text("Saved on this PC and used in every match.")));

        // ---- Credits ----
        var (creditsScreen, credits) = MenuScreen("CreditsScreen", 760, 8);
        _credits = creditsScreen;
        _creditsBack = Button(BackText, ShowHome);
        credits.AddChild(Row(_creditsBack));
        var lines = Column();
        foreach (var (heading, text) in Credits.Entries)
        {
            lines.AddChild(Text($"{heading}: {text}"));
        }
        lines.AddChild(Text(VersionLine));
        credits.AddChild(Section("Credits", lines));
    }

    // Stage M3: the menu screens' look, set again with the text size and high contrast. High contrast keeps the plain
    // black-and-white look of the rest of the game (the screens take the main theme), and the roster item is greyed by
    // fading it instead.
    private Label _menuTitle = null!;

    private void ApplyMenuLook()
    {
        _menuTheme = _highContrast ? null : MenuLook.Theme(_theme.DefaultFontSize);
        FitMenuPanels();
        foreach (var screen in _menuScreens)
        {
            screen.Theme = _menuTheme;
        }
        _roster.Modulate = new Color(1, 1, 1, _highContrast ? 0.55f : 1f);
        if (!_highContrast)
        {
            _menuTitle.AddThemeColorOverride("font_color", MenuLook.Color(MenuPalette.Bone));
        }
        _menuTitle.AddThemeFontSizeOverride("font_size", UiLook.HeadingSize((int)(_theme.DefaultFontSize * 2.4f)));
    }

    // For the UI check (stage M3): the menu's own theme while it is applied (null in high contrast), and the roster item.
    public Theme? MenuTheme => _menuTheme;

    public Button RosterButton => _roster;

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
        FitMenuPanels();
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

    // For the UI check (stage M3): the Play screen, as Play shows it.
    public void ShowPlayForCheck() => ShowPlay();

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
        MoveMenu(_settingsBody, 2, true);
        _settingsBack.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void ShowCredits()
    {
        ShowScreen(_credits);
        _creditsBack.CallDeferred(Control.MethodName.GrabFocus);
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
