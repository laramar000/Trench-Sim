using Client.Core;
using Godot;

// Visible dice rolls (QoL stages F2 and F3): the server's rolls drawn as dice, one box per action. The newest box goes
// beside its model when it fits there without covering the models in the roll, any label, the tray or the action bar
// (DicePlacement); the other boxes, rolls without a model (morale, roll-offs) and any box whose model is off screen or
// crowded go in the tray, the battlefield's bottom-left corner, newest at the bottom. The camera never moves for a roll.
// Each die slides in and settles on its real face (no other face is ever drawn); dice the rules dropped are grey and
// struck through. A box fades after the chosen time; its ✕ closes it, and every other click goes through to the
// battlefield. What is in a box comes only from DiceFeed
// (Client.Core), which reads the server's events.
public partial class DiceTray : Control
{
    public const float Margin = 10;
    public const float Spacing = 6;
    public const double SettleSeconds = 0.35;
    public const double DieDelay = 0.07;

    private readonly Dictionary<int, PanelContainer> _panels = [];
    private readonly Dictionary<int, Button> _closeButtons = [];
    private float _scale = 1f;
    private bool _highContrast;
    private Rect2? _besideRect;

    public DiceFeed Feed { get; } = new();

    // Where a model is on screen now (null when it is not drawn or behind the camera), and what a box beside a model must
    // not cover; set by the battlefield.
    public Func<Rules.Core.State.ModelId, Vector2?>? ScreenOfModel { get; set; }
    public Func<IEnumerable<Rect2>>? Avoid { get; set; }

    // The box ids on screen now, oldest first; the one beside its model (null when all are in the tray).
    public IReadOnlyList<int> BoxIds => [.. _panels.Keys];
    public int? BesideId { get; private set; }

    // What the box beside its model had to avoid when it was last placed (for the UI check).
    public IReadOnlyList<Rect2> LastAvoided { get; private set; } = [];

