using Rules.Core;
using Rules.Core.Dice;
using Rules.Core.State;

namespace Client.Core;

// An attack preview's dice: what each roll starts with before any Blood Markers are spent, from the rules' own numbers
// (AttackDice) and the match state. No odds and no expected result: the server rolls. HitPenalty and its reasons are
// for the Success Roll; Risky when the attacker's warband is Shaken; InjuryBonus is +1 for a Melee Attack on a Down
// target (a Critical Success adds one more, which only the dice decide); InjuryModifier is the weapon's modifier and
// the target's Armour together. AttackerMarkers can be spent by the opponent as penalty dice on the Success Roll, and
// TargetMarkers by you as extra injury dice once the attack hits.
public sealed record DicePreview(
    int HitPenalty,
    IReadOnlyList<string> HitReasons,
    bool Risky,
    int InjuryBonus,
    int InjuryModifier,
    int AttackerMarkers,
    int TargetMarkers)
{
    // Elevated position (p. 43, line-of-sight stage L3): +1 DICE on the hit roll when 3" or more above the target.
    public int HitBonus { get; init; }

    // Roster stage R1: the attacker's own dice. OwnHitDice is its Ranged or Melee Characteristic and its weapon's DICE
    // on the Success Roll (OwnHitParts names them); OwnInjuryDice is its weapon's INJURY DICE on the Injury Roll.
    public int OwnHitDice { get; init; }
    public IReadOnlyList<string> OwnHitParts { get; init; } = [];
    public int OwnInjuryDice { get; init; }

    // Roster stage R5c (U107): the INJURY DICE the target's own battlekit adds to attacks that hit it (Holy Smoke: -1).
    public int TargetInjuryDice { get; init; }

    // The Success Roll's dice before markers, as the server rolls them: +DICE and -DICE cancel in pairs (p. 15).
    public int HitNetDice => HitBonus + AimDice - HitPenalty + OwnHitDice;

    // Roster stage R6b (U118): +2 DICE on a Ranged Attack after a successful Aim ACTION this activation.
    public int AimDice { get; init; }

    // The Injury Roll's dice before markers and a Critical Success.
    public int InjuryNetDice => InjuryBonus + OwnInjuryDice + ShotgunInjuryDice + TargetInjuryDice + FinishDice;

    // Roster stage R6a (U117): Finish the Fallen's +1 INJURY DICE at a Down target that is neither CULT nor DEMONIC.
    public int FinishDice { get; init; }

    // Whether Risky comes from a Shaken warband (else from a RISKY weapon), and a Critical Success's INJURY DICE.
    public bool ShakenRisky { get; init; } = true;
    public int CriticalDice { get; init; } = 1;

    // Roster stage R2: SHOTGUN's -1 INJURY DICE at Long Range (in InjuryNetDice and the injury line).
    public int ShotgunInjuryDice { get; init; }

    // The weapon's keywords that change this attack's rolls, named for the preview ("CRITICAL", "IGNORE COVER").
    public IReadOnlyList<string> KeywordNotes { get; init; } = [];

    // Roster stage R4a: the extra BLOOD MARKERS the weapon's SHRAPNEL, GAS and FIRE put on this target.
    public int ExtraMarkers { get; init; }

    // Roster stage R4b (U102): the models a BLAST attack hits if it hits, the target first (empty without BLAST).
    public IReadOnlyList<ModelId> BlastHits { get; init; } = [];

    // Roster stage R3: weapon 1 is the model's second weapon of that kind (ModelProfile.Using).
    internal static DicePreview Of(MatchState state, ModelState attacker, ModelState target, AttackKind kind, int hitPenalty, IReadOnlyList<string> reasons, int hitBonus = 0, int shotgunInjury = 0, int weapon = 0)
    {
        var setup = state.Setup!;
        var profile = setup.ProfileOf(attacker.Id)!.Using(kind, weapon);
        var modifier = InjuryRoll.CombineModifiers(AttackDice.InjuryModifiers(profile, setup.ProfileOf(target.Id)!, kind));
        var bonus = AttackDice.InjuryBonus(critical: false, meleeOnDownTarget: kind == AttackKind.Melee && target.Status == ModelStatus.Down);
        return new(hitPenalty, reasons, AttackDice.Risky(profile, kind, state.IsShaken(attacker.Seat)), bonus, modifier, attacker.BloodMarkers, target.BloodMarkers)
        {
            ShakenRisky = state.IsShaken(attacker.Seat),
            ShotgunInjuryDice = shotgunInjury,
            // SHRAPNEL, GAS and FIRE are named only when the target does not NEGATE them (roster stage R4a).
            KeywordNotes = [.. Notes(profile.AttackKeywords(kind) & ~setup.ProfileOf(target.Id)!.Negates & ~(profile.Traits.HasFlag(ModelTraits.NegateHeavy) ? WeaponKeywords.Heavy : WeaponKeywords.None), kind), .. CleaveNote(profile, kind), .. BlastNote(state, attacker, target, profile, kind, weapon)],
            BlastHits = kind == AttackKind.Ranged ? TurnSequence.BlastHits(state, attacker.Id, target.Id, weapon) : [],
            ExtraMarkers = AttackDice.ExtraMarkers(profile, kind, setup.ProfileOf(target.Id)!),
            CriticalDice = AttackDice.CriticalInjuryDice(profile.AttackKeywords(kind)),
            HitBonus = hitBonus,
            AimDice = kind == AttackKind.Ranged ? TurnSequence.AimedDice(state, attacker.Id) : 0,
            OwnHitDice = AttackDice.OwnDice(profile, kind),
            OwnHitParts = OwnParts(profile, kind),
            OwnInjuryDice = AttackDice.OwnInjuryDice(profile, kind),
            TargetInjuryDice = setup.ProfileOf(target.Id)!.InjuryDiceTaken,
            FinishDice = kind == AttackKind.Melee && profile.FinishesTheFallen && target.Status == ModelStatus.Down
                && (setup.ProfileOf(target.Id)!.Traits & (ModelTraits.Cult | ModelTraits.Demonic)) == 0 ? 1 : 0,
        };
    }

    // "Ranged +1", "ranged weapon +1": the parts of the attacker's own Success Roll dice that are not zero.
    public static IReadOnlyList<string> OwnParts(ModelProfile profile, AttackKind kind)
    {
        var (characteristic, weapon) = OwnSplit(profile, kind);
        var parts = new List<string>();
        if (characteristic != 0)
        {
            parts.Add($"{CharacteristicWord(profile, kind)} {characteristic:+0;-0}");
        }
        if (weapon != 0)
        {
            parts.Add($"{WeaponWord(profile, kind)} {weapon:+0;-0}");
        }
        return parts;
    }

    // The model's characteristic and its weapon's DICE, the two parts of AttackDice.OwnDice.
    public static (int Characteristic, int Weapon) OwnSplit(ModelProfile profile, AttackKind kind) =>
        kind == AttackKind.Melee ? (profile.MeleeCharacteristicUsed, profile.MeleeAttackDice - profile.MeleeCharacteristicUsed) : (profile.RangedDice, profile.RangedWeaponDice);

    // The name of the characteristic an attack uses: Ranged, Melee, or Ranged for a PISTOL in melee whose Ranged is better.
    public static string CharacteristicWord(ModelProfile profile, AttackKind kind) =>
        kind == AttackKind.Melee && profile.MeleeCharacteristicUsed != profile.MeleeDice ? "Ranged (PISTOL)"
        : kind == AttackKind.Melee ? "Melee" : "Ranged";

    // The keywords the engine plays that this attack's weapon has, in the book's words.
    public static IReadOnlyList<string> Notes(WeaponKeywords k, AttackKind kind)
    {
        var notes = new List<string>();
        void Add(WeaponKeywords flag, string text)
        {
            if (k.HasFlag(flag))
            {
                notes.Add(text);
            }
        }
        Add(WeaponKeywords.Critical, "CRITICAL: +2 INJURY DICE on a critical hit");
        Add(WeaponKeywords.IgnoreArmour, "IGNORE ARMOUR: the target's Armour does not count");
        Add(WeaponKeywords.ArmourPiercing, "ARMOUR PIERCING: the target's Armour counts 1 less");
        Add(WeaponKeywords.Risky, "RISKY: a failed hit roll ends the activation");
        Add(WeaponKeywords.Reload, "RELOAD: the activation ends after this attack");
        Add(WeaponKeywords.Shrapnel, "SHRAPNEL: +1 BLOOD MARKER on the target after the Injury Roll");
        Add(WeaponKeywords.Gas, "GAS: +1 BLOOD MARKER on the target after the Injury Roll");
        Add(WeaponKeywords.Fire, "FIRE: +1 BLOOD MARKER on the target after the Injury Roll");
        Add(WeaponKeywords.Deadly, "DEADLY: the Injury Roll is 3D6 (INJURY DICE keep the 3 highest or lowest)");
        if (kind == AttackKind.Ranged)
        {
            Add(WeaponKeywords.Shotgun, "SHOTGUN: at Long Range -1 INJURY DICE instead of -1 DICE");
            Add(WeaponKeywords.IgnoreCover, "IGNORE COVER");
            Add(WeaponKeywords.IgnoreLongRange, "IGNORE LONG RANGE");
            Add(WeaponKeywords.IgnoreElevatedPosition, "IGNORE ELEVATED POSITION");
            Add(WeaponKeywords.Assault, "ASSAULT: may still Charge or Fight this activation");
            Add(WeaponKeywords.Heavy, "HEAVY: no Move, Charge, Retreat or Dash this activation");
            Add(WeaponKeywords.Scatter, "SCATTER: a miss scatters (7 - the roll)\"; the opponent picks where");
        }
        return notes;
    }

    // Roster stage R3: CLEAVE (X) makes a Fight X Melee Attacks with the weapon (p. 53).
    public static IReadOnlyList<string> CleaveNote(ModelProfile profile, AttackKind kind) =>
        kind == AttackKind.Melee && profile.MeleeAttacksWith(0) > 1 ? [$"CLEAVE {profile.MeleeAttacksWith(0)}: this weapon makes {profile.MeleeAttacksWith(0)} Melee Attacks in a Fight"] : [];

    // Roster stage R4b: BLAST (X"): who else the blast hits if the attack hits (p. 53), the attacker's own models marked.
    private static IReadOnlyList<string> BlastNote(MatchState state, ModelState attacker, ModelState target, ModelProfile profile, AttackKind kind, int weapon)
    {
        if (kind != AttackKind.Ranged || profile.RangedWeaponBlast == 0)
        {
            return [];
        }
        var others = TurnSequence.BlastHits(state, attacker.Id, target.Id, weapon).Skip(1)
            .Select(id => id.Value.ToUpperInvariant() + (state.FindModel(id)?.Seat == attacker.Seat ? " (yours)" : "")).ToList();
        return [$"BLAST {profile.RangedWeaponBlast}\": on a hit, also hits {(others.Count == 0 ? "nobody else" : string.Join(", ", others))}"];
    }

    public static string WeaponWord(ModelProfile profile, AttackKind kind) =>
        kind == AttackKind.Melee && profile.HasMeleeWeapon ? "melee weapon" : "ranged weapon";

    // The lines the preview card and the panel show, short enough to read at a glance.
    public IReadOnlyList<string> Lines(ModelId attacker, ModelId target)
    {
        var a = attacker.Value.ToUpperInvariant();
        var t = target.Value.ToUpperInvariant();
        var lines = new List<string>
        {
            HitPenalty == 0 ? "Hit roll: no penalty dice" : $"Hit roll: -{HitPenalty} DICE ({string.Join("; ", HitReasons)})",
        };
        if (HitBonus > 0)
        {
            lines.Insert(0, $"Hit roll: +{HitBonus} DICE (Elevated position, 3\" or more above the target, p. 43)");
        }
        if (AimDice > 0)
        {
            lines.Insert(0, $"Hit roll: +{AimDice} DICE (Aim ACTION this activation, Warbands p. 28)");
        }
        if (OwnHitDice != 0)
        {
            lines.Insert(0, $"Hit roll: {OwnHitDice:+0;-0} DICE ({string.Join(", ", OwnHitParts)})");
        }
        if (Risky)
        {
            lines.Add(ShakenRisky ? "RISKY: your warband is Shaken" : "RISKY: the weapon is RISKY");
        }
        lines.AddRange(KeywordNotes);
        var injury = new List<string>();
        if (InjuryBonus > 0)
        {
            injury.Add($"+{InjuryBonus} DICE (target Down)");
        }
        if (OwnInjuryDice != 0)
        {
            injury.Add($"{OwnInjuryDice:+0;-0} DICE (weapon)");
        }
        if (ShotgunInjuryDice != 0)
        {
            injury.Add($"{ShotgunInjuryDice:+0;-0} DICE (SHOTGUN at Long Range)");
        }
        if (TargetInjuryDice != 0)
        {
            injury.Add($"{TargetInjuryDice:+0;-0} DICE (target's battlekit)");
        }
        if (FinishDice != 0)
        {
            injury.Add($"+{FinishDice} DICE (Finish the Fallen)");
        }
        if (InjuryModifier != 0)
        {
            injury.Add($"modifier {(InjuryModifier > 0 ? "+" : "")}{InjuryModifier}");
        }
        lines.Add($"Injury roll: {(injury.Count == 0 ? "no extra dice" : string.Join(", ", injury))}; +{CriticalDice} DICE on a critical hit");
        if (AttackerMarkers > 0)
        {
            lines.Add($"Opponent may spend {a}'s {Markers(AttackerMarkers)}: -1 DICE each");
        }
        if (TargetMarkers > 0)
        {
            lines.Add($"You may spend {t}'s {Markers(TargetMarkers)}: +1 INJURY DICE each");
        }
        return lines;
    }

    private static string Markers(int n) => $"{n} Blood Marker{(n == 1 ? "" : "s")}";
}
