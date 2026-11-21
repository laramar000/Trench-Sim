namespace Client.Core;

// A rectangle on the screen, in pixels (x right, y down).
public readonly record struct ScreenBox(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public bool Intersects(ScreenBox other) => X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;

    public bool Inside(ScreenBox outer) => X >= outer.X && Y >= outer.Y && Right <= outer.Right && Bottom <= outer.Bottom;

    // A square of the given half-size around a point.
    public static ScreenBox Around(double x, double y, double half) => new(x - half, y - half, 2 * half, 2 * half);
}

// Where a box of dice goes beside its model (visible dice rolls, QoL stage F3): right of the model, else left, above or
// below it (close in, then a little further out), else at a corner: the first of these that stays inside the
// battlefield and covers nothing to avoid (the models in the roll, every label, the dice tray, the action bar). Null
// when none fits (or the model is off screen): the box goes in the tray.
public static class DicePlacement
{
    public const double Gap = 26;

    public static ScreenBox? Beside(double modelX, double modelY, double width, double height, ScreenBox field, IReadOnlyList<ScreenBox> avoid, ScreenBox? previous = null)
    {
        ArgumentNullException.ThrowIfNull(avoid);
        if (!ScreenBox.Around(modelX, modelY, 0).Inside(field))
        {
            return null;
        }
        bool Fits(ScreenBox box) => box.Inside(field) && !avoid.Any(a => a.Intersects(box));
        // Keep the place it had while it still fits, so a box does not jump about as the labels move.
        if (previous is { } kept && Math.Abs(kept.Width - width) < 1 && Math.Abs(kept.Height - height) < 1 && Fits(kept)
            && Math.Abs(kept.X + width / 2 - modelX) < width / 2 + Gap + 80 + 1 && Math.Abs(kept.Y + height / 2 - modelY) < height / 2 + Gap + 80 + 1)
        {
            return kept;
        }
        // Right, left, above and below, close in first, then a little further out, then the four corners.
        var candidates = new List<ScreenBox>();
        foreach (var gap in new[] { Gap, Gap + 40, Gap + 80 })
        {
            candidates.Add(new(modelX + gap, modelY - height / 2, width, height));
            candidates.Add(new(modelX - gap - width, modelY - height / 2, width, height));
            candidates.Add(new(modelX - width / 2, modelY - gap - height, width, height));
            candidates.Add(new(modelX - width / 2, modelY + gap, width, height));
        }
        candidates.Add(new(modelX + Gap, modelY - Gap - height, width, height));
        candidates.Add(new(modelX - Gap - width, modelY - Gap - height, width, height));
        candidates.Add(new(modelX + Gap, modelY + Gap, width, height));
        candidates.Add(new(modelX - Gap - width, modelY + Gap, width, height));
        foreach (var box in candidates)
        {
            if (Fits(box))
            {
                return box;
            }
        }
        return null;
    }
}
