using System.Collections.Immutable;

namespace Rules.Core.State;

// Roster stage R11 (U136): the Court's Goetic spells the engine plays (Warbands pp. 157-165). Each is a flag, so a model's
// `ModelProfile.Spells` is the set it knows. A spell is cast with CastSpell, paid for in BLOOD MARKERS taken from any models
// without CULT or DEMONIC (the caster's own side or the enemy's, anywhere on the battlefield), with no Success Roll; the
// same spell at most once per activation.
[Flags]
public enum SpellKind
{
    None = 0,

    // What is Yours is Mine (cost 1): move 1 BLOOD or BLESSING MARKER from a model in sight to the caster.
    WhatIsYoursIsMine = 1,

    // Exquisite Pain (cost 1-2): the markers paid go onto a model in sight.
    ExquisitePain = 2,

    // Coveted Position (cost 2): swap places with a model within 12" in sight that is over 1" from others.
    CovetedPosition = 4,
}

// Some of one model's BLOOD MARKERS spent on a spell.
public sealed record MarkerPayment(ModelId Model, int Count);

public static class Spells
{
    public static readonly SpellKind All = SpellKind.WhatIsYoursIsMine | SpellKind.ExquisitePain | SpellKind.CovetedPosition;

    // The number of markers a spell may cost: from Min to Max.
    public static (int Min, int Max) Cost(SpellKind spell) => spell switch
    {
        SpellKind.WhatIsYoursIsMine => (1, 1),
        SpellKind.ExquisitePain => (1, 2),
        SpellKind.CovetedPosition => (2, 2),
        _ => throw new ArgumentOutOfRangeException(nameof(spell), "One spell at a time."),
    };

    public static string Name(SpellKind spell) => spell switch
    {
        SpellKind.WhatIsYoursIsMine => "What is Yours is Mine",
        SpellKind.ExquisitePain => "Exquisite Pain",
        SpellKind.CovetedPosition => "Coveted Position",
        _ => spell.ToString(),
    };

    // A single defined spell (not None, not several together).
    public static bool IsOne(SpellKind spell) => spell != SpellKind.None && (spell & (spell - 1)) == 0 && (spell & ~All) == 0;

    public static ImmutableArray<MarkerPayment> None => [];
}
