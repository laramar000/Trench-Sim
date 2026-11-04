using Rules.Core.Geometry;

namespace Rules.Core.State;

// What a model can do, as the rules need it: how far it moves, how big its base is, and its one ranged weapon.
// Names, art and warband entries live in content outside Rules.Core; nothing here says who the model is.
// RangedRange zero means no ranged weapon; HasMeleeWeapon says whether the model has one Melee Weapon (pp. 45-46;
// a second ranged or Melee Weapon, Off-Hand attacks and CLEAVE since roster stage R3, U100). Injury modifiers follow Injury Rolls (pp. 46, 48): the weapon's
// modifier is added by the attacker, Armour (zero or negative) belongs to the target.
// RangedAlsoMelee: the ranged weapon has "Melee" under its Range (a Dual-purpose weapon such as a Pistol, 12"/Melee;
// pp. 56, 68, 70), so it can also make Melee Attacks (C19a). A model with a Melee Weapon attacks with that one in melee
// (the choice between the two is not modelled, U54); without one, it attacks with the dual-purpose weapon and that
// weapon's own injury modifier.
public sealed record ModelProfile
{
    public const int MaxModifier = 3;

    public ModelProfile(Length movement, Length baseRadius, Length rangedRange, int rangedInjuryModifier = 0, int armour = 0, bool hasMeleeWeapon = false, int meleeInjuryModifier = 0)
    {
        if (movement <= Length.Zero || movement > Length.FromInches(24))
        {
            throw new ArgumentOutOfRangeException(nameof(movement), "Movement must be above 0 and at most 24 inches.");
        }
        if (baseRadius <= Length.Zero || baseRadius > Length.FromInches(3))
        {
            throw new ArgumentOutOfRangeException(nameof(baseRadius), "A base radius must be above 0 and at most 3 inches.");
        }
        if (rangedRange < Length.Zero || rangedRange > Length.FromInches(60))
        {
            throw new ArgumentOutOfRangeException(nameof(rangedRange), "Range must be 0 (no ranged weapon) to 60 inches.");
        }
        if (rangedRange.Thousandths % 2 != 0)
        {
            throw new ArgumentException("Range must be an even number of thousandths so half the range is exact.", nameof(rangedRange));
        }
        if (rangedInjuryModifier is < -MaxModifier or > MaxModifier)
        {
            throw new ArgumentOutOfRangeException(nameof(rangedInjuryModifier));
        }
        if (armour is > 0 or < -MaxModifier)
        {
            throw new ArgumentOutOfRangeException(nameof(armour), "Armour is an injury modifier of 0 to -3.");
        }
        if (meleeInjuryModifier is < -MaxModifier or > MaxModifier)
        {
            throw new ArgumentOutOfRangeException(nameof(meleeInjuryModifier));
        }
        if (!hasMeleeWeapon && meleeInjuryModifier != 0)
        {
            throw new ArgumentException("A model without a Melee Weapon has no melee injury modifier.", nameof(meleeInjuryModifier));
        }
        Movement = movement;
        BaseRadius = baseRadius;
        RangedRange = rangedRange;
        RangedInjuryModifier = rangedInjuryModifier;
        Armour = armour;
        HasMeleeWeapon = hasMeleeWeapon;
        MeleeInjuryModifier = meleeInjuryModifier;
    }

    public Length Movement { get; }
    public Length BaseRadius { get; }
    public Length RangedRange { get; }
    public int RangedInjuryModifier { get; }
    public int Armour { get; }
    public bool HasMeleeWeapon { get; }
    public int MeleeInjuryModifier { get; }

    // An init property rather than a constructor parameter, and left off the wire when false, so every setup without it
    // (all existing records and their content fingerprints) is written exactly as before.
    public bool RangedAlsoMelee
    {
        get => _rangedAlsoMelee;
        init
        {
            if (value && RangedRange <= Length.Zero)
            {
                throw new ArgumentException("Only a ranged weapon can also be a Melee Weapon (\"12\"/Melee\").", nameof(RangedAlsoMelee));
            }
            _rangedAlsoMelee = value;
        }
    }

    private readonly bool _rangedAlsoMelee;

    // Roster stage R1: the dice a model's own profile adds to its attacks, as +DICE and -DICE (p. 15). RangedDice and
    // MeleeDice are the model's Ranged and Melee Characteristics; the weapon fields are the DICE and INJURY DICE of
    // its ranged weapon and its Melee Weapon. All are init properties, zero unless set, and left off the wire and out
    // of the state hash when zero, so every setup without them (all existing records) is written exactly as before.
    public const int MaxDice = 6;

    public int RangedDice { get => _rangedDice; init => _rangedDice = Dice(value, nameof(RangedDice)); }

    public int MeleeDice { get => _meleeDice; init => _meleeDice = Dice(value, nameof(MeleeDice)); }

