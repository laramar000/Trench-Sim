using Client.Core;
using Godot;

// Roster builder stage 3 (2026-10-30): the Build Your Own Roster screen. It only lays out Client.Core.RosterBuilder:
// choose the faction and list, recruit models, give the chosen model its battlekit and upgrades, read the total and
// every problem in words, and save and load roster files. Nothing here is sent to a server or used in a match yet.
public partial class Main
{
    public const string RosterTitle = "Build your own roster";
    public const string SaveRosterText = "Save roster";

    private RosterBuilder _builder = null!;
    private VBoxContainer _rosterScreen = null!;
    private Button _rosterBack = null!;
    private LineEdit _rosterName = null!;
    private OptionButton _rosterFaction = null!;
    private OptionButton _rosterVariant = null!;
    private Label _rosterTotal = null!;
    private VBoxContainer _rosterRecruit = null!;
    private VBoxContainer _rosterRules = null!;
    private VBoxContainer _rosterModels = null!;
    private VBoxContainer _rosterKit = null!;
    private VBoxContainer _rosterCheck = null!;
    private VBoxContainer _rosterSaved = null!;
    private Label _rosterMessage = null!;
    private string? _rosterFolder;

    // Roster layouts (owner, 2026-11-03; ClientSettings.RosterLayout): 0 wide with two columns (the default: recruit and battlekit
    // on the left in a column of their own that scrolls, the warband, the check and Save on the right, always in view), 1 classic
    // (one column, the plain list), 2 compact (one column, the battlekit under headings). The sections are built once and set in
    // place again when the layout changes.
    private int _rosterLayout;
    private bool _rosterBuilt;
    private VBoxContainer _rosterBody = null!;
    private int _rosterPanelAt;
    private Control _rosterHeader = null!, _rosterRulesSection = null!, _rosterRecruitSection = null!, _rosterModelsSection = null!,
        _rosterKitSection = null!, _rosterCheckSection = null!, _rosterSaveSection = null!;
    private Control? _rosterColumns;
    private HBoxContainer[] _rosterFields = [];
    private ProgressBar _rosterBar = null!, _rosterGloryBar = null!;
    private VBoxContainer _rosterProblems = null!;
    private Label _rosterNote = null!;
    private bool _problemsOpen;
    private readonly StyleBoxFlat _rosterBarFill = new();
    private readonly StyleBoxFlat _rosterGloryFill = new();
    private VBoxContainer _rosterFieldsHolder = null!;
    private ScrollContainer? _rosterLeftScroll;
    private readonly Dictionary<string, bool> _kitOpen = new() { ["Ranged weapons"] = true, ["Melee weapons"] = true };
    private bool _playedWithoutOpen;
    private bool _rulesOpen;

