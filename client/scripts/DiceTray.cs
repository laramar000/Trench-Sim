using Client.Core;
using Godot;

// Visible dice rolls (QoL stage F2): the server's rolls drawn as dice in a corner of the battlefield, one box per
// action, newest at the bottom. Each die slides in and settles on its real face (no other face is ever drawn); dice the
// rules dropped are grey and struck through. A box fades after the chosen time; a click closes it. What is in a box
// comes only from DiceFeed (Client.Core), which reads the server's events.
public partial class DiceTray : VBoxContainer
{
    public const float Margin = 10;
    public const double SettleSeconds = 0.35;
    public const double DieDelay = 0.07;

    private readonly Dictionary<int, PanelContainer> _panels = [];
    private float _scale = 1f;
    private bool _highContrast;

    public DiceFeed Feed { get; } = new();

    // The box ids on screen now, oldest first, and their rows as text (for the UI check).
    public IReadOnlyList<int> BoxIds => [.. _panels.Keys];

    public static double Now => Time.GetTicksMsec() / 1000.0;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 6);
    }

    public void SetLook(float scale, bool highContrast)
    {
        _scale = scale;
        _highContrast = highContrast;
        Rebuild();
    }

    // The screen rectangle of a box (in the battlefield's coordinates), or null when it is not shown.
    public Rect2? BoxRect(int id) => _panels.TryGetValue(id, out var panel) && IsInstanceValid(panel) ? new Rect2(Position + panel.Position, panel.Size) : null;

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
        if (GetParent() is Control parent)
        {
            var size = GetCombinedMinimumSize();
            Size = size;
            Position = new Vector2(Margin, Math.Max(Margin, parent.Size.Y - size.Y - Margin));
        }
    }

    private void Rebuild()
    {
        foreach (var child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        _panels.Clear();
        foreach (var box in Feed.Boxes)
        {
            var panel = MakeBox(box);
            _panels[box.Id] = panel;
            AddChild(panel);
        }
        Size = GetCombinedMinimumSize();
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
        var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, TooltipText = "Click to close. Every roll stays in the combat log." };
        panel.AddThemeStyleboxOverride("panel", style);
        var id = box.Id;
        panel.GuiInput += input =>
        {
            if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                Feed.Dismiss(id);
                panel.AcceptEvent();
            }
        };
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 4);
        foreach (var row in box.Rows)
        {
            rows.AddChild(MakeRow(row));
        }
        panel.AddChild(rows);
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