    public static double Now => Time.GetTicksMsec() / 1000.0;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        // After the battlefield has moved its labels this frame, so a box beside a model is placed against where they are.
        ProcessPriority = 100;
    }

    public void SetLook(float scale, bool highContrast)
    {
        _scale = scale;
        _highContrast = highContrast;
        Rebuild();
    }

    // The screen rectangle of a box (in the battlefield's coordinates), or null when it is not shown.
    public Rect2? BoxRect(int id) => _panels.TryGetValue(id, out var panel) && IsInstanceValid(panel) ? new Rect2(Position + panel.Position, panel.Size) : null;

    // The ✕ of a box, in the battlefield's coordinates (for the UI check).
    public Rect2? CloseRect(int id) => _closeButtons.TryGetValue(id, out var close) && IsInstanceValid(close) && BoxRect(id) is { } box
        ? new Rect2(close.GetGlobalRect().Position - GetGlobalRect().Position + Position, close.Size) : null;

    public float BoxAlpha(int id) => _panels.TryGetValue(id, out var panel) ? panel.Modulate.A : 0;

    public override void _Process(double delta)
    {
        var now = Now;
        Feed.Expire(now);
        if (Feed.TakeChanged())
        {
            Rebuild();
        }
        foreach (var box in Feed.Boxes)
        {
            if (_panels.TryGetValue(box.Id, out var panel))
            {
                panel.Modulate = new Color(1, 1, 1, (float)Feed.Alpha(box, now));
            }
        }
        Layout();
    }

    // Places the boxes: the newest beside its model if it fits, the rest stacked up from the bottom-left corner.
    public void Layout()
    {
        if (GetParent() is not Control parent)
        {
            return;
        }
        Position = Vector2.Zero;
        Size = parent.Size;
        var boxes = Feed.Boxes.Where(b => _panels.ContainsKey(b.Id)).ToList();
        foreach (var box in boxes)
        {
            _panels[box.Id].Size = _panels[box.Id].GetCombinedMinimumSize();
        }
        var newest = boxes.Count > 0 ? boxes[^1] : null;
        var tray = newest is null ? boxes : boxes.Take(boxes.Count - 1).ToList();
        var trayRects = Stack(tray);
        Rect2? beside = null;
        if (newest is { Model: { } model } && ScreenOfModel?.Invoke(model) is { } at)
        {
            var size = _panels[newest.Id].Size;
            var avoid = new List<Rect2>(Avoid?.Invoke() ?? []);
            foreach (var id in newest.Rows.SelectMany(r => new[] { r.Roll.Model, r.Roll.Other }).OfType<Rules.Core.State.ModelId>().Distinct())
            {
                if (ScreenOfModel(id) is { } point)
                {
                    var half = 22 * _scale;
                    avoid.Add(new Rect2(point - new Vector2(half, half), new Vector2(2 * half, 2 * half)));
                }
            }
            avoid.AddRange(trayRects.Values);
            LastAvoided = avoid;
            var field = new ScreenBox(Margin, Margin, Size.X - 2 * Margin, Size.Y - 2 * Margin);
            var previous = _besideRect is { } p && BesideId == newest.Id ? new ScreenBox(p.Position.X, p.Position.Y, p.Size.X, p.Size.Y) : (ScreenBox?)null;
            if (DicePlacement.Beside(at.X, at.Y, size.X, size.Y, field, [.. avoid.Select(r => new ScreenBox(r.Position.X, r.Position.Y, r.Size.X, r.Size.Y))], previous) is { } chosen)
            {
                beside = new Rect2((float)chosen.X, (float)chosen.Y, (float)chosen.Width, (float)chosen.Height);
            }
        }
        if (newest is not null && beside is null)
        {
            trayRects = Stack(boxes);
        }
        foreach (var (id, rect) in trayRects)
        {
            _panels[id].Position = rect.Position;
        }
        if (beside is { } placed && newest is not null)
        {
            _panels[newest.Id].Position = placed.Position;
            BesideId = newest.Id;
            _besideRect = placed;
        }
        else
        {
            BesideId = null;
            _besideRect = null;
        }
    }

    // The tray: boxes stacked up from the bottom-left corner, the last one at the bottom.
    private Dictionary<int, Rect2> Stack(IReadOnlyList<DiceBox> boxes)
    {
        var rects = new Dictionary<int, Rect2>();
        var y = Size.Y - Margin;
        for (var i = boxes.Count - 1; i >= 0; i--)
        {
            var size = _panels[boxes[i].Id].Size;
            y -= size.Y;
            rects[boxes[i].Id] = new Rect2(new Vector2(Margin, y), size);
            y -= Spacing;
        }
        return rects;
    }

    private void Rebuild()
    {
        foreach (var child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        _panels.Clear();
        _closeButtons.Clear();
        foreach (var box in Feed.Boxes)
        {
            var panel = MakeBox(box);
            _panels[box.Id] = panel;
            AddChild(panel);
        }
        Layout();
    }

    private PanelContainer MakeBox(DiceBox box)
    {
        var last = box.Rows[^1];
        var verdict = last.FollowUp is null ? last.Roll.Verdict : last.FollowUpVerdict;
        var style = new StyleBoxFlat
        {
            BgColor = _highContrast ? new Color(0, 0, 0, 0.95f) : new Color(0.07f, 0.07f, 0.09f, 0.88f),
            BorderColor = Tone(verdict),
            BorderWidthLeft = 5,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 6,
            ContentMarginBottom = 6,
        };
        // The box lets every click through to the battlefield, so it never blocks play; only its ✕ closes it.
        var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", style);
        var id = box.Id;
        var close = new Button
        {
            Text = "✕",
            Flat = true,
            FocusMode = FocusModeEnum.None,
            MouseFilter = MouseFilterEnum.Stop,
            TooltipText = "Close. Every roll stays in the combat log.",
            SizeFlagsVertical = SizeFlags.ShrinkBegin,
        };
        close.AddThemeFontSizeOverride("font_size", (int)(14 * _scale));
        close.Pressed += () => Feed.Dismiss(id);
        _closeButtons[id] = close;
        var rows = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        rows.AddThemeConstantOverride("separation", 4);
        foreach (var row in box.Rows)
        {
            rows.AddChild(MakeRow(row));
        }
        var top = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        top.AddChild(rows);
        top.AddChild(close);
        panel.AddChild(top);
        return panel;
    }

    private HBoxContainer MakeRow(DiceRow row)
    {
        var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", 10);
        line.AddChild(new DieStrip(row.Roll.Dice, Now, _scale, _highContrast));
        var words = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        words.AddThemeConstantOverride("separation", 0);
        var width = 300 * _scale;
        words.AddChild(Text(row.Roll.Title, 14, _highContrast ? Colors.White : new Color(0.92f, 0.92f, 0.9f), width));
        words.AddChild(Text(row.Roll.Rule, 12, _highContrast ? Colors.White : new Color(0.7f, 0.7f, 0.72f), width));
        var result = row.FollowUp is null ? row.Roll.Result : $"{row.Roll.Result}   {row.FollowUp}";
        var tone = row.FollowUp is null ? row.Roll.Verdict : row.FollowUpVerdict;
        words.AddChild(Text(result, 17, Tone(tone), width));
        line.AddChild(words);
        return line;
    }

    private Label Text(string text, int size, Color colour, float width)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(width, 0),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", (int)(size * _scale));
        label.AddThemeColorOverride("font_color", colour);
        return label;
    }

    // Colour is a second cue only: every result also starts with ✓, ✓✓, ✗ or •.
    private Color Tone(RollVerdict verdict) => verdict switch
    {
        RollVerdict.Critical => _highContrast ? Colors.Yellow : new Color(1f, 0.82f, 0.3f),
        RollVerdict.Pass => _highContrast ? Colors.Lime : new Color(0.45f, 0.88f, 0.5f),
        RollVerdict.Fail => _highContrast ? new Color(1f, 0.4f, 0.4f) : new Color(0.95f, 0.45f, 0.4f),
        _ => _highContrast ? Colors.White : new Color(0.85f, 0.85f, 0.88f),
    };
}