    public int RangedWeaponDice { get => _rangedWeaponDice; init => _rangedWeaponDice = Dice(RangedWeapon(value, nameof(RangedWeaponDice)), nameof(RangedWeaponDice)); }

    public int RangedWeaponInjuryDice { get => _rangedWeaponInjuryDice; init => _rangedWeaponInjuryDice = Dice(RangedWeapon(value, nameof(RangedWeaponInjuryDice)), nameof(RangedWeaponInjuryDice)); }

    public int MeleeWeaponDice { get => _meleeWeaponDice; init => _meleeWeaponDice = Dice(MeleeWeapon(value, nameof(MeleeWeaponDice)), nameof(MeleeWeaponDice)); }

    public int MeleeWeaponInjuryDice { get => _meleeWeaponInjuryDice; init => _meleeWeaponInjuryDice = Dice(MeleeWeapon(value, nameof(MeleeWeaponInjuryDice)), nameof(MeleeWeaponInjuryDice)); }

    private readonly int _rangedDice;
    private readonly int _meleeDice;
    private readonly int _rangedWeaponDice;
    private readonly int _rangedWeaponInjuryDice;
    private readonly int _meleeWeaponDice;
    private readonly int _meleeWeaponInjuryDice;

    // Roster stage R2: the keywords of the ranged weapon and of the Melee Weapon (WeaponKeywords). Init properties,
    // None unless set, left off the wire and out of the state hash when None.
    public WeaponKeywords RangedWeaponKeywords
    {
        get => _rangedWeaponKeywords;
        init => _rangedWeaponKeywords = value != WeaponKeywords.None && !HasRangedWeapon
            ? throw new ArgumentException("Only a model with a ranged weapon has ranged weapon keywords.", nameof(RangedWeaponKeywords))
            : Known(value, nameof(RangedWeaponKeywords));
    }

    public WeaponKeywords MeleeWeaponKeywords
    {
        get => _meleeWeaponKeywords;
        init => _meleeWeaponKeywords = value != WeaponKeywords.None && !HasMeleeWeapon
            ? throw new ArgumentException("Only a model with a Melee Weapon has melee weapon keywords.", nameof(MeleeWeaponKeywords))
            : Known(value, nameof(MeleeWeaponKeywords));
    }

    private readonly WeaponKeywords _rangedWeaponKeywords;
    private readonly WeaponKeywords _meleeWeaponKeywords;

    private const WeaponKeywords AllKeywords = WeaponKeywords.Critical | WeaponKeywords.IgnoreArmour | WeaponKeywords.ArmourPiercing
        | WeaponKeywords.Shotgun | WeaponKeywords.Risky | WeaponKeywords.Pistol | WeaponKeywords.IgnoreCover
        | WeaponKeywords.IgnoreLongRange | WeaponKeywords.IgnoreElevatedPosition | WeaponKeywords.Assault | WeaponKeywords.Heavy
        | WeaponKeywords.Reload | WeaponKeywords.Block | WeaponKeywords.Shrapnel | WeaponKeywords.Gas | WeaponKeywords.Fire
        | WeaponKeywords.Scatter | WeaponKeywords.Deadly;

    // Roster stage R4a: the keywords that put an extra BLOOD MARKER on the target, which NEGATE can turn away.
    public const WeaponKeywords MarkerKeywords = WeaponKeywords.Shrapnel | WeaponKeywords.Gas | WeaponKeywords.Fire;

    // HEAVY battlekit (a HEAVY weapon here; other HEAVY pieces are equipment, not played): no Charge Bonus (p. 55),
    // unless the model has NEGATE HEAVY (R5b).
    public bool NoChargeBonus => (AllWeaponKeywords & WeaponKeywords.Heavy) != 0 && !Traits.HasFlag(ModelTraits.NegateHeavy);

    // ---- Roster stage R5b (U106): model keywords ----

    private const ModelTraits AllTraits = ModelTraits.Fear | ModelTraits.NegateFear | ModelTraits.Tough | ModelTraits.NegateHeavy | ModelTraits.Leader
        | ModelTraits.Principality | ModelTraits.Cult | ModelTraits.Demonic;

    // FEAR, NEGATE FEAR, TOUGH, NEGATE HEAVY (STRONG) and LEADER; roster stage R6a: PRINCIPALITY, CULT and DEMONIC.
    public ModelTraits Traits
    {
        get => _traits;
        init => _traits = (value & ~AllTraits) != 0 ? throw new ArgumentOutOfRangeException(nameof(Traits), "Unknown model keywords.") : value;
    }

    private readonly ModelTraits _traits;

    // FEAR does not affect a model that has FEAR itself or NEGATE FEAR (pp. 54, 56).
    public bool IgnoresFear => (Traits & (ModelTraits.Fear | ModelTraits.NegateFear)) != 0;

