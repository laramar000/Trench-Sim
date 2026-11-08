using System.Collections.Immutable;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;

namespace Rules.Tests.Match;

// Roster stage R11 (U136): the Court's Goetic spells What is Yours is Mine, Exquisite Pain and Coveted Position, and the payment:
// BLOOD MARKERS from models without CULT or DEMONIC, anywhere, no roll, each spell once per activation.
public class SpellTests
{
    private static readonly ModelProfile Court = Rifleman with { Spells = SpellKind.WhatIsYoursIsMine | SpellKind.ExquisitePain | SpellKind.CovetedPosition | SpellKind.CharmOfAcedia | SpellKind.BlackHeart | SpellKind.BloodMagic | SpellKind.ShadowWalker | SpellKind.CallOfTheFlesh, Traits = ModelTraits.Demonic };

    // A1 (the Court caster, DEMONIC, 2 SIN MARKERS) at (10, 10), A2 (DEMONIC, 3 SIN MARKERS) at (2, 30), B1 (16, 10), B2 (30, 30), activated A1;
    // B1 and B2 carry BLOOD MARKERS.
    private static BoardPlay Ready(int blood = 2, params TerrainBlock[] terrain) => Ready(blood, Rifleman, terrain);

    // The same set-up with the enemy (and A2) played by another profile, e.g. one that can fight.
    private static BoardPlay Ready(int blood, ModelProfile others, TerrainBlock[] terrain)
    {
        var scenario = new Scenario(Field("36", terrain, [new DeploymentZone("west", F("0", "0", "6", "36")), new DeploymentZone("east", F("30", "0", "36", "36"))]), []);
        var play = new BoardPlay(2, id => id == A(1) ? Court : id == A(2) ? Rifleman with { Traits = ModelTraits.Demonic } : others, scenario: scenario).ToActivation();
        play.Put(A(1), "10", "10").Put(A(2), "2", "30").Put(B(1), "16", "10").Put(B(2), "30", "30").Mark(B(1), blood).Mark(B(2), blood).Sin(A(1), 2).Sin(A(2), 3);
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    private static ImmutableArray<MarkerPayment> Pay(params (string, int)[] pays) => [.. pays.Select(p => new MarkerPayment(new ModelId(p.Item1), p.Item2))];

    private static CastSpell Cast(SpellKind spell, string target, params (string, int)[] pays) =>
        new(Seat.One, A(1), spell, Pay(pays)) { Target = new ModelId(target) };

    [Fact]
    [Trait("Rule", "What is Yours is Mine p. 157: paid with 1 marker, moves 1 BLOOD or BLESSING MARKER from a model in sight to the caster")]
    public void WhatIsYoursIsMineMovesAMarkerToTheCaster()
    {
        var play = Ready();
        var events = play.Do(Cast(SpellKind.WhatIsYoursIsMine, "B1", ("A2", 1))).Events;
        Assert.Equal(new SpellCast(A(1), SpellKind.WhatIsYoursIsMine, Pay(("A2", 1))) { Target = B(1) }.Spell, ((SpellCast)events.Single()).Spell);
        // A2 paid 1 SIN MARKER; B1 lost 1 BLOOD MARKER to the caster.
        Assert.Equal((2, 1, 1, 2), (play.M(B(2)).BloodMarkers, play.M(B(1)).BloodMarkers, play.M(A(1)).BloodMarkers, play.M(A(2)).SinMarkers));
        // Once per activation.
        play.Refused(Cast(SpellKind.WhatIsYoursIsMine, "B1", ("A2", 1)), ReasonCode.SpellRefused);
        // A blessing marker instead.
        var blessing = Ready();
        blessing.State = blessing.State with { Models = [.. blessing.State.Models.Select(m => m.Id == B(1) ? m with { BlessingMarkers = 2 } : m)] };
        blessing.Do(Cast(SpellKind.WhatIsYoursIsMine, "B1", ("A2", 1)) with { TakeBlessing = true });
        Assert.Equal((1, 1), (blessing.M(B(1)).BlessingMarkers, blessing.M(A(1)).BlessingMarkers));
    }

    [Fact]
    public void ThePaymentIsCheckedAgainstTheRules()
    {
        var play = Ready();
        play.Refused(Cast(SpellKind.WhatIsYoursIsMine, "B1"), ReasonCode.SpellRefused);                       // nothing paid
        play.Refused(Cast(SpellKind.WhatIsYoursIsMine, "B1", ("A2", 2)), ReasonCode.SpellRefused);           // 2 for a cost of 1
        play.Refused(Cast(SpellKind.WhatIsYoursIsMine, "B1", ("A2", 3)), ReasonCode.SpellRefused);           // more than it has
        play.Refused(Cast(SpellKind.WhatIsYoursIsMine, "B1", ("Z8", 1)), ReasonCode.SpellRefused);           // no such payer
        play.Refused(Cast(SpellKind.WhatIsYoursIsMine, "B1", ("A2", 1), ("A2", 1)), ReasonCode.SpellRefused); // twice the same payer
        play.Refused(Cast(SpellKind.WhatIsYoursIsMine, "B1", ("Z9", 1)), ReasonCode.SpellRefused);           // no such model
        // Only friendly DEMONIC models pay (October 2026 Review, U142): not a friend without the keyword, not an enemy that has it.
        var others = new BoardPlay(2, id => id == A(1) ? Court : id == B(2) ? Rifleman with { Traits = ModelTraits.Demonic } : Rifleman).ToActivation();
        others.Put(A(1), "10", "10").Put(A(2), "2", "30").Put(B(1), "16", "10").Put(B(2), "30", "30").Mark(B(1), 2).Sin(A(1), 1).Sin(A(2), 2).Sin(B(2), 2);
        others.Do(new ActivateModel(Seat.One, A(1)));
        others.Refused(Cast(SpellKind.WhatIsYoursIsMine, "B1", ("A2", 1)), ReasonCode.SpellRefused);   // a friend that is not DEMONIC
        others.Refused(Cast(SpellKind.WhatIsYoursIsMine, "B1", ("B2", 1)), ReasonCode.SpellRefused);   // an enemy that is
        others.Do(Cast(SpellKind.WhatIsYoursIsMine, "B1", ("A1", 1)));
        Assert.Equal(0, others.M(A(1)).SinMarkers);
        // A model without the spell cannot cast it.
        var plain = new BoardPlay(2).ToActivation();
        plain.Put(A(1), "10", "10").Put(A(2), "2", "30").Put(B(1), "16", "10").Put(B(2), "30", "30").Mark(B(1), 2);
        plain.Do(new ActivateModel(Seat.One, A(1)));
        plain.Refused(Cast(SpellKind.WhatIsYoursIsMine, "B1", ("A1", 1)), ReasonCode.SpellRefused);
    }

    [Fact]
    [Trait("Rule", "Exquisite Pain p. 157: the markers paid (1-2) go onto a model in sight")]
    public void ExquisitePainPutsThePaidMarkersOnATarget()
    {
        var play = Ready();
        play.Do(Cast(SpellKind.ExquisitePain, "B1", ("A2", 1), ("A1", 1)));
        // 2 SIN MARKERS paid (one from each friendly model), 2 BLOOD MARKERS put on B1: 2 + 2 = 4.
        Assert.Equal((4, 2, 2, 1), (play.M(B(1)).BloodMarkers, play.M(B(2)).BloodMarkers, play.M(A(2)).SinMarkers, play.M(A(1)).SinMarkers));
        var tooMany = Ready(5);
        tooMany.Refused(Cast(SpellKind.ExquisitePain, "B1", ("A2", 2)), ReasonCode.SpellRefused);       // B1 would have 7
        tooMany.Do(Cast(SpellKind.ExquisitePain, "B1", ("A2", 1)));
        Assert.Equal(6, tooMany.M(B(1)).BloodMarkers);
        Ready().Refused(Cast(SpellKind.ExquisitePain, "B1", ("A2", 3), ("A1", 1)), ReasonCode.SpellRefused); // 4 is more than 3
    }

    [Fact]
    [Trait("Rule", "Coveted Position p. 157: swap places with a model within 12\" in sight that is over 1\" from others")]
    public void CovetedPositionSwapsPlaces()
    {
        var play = Ready();
        play.Put(B(1), "20", "10");
        play.Do(Cast(SpellKind.CovetedPosition, "B1", ("A2", 2)));
        Assert.Equal((P("20", "10"), P("10", "10")), (play.M(A(1)).Position, play.M(B(1)).Position));
        // Beyond 12", or in the way of a wall's sight, it is refused; so is a target within 1" of another model.
        var far = Ready();
        far.Put(B(1), "25", "10");
        far.Refused(Cast(SpellKind.CovetedPosition, "B1", ("A2", 2)), ReasonCode.SpellRefused);
        var walled = Ready(2, Wall("w", "13", "5", "14", "15"));
        walled.Refused(Cast(SpellKind.CovetedPosition, "B1", ("A2", 2)), ReasonCode.SpellRefused);
        var crowded = Ready();
        crowded.Put(B(1), "20", "10").Put(B(2), "20", "11.5");
        crowded.Refused(Cast(SpellKind.CovetedPosition, "B1", ("A1", 1), ("A2", 1)), ReasonCode.SpellRefused);
    }

    [Fact]
    public void SpellsAreCastBetweenActionsAndResetEachActivation()
    {
        var play = Ready();
        play.Do(Cast(SpellKind.ExquisitePain, "B1", ("A2", 1)));
        Assert.Equal(SpellKind.ExquisitePain, play.State.SpellsCast);
        // A different spell is still open.
        play.Do(Cast(SpellKind.WhatIsYoursIsMine, "B1", ("A2", 1)));
        play.Do(new EndActivation(Seat.One));
        Assert.Equal(SpellKind.WhatIsYoursIsMine | SpellKind.ExquisitePain, play.State.SpellsCast);
        play.Do(new ActivateModel(Seat.Two, B(1)));
        Assert.Equal(SpellKind.None, play.State.SpellsCast);
    }

    [Fact]
    public void ForgedSpellsAreRefused()
    {
        var play = Ready();
        var good = new SpellCast(A(1), SpellKind.ExquisitePain, Pay(("A2", 1))) { Target = B(1) };
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(play.State, good with { Payment = Pay(("A2", 4)) }));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(play.State, good with { Target = null }));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(play.State, good with { Spell = SpellKind.WhatIsYoursIsMine | SpellKind.ExquisitePain }));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(play.State, good with { Model = A(2) }));
        var cast = Reducer.Apply(play.State, good);
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(cast, good));
        Assert.Equal(StateHash.Compute(cast), StateHash.Compute(Reducer.Apply(play.State, good)));
    }

    [Fact]
    public void TheHashSeesSpellsOnlyWhenTheyAreThere()
    {
        var plain = new BoardPlay(2).ToActivation();
        Assert.DoesNotContain("spells", StateHash.Canonical(plain.State), StringComparison.Ordinal);
        Assert.Contains("spells=", StateHash.Canonical(Ready().State), StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => Rifleman with { Spells = (SpellKind)(1 << 20) });
    }

    private static CastSpell Charm(params (string, int)[] pays) => new(Seat.One, A(1), SpellKind.CharmOfAcedia, Pay(pays));

    private static DashModel DashTo(string x) => new(Seat.One, A(1), Path(("" + x, "10")));

    // Roster stage R13 (U138): Charm of Acedia (cost 1): the caster's next Success Roll is a Success.
    [Fact]
    [Trait("Rule", "Charm of Acedia p. 159: the first roll of its next ACTION is a Success")]
    public void CharmOfAcediaMakesTheNextRollASuccess()
    {
        var play = Ready();
        play.Do(Charm(("A2", 1)));
        Assert.Equal(A(1), play.State.CharmedModel);
        // A Dash roll of 1 + 1 fails by the dice, but the charm makes it a Success: the model moves and the activation goes on.
        var events = play.Do(DashTo("13"), 1, 1).Events;
        var rolled = events.OfType<DashRolled>().Single();
        Assert.Equal((true, SuccessOutcome.Success, false), (rolled.Check.Charmed, rolled.Check.Outcome, rolled.Check.RiskyFailure));
        Assert.DoesNotContain(events, e => e is ActivationEnded);
        Assert.Equal(P("13", "10"), play.M(A(1)).Position);
        Assert.Null(play.State.CharmedModel);
        // The charm is spent: the next roll is the dice's.
        var shot = play.Do(new Shoot(Seat.One, A(1), B(1)), 1, 1).Events.OfType<RangedSuccessRolled>().Single();
        Assert.Equal((false, SuccessOutcome.Failure), (shot.Check.Charmed, shot.Check.Outcome));
    }

    [Fact]
    public void ACharmLastsThisActivationOnlyAndAGoodRollStaysWhatItWas()
    {
        // October 2026 Review (U142): the next Success Roll "this Activation": an unused Charm ends with it.
        var play = Ready();
        play.Do(Charm(("A2", 1)));
        play.Do(new EndActivation(Seat.One));
        Assert.Null(play.State.CharmedModel);
        // A good roll stays what it was (a Critical Success is not made worse); the charm is still used up.
        var good = Ready();
        good.Do(Charm(("A2", 1)));
        var rolled = good.Do(DashTo("13"), 6, 6).Events.OfType<DashRolled>().Single();
        Assert.Equal((true, SuccessOutcome.CriticalSuccess), (rolled.Check.Charmed, rolled.Check.Outcome));
        Assert.Null(good.State.CharmedModel);
    }

    // Roster stage R14 (U139): Black Heart (+1 DICE on the next Success Roll) and Blood Magic (+1 INJURY DICE for one attack).
    private static CastSpell Free(SpellKind spell, params (string, int)[] pays) => new(Seat.One, A(1), spell, Pay(pays));

    [Fact]
    [Trait("Rule", "Black Heart p. 160: cast before a Success or Risky roll, +1 DICE")]
    public void BlackHeartAddsADieToTheNextSuccessRollAndIsUsedUp()
    {
        var play = Ready();
        play.Refused(Free(SpellKind.BlackHeart), ReasonCode.SpellRefused);                                                                // nothing paid
        play.Refused(new CastSpell(Seat.One, A(1), SpellKind.BlackHeart, Pay(("A2", 1))) { Target = B(1) }, ReasonCode.SpellRefused);   // no target
        play.Do(Free(SpellKind.BlackHeart, ("A2", 1)));
        Assert.Equal(A(1), play.State.BlackHeartModel);
        play.Refused(Free(SpellKind.BlackHeart, ("A1", 1)), ReasonCode.SpellRefused);                                                    // already in place
        var rolled = play.Do(DashTo("13"), 6, 5, 4).Events.OfType<DashRolled>().Single();
        Assert.Equal(1, rolled.Check.NetDice);
        Assert.Null(play.State.BlackHeartModel);
        // Used up: the next roll is back to plain dice.
        var plain = play.Do(new Shoot(Seat.One, A(1), B(1)), 1, 1).Events.OfType<RangedSuccessRolled>().Single();
        Assert.Equal(0, plain.Check.NetDice);
    }

    [Fact]
    public void ABlackHeartNotUsedEndsWithTheActivation()
    {
        var play = Ready();
        play.Do(Free(SpellKind.BlackHeart, ("A2", 1)));
        play.Do(new EndActivation(Seat.One));
        Assert.Null(play.State.BlackHeartModel);
    }

    [Fact]
    [Trait("Rule", "Blood Magic p. 166: free, cast before its Injury Rolls, +1 INJURY DICE to all of them for that attack")]
    public void BloodMagicAddsAnInjuryDieForOneAttack()
    {
        var play = Ready();
        play.Mark(B(1), 0);
        play.Do(Free(SpellKind.BloodMagic, ("A2", 1)));
        Assert.Equal(A(1), play.State.BloodMagicModel);
        // A Dash does not spend it.
        play.Do(DashTo("11"), 6, 6);
        Assert.Equal(A(1), play.State.BloodMagicModel);
        var injury = play.Do(new Shoot(Seat.One, A(1), B(1)), 6, 6, 3, 3, 3, 3).Events.OfType<InjuryRolled>().Single();
        // The same shot without the spell rolls one die fewer.
        var control = Ready();
        control.Mark(B(1), 0);
        control.Do(DashTo("11"), 6, 6);
        var plain = control.Do(new Shoot(Seat.One, A(1), B(1)), 6, 6, 3, 3, 3).Events.OfType<InjuryRolled>().Single();
        Assert.Equal(plain.Injury.NetDice + 1, injury.Injury.NetDice);
        Assert.Null(play.State.BloodMagicModel);
    }

    [Fact]
    public void ForgedFreeSpellDiceAreRefused()
    {
        var play = Ready();
        var natural = Ready().Do(DashTo("13"), 1, 1).Events.OfType<DashRolled>().Single();
        play.Do(Free(SpellKind.BlackHeart, ("A2", 1)));
        var declared = Reducer.Apply(play.State, new DashDeclared(A(1), [P("13", "10")]));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(declared, new DashRolled(A(1), natural.Check)));
    }

    // Roster stage R15 (U140): Shadow-walker (a Retreat with no free attacks) and Call of the Flesh (the next enemy must close in).
    [Fact]
    [Trait("Rule", "Shadow-walker p. 165: before it Retreats, enemies get no attack as it leaves")]
    public void AShadowWalkerRetreatsWithoutFreeAttacks()
    {
        var control = Ready(2, MeleePlay.Brawler, []);
        control.Put(B(1), "11", "10");
        control.Do(new RetreatModel(Seat.One, A(1), Path(("6", "10"))));
        Assert.Equal(DecisionKind.FreeFight, control.State.Pending!.Kind);

        var play = Ready(2, MeleePlay.Brawler, []);
        play.Put(B(1), "11", "10");
        play.Refused(Free(SpellKind.ShadowWalker, ("A2", 1)), ReasonCode.SpellRefused);   // costs 2
        play.Do(Free(SpellKind.ShadowWalker, ("A2", 2)));
        Assert.Equal(A(1), play.State.ShadowWalking);
        play.Refused(Free(SpellKind.ShadowWalker, ("A1", 2)), ReasonCode.SpellRefused);   // already in place
        var events = play.Do(new RetreatModel(Seat.One, A(1), Path(("6", "10")))).Events;
        Assert.Null(play.State.Pending);
        Assert.Contains(events, e => e is RetreatResolved { Moved: true });
        Assert.Equal(P("6", "10"), play.M(A(1)).Position);
    }

    [Fact]
    public void AShadowWalkerNotUsedEndsWithTheActivationAndNeedsTheRetreatStillToCome()
    {
        var play = Ready();
        play.Do(new MoveModel(Seat.One, A(1), Path(("9", "10"))));
        play.Refused(Free(SpellKind.ShadowWalker, ("A2", 2)), ReasonCode.SpellRefused);   // it has moved already
        var other = Ready();
        other.Do(Free(SpellKind.ShadowWalker, ("A2", 2)));
        other.Do(new EndActivation(Seat.One));
        Assert.Null(other.State.ShadowWalking);
    }

    [Fact]
    [Trait("Rule", "Call of the Flesh p. 163: the next enemy to Activate must first Move, Charge or Retreat as close as possible, and cannot attack the caster that Turn")]
    public void CallOfTheFleshMakesTheNextEnemyCloseInAndSparesTheCaster()
    {
        var play = Ready();
        var cast = play.Do(Free(SpellKind.CallOfTheFlesh, ("A2", 2)));
        Assert.Contains(cast.Events, e => e is ActivationEnded { Model: var m } && m == A(1));
        Assert.Equal((A(1), false), (play.State.CallTarget, play.State.CallActive));
        Assert.Null(play.State.ActiveModel);
        play.Do(new ActivateModel(Seat.Two, B(1)));
        Assert.True(play.State.CallActive);
        // Nothing but a move towards the caster first.
        play.Refused(new DashModel(Seat.Two, B(1), Path(("20", "10"))), ReasonCode.ActionExcluded);
        play.Refused(new Shoot(Seat.Two, B(1), A(1)), ReasonCode.ActionExcluded);
        play.Refused(new MoveModel(Seat.Two, B(1), Path(("20", "10"))), ReasonCode.ActionExcluded);   // away from the caster
        play.Do(new MoveModel(Seat.Two, B(1), Path(("13", "10"))));
        // The caster is still safe from it; anything else is free.
        play.Refused(new Shoot(Seat.Two, B(1), A(1)), ReasonCode.ActionExcluded);
        play.Do(new EndActivation(Seat.Two));
        Assert.Null(play.State.CallTarget);
        Assert.False(play.State.CallActive);
    }

    [Fact]
    public void CharmOfAcediaIsCheckedAndForgedRollsAreRefused()
    {
        var play = Ready();
        play.Refused(new CastSpell(Seat.One, A(1), SpellKind.CharmOfAcedia, Pay(("A2", 1))) { Target = B(1) }, ReasonCode.SpellRefused);   // no target
        play.Refused(Charm(), ReasonCode.SpellRefused);                                                                                        // nothing paid
        play.Do(Charm(("A2", 1)));
        play.Refused(Charm(("A1", 1)), ReasonCode.SpellRefused);                                                                               // once per activation
        // A roll the charm did not touch is refused while it is on, and a Charmed roll when it is not.
        var natural = Ready().Do(DashTo("13"), 6, 6).Events.OfType<DashRolled>().Single();
        var plainDeclared = Reducer.Apply(Ready().State, new DashDeclared(A(1), [P("13", "10")]));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(plainDeclared, new DashRolled(A(1), natural.Check with { Charmed = true })));
        var charmedPlay = Ready();
        charmedPlay.Do(Charm(("A2", 1)));
        var charmedDeclared = Reducer.Apply(charmedPlay.State, new DashDeclared(A(1), [P("13", "10")]));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(charmedDeclared, new DashRolled(A(1), natural.Check)));
        Reducer.Apply(charmedDeclared, new DashRolled(A(1), natural.Check with { Charmed = true }));
    }

    [Fact]
    public void TheHashAndTheWireSeeTheCharm()
    {
        var play = Ready();
        Assert.DoesNotContain("charmed", StateHash.Canonical(play.State), StringComparison.Ordinal);
        play.Do(Charm(("A2", 1)));
        Assert.Contains("charmed=A1", StateHash.Canonical(play.State), StringComparison.Ordinal);
    }
}
