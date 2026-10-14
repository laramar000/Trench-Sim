namespace Rules.Core.State;

// Ids are opaque, content-free names. They are restricted to a small safe alphabet so the
// canonical state text (see StateHash) can never be made ambiguous by an id.
internal static class IdRules
{
    public const int MaxLength = 32;

    public static string Check(string? value, string what)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length is 0 or > MaxLength)
        {
            throw new ArgumentException($"{what} must be 1 to {MaxLength} characters.", what);
        }
        foreach (var c in value)
        {
            var ok = c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' or '.';
            if (!ok)
            {
                throw new ArgumentException($"{what} may only use letters, digits, '-', '_' and '.'.", what);
            }
        }
        return value;
    }
}

public readonly record struct ModelId
{
    public ModelId(string value) => Value = IdRules.Check(value, "ModelId");

    public string Value { get; }

    public override string ToString() => Value;
}