    // ---- Roster stage R5c (U107): battlekit that changes Injury Rolls ----

    // INJURY DICE added to the Injury Rolls of attacks that hit the model (Holy Smoke, Zealot's Draught: -1).
    public int InjuryDiceTaken { get => _injuryTaken; init => _injuryTaken = Dice(value, nameof(InjuryDiceTaken)); }

    private readonly int _injuryTaken;

    // INJURY MODIFIER added to the Injury Rolls of the model's Melee Attacks, whatever it fights with (a Grovelling
    // Devotee: +1 each), as a modifier of its own (p. 48).
    public int MeleeInjuryBonus
    {
        get => _meleeInjuryBonus;
        init => _meleeInjuryBonus = value is < 0 or > MaxModifier ? throw new ArgumentOutOfRangeException(nameof(MeleeInjuryBonus), $"The melee injury bonus is 0 to +{MaxModifier}.") : value;
    }

    private readonly int _meleeInjuryBonus;

    // ---- Roster stage R5d (U108): battlekit for Risky rolls and Cover ----

    // A failed Risky Success Roll does not end the model's activation (the Supreme Pontiff's Crucifix); what else the
    // failure does still happens.
    public bool KeepsGoingAfterRisky { get; init; }

    // +DICE to the model's Risky Success Rolls to climb a sheer surface (a Mountaineer Kit: +1).
    public int ClimbDice { get => _climbDice; init => _climbDice = value is < 0 or > MaxDice ? throw new ArgumentOutOfRangeException(nameof(ClimbDice), $"Climb dice are 0 to +{MaxDice}.") : value; }

    private readonly int _climbDice;

    // The model carries a Musical Instrument: +1 DICE to the Dash rolls of friendly models within 4" of it.
    public bool Fanfare { get; init; }

    // Cover gives -2 DICE instead of -1 DICE to Ranged Attacks at the model (a Cloak of the Mountain).
    public bool BlendsIn { get; init; }

    // ---- Roster stage R5e (U109): an aura on Ranged Attacks and a ward against BLOOD MARKERS ----

    // The model carries the Urn of the Bitter Ashes: -1 DICE to Ranged Attacks that target it or a model within 3" of it.
    public bool SinisterWhispers { get; init; }

    // The model's Sacrificial Lamb was sacrificed when it was deployed: the first BLOOD MARKER placed on it is ignored.
    public bool WardedByBlood { get; init; }

    // ---- Roster stage R5f (U110): the COVER keyword (p. 54) ----

    // The model always has COVER (a shield with the keyword).
    public bool HasCover { get; init; }

    // The model has a Shovel: starting the game on Open terrain it has COVER until it moves away from where it started.
    public bool DigsIn { get; init; }

    // ---- Roster stage R5g (U112): BLOOD MARKERS placed on nearby enemies ----

    // When the model ends an activation, each enemy model within 1" gets 1 BLOOD MARKER (a Crown of Hellfire).
    public bool BurnsAtActivationEnd { get; init; }

    // When the model is taken Out of Action, before it is removed, each enemy model within 1" gets 1 BLOOD MARKER
    // (a Damned Pact).
    public bool BurnsWhenTakenOut { get; init; }

    // ---- Field Shrine (Battlekit p. 82; U127) ----

    // The model carries a Field Shrine: after it is deployed, its player may deploy the shrine too.
    public bool FieldShrine { get; init; }

    // ---- Roster stage R5h (U113): the Treat ACTION ----

    // The model has a Medi-kit: it can take a Treat ACTION (p. 84).
    public bool Treats { get; init; }

    // The model has Holy Water of Lalibela: +1 DICE to its Success Rolls for ACTIONS that can remove BLOOD MARKERS (its
    // Treat ACTION; Warbands p. 44).
    public bool HealingWaters { get; init; }

    // ---- Roster stage R5i (U114): Piston Legs ----

    // The model can take a Grind to Dust ACTION (Warbands p. 66): a Melee Attack, with no Melee Weapon, at a Down enemy
    // within 1" on a base of 32 mm or smaller. Its attack is the melee "weapon" GrindWeapon.
    public bool GrindsToDust { get; init; }

    // Roster stage R6d (U120): the model can take a Trample ACTION (Warbands pp. 140, 88): a Melee Attack without a
    // weapon, with IGNORE ARMOUR, at a Down enemy. Its attack is the melee "weapon" TrampleWeapon.
    public bool Tramples { get; init; }

    // Roster stage R6d (U120): Overwhelming Horde (Warbands p. 137): its attacks without a weapon (its weapon 0, which the
    // roster gave it from the ability) get +1 DICE for each other friendly model within 3".
    public bool Horde { get; init; }

    // ---- Roster stage R6e (U121): ACTIONS that help friends ----

