using Godot;

// Battlefield UI 3, stage U3: a small Wi-Fi-style connection indicator (three bars, or a cross) in the battlefield's
// top-left corner. The bars come from Client.Core.ConnectionQuality (the connection state and the last measured command
// round trip); the tooltip and the details card say it in words, so nothing depends on colour.
public partial class SignalIcon : Button
{
    private int? _bars = 3;
    private bool _highContrast;

    public SignalIcon()
    {
        Name = "ConnectionIcon";
        FocusMode = FocusModeEnum.All;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        Flat = true;
        SetScale(1f);
    }

    // 0 to 3 bars, or null for a cross.
    public int? Bars
    {
        get => _bars;
        set
        {
            if (_bars != value)
            {
                _bars = value;
                QueueRedraw();
            }
        }
    }

    public bool HighContrast
    {
        get => _highContrast;
        set
        {
            _highContrast = value;
            QueueRedraw();
        }
    }

    public void SetScale(float scale) => CustomMinimumSize = new Vector2(28, 28) * scale;

    public Color Ink => _bars switch
    {
        null => _highContrast ? Colors.White : new Color(0.95f, 0.35f, 0.3f),
        3 => _highContrast ? Colors.Yellow : new Color(0.55f, 0.95f, 0.6f),
        2 => _highContrast ? Colors.Yellow : new Color(1f, 0.8f, 0.3f),
        _ => _highContrast ? Colors.White : new Color(1f, 0.5f, 0.3f),
    };

    public override void _Draw()
    {
        var s = Math.Min(Size.X, Size.Y);
        var o = (Size - new Vector2(s, s)) / 2;
        var dim = new Color(0.6f, 0.6f, 0.6f, 0.45f);
        var outline = new Color(0, 0, 0, 0.85f);
        for (var i = 0; i < 3; i++)
        {
            var height = s * (0.3f + 0.22f * i);
            var bar = new Rect2(o + new Vector2(s * (0.14f + 0.26f * i), s * 0.86f - height), new Vector2(s * 0.18f, height));
            DrawRect(bar.Grow(1.5f), outline);
            DrawRect(bar, _bars is { } n && i < n ? Ink : dim);
        }
        if (_bars is null)
        {
            var w = Math.Max(2f, s * 0.1f);
            DrawLine(o + new Vector2(s * 0.2f, s * 0.2f), o + new Vector2(s * 0.8f, s * 0.8f), outline, w + 2);
            DrawLine(o + new Vector2(s * 0.8f, s * 0.2f), o + new Vector2(s * 0.2f, s * 0.8f), outline, w + 2);
            DrawLine(o + new Vector2(s * 0.2f, s * 0.2f), o + new Vector2(s * 0.8f, s * 0.8f), Ink, w);
            DrawLine(o + new Vector2(s * 0.8f, s * 0.2f), o + new Vector2(s * 0.2f, s * 0.8f), Ink, w);
        }
    }
}