    private void BuildRosterScreen()
    {
        _builder = RosterBuilder.Shipped();
        var (screen, body) = MenuScreen("RosterScreen", 760, 8);
        _rosterBody = body;
        _rosterPanelAt = _menuPanels.Count - 1;
        _rosterScreen = screen;
        _rosterBack = Button(BackText, ShowHome);
        body.AddChild(Row(_rosterBack));

        _rosterName = new LineEdit { Name = "RosterName", Text = _builder.Name, CustomMinimumSize = new Vector2(260, 0) };
        _rosterName.TextChanged += text => _builder.Name = text;
        _rosterFaction = new OptionButton { Name = "RosterFaction" };
        foreach (var f in _builder.Factions)
        {
            _rosterFaction.AddItem(f.Name);
            _rosterFaction.SetItemMetadata(_rosterFaction.ItemCount - 1, f.Id);
        }
        _rosterFaction.ItemSelected += i =>
        {
            _builder.ChooseFaction((string)_rosterFaction.GetItemMetadata((int)i));
            FillVariants();
            RefreshRoster();
        };
        _rosterVariant = new OptionButton { Name = "RosterVariant" };
        _rosterVariant.ItemSelected += i =>
        {
            var id = (string)_rosterVariant.GetItemMetadata((int)i);
            _builder.ChooseVariant(id.Length == 0 ? null : id);
            RefreshRoster();
        };
        FillVariants();
        _rosterTotal = Text("");
        _rosterTotal.Name = "RosterTotal";
        _rosterFields = [Field("Warband name:", _rosterName), Field("Faction:", _rosterFaction), Field("List:", _rosterVariant)];
        _rosterFieldsHolder = Column();
        // The ducats spent against the budget, as a bar under the total (wide and compact layouts); rust when over it.
        _rosterBar = new ProgressBar { Name = "RosterBar", ShowPercentage = false, CustomMinimumSize = new Vector2(0, 10) };
        _rosterBar.AddThemeStyleboxOverride("fill", _rosterBarFill);
        _rosterBar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0.13f, 0.1f, 0.085f), BorderColor = UiLook.Iron, BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1 });
        // And the Glory spent against its budget, when the list has one, in a cool blue under it.
        _rosterGloryBar = new ProgressBar { Name = "RosterGloryBar", ShowPercentage = false, CustomMinimumSize = new Vector2(0, 6), TooltipText = "Glory spent against the list's Glory budget" };
        _rosterGloryBar.AddThemeStyleboxOverride("fill", _rosterGloryFill);
        _rosterGloryBar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0.13f, 0.1f, 0.085f), BorderColor = UiLook.Iron, BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1 });
        _rosterNote = Text(RosterBuilder.SourceNote);
        // The problems in red: the total turns red while the list has any, and clicking it (or Enter or Space on it) says what is wrong.
        _rosterTotal.MouseFilter = Control.MouseFilterEnum.Stop;
        _rosterTotal.FocusMode = Control.FocusModeEnum.All;
        _rosterTotal.GuiInput += ev =>
        {
            if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } || ev is InputEventKey { Pressed: true, Keycode: Key.Enter or Key.KpEnter or Key.Space })
            {
                if (_builder.Check().Problems.Count > 0)
                {
                    _problemsOpen = !_problemsOpen;
                    RefreshRoster();
                }
                _rosterTotal.AcceptEvent();
            }
        };
        _rosterProblems = Column();
        _rosterProblems.Name = "RosterProblems";
        _rosterHeader = Section(RosterTitle, Column(_rosterNote, _rosterFieldsHolder, _rosterTotal, _rosterBar, _rosterGloryBar, _rosterProblems));

        _rosterRules = Column();
        _rosterRules.Name = "RosterRules";
        _rosterRulesSection = Section("List rules", _rosterRules);
        _rosterRecruit = Column();
        _rosterRecruit.Name = "RosterRecruit";
        _rosterRecruitSection = Section("Recruit", _rosterRecruit);
        _rosterModels = Column();
        _rosterModels.Name = "RosterModels";
        _rosterModelsSection = Section("Your warband", _rosterModels);
        _rosterKit = Column();
        _rosterKit.Name = "RosterKit";
        _rosterKitSection = Section("Battlekit", _rosterKit);
        _rosterCheck = Column();
        _rosterCheck.Name = "RosterCheck";
        _rosterCheckSection = Section("Check", _rosterCheck);

        _rosterSaved = Column();
        _rosterSaved.Name = "RosterSaved";
        _rosterMessage = Text("");
        _rosterMessage.Name = "RosterMessage";
        var save = Button(SaveRosterText, SaveRoster);
        save.Name = "SaveRoster";
        save.ThemeTypeVariation = "MenuPrimary";
        _rosterSaveSection = Section("Save and load", Column(Row(save), _rosterMessage, Text("Saved rosters on this PC:"), _rosterSaved));
        _rosterBuilt = true;
        ArrangeRoster();
        GetViewport().SizeChanged += FitRosterColumns;
    }

    // Sets the sections in place for the layout (see the fields): header first, then the columns or the one column.
    private void ArrangeRoster()
    {
        if (!_rosterBuilt)
        {
            return;
        }
        foreach (var section in new[] { _rosterHeader, _rosterRulesSection, _rosterRecruitSection, _rosterModelsSection, _rosterKitSection, _rosterCheckSection, _rosterSaveSection })
        {
            section.GetParent()?.RemoveChild(section);
        }
        if (_rosterColumns is not null)
        {
            _rosterBody.RemoveChild(_rosterColumns);
            _rosterColumns.QueueFree();
            _rosterColumns = null;
            _rosterLeftScroll = null;
        }
        var wide = _rosterLayout == 0;
        // Condensed (wide and compact): the note under the title is small and dim, and the sections sit closer.
        _rosterBody.AddThemeConstantOverride("separation", _rosterLayout == 1 ? 10 : 6);
        if (_rosterLayout == 1)
        {
            _rosterNote.RemoveThemeFontSizeOverride("font_size");
            _rosterNote.RemoveThemeColorOverride("font_color");
        }
        else
        {
            _rosterNote.AddThemeFontSizeOverride("font_size", (int)(_theme.DefaultFontSize * 0.8f));
            _rosterNote.AddThemeColorOverride("font_color", _highContrast ? Colors.White : UiLook.Muted);
        }
        FitRosterPanel();
        // The name, faction and list share one line in the wide layout, and have a line each in the others.
        foreach (var child in _rosterFieldsHolder.GetChildren())
        {
            _rosterFieldsHolder.RemoveChild(child);
            if (child is Container old)
            {
                foreach (var field in old.GetChildren())
                {
                    old.RemoveChild(field);
                }
                old.QueueFree();
            }
        }
        Container fields = wide ? new HBoxContainer { Name = "RosterFieldsRow" } : new VBoxContainer { Name = "RosterFieldsColumn" };
        fields.AddThemeConstantOverride("separation", 14);
        foreach (var field in _rosterFields)
        {
            field.GetParent()?.RemoveChild(field);
            field.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            fields.AddChild(field);
        }
        _rosterFieldsHolder.AddChild(fields);
        _rosterBody.AddChild(_rosterHeader);
        if (wide)
        {
            var left = Column(_rosterRulesSection, _rosterRecruitSection, _rosterKitSection);
            left.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _rosterLeftScroll = new ScrollContainer { Name = "RosterLeftScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1f };
            _rosterLeftScroll.AddChild(left);
            var right = Column(_rosterModelsSection, _rosterCheckSection, _rosterSaveSection);
            right.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var columns = new HBoxContainer { Name = "RosterColumns" };
            columns.AddThemeConstantOverride("separation", 14);
            columns.AddChild(_rosterLeftScroll);
            columns.AddChild(right);
            _rosterColumns = columns;
            _rosterBody.AddChild(columns);
            FitRosterColumns();
        }
        else
        {
            foreach (var section in new[] { _rosterRulesSection, _rosterRecruitSection, _rosterModelsSection, _rosterKitSection, _rosterCheckSection, _rosterSaveSection })
            {
                _rosterBody.AddChild(section);
            }
        }
    }

    // The wide layout is three fifths of the window across, with a fifth empty on each side (owner, 2026-11-03), and at least
    // 900 px; the others keep the menu's 760 px.
    private void FitRosterPanel()
    {
        var width = _rosterLayout == 0 ? (int)Math.Max(900, GetViewportRect().Size.X * 0.6f) : 760;
        _menuPanels[_rosterPanelAt] = (_menuPanels[_rosterPanelAt].Panel, width);
        FitMenuPanels();
    }

    // The wide layout's left column is as tall as the window leaves room for, so only it scrolls.
    private void FitRosterColumns()
    {
        if (_rosterBuilt)
        {
            FitRosterPanel();
        }
        if (_rosterLeftScroll is { } scroll)
        {
            scroll.CustomMinimumSize = new Vector2(0, Math.Max(320, GetViewportRect().Size.Y - 360));
        }
    }

    // The player's choice in the settings (and the UI check's): 0 wide, 1 classic, 2 compact.
    public void ChooseRosterLayout(int layout)
    {
        _rosterLayout = Math.Clamp(layout, 0, ClientSettings.RosterLayoutChoices - 1);
        ArrangeRoster();
        if (_rosterBuilt)
        {
            RefreshRoster();
        }
    }

    public int RosterLayoutShown => _rosterLayout;

    // For the UI check: as if the player had clicked the red problem line.
    public void ShowRosterProblems()
    {
        _problemsOpen = true;
        RefreshRoster();
    }

    private void FillVariants()
    {
        _rosterVariant.Clear();
        foreach (var v in _builder.Variants)
        {
            _rosterVariant.AddItem(v is null ? "Standard list" : v.Name);
            _rosterVariant.SetItemMetadata(_rosterVariant.ItemCount - 1, v?.Id ?? "");
        }
        _rosterVariant.Select(Math.Max(0, _builder.Variants.ToList().FindIndex(v => v?.Id == _builder.Variant)));
    }

    // Where roster files are kept: user://rosters, or the UI check's own folder in its output folder.
    public string RosterFolder => _rosterFolder ??= OS.GetCmdlineUserArgs() is var args && Arg(args, "--ui-check") is { } checking
        ? System.IO.Path.GetFullPath(System.IO.Path.Combine(Arg(args, "--out") ?? "ui-check", $"rosters-{checking}"))
        : ProjectSettings.GlobalizePath("user://rosters");

    private void ShowRoster()
    {
        ShowScreen(_rosterScreen);
        _rosterMessage.Text = "";
        RefreshRoster();
        _rosterBack.CallDeferred(Control.MethodName.GrabFocus);
    }

    private static void Empty(Container box)
    {
        foreach (var child in box.GetChildren())
        {
            box.RemoveChild(child);
            child.QueueFree();
        }
    }

    // Everything below the faction choice is drawn again from the builder after each change.
    private void RefreshRoster(string? focus = null)
    {
        _rosterTotal.Text = $"{_builder.ListName}: {_builder.TotalText}";
        var problems = _builder.Check().Problems;
        var red = _highContrast ? Colors.Yellow : new Color(1f, 0.45f, 0.38f);
        if (problems.Count > 0)
        {
            _rosterTotal.AddThemeColorOverride("font_color", red);
            _rosterTotal.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
            _rosterTotal.TooltipText = "Click to see what is wrong";
        }
        else
        {
            _rosterTotal.RemoveThemeColorOverride("font_color");
            _rosterTotal.MouseDefaultCursorShape = Control.CursorShape.Arrow;
            _rosterTotal.TooltipText = "";
            _problemsOpen = false;
        }
        Empty(_rosterProblems);
        if (_problemsOpen)
        {
            foreach (var problem in problems)
            {
                var line = Text("✕ " + problem);
                line.AddThemeColorOverride("font_color", red);
                _rosterProblems.AddChild(line);
            }
        }
        var spent = _builder.Check().Ducats;
        _rosterBar.MaxValue = Math.Max(1, _builder.BudgetDucats);
        _rosterBar.Value = Math.Min(spent, _builder.BudgetDucats);
        _rosterBar.Visible = _rosterLayout != 1;
        var glory = _builder.Check().Glory;
        _rosterGloryBar.Visible = _rosterLayout != 1 && _builder.BudgetGlory > 0;
        _rosterGloryBar.MaxValue = Math.Max(1, _builder.BudgetGlory);
        _rosterGloryBar.Value = Math.Min(glory, _builder.BudgetGlory);
        _rosterGloryFill.BgColor = glory > _builder.BudgetGlory ? new Color(0.72f, 0.2f, 0.14f) : _highContrast ? Colors.White : new Color(0.45f, 0.62f, 0.85f);
        _rosterBarFill.BgColor = spent > _builder.BudgetDucats ? new Color(0.72f, 0.2f, 0.14f) : _highContrast ? Colors.Yellow : UiLook.Gold;

        Empty(_rosterRules);
        var ruleLines = _builder.RuleLines.DefaultIfEmpty("No special rules for this list.").ToList();
        if (_rosterLayout == 1 || ruleLines.Count < 2)
        {
            foreach (var line in ruleLines)
            {
                _rosterRules.AddChild(Text(line));
            }
        }
        else
        {
            // Wide and compact: a long list's rules fold under one heading with a count, so the recruit buttons are not pushed down.
            var fold = Column();
            fold.Visible = _rulesOpen;
            foreach (var line in ruleLines)
            {
                fold.AddChild(Text(line));
            }
            Button header = null!;
            string Title() => $"{(fold.Visible ? "▼" : "▶")} Rules of this list · {ruleLines.Count}";
            header = Button(Title(), () =>
            {
                fold.Visible = !fold.Visible;
                _rulesOpen = fold.Visible;
                header.Text = Title();
            });
            header.Name = "ListRulesFold";
            header.Flat = true;
            header.Alignment = HorizontalAlignment.Left;
            _rosterRules.AddChild(header);
            _rosterRules.AddChild(fold);
        }

        Empty(_rosterRecruit);
        foreach (var e in _builder.Entries)
        {
            var add = Button($"{e.Name} · {e.CostText} · {e.LimitText}", () =>
            {
                _builder.Add(e.Id);
                RefreshRoster("ModelButton-" + _builder.Selected);
            });
            add.Name = "RecruitButton-" + e.Id;
            _rosterRecruit.AddChild(add);
        }

        Empty(_rosterModels);
        if (_builder.Models.Count == 0)
        {
            _rosterModels.AddChild(Text("No models yet. Recruit them above."));
        }
        foreach (var m in _builder.Models)
        {
            var pick = Button((m.Index == _builder.Selected ? "▶ " : "") + $"{m.Title} · {m.CostText}", () =>
            {
                _builder.Select(m.Index);
                RefreshRoster("ModelButton-" + m.Index);
            });
            pick.Name = "ModelButton-" + m.Index;
            pick.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            pick.ClipText = true;
            pick.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            var remove = Button("Remove", () =>
            {
                _builder.Remove(m.Index);
                RefreshRoster(_builder.Selected >= 0 ? "ModelButton-" + _builder.Selected : null);
            });
            remove.Name = "RemoveModel-" + m.Index;
            _rosterModels.AddChild(Row(pick, remove));
        }

        Empty(_rosterKit);
        if (_builder.Selected < 0)
        {
            _rosterKit.AddChild(Text("Choose a model in Your warband to give it battlekit."));
        }
        else
        {
            _rosterKit.AddChild(Text($"For {_builder.Models[_builder.Selected].Title}"));
            foreach (var c in _builder.Carried)
            {
                if (c.Position < 0)
                {
                    _rosterKit.AddChild(Text($"{c.Name} · {c.CostText}"));
                    continue;
                }
                var drop = Button($"Remove {c.Name} · {c.CostText}", () =>
                {
                    _builder.RemoveKit(c.Position);
                    RefreshRoster("ModelButton-" + _builder.Selected);
                });
                drop.Name = "RemoveKit-" + c.Position;
                _rosterKit.AddChild(drop);
            }
            foreach (var u in _builder.Upgrades)
            {
                var toggle = new CheckButton { Name = "Upgrade-" + u.Id, Text = $"{u.Name} · {u.CostText}", ButtonPressed = u.Bought, FocusMode = FocusModeEnum.All };
                toggle.Toggled += _ =>
                {
                    _builder.ToggleUpgrade(u.Id);
                    RefreshRoster("Upgrade-" + u.Id);
                };
                _rosterKit.AddChild(toggle);
            }
            var choices = _builder.KitChoices;
            if (choices.Count == 0)
            {
                _rosterKit.AddChild(Text("This model cannot be given battlekit."));
            }
            Button GiveButton(RosterBuilder.KitRow k)
            {
                var give = Button($"{k.Name} · {k.CostText}", () =>
                {
                    _builder.AddKit(k.Id);
                    RefreshRoster(AddKitName(k.Id));
                });
                give.Name = AddKitName(k.Id);
                give.TooltipText = k.Rules;
                return give;
            }
            if (_rosterLayout == 1)
            {
                foreach (var k in choices)
                {
                    _rosterKit.AddChild(GiveButton(k));
                }
            }
            else
            {
                AddKitGroups(choices, GiveButton);
            }
        }

        Empty(_rosterCheck);
        // Problems show only when the red total is clicked; the check panel keeps the notes.
        var lines = _builder.CheckLines.Where(l => !l.StartsWith("✕", StringComparison.Ordinal)).ToList();
        if (lines.Count == 0 && problems.Count == 0)
        {
            _rosterCheck.AddChild(Text("Legal, nothing to note."));
        }
        foreach (var line in lines)
        {
            _rosterCheck.AddChild(Text(line));
        }
        var matchLines = _builder.MatchLines;
        var without = _rosterLayout == 1 ? [] : matchLines.Where(l => l.Contains("Played without", StringComparison.Ordinal)).ToList();
        foreach (var line in matchLines.Where(l => !without.Contains(l)))
        {
            _rosterCheck.AddChild(Text(line));
        }
        if (without.Count > 0)
        {
            // The same lines, folded: one header says how many.
            var fold = Column();
            fold.Visible = _playedWithoutOpen;
            foreach (var line in without)
            {
                fold.AddChild(Text(line));
            }
            Button header = null!;
            string Title() => $"{(fold.Visible ? "▼" : "▶")} Played without · {without.Count}";
            header = Button(Title(), () =>
            {
                fold.Visible = !fold.Visible;
                _playedWithoutOpen = fold.Visible;
                header.Text = Title();
            });
            header.Name = "PlayedWithoutFold";
            header.Flat = true;
            header.Alignment = HorizontalAlignment.Left;
            _rosterCheck.AddChild(header);
            _rosterCheck.AddChild(fold);
        }

        Empty(_rosterSaved);
        var saved = RosterBuilder.Saved(RosterFolder);
        if (saved.Count == 0)
        {
            _rosterSaved.AddChild(Text("None yet."));
        }
        foreach (var file in saved)
        {
            var load = Button("Load " + file[..^".roster.json".Length], () => LoadRoster(file));
            load.Name = "LoadRoster-" + file[..^".roster.json".Length];
            _rosterSaved.AddChild(load);
        }

        if (focus is not null && _rosterScreen.FindChild(focus, true, false) is Control again)
        {
            again.CallDeferred(Control.MethodName.GrabFocus);
        }
    }

    private static readonly string[] KitGroupOrder = ["Ranged weapons", "Melee weapons", "Grenades", "Shields and armour", "Equipment", "Glory items"];

    private static string KitGroupOf(RosterBuilder.KitRow k) => k.CostText.EndsWith("Glory", StringComparison.Ordinal) ? "Glory items" : k.Category switch
    {
        "Ranged" => "Ranged weapons",
        "Melee" => "Melee weapons",
        "Grenade" => "Grenades",
        "Shield" or "Armour" => "Shields and armour",
        _ => "Equipment",
    };

    // The battlekit under collapsible headings (wide and compact layouts): weapons open, the rest folded until opened; each
    // heading says how many pieces it holds, and what is open is kept while the screen is drawn again.
    private void AddKitGroups(IReadOnlyList<RosterBuilder.KitRow> choices, Func<RosterBuilder.KitRow, Button> give)
    {
        foreach (var name in KitGroupOrder)
        {
            var items = choices.Where(k => KitGroupOf(k) == name).ToList();
            if (items.Count == 0)
            {
                continue;
            }
            // Two to a row only where the window is very wide (the wide layout is three fifths of it).
            var grid = new GridContainer { Columns = _rosterLayout == 0 && _menuPanels[_rosterPanelAt].Width >= 1400 ? 2 : 1, Visible = _kitOpen.GetValueOrDefault(name) };
            grid.AddThemeConstantOverride("h_separation", 8);
            grid.AddThemeConstantOverride("v_separation", 4);
            foreach (var k in items)
            {
                var button = give(k);
                button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                button.ClipText = true;
                button.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                grid.AddChild(button);
            }
            Button header = null!;
            string Title() => $"{(grid.Visible ? "▼" : "▶")} {name} · {items.Count}";
            header = Button(Title(), () =>
            {
                grid.Visible = !grid.Visible;
                _kitOpen[name] = grid.Visible;
                header.Text = Title();
            });
            header.Name = "KitGroup-" + name.Replace(' ', '-');
            header.Flat = true;
            header.Alignment = HorizontalAlignment.Left;
            _rosterKit.AddChild(header);
            _rosterKit.AddChild(grid);
        }
    }

    private void SaveRoster()
    {
        var path = _builder.Save(RosterFolder);
        _rosterMessage.Text = $"Saved as {System.IO.Path.GetFileName(path)}.";
        RefreshRoster("SaveRoster");
    }

    private void LoadRoster(string file)
    {
        var refused = _builder.Load(RosterFolder, file);
        _rosterMessage.Text = refused ?? $"Loaded {file[..^".roster.json".Length]}.";
        if (refused is null)
        {
            _rosterName.Text = _builder.Name;
            _rosterFaction.Select(Math.Max(0, _builder.Factions.ToList().FindIndex(f => f.Id == _builder.Faction.Id)));
            FillVariants();
        }
        RefreshRoster("SaveRoster");
    }

    // For the UI check: the builder behind the screen.
    public RosterBuilder RosterBuilderShown => _builder;

    // For the roster-look check: draw the roster screen again after the check changed the builder.
    public void RefreshRosterScreen() => RefreshRoster();

    // Godot node names cannot hold '@', which marks a piece bought from another faction's armoury.
    public static string AddKitName(string kitRef) => "AddKit-" + kitRef.Replace("@", "-from-");
}