    // Enforced Orthodoxy ACTION (Warbands p. 55): a Risky Success Roll at +1 DICE; on a Success every Down friendly model
    // within 8" stands up.
    public bool EnforcesOrthodoxy { get; init; }

    // Laying on of Hands ACTION (p. 53): a Success Roll; on a Success 1 BLOOD MARKER (3 on a Critical) comes off a friendly
    // model within 6", the model itself included.
    public bool LaysOnHands { get; init; }

    // ---- Roster stage R5j (U115): the Martyrdom Device ----

    // The model can take a Trigger Martyrdom Device ACTION once per game (Warbands p. 58): an Injury Roll for every model
    // within 3" of it, itself included.
    public bool Martyrdom { get; init; }

    // ---- Roster stage R5k (U116): a Talisman (a Blessed Icon or an Unholy Trinket) ----

    // How many Talismans the model carries (0 to 2). Each is used once per game: when a Risky Success Roll of the model is
    // a Failure, its player can say it uses one, and the activation does not end (core rulebook pp. 81, 86).
    public int Talismans { get => _talismans; init => _talismans = value is < 0 or > MaxTalismans ? throw new ArgumentOutOfRangeException(nameof(Talismans), $"A model carries 0 to {MaxTalismans} Talismans.") : value; }

    private readonly int _talismans;

    public const int MaxTalismans = 2;

    // ---- Roster stage R6a (U117): New Antioch unit abilities ----

    // Absolute Faith (Sniper Priest, Warbands p. 28): the opponent cannot spend BLOOD MARKERS for -DICE on its Ranged Attacks.
    public bool ShotsIgnoreMarkers { get; init; }

    // Expert Medic (Combat Medic, Warbands p. 34): +2 DICE to its Treat ACTION's Risky Success Roll.
    public bool ExpertMedic { get; init; }

    // Finish the Fallen (Combat Medic, p. 34): +1 INJURY DICE to its Melee Attacks' Injury Rolls at a Down target that is
    // neither CULT nor DEMONIC.
    public bool FinishesTheFallen { get; init; }

    // Shock Charge (Shock Trooper, p. 32): its Charge Bonus is the highest of 2D6.
    public bool ExtraChargeDie { get; init; }

    // Onward Christian Soldiers! (Trench Cleric, p. 29): friendly PRINCIPALITY models within 8" of it have NEGATE FEAR.
    public bool InspiresFaithful { get; init; }

    // ---- Roster stage R6b (U118): ACTIONS a model takes on itself ----

    // Aim ACTION (Warbands p. 28): a Risky Success Roll at +2 DICE; on a Success its Ranged Attacks get +2 DICE for the
    // rest of the activation.
    public bool Aims { get; init; }

    // Fortify ACTION (Warbands pp. 33, 85): a Risky Success Roll; on a Success it has COVER until it moves.
    public bool Fortifies { get; init; }

    // ---- Roster stage R6c (U119): unit abilities that change a number ----

    // Agile (Warbands pp. 57, 86): +1 DICE to its Risky rolls to Dash or climb (the engine has no Jump or Diving Charge).
    public bool Agile { get; init; }

    // Frightening Speed, Loping Dash, Mad Dash (pp. 138, 113, 58): +1 DICE to the Risky roll of its Dash ACTION.
    public bool SwiftDash { get; init; }

    // Frightening Speed (p. 138): its Movement is not halved when it stands up.
    public bool QuickToRise { get; init; }
    public bool Hateful { get; init; }
    // Hide ACTION (Warbands p. 110; roster stage R7c, U130): a Risky Success Roll at +1 DICE while touching terrain at least 1/2" high; on a Success it cannot be targeted by Ranged Attacks or Charges until it acts or an enemy comes within 1.5".
    public bool Hides { get; init; }
    // Defuse Mine (roster stage R7b, U129): a Risky roll when its move halts at a Mine Marker removes the marker.
    public bool DefusesMines { get; init; }

    // Undead Fortitude (p. 134): -1 INJURY DICE to Injury Rolls against it, unless the attack has FIRE.
    public bool UndeadFortitude { get; init; }

    // Abiotic Life (p. 116): -1 INJURY DICE to Injury Rolls against it from attacks with GAS.
    public bool AbioticLife { get; init; }

    // Stealth Generator (p. 110): -1 DICE to Ranged Attacks that target it.
    public bool Stealthy { get; init; }

    // Mubarizun (p. 81): +1 INJURY DICE to its attacks' Injury Rolls against TOUGH targets.
    public bool HuntsTheTough { get; init; }

    // Counter-Charge (p. 87): when its first ACTION of an activation is a Charge, +1 DICE to its Melee Attacks for the
    // rest of that activation.
    public bool CounterCharge { get; init; }

    // Memento Mori (p. 53): its first Out of Action result counts as No Effect (it is never TOUGH).
    public bool MementoMori { get; init; }

