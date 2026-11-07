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
    private static readonly ModelProfile Court = Rifleman with { Spells = SpellKind.WhatIsYoursIsMine | SpellKind.ExquisitePain | SpellKind.CovetedPosition | SpellKind.CharmOfAcedia, Traits = ModelTraits.Demonic };

    // A1 (the Court caster, DEMONIC) at (10, 10), A2 at (2, 30), B1 (16, 10), B2 (30, 30), activated A1; B1 and B2 carry markers.
    private static BoardPlay Ready(int blood = 2, params TerrainBlock[] terrain)
    {
        var scenario = new Scenario(Field("36", terrain, [new DeploymentZone("west", F("0", "0", "6", "36")), new DeploymentZone("east", F("30", "0", "36", "36"))]), []);
        var play = new BoardPlay(2, id => id == A(1) ? Court : Rifleman, scenario: scenario).ToActivation();
        play.Put(A(1), "10", "10").Put(A(2), "2", "30").Put(B(1), "16", "10").Put(B(2), "30", "30").Mark(B(1), blood).Mark(B(2), blood);
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
        var events = play.Do(Cast(SpellKind.WhatIsYoursIsMine, "B1", ("B2", 1))).Events;
        Assert.Equal(new SpellCast(A(1), SpellKind.WhatIsYoursIsMine, Pay(("B2", 1))) { Target = B(1) }.Spell, ((SpellCast)events.Single()).Spell);
        // B2 paid 1; B1 lost 1 to the caster.
        Assert.Equal((1, 1, 1), (play.M(B(2)).BloodMarkers, play.M(B(1)).BloodMarkers, play.M(A(1)).BloodMarkers));
        // Once per activation.
        play.Refused(Cast(SpellKind.WhatIsYoursIsMine, "B1", ("B2", 1)), ReasonCode.SpellRefused);
        // A blessing marker instead.
        var blessing = Ready();
        blessing.State = blessing.State with { Models = [.. blessing.State.Models.Select(m => m.Id == B(1) ? m with { BlessingMarkers = 2 } : m)] };
        blessing.Do(Cast(SpellKind.WhatIsYoursIsMine, "B1", ("B2", 1)) with { TakeBlessing = true });
        Assert.Equal((1, 1), (blessing.M(B(1)).BlessingMarkers, blessing.M(A(1)).BlessingMarkers));
    }

    [Fact]
    public void ThePaymentIsCheckedAgainstTheRules()
    {
        var play = Ready();
        play.Refused(Cast(SpellKind.WhatIsYoursIsMine, "B1"), ReasonCode.SpellRefused);                       // nothing paid
        play.Refused(Cast(SpellKind.WhatIsYoursIsMine, "B1", ("B2", 2)), ReasonCode.SpellRefused);           // 2 for a cost of 1
        play.Refused(Cast(SpellKind.WhatIsYoursIsMine, "B1", ("B2", 3)), ReasonCode.SpellRefused);           // more than it has
        play.Refused(Cast(SpellKind.WhatIsYoursIsMine, "B1", ("A2", 1)), ReasonCode.SpellRefused);           // A2 has no markers
        play.Refused(Cast(SpellKind.WhatIsYoursIsMine, "B1", ("B2", 1), ("B2", 1)), ReasonCode.SpellRefused); // twice the same payer
        play.Refused(Cast(SpellKind.WhatIsYoursIsMine, "B1", ("Z9", 1)), ReasonCode.SpellRefused);           // no such model
        // Markers on a CULT or DEMONIC model cannot pay.
        var demon = new BoardPlay(2, id => id == A(1) ? Court : id == B(2) ? Rifleman with { Traits = ModelTraits.Cult } : Rifleman).ToActivation();
        demon.Put(A(1), "10", "10").Put(A(2), "2", "30").Put(B(1), "16", "10").Put(B(2), "30", "30").Mark(B(1), 2).Mark(B(2), 2);
        demon.Do(new ActivateModel(Seat.One, A(1)));
        demon.Refused(Cast(SpellKind.WhatIsYoursIsMine, "B1", ("B2", 1)), ReasonCode.SpellRefused);
        demon.Do(Cast(SpellKind.WhatIsYoursIsMine, "B1", ("B1", 1)));
        // A model without the spell cannot cast it.
        var plain = new BoardPlay(2).ToActivation();
        plain.Put(A(1), "10", "10").Put(A(2), "2", "30").Put(B(1), "16", "10").Put(B(2), "30", "30").Mark(B(1), 2);
        plain.Do(new ActivateModel(Seat.One, A(1)));
        plain.Refused(Cast(SpellKind.WhatIsYoursIsMine, "B1", ("B1", 1)), ReasonCode.SpellRefused);
    }

    [Fact]
    [Trait("Rule", "Exquisite Pain p. 157: the markers paid (1-2) go onto a model in sight")]
    public void ExquisitePainPutsThePaidMarkersOnATarget()
    {
        var play = Ready();
        play.Do(Cast(SpellKind.ExquisitePain, "B1", ("B2", 1), ("B1", 1)));
        // 2 paid (one from each), 2 put on B1: B1 had 2, paid 1, takes 2 = 3; B2 paid 1 = 1.
        Assert.Equal((3, 1), (play.M(B(1)).BloodMarkers, play.M(B(2)).BloodMarkers));
        var tooMany = Ready(5);
        tooMany.Refused(Cast(SpellKind.ExquisitePain, "B1", ("B2", 2)), ReasonCode.SpellRefused);       // B1 would have 7
        tooMany.Do(Cast(SpellKind.ExquisitePain, "B1", ("B2", 1)));
        Assert.Equal(6, tooMany.M(B(1)).BloodMarkers);
        Ready().Refused(Cast(SpellKind.ExquisitePain, "B1", ("B2", 1), ("B1", 1), ("A2", 1)), ReasonCode.SpellRefused); // 3 is more than 2
    }

    [Fact]
    [Trait("Rule", "Coveted Position p. 157: swap places with a model within 12\" in sight that is over 1\" from others")]
    public void CovetedPositionSwapsPlaces()
    {
        var play = Ready();
        play.Put(B(1), "20", "10");
        play.Do(Cast(SpellKind.CovetedPosition, "B1", ("B2", 2)));
        Assert.Equal((P("20", "10"), P("10", "10")), (play.M(A(1)).Position, play.M(B(1)).Position));
        // Beyond 12", or in the way of a wall's sight, it is refused; so is a target within 1" of another model.
        var far = Ready();
        far.Put(B(1), "25", "10");
        far.Refused(Cast(SpellKind.CovetedPosition, "B1", ("B2", 2)), ReasonCode.SpellRefused);
        var walled = Ready(2, Wall("w", "13", "5", "14", "15"));
        walled.Refused(Cast(SpellKind.CovetedPosition, "B1", ("B2", 2)), ReasonCode.SpellRefused);
        var crowded = Ready();
        crowded.Put(B(1), "20", "10").Put(B(2), "20", "11.5");
        crowded.Refused(Cast(SpellKind.CovetedPosition, "B1", ("B1", 1), ("B2", 1)), ReasonCode.SpellRefused);
    }

    [Fact]
    public void SpellsAreCastBetweenActionsAndResetEachActivation()
    {
        var play = Ready();
        play.Do(Cast(SpellKind.ExquisitePain, "B1", ("B2", 1)));
        Assert.Equal(SpellKind.ExquisitePain, play.State.SpellsCast);
        // A different spell is still open.
        play.Do(Cast(SpellKind.WhatIsYoursIsMine, "B1", ("B2", 1)));
        play.Do(new EndActivation(Seat.One));
        Assert.Equal(SpellKind.WhatIsYoursIsMine | SpellKind.ExquisitePain, play.State.SpellsCast);
        play.Do(new ActivateModel(Seat.Two, B(1)));
        Assert.Equal(SpellKind.None, play.State.SpellsCast);
    }

    [Fact]
    public void ForgedSpellsAreRefused()
    {
        var play = Ready();
        var good = new SpellCast(A(1), SpellKind.ExquisitePain, Pay(("B2", 1))) { Target = B(1) };
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(play.State, good with { Payment = Pay(("B2", 3)) }));
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
        Assert.Throws<ArgumentOutOfRangeException>(() => Rifleman with { Spells = (SpellKind)64 });
    }

    private static CastSpell Charm(params (string, int)[] pays) => new(Seat.One, A(1), SpellKind.CharmOfAcedia, Pay(pays));

    private static DashModel DashTo(string x) => new(Seat.One, A(1), Path(("" + x, "10")));

    // Roster stage R13 (U138): Charm of Acedia (cost 1): the caster's next Success Roll is a Success.
    [Fact]
    [Trait("Rule", "Charm of Acedia p. 159: the first roll of its next ACTION is a Success")]
    public void CharmOfAcediaMakesTheNextRollASuccess()
    {
        var play = Ready();
        play.Do(Charm(("B2", 1)));
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
    public void ACharmWaitsForTheCastersRollAndKeepsAGoodOne()
    {
        var play = Ready();
        play.Do(Charm(("B2", 1)));
        play.Do(new EndActivation(Seat.One));
        // Another model's roll does not use it up, and is not made a Success.
        play.Do(new ActivateModel(Seat.Two, B(1)));
        play.Do(new DashModel(Seat.Two, B(1), Path(("19", "10"))));   // the opponent may spend B1's markers
        var theirs = play.Do(new SpendMarkers(Seat.One, 0), 1, 1).Events.OfType<DashRolled>().Single();
        Assert.Equal((false, SuccessOutcome.Failure), (theirs.Check.Charmed, theirs.Check.Outcome));
        Assert.Equal(A(1), play.State.CharmedModel);
        // A good roll stays what it was (a Critical Success is not made worse); the charm is still used up.
        var good = Ready();
        good.Do(Charm(("B2", 1)));
        var rolled = good.Do(DashTo("13"), 6, 6).Events.OfType<DashRolled>().Single();
        Assert.Equal((true, SuccessOutcome.CriticalSuccess), (rolled.Check.Charmed, rolled.Check.Outcome));
        Assert.Null(good.State.CharmedModel);
    }

    [Fact]
    public void CharmOfAcediaIsCheckedAndForgedRollsAreRefused()
    {
        var play = Ready();
        play.Refused(new CastSpell(Seat.One, A(1), SpellKind.CharmOfAcedia, Pay(("B2", 1))) { Target = B(1) }, ReasonCode.SpellRefused);   // no target
        play.Refused(Charm(), ReasonCode.SpellRefused);                                                                                        // nothing paid
        play.Do(Charm(("B2", 1)));
        play.Refused(Charm(("B1", 1)), ReasonCode.SpellRefused);                                                                               // once per activation
        // A roll the charm did not touch is refused while it is on, and a Charmed roll when it is not.
        var natural = Ready().Do(DashTo("13"), 6, 6).Events.OfType<DashRolled>().Single();
        var plainDeclared = Reducer.Apply(Ready().State, new DashDeclared(A(1), [P("13", "10")]));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(plainDeclared, new DashRolled(A(1), natural.Check with { Charmed = true })));
        var charmedPlay = Ready();
        charmedPlay.Do(Charm(("B2", 1)));
        var charmedDeclared = Reducer.Apply(charmedPlay.State, new DashDeclared(A(1), [P("13", "10")]));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(charmedDeclared, new DashRolled(A(1), natural.Check)));
        Reducer.Apply(charmedDeclared, new DashRolled(A(1), natural.Check with { Charmed = true }));
    }

    [Fact]
    public void TheHashAndTheWireSeeTheCharm()
    {
        var play = Ready();
        Assert.DoesNotContain("charmed", StateHash.Canonical(play.State), StringComparison.Ordinal);
        play.Do(Charm(("B2", 1)));
        Assert.Contains("charmed=A1", StateHash.Canonical(play.State), StringComparison.Ordinal);
    }
}
