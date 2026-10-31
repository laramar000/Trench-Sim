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
    private VBoxContainer _rosterModels = null!;
    private VBoxContainer _rosterKit = null!;
    private VBoxContainer _rosterCheck = null!;
    private VBoxContainer _rosterSaved = null!;
    private Label _rosterMessage = null!;
    private string? _rosterFolder;

    private void BuildRosterScreen()
    {
        _builder = RosterBuilder.Shipped();
        var (screen, body) = MenuScreen("RosterScreen", 760, 8);
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
        body.AddChild(Section(RosterTitle, Column(
            Text(RosterBuilder.PracticeNote),
            Field("Warband name:", _rosterName),
            Field("Faction:", _rosterFaction),
            Field("List:", _rosterVariant),
            _rosterTotal)));

        _rosterRecruit = Column();
        _rosterRecruit.Name = "RosterRecruit";
        body.AddChild(Section("Recruit", _rosterRecruit));
        _rosterModels = Column();
        _rosterModels.Name = "RosterModels";
        body.AddChild(Section("Your warband", _rosterModels));
        _rosterKit = Column();
        _rosterKit.Name = "RosterKit";
        body.AddChild(Section("Battlekit", _rosterKit));
        _rosterCheck = Column();
        _rosterCheck.Name = "RosterCheck";
        body.AddChild(Section("Check", _rosterCheck));

        _rosterSaved = Column();
        _rosterSaved.Name = "RosterSaved";
        _rosterMessage = Text("");
        _rosterMessage.Name = "RosterMessage";
        var save = Button(SaveRosterText, SaveRoster);
        save.Name = "SaveRoster";
        save.ThemeTypeVariation = "MenuPrimary";
        body.AddChild(Section("Save and load", Column(Row(save), _rosterMessage, Text("Saved rosters on this PC:"), _rosterSaved)));
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

        Empty(_rosterRecruit);
        foreach (var e in _builder.Entries)
        {
            var add = Button($"Add {e.Name} · {e.CostText} · {e.LimitText}", () =>
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
            foreach (var k in choices)
            {
                var give = Button($"Add {k.Name} · {k.CostText}", () =>
                {
                    _builder.AddKit(k.Id);
                    RefreshRoster("AddKit-" + k.Id);
                });
                give.Name = "AddKit-" + k.Id;
                give.TooltipText = k.Rules;
                _rosterKit.AddChild(give);
            }
        }

        Empty(_rosterCheck);
        var lines = _builder.CheckLines;
        if (lines.Count == 0)
        {
            _rosterCheck.AddChild(Text("Legal, nothing to note."));
        }
        foreach (var line in lines)
        {
            _rosterCheck.AddChild(Text(line));
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
}