    // The keywords of every weapon the model has.
    public WeaponKeywords AllWeaponKeywords => RangedWeaponKeywords | MeleeWeaponKeywords
        | (SecondRangedWeapon?.Keywords ?? WeaponKeywords.None) | (SecondMeleeWeapon?.Keywords ?? WeaponKeywords.None)
        | (Grenade?.Keywords ?? WeaponKeywords.None);

    internal static WeaponKeywords Known(WeaponKeywords value, string name) => (value & ~AllKeywords) != 0
        ? throw new ArgumentOutOfRangeException(name, "Unknown weapon keywords.")
        : value;

    // ---- Roster stage R3 (U100): second weapons, CLEAVE, BLOCK and IMPERVIOUS ----
    // All init properties, left off the wire and out of the state hash when absent, zero or false.

    public const int MaxCleave = 6;

    // A second ranged weapon (two 1-handed ranged weapons, p. 69): a Shoot ACTION picks one of the two (weapon 1).
    public Weapon? SecondRangedWeapon
    {
        get => _secondRanged;
        init => _secondRanged = value is null ? null
            : !HasRangedWeapon ? throw new ArgumentException("A second ranged weapon needs a first one.", nameof(SecondRangedWeapon))
            : !value.IsRanged ? throw new ArgumentException("A second ranged weapon has a range.", nameof(SecondRangedWeapon))
            : value;
    }

    // A second Melee Weapon (Multiple Melee Weapons, p. 45): a Fight ACTION makes an attack with each, the second with
    // -1 DICE (Off-Hand). A dual-purpose weapon used in melee as a second weapon is given here as a Melee Weapon.
    public Weapon? SecondMeleeWeapon
    {
        get => _secondMelee;
        init => _secondMelee = value is null ? null
            : !CanMakeMeleeAttack ? throw new ArgumentException("A second Melee Weapon needs a first one.", nameof(SecondMeleeWeapon))
            : value.IsRanged ? throw new ArgumentException("A second Melee Weapon has no range.", nameof(SecondMeleeWeapon))
            : value;
    }

    // CLEAVE (X) of the Melee Weapon (p. 53): a Fight with it makes X Melee Attacks; 0 means no CLEAVE.
    public int MeleeWeaponCleave
    {
        get => _meleeCleave;
        init => _meleeCleave = value is < 0 or > MaxCleave ? throw new ArgumentOutOfRangeException(nameof(MeleeWeaponCleave), $"CLEAVE is 0 (none) to {MaxCleave}.")
            : value != 0 && !HasMeleeWeapon ? throw new ArgumentException("Only a Melee Weapon has CLEAVE.", nameof(MeleeWeaponCleave))
            : value;
    }

    // The model itself has BLOCK (p. 53), as well as any weapon with it.
    public bool Block { get; init; }

    // The part of Armour that comes from IMPERVIOUS battlekit (p. 55), which IGNORE ARMOUR and ARMOUR PIERCING leave
    // alone: from Armour up to 0.
    public int ImperviousArmour
    {
        get => _impervious;
        init => _impervious = value > 0 || value < Armour ? throw new ArgumentOutOfRangeException(nameof(ImperviousArmour), "The IMPERVIOUS part of Armour is between the Armour and 0.") : value;
    }

    // ---- Roster stage R4a (U101): grenades and NEGATE ----

    // The model's grenades (one type, p. 69), thrown with a Shoot ACTION as weapon 2 (p. 68). A model without a
    // ranged weapon may still have them.
    public Weapon? Grenade
    {
        get => _grenade;
        init => _grenade = value is null ? null
            : !value.IsRanged || value.AlsoMelee ? throw new ArgumentException("Grenades are thrown: they have a range and are not Melee.", nameof(Grenade))
            : value;
    }

    // NEGATE SHRAPNEL, NEGATE GAS, NEGATE FIRE (p. 56; DEMONIC has NEGATE FIRE, p. 54): those keywords put no extra
    // BLOOD MARKER on the model.
    public WeaponKeywords Negates
    {
        get => _negates;
        init => _negates = (value & ~MarkerKeywords) != 0 ? throw new ArgumentOutOfRangeException(nameof(Negates), "Only SHRAPNEL, GAS and FIRE can be negated.") : value;
    }

    // ---- Roster stage R4b (U102): BLAST ----

    public const int MaxBlast = 6;

    // BLAST (X") of the ranged weapon (p. 53), in inches; 0 means no BLAST. A second ranged weapon and grenades carry
    // their own (Weapon.Blast).
    public int RangedWeaponBlast
    {
        get => _rangedBlast;
        init => _rangedBlast = value is < 0 or > MaxBlast ? throw new ArgumentOutOfRangeException(nameof(RangedWeaponBlast), $"BLAST is 0 (none) to {MaxBlast} inches.")
            : value != 0 && !HasRangedWeapon ? throw new ArgumentException("Only a ranged weapon has BLAST.", nameof(RangedWeaponBlast))
            : value;
    }

