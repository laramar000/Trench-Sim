using Godot;

// Battlefield UI 2, stage N3: a small square button in the battlefield's top-right corner, drawn as a plain symbol
// (three lines for the Menu, a book for the combat log, ? for the keys, a diamond for the scenario, and the camera's
// centre, fit and reset). The symbol is drawn with lines, so it never depends on a font having the glyph; what the
// button does is on its tooltip, and its name says it for the UI check.
public partial class IconButton : Button
{
    public enum Symbol
    {
        Menu,
        Log,
        Keys,
        Scenario,
        Centre,
        Fit,
        Reset,
    }

    private readonly Symbol _symbol;

    public IconButton(Symbol symbol, string tooltip)
    {
        _symbol = symbol;
        TooltipText = tooltip;
        FocusMode = FocusModeEnum.All;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        Name = $"Icon{symbol}";
        SetScale(1f);
    }

    // Godot needs a constructor without arguments for every script class.
    public IconButton() : this(Symbol.Menu, "")
    {
    }

    public Symbol Shows => _symbol;

    // Whether the panel it opens is open: the symbol is drawn on a lit square then.
    public bool Lit
    {
        get => _lit;
        set
        {
            if (_lit != value)
            {
                _lit = value;
                QueueRedraw();
            }
        }
    }

    private bool _lit;

    private bool _highContrast;

    public bool HighContrast
    {
        get => _highContrast;
        set
        {
            _highContrast = value;
            QueueRedraw();
        }
    }

    public void SetScale(float scale) => CustomMinimumSize = new Vector2(36, 36) * scale;

    public override void _Draw()
    {
        var size = Size;
        var s = Math.Min(size.X, size.Y);
        var o = (size - new Vector2(s, s)) / 2;
        var ink = _highContrast ? Colors.Yellow : new Color(0.95f, 0.95f, 0.92f);
        if (_lit)
        {
            DrawRect(new Rect2(Vector2.Zero, size), new Color(1, 1, 1, 0.18f));
        }
        var w = Math.Max(2f, s * 0.07f);
        Vector2 P(float x, float y) => o + new Vector2(x * s, y * s);
        switch (_symbol)
        {
            case Symbol.Menu:
                foreach (var y in new[] { 0.32f, 0.5f, 0.68f })
                {
                    DrawLine(P(0.25f, y), P(0.75f, y), ink, w);
                }
                break;
            case Symbol.Log:
                // An open book: two pages from a spine, with lines of text.
                DrawPolyline([P(0.5f, 0.3f), P(0.22f, 0.24f), P(0.22f, 0.74f), P(0.5f, 0.8f), P(0.78f, 0.74f), P(0.78f, 0.24f), P(0.5f, 0.3f), P(0.5f, 0.8f)], ink, w * 0.8f);
                foreach (var y in new[] { 0.4f, 0.52f, 0.64f })
                {
                    DrawLine(P(0.29f, y), P(0.43f, y + 0.02f), ink, w * 0.5f);
                    DrawLine(P(0.57f, y + 0.02f), P(0.71f, y), ink, w * 0.5f);
                }
                break;
            case Symbol.Keys:
                var font = GetThemeDefaultFont();
                var fontSize = (int)(s * 0.6f);
                var text = font.GetStringSize("?", HorizontalAlignment.Left, -1, fontSize);
                DrawString(font, o + new Vector2((s - text.X) / 2, (s + text.Y * 0.62f) / 2), "?", HorizontalAlignment.Left, -1, fontSize, ink);
                break;
            case Symbol.Scenario:
                DrawColoredPolygon([P(0.5f, 0.24f), P(0.76f, 0.5f), P(0.5f, 0.76f), P(0.24f, 0.5f)], ink);
                break;
            case Symbol.Centre:
                DrawArc(P(0.5f, 0.5f), s * 0.2f, 0, Mathf.Tau, 24, ink, w * 0.8f);
                DrawLine(P(0.5f, 0.18f), P(0.5f, 0.36f), ink, w * 0.8f);
                DrawLine(P(0.5f, 0.64f), P(0.5f, 0.82f), ink, w * 0.8f);
                DrawLine(P(0.18f, 0.5f), P(0.36f, 0.5f), ink, w * 0.8f);
                DrawLine(P(0.64f, 0.5f), P(0.82f, 0.5f), ink, w * 0.8f);
                break;
            case Symbol.Fit:
                foreach (var (x, y, dx, dy) in new[] { (0.24f, 0.24f, 1f, 1f), (0.76f, 0.24f, -1f, 1f), (0.24f, 0.76f, 1f, -1f), (0.76f, 0.76f, -1f, -1f) })
                {
                    DrawPolyline([P(x + dx * 0.16f, y), P(x, y), P(x, y + dy * 0.16f)], ink, w * 0.8f);
                }
                break;
            case Symbol.Reset:
                DrawArc(P(0.5f, 0.52f), s * 0.24f, -Mathf.Pi * 0.35f, Mathf.Pi * 1.35f, 24, ink, w * 0.8f);
                var tip = P(0.5f, 0.52f) + new Vector2(Mathf.Cos(-Mathf.Pi * 0.35f), Mathf.Sin(-Mathf.Pi * 0.35f)) * s * 0.24f;
                DrawColoredPolygon([tip + new Vector2(-s * 0.1f, -s * 0.06f), tip + new Vector2(s * 0.08f, -s * 0.1f), tip + new Vector2(s * 0.02f, s * 0.1f)], ink);
                break;
        }
    }
}
