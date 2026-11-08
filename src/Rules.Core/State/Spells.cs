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

    // Charm of Acedia (cost 1): the first roll of the caster's next ACTION is a Success (U138: its next Success Roll).
    CharmOfAcedia = 8,

    // Black Heart (cost 1): +1 DICE on the caster's next Success Roll (U139: cast ahead of it, between actions).
    BlackHeart = 16,

    // Blood Magic (cost 1): +1 INJURY DICE on the Injury Rolls of the caster's next attack (U139).
    BloodMagic = 32,

    // Shadow-walker (cost 2): the caster's next Retreat this activation draws no free attacks (R15, U140).
    ShadowWalker = 64,

    // Call of the Flesh (cost 2, ACTION that ends the caster's activation): the next enemy to activate must first close in on the caster (R15, U140).
    CallOfTheFlesh = 128,

    // The rest are played in later stages (R16-R18); they are listed so the flags stay stable.
    LightOfTheFallen = 256,
    UncaringGluttony = 512,
    BurningInferno = 1024,
    Slavemaster = 2048,
    LeftHandPath = 4096,
    BlessingOfTheSerpentMoon = 8192,
    OracleBeastCloak = 16384,
    TooProudToFall = 32768,

    // Raining Blood (cost 1-3, Cast Spell ACTION): 1 BLOOD MARKER on each of as many enemy models within 18" as SIN MARKERS were paid (R17, U147).
    RainingBlood = 65536,
}

// October 2026 Review (U149): what a What is Yours is Mine takes from its target, in any mix of kinds (as many in all as SIN MARKERS were paid).
public sealed record MarkerMix(int Blood, int Blessing, int Sin);

// Some of one model's BLOOD MARKERS spent on a spell.
public sealed record MarkerPayment(ModelId Model, int Count);

public static class Spells
{
    public static readonly SpellKind All = SpellKind.WhatIsYoursIsMine | SpellKind.ExquisitePain | SpellKind.CovetedPosition | SpellKind.CharmOfAcedia | SpellKind.BlackHeart | SpellKind.BloodMagic
        | SpellKind.ShadowWalker | SpellKind.CallOfTheFlesh
        | SpellKind.BlessingOfTheSerpentMoon | SpellKind.OracleBeastCloak | SpellKind.TooProudToFall | SpellKind.LightOfTheFallen | SpellKind.RainingBlood | SpellKind.UncaringGluttony | SpellKind.BurningInferno | SpellKind.LeftHandPath;

    // The number of markers a spell may cost: from Min to Max.
    public static (int Min, int Max) Cost(SpellKind spell) => spell switch
    {
        SpellKind.WhatIsYoursIsMine => (1, 3),
        SpellKind.ExquisitePain or SpellKind.RainingBlood or SpellKind.BurningInferno => (1, 3),
        SpellKind.CovetedPosition => (2, 2),
        SpellKind.CharmOfAcedia or SpellKind.BlackHeart or SpellKind.BloodMagic => (1, 1),
        SpellKind.ShadowWalker or SpellKind.CallOfTheFlesh or SpellKind.TooProudToFall or SpellKind.LightOfTheFallen or SpellKind.UncaringGluttony or SpellKind.LeftHandPath => (2, 2),
        SpellKind.BlessingOfTheSerpentMoon => (2, 6),
        SpellKind.OracleBeastCloak => (3, 3),
        _ => throw new ArgumentOutOfRangeException(nameof(spell), "One spell at a time."),
    };

    public static string Name(SpellKind spell) => spell switch
    {
        SpellKind.WhatIsYoursIsMine => "What is Yours is Mine",
        SpellKind.ExquisitePain => "Exquisite Pain",
        SpellKind.CovetedPosition => "Coveted Position",
        SpellKind.CharmOfAcedia => "Charm of Acedia",
        SpellKind.BlackHeart => "Black Heart",
        SpellKind.BloodMagic => "Blood Magic",
        SpellKind.ShadowWalker => "Shadow-walker",
        SpellKind.CallOfTheFlesh => "Call of the Flesh",
        SpellKind.BlessingOfTheSerpentMoon => "Blessing of the Serpent Moon",
        SpellKind.OracleBeastCloak => "Oracle Beast Cloak",
        SpellKind.TooProudToFall => "Too Proud to Fall",
        SpellKind.LightOfTheFallen => "Light of the Fallen",
        SpellKind.RainingBlood => "Raining Blood",
        SpellKind.UncaringGluttony => "Uncaring Gluttony",
        SpellKind.BurningInferno => "Burning Inferno",
        SpellKind.LeftHandPath => "Left-Hand Path",
        _ => spell.ToString(),
    };

    // A single defined spell (not None, not several together).
    public static bool IsOne(SpellKind spell) => spell != SpellKind.None && (spell & (spell - 1)) == 0 && (spell & ~All) == 0;

    public static ImmutableArray<MarkerPayment> None => [];
}