    private readonly int _rangedBlast;
    private readonly Weapon? _grenade;
    private readonly WeaponKeywords _negates;
    private readonly Weapon? _secondRanged;
    private readonly Weapon? _secondMelee;
    private readonly int _meleeCleave;
    private readonly int _impervious;

    // BLOCK on the model or on any of its weapons (p. 53).
    public bool HasBlock => Block || AllWeaponKeywords.HasFlag(WeaponKeywords.Block);

    // How many weapons of a kind the model can attack with: 0, 1 or 2.
    public int Weapons(AttackKind kind) => kind == AttackKind.Melee
        ? CanMakeMeleeAttack ? SecondMeleeWeapon is null ? 1 : 2 : 0
        : HasRangedWeapon ? SecondRangedWeapon is null ? 1 : 2 : 0;

    // Whether the model has that weapon: 0 its first ranged or Melee Weapon (or dual-purpose one), 1 its second, and for
    // ranged attacks 2 its grenades.
    public bool Has(AttackKind kind, int weapon) => weapon switch
    {
        0 or 1 => Weapons(kind) > weapon,
        2 => kind == AttackKind.Ranged && Grenade is not null,
        3 => kind == AttackKind.Melee && GrindsToDust,
        4 => kind == AttackKind.Melee && Tramples,
        _ => false,
    };

    public const int GrenadeWeapon = 2;

    // Roster stage R5i (U114): a Grind to Dust ACTION's Melee Attack, made without a Melee Weapon.
    public const int GrindWeapon = 3;

    // Roster stage R6d (U120): a Trample ACTION's Melee Attack, made without a Melee Weapon, with IGNORE ARMOUR.
    public const int TrampleWeapon = 4;

    // The Melee Attacks that are an ACTION of their own, not a Fight: Grind to Dust and Trample.
    public static bool IsActionAttack(int weapon) => weapon is GrindWeapon or TrampleWeapon;

    // The Melee Attacks a Fight makes with a weapon: CLEAVE (X), else one.
    public int MeleeAttacksWith(int weapon) => IsActionAttack(weapon) ? 1 : Math.Max(1, weapon == 0 ? (HasMeleeWeapon ? MeleeWeaponCleave : 0) : SecondMeleeWeapon?.Cleave ?? 0);

