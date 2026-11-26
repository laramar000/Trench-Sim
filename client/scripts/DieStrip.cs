using Client.Core;
using Godot;

// The dice of one roll, in the server's order. Each slides in and turns onto its face, which is the real one from the
// first frame it is drawn; its number is written under it.
public partial class DieStrip : Control
{
    private readonly IReadOnlyList<DieFace> _dice;
    private readonly double _born;
    private readonly float _scale;
    private readonly bool _highContrast;

    public DieStrip(IReadOnlyList<DieFace> dice, double born, float scale, bool highContrast)
    {
        _dice = dice;
        _born = born;
        _scale = scale;
        _highContrast = highContrast;
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(dice.Count * (Side + Gap) - Gap + 4, Side + 18 * scale);
    }

    // Godot needs a constructor without arguments for every script class.
    public DieStrip() : this([], 0, 1, false)
    {
    }

    private float Side => 28 * _scale;
    private float Gap => 6 * _scale;

    public bool Settled => DiceTray.Now - _born >= DiceTray.SettleSeconds + DiceTray.DieDelay * _dice.Count;

    public override void _Process(double delta)
    {
        if (!Settled)
        {
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var age = DiceTray.Now - _born;
        var font = ThemeDB.FallbackFont;
        for (var i = 0; i < _dice.Count; i++)
        {
            var die = _dice[i];
            var p = (float)Math.Clamp((age - i * DiceTray.DieDelay) / DiceTray.SettleSeconds, 0, 1);
            var eased = 1 - (1 - p) * (1 - p) * (1 - p);
            var centre = new Vector2(2 + i * (Side + Gap) + Side / 2, Side / 2 + 1);
            DrawSetTransform(centre + new Vector2(-(1 - eased) * 24 * _scale, 0), -(1 - eased) * 1.6f, Vector2.One);
            var body = die.Kept ? (_highContrast ? Colors.White : new Color(0.96f, 0.95f, 0.9f)) : new Color(0.42f, 0.42f, 0.44f);
            var pip = die.Kept ? new Color(0.08f, 0.08f, 0.1f) : new Color(0.22f, 0.22f, 0.24f);
            var half = Side / 2;
            var box = new StyleBoxFlat { BgColor = new Color(body, eased), CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5, CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5 };
            DrawStyleBox(box, new Rect2(-half, -half, Side, Side));
            foreach (var (x, y) in Pips(die.Value))
            {
                DrawCircle(new Vector2(x, y) * Side * 0.27f, Side * 0.085f, new Color(pip, eased));
            }
            if (!die.Kept)
            {
                DrawLine(new Vector2(-half, half), new Vector2(half, -half), new Color(0.9f, 0.3f, 0.3f, eased), 2.5f * _scale);
            }
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            var number = die.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var size = (int)(12 * _scale);
            var width = font.GetStringSize(number, HorizontalAlignment.Left, -1, size).X;
            DrawString(font, new Vector2(centre.X - width / 2, Side + 14 * _scale), number, HorizontalAlignment.Left, -1, size,
                die.Kept ? new Color(0.9f, 0.9f, 0.88f, eased) : new Color(0.6f, 0.6f, 0.62f, eased));
        }
    }

    private static (float X, float Y)[] Pips(int value) => value switch
    {
        1 => [(0, 0)],
        2 => [(-1, -1), (1, 1)],
        3 => [(-1, -1), (0, 0), (1, 1)],
        4 => [(-1, -1), (1, -1), (-1, 1), (1, 1)],
        5 => [(-1, -1), (1, -1), (0, 0), (-1, 1), (1, 1)],
        _ => [(-1, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (1, 1)],
    };
}