    // The profile as an attack with one of the model's weapons sees it: weapon 0 is the profile itself; weapon 1 puts
    // the second weapon of that kind in the first one's place, so every rule about "the weapon" reads that weapon.
    public ModelProfile Using(AttackKind kind, int weapon)
    {
        if (weapon == 0)
        {
            return this;
        }
        if (!Has(kind, weapon))
        {
            throw new ArgumentOutOfRangeException(nameof(weapon), "The model has no such weapon.");
        }
        if (kind == AttackKind.Ranged)
        {
            var w = weapon == GrenadeWeapon ? Grenade! : SecondRangedWeapon!;
            return new ModelProfile(Movement, BaseRadius, w.Range, w.InjuryModifier, Armour, HasMeleeWeapon, MeleeInjuryModifier)
            {
                RangedAlsoMelee = w.AlsoMelee,
                RangedDice = RangedDice,
                MeleeDice = MeleeDice,
                RangedWeaponDice = w.Dice,
                RangedWeaponInjuryDice = w.InjuryDice,
                MeleeWeaponDice = MeleeWeaponDice,
                MeleeWeaponInjuryDice = MeleeWeaponInjuryDice,
                RangedWeaponKeywords = w.Keywords,
                RangedWeaponBlast = w.Blast,
                MeleeWeaponKeywords = MeleeWeaponKeywords,
                MeleeWeaponCleave = MeleeWeaponCleave,
                Block = Block,
                ImperviousArmour = ImperviousArmour,
                Negates = Negates,
                Traits = Traits,
                InjuryDiceTaken = InjuryDiceTaken,
                MeleeInjuryBonus = MeleeInjuryBonus,
                KeepsGoingAfterRisky = KeepsGoingAfterRisky,
                ClimbDice = ClimbDice,
                Fanfare = Fanfare,
                BlendsIn = BlendsIn,
                SinisterWhispers = SinisterWhispers,
                WardedByBlood = WardedByBlood,
                HasCover = HasCover,
                DigsIn = DigsIn,
                BurnsAtActivationEnd = BurnsAtActivationEnd,
                BurnsWhenTakenOut = BurnsWhenTakenOut,
                Treats = Treats,
                FieldShrine = FieldShrine,
                HealingWaters = HealingWaters,
                GrindsToDust = GrindsToDust,
                Tramples = Tramples,
                Horde = Horde,
                EnforcesOrthodoxy = EnforcesOrthodoxy,
                LaysOnHands = LaysOnHands,
                Martyrdom = Martyrdom,
                Talismans = Talismans,
                ShotsIgnoreMarkers = ShotsIgnoreMarkers,
                ExpertMedic = ExpertMedic,
                FinishesTheFallen = FinishesTheFallen,
                ExtraChargeDie = ExtraChargeDie,
                InspiresFaithful = InspiresFaithful,
                Aims = Aims,
                Fortifies = Fortifies,
                Agile = Agile,
                SwiftDash = SwiftDash,
                QuickToRise = QuickToRise,
                Hateful = Hateful,
                Hides = Hides,
                DefusesMines = DefusesMines,
                UndeadFortitude = UndeadFortitude,
                AbioticLife = AbioticLife,
                Stealthy = Stealthy,
                HuntsTheTough = HuntsTheTough,
                CounterCharge = CounterCharge,
                MementoMori = MementoMori,
            };
        }
        // Roster stage R5i (U114): a Grind to Dust attack uses no Melee Weapon: no weapon DICE, INJURY DICE, modifier,
        // keywords or CLEAVE; the model's own Melee dice and traits still count.
        // Roster stage R6d (U120): a Trample ACTION's attack is the same, with IGNORE ARMOUR.
        if (IsActionAttack(weapon))
        {
            return new ModelProfile(Movement, BaseRadius, RangedRange, RangedInjuryModifier, Armour, hasMeleeWeapon: true, meleeInjuryModifier: 0)
            {
                MeleeWeaponKeywords = weapon == TrampleWeapon ? WeaponKeywords.IgnoreArmour : WeaponKeywords.None,
                RangedDice = RangedDice,
                MeleeDice = MeleeDice,
                RangedWeaponDice = RangedWeaponDice,
                RangedWeaponInjuryDice = RangedWeaponInjuryDice,
                RangedWeaponKeywords = RangedWeaponKeywords,
                RangedWeaponBlast = RangedWeaponBlast,
                Block = Block,
                ImperviousArmour = ImperviousArmour,
                Negates = Negates,
                Traits = Traits,
                InjuryDiceTaken = InjuryDiceTaken,
                MeleeInjuryBonus = MeleeInjuryBonus,
                KeepsGoingAfterRisky = KeepsGoingAfterRisky,
                ClimbDice = ClimbDice,
                Fanfare = Fanfare,
                BlendsIn = BlendsIn,
                SinisterWhispers = SinisterWhispers,
                WardedByBlood = WardedByBlood,
                HasCover = HasCover,
                DigsIn = DigsIn,
                BurnsAtActivationEnd = BurnsAtActivationEnd,
                BurnsWhenTakenOut = BurnsWhenTakenOut,
                Treats = Treats,
                FieldShrine = FieldShrine,
                HealingWaters = HealingWaters,
                GrindsToDust = GrindsToDust,
                Tramples = Tramples,
                Horde = Horde,
                EnforcesOrthodoxy = EnforcesOrthodoxy,
                LaysOnHands = LaysOnHands,
                Martyrdom = Martyrdom,
                Talismans = Talismans,
                ShotsIgnoreMarkers = ShotsIgnoreMarkers,
                ExpertMedic = ExpertMedic,
                FinishesTheFallen = FinishesTheFallen,
                ExtraChargeDie = ExtraChargeDie,
                InspiresFaithful = InspiresFaithful,
                Aims = Aims,
                Fortifies = Fortifies,
                Agile = Agile,
                SwiftDash = SwiftDash,
                QuickToRise = QuickToRise,
                Hateful = Hateful,
                Hides = Hides,
                DefusesMines = DefusesMines,
                UndeadFortitude = UndeadFortitude,
                AbioticLife = AbioticLife,
                Stealthy = Stealthy,
                HuntsTheTough = HuntsTheTough,
                CounterCharge = CounterCharge,
                MementoMori = MementoMori,
            };
        }
        var m = SecondMeleeWeapon!;
        return new ModelProfile(Movement, BaseRadius, RangedRange, RangedInjuryModifier, Armour, hasMeleeWeapon: true, m.InjuryModifier)
        {
            RangedAlsoMelee = RangedAlsoMelee,
            RangedDice = RangedDice,
            MeleeDice = MeleeDice,
            RangedWeaponDice = RangedWeaponDice,
            RangedWeaponInjuryDice = RangedWeaponInjuryDice,
            MeleeWeaponDice = m.Dice,
            MeleeWeaponInjuryDice = m.InjuryDice,
            RangedWeaponKeywords = RangedWeaponKeywords,
            RangedWeaponBlast = RangedWeaponBlast,
            MeleeWeaponKeywords = m.Keywords,
            MeleeWeaponCleave = m.Cleave,
            Block = Block,
            ImperviousArmour = ImperviousArmour,
            Negates = Negates,
            Traits = Traits,
            InjuryDiceTaken = InjuryDiceTaken,
            MeleeInjuryBonus = MeleeInjuryBonus,
            KeepsGoingAfterRisky = KeepsGoingAfterRisky,
            ClimbDice = ClimbDice,
            Fanfare = Fanfare,
            BlendsIn = BlendsIn,
            SinisterWhispers = SinisterWhispers,
            WardedByBlood = WardedByBlood,
            HasCover = HasCover,
            DigsIn = DigsIn,
            BurnsAtActivationEnd = BurnsAtActivationEnd,
            BurnsWhenTakenOut = BurnsWhenTakenOut,
            Treats = Treats,
            FieldShrine = FieldShrine,
            HealingWaters = HealingWaters,
            GrindsToDust = GrindsToDust,
            Tramples = Tramples,
            Horde = Horde,
            EnforcesOrthodoxy = EnforcesOrthodoxy,
            LaysOnHands = LaysOnHands,
            Martyrdom = Martyrdom,
            Talismans = Talismans,
            ShotsIgnoreMarkers = ShotsIgnoreMarkers,
            ExpertMedic = ExpertMedic,
            FinishesTheFallen = FinishesTheFallen,
            ExtraChargeDie = ExtraChargeDie,
            InspiresFaithful = InspiresFaithful,
            Aims = Aims,
            Fortifies = Fortifies,
            Agile = Agile,
            SwiftDash = SwiftDash,
            QuickToRise = QuickToRise,
            Hateful = Hateful,
            Hides = Hides,
            DefusesMines = DefusesMines,
            UndeadFortitude = UndeadFortitude,
            AbioticLife = AbioticLife,
            Stealthy = Stealthy,
            HuntsTheTough = HuntsTheTough,
            CounterCharge = CounterCharge,
            MementoMori = MementoMori,
        };
    }

    // The keywords of the weapon an attack of this kind is made with (a Melee Attack: the Melee Weapon, else the
    // dual-purpose weapon, U54).
    public WeaponKeywords AttackKeywords(AttackKind kind) => kind == AttackKind.Melee
        ? HasMeleeWeapon ? MeleeWeaponKeywords : RangedAlsoMelee ? RangedWeaponKeywords : WeaponKeywords.None
        : RangedWeaponKeywords;

    // A Ranged Attack's Success Roll: the Ranged Characteristic and the ranged weapon's DICE.
    public int RangedAttackDice => RangedDice + RangedWeaponDice;

    public int RangedAttackInjuryDice => RangedWeaponInjuryDice;

    // A Melee Attack's Success Roll: the Melee Characteristic and the DICE of the weapon it attacks with (its Melee
    // Weapon, else its dual-purpose weapon, as MeleeAttackInjuryModifier). A PISTOL used in melee may use the Ranged
    // Characteristic instead (p. 56): the better of the two is used (U98).
    public int MeleeAttackDice => MeleeCharacteristicUsed + (HasMeleeWeapon ? MeleeWeaponDice : RangedAlsoMelee ? RangedWeaponDice : 0);

    // The characteristic a Melee Attack uses: Melee, or the better of Ranged and Melee for a PISTOL used in melee.
    public int MeleeCharacteristicUsed =>
        AttackKeywords(AttackKind.Melee).HasFlag(WeaponKeywords.Pistol) ? Math.Max(MeleeDice, RangedDice) : MeleeDice;

    public int MeleeAttackInjuryDice => HasMeleeWeapon ? MeleeWeaponInjuryDice : RangedAlsoMelee ? RangedWeaponInjuryDice : 0;

    private static int Dice(int value, string name) => value is < -MaxDice or > MaxDice
        ? throw new ArgumentOutOfRangeException(name, $"Dice are -{MaxDice} to +{MaxDice}.")
        : value;

    private int RangedWeapon(int value, string name) => value != 0 && !HasRangedWeapon
        ? throw new ArgumentException("Only a model with a ranged weapon has ranged weapon dice.", name)
        : value;

    private int MeleeWeapon(int value, string name) => value != 0 && !HasMeleeWeapon
        ? throw new ArgumentException("Only a model with a Melee Weapon has melee weapon dice.", name)
        : value;

    public bool HasRangedWeapon => RangedRange > Length.Zero;

    // Whether the model has any weapon it can make a Melee Attack with (pp. 15, 45, 68): a Melee Weapon, or a ranged
    // weapon with "Melee" under its Range.
    public bool CanMakeMeleeAttack => HasMeleeWeapon || RangedAlsoMelee;

    // The injury modifier of the weapon the model makes Melee Attacks with: its Melee Weapon's, else its dual-purpose
    // weapon's own (C19a).
    public int MeleeAttackInjuryModifier => HasMeleeWeapon ? MeleeInjuryModifier : RangedAlsoMelee ? RangedInjuryModifier : 0;
}
