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

// Roster stage R16 (U141): the Court spells cast in answer to an Injury Roll: Blessing of the Serpent Moon (before it),
// Oracle Beast Cloak and Too Proud to Fall (once its result is known).
public class ReactionSpellTests
{
    private sealed class Always(int value) : IDiceSource
    {
        public int RollD6() => value;
    }

    private static DecideResult Go(BoardPlay play, Command command, int die)
    {
        var result = TurnSequence.Decide(play.State, command, new Always(die));
        Assert.True(result.IsAccepted, result.Rejection?.Text);
        play.State = result.State!;
        play.Log.AddRange(result.Events);
        return result;
    }

    private static ImmutableArray<MarkerPayment> Pay(params (string, int)[] pays) => [.. pays.Select(p => new MarkerPayment(new ModelId(p.Item1), p.Item2))];

    // A1 (the shooter, active) at (10, 10); B1 (knows `spells`) at (16, 10); A2 and B2 carry 3 BLOOD MARKERS each to pay with.
    private static BoardPlay Ready(SpellKind spells)
    {
        var scenario = new Scenario(Field("36", [], [new DeploymentZone("west", F("0", "0", "6", "36")), new DeploymentZone("east", F("30", "0", "36", "36"))]), []);
        var play = new BoardPlay(2, id => id == B(1) ? Rifleman with { Spells = spells } : Rifleman, scenario: scenario).ToActivation();
        play.Put(A(1), "10", "10").Put(A(2), "2", "30").Put(B(1), "16", "10").Put(B(2), "30", "30").Mark(A(2), 3).Mark(B(2), 3);
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    // The shot hits (all sixes); the next decision is the one the test is about.
    private static DecideResult Shoot(BoardPlay play) => Go(play, new Shoot(Seat.One, A(1), B(1)), 6);

    private static CastSpell Cast(SpellKind spell, params (string, int)[] pays) => new(Seat.Two, B(1), spell, Pay(pays));

    [Fact]
    [Trait("Rule", "Blessing of the Serpent Moon p. 163: cast before an Injury Roll for it: -1 INJURY MODIFIER per 2 markers paid")]
    public void ABlessingLowersTheInjuryModifierBeforeTheRoll()
    {
        var play = Ready(SpellKind.BlessingOfTheSerpentMoon);
        Shoot(play);
        Assert.Equal(new PendingDecision(DecisionKind.UseBlessing, Seat.Two), play.State.Pending);
        play.Refused(Cast(SpellKind.BlessingOfTheSerpentMoon, ("A2", 3)), ReasonCode.SpellRefused);    // 2, 4 or 6 only
        play.Refused(Cast(SpellKind.OracleBeastCloak, ("A2", 3)), ReasonCode.SpellRefused);            // not known, not on offer
        play.Refused(new CastSpell(Seat.One, B(1), SpellKind.BlessingOfTheSerpentMoon, Pay(("A2", 2))), ReasonCode.NotYourTurn);
        var result = Go(play, Cast(SpellKind.BlessingOfTheSerpentMoon, ("A2", 2)), 1);
        var injury = result.Events.OfType<InjuryRolled>().Single();
        Assert.Equal(-1, injury.Injury.Modifier);
        Assert.Equal(1, play.M(A(2)).BloodMarkers);
    }

    [Fact]
    public void ABlessingOfFourMarkersGivesMinusTwoAndADeclinedOneNothing()
    {
        var four = Ready(SpellKind.BlessingOfTheSerpentMoon);
        Shoot(four);
        Assert.Equal(-2, Go(four, Cast(SpellKind.BlessingOfTheSerpentMoon, ("A2", 3), ("B2", 1)), 1).Events.OfType<InjuryRolled>().Single().Injury.Modifier);

        var declined = Ready(SpellKind.BlessingOfTheSerpentMoon);
        Shoot(declined);
        declined.Refused(new UseTalisman(Seat.Two), ReasonCode.WrongDecision);
        var events = Go(declined, new DeclineReaction(Seat.Two), 1).Events;
        Assert.Contains(events, e => e is ReactionDeclined);
        Assert.Equal(0, events.OfType<InjuryRolled>().Single().Injury.Modifier);
        Assert.Equal(3, declined.M(A(2)).BloodMarkers);
    }

    [Fact]
    public void NoBlessingIsOfferedWithTooFewMarkersToPay()
    {
        var play = Ready(SpellKind.BlessingOfTheSerpentMoon);
        play.Mark(A(2), 1).Mark(B(2), 0);
        var events = Shoot(play).Events;
        Assert.Null(play.State.Pending);
        Assert.Single(events.OfType<InjuryRolled>());
    }

    [Fact]
    [Trait("Rule", "Oracle Beast Cloak p. 165: once per Turn after an Injury Roll for it, the result becomes No Effect")]
    public void AnOracleBeastCloakTurnsAnyResultIntoNoEffectOncePerTurn()
    {
        var play = Ready(SpellKind.OracleBeastCloak);
        var made = Shoot(play).Events;
        Assert.Contains(made, e => e is InjuryRollMade);
        Assert.DoesNotContain(made, e => e is InjuryRolled);
        Assert.Equal(new PendingDecision(DecisionKind.UseInjuryReaction, Seat.Two), play.State.Pending);
        Assert.Equal(ModelStatus.Standing, play.M(B(1)).Status);   // the result is not applied yet
        play.Refused(Cast(SpellKind.OracleBeastCloak, ("A2", 2)), ReasonCode.SpellRefused);            // costs 3
        play.Refused(Cast(SpellKind.TooProudToFall, ("A2", 2)), ReasonCode.SpellRefused);              // not known
        var events = Go(play, Cast(SpellKind.OracleBeastCloak, ("A2", 3)), 1).Events;
        var injury = events.OfType<InjuryRolled>().Single();
        Assert.True(injury.Cloaked);
        Assert.Equal(InjuryOutcome.NoEffect, injury.Result);
        Assert.Equal(ModelStatus.Standing, play.M(B(1)).Status);
        Assert.Equal(0, play.M(A(2)).BloodMarkers);
        Assert.True(play.M(B(1)).OracleUsed);
        Assert.Null(play.State.Held);
        Assert.Equal(SpellKind.None, play.State.HeldReaction);
        // Used for this Turn: nothing more is offered against any result.
        Assert.Equal(SpellKind.None, Board.ReactionsFor(play.State, play.M(B(1)), InjuryOutcome.OutOfAction));
    }

    [Fact]
    public void ADeclinedOracleLetsTheResultStandAndNothingIsSpent()
    {
        var play = Ready(SpellKind.OracleBeastCloak);
        Shoot(play);
        var injury = Go(play, new DeclineReaction(Seat.Two), 1).Events.OfType<InjuryRolled>().Single();
        Assert.False(injury.Cloaked);
        Assert.Equal(ModelStatus.OutOfAction, play.M(B(1)).Status);
        Assert.False(play.M(B(1)).OracleUsed);
        Assert.Equal(3, play.M(A(2)).BloodMarkers);
    }

    // The shot's two dice are sixes (a hit); every die after them is `rest`.
    private sealed class Script(int hit, int rest) : IDiceSource
    {
        private int _rolled;

        public int RollD6() => _rolled++ < 2 ? hit : rest;
    }

    private static DecideResult GoScripted(BoardPlay play, Command command, int rest)
    {
        var result = TurnSequence.Decide(play.State, command, new Script(6, rest));
        Assert.True(result.IsAccepted, result.Rejection?.Text);
        play.State = result.State!;
        play.Log.AddRange(result.Events);
        return result;
    }

    [Fact]
    [Trait("Rule", "Too Proud to Fall p. 164: when taken Down, it stays standing; other effects still apply")]
    public void TooProudToFallKeepsAModelTakenDownStandingAndLeavesTheMarkers()
    {
        // Out of Action (6 + 6): not a Down result, so it is not on offer and the model is out.
        var out_ = Ready(SpellKind.TooProudToFall);
        var events = Shoot(out_).Events;
        Assert.DoesNotContain(events, e => e is InjuryRollMade);
        Assert.Equal(ModelStatus.OutOfAction, out_.M(B(1)).Status);

        // 4 + 4 = 8: Down, on offer.
        var play = Ready(SpellKind.TooProudToFall);
        Assert.Contains(GoScripted(play, new Shoot(Seat.One, A(1), B(1)), 4).Events, e => e is InjuryRollMade);
        Assert.Equal(new PendingDecision(DecisionKind.UseInjuryReaction, Seat.Two), play.State.Pending);
        play.Refused(Cast(SpellKind.TooProudToFall, ("A2", 3)), ReasonCode.SpellRefused);   // costs 2
        var injury = Go(play, Cast(SpellKind.TooProudToFall, ("A2", 2)), 1).Events.OfType<InjuryRolled>().Single();
        Assert.True(injury.Upheld);
        Assert.Equal(InjuryOutcome.Down, injury.Result);
        Assert.Equal((ModelStatus.Standing, 1), (play.M(B(1)).Status, play.M(B(1)).BloodMarkers));
        Assert.NotNull(play.M(B(1)).Position);
        Assert.Equal(1, play.M(A(2)).BloodMarkers);

        // Declined: Down as usual.
        var down = Ready(SpellKind.TooProudToFall);
        GoScripted(down, new Shoot(Seat.One, A(1), B(1)), 4);
        Go(down, new DeclineReaction(Seat.Two), 1);
        Assert.Equal(ModelStatus.Down, down.M(B(1)).Status);
    }

    [Fact]
    public void BothReactionsAreOfferedAgainstADownResultButOnlyOneIsCast()
    {
        var play = Ready(SpellKind.OracleBeastCloak | SpellKind.TooProudToFall);
        GoScripted(play, new Shoot(Seat.One, A(1), B(1)), 4);
        Assert.Equal(SpellKind.OracleBeastCloak | SpellKind.TooProudToFall, Board.ReactionsFor(play.State, play.M(B(1)), InjuryOutcome.Down));
        var legal = TurnSequence.LegalCommands(play.State).OfType<CastSpell>().Select(c => c.Spell).ToHashSet();
        Assert.Equal([SpellKind.OracleBeastCloak, SpellKind.TooProudToFall], legal.OrderBy(x => x));
        Go(play, Cast(SpellKind.OracleBeastCloak, ("A2", 3)), 1);
        Assert.Null(play.State.Pending);
        Assert.Equal(ModelStatus.Standing, play.M(B(1)).Status);
    }

    [Fact]
    public void ForgedLogsCannotSkipTheHoldOrInventAReaction()
    {
        var play = Ready(SpellKind.OracleBeastCloak);
        var before = play.State;
        var events = Shoot(play).Events;
        var state = before;
        foreach (var e in events.TakeWhile(x => x is not InjuryRollMade))
        {
            state = Reducer.Apply(state, e);
        }
        var made = events.OfType<InjuryRollMade>().Single();
        // The roll applied with no hold and no answer.
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(state, new InjuryRolled(A(1), B(1), made.Injury)));
        // A cloak nobody cast.
        var held = Reducer.Apply(Reducer.Apply(state, made), events.OfType<DecisionRequested>().Last());
        var declined = Reducer.Apply(held, new ReactionDeclined(Seat.Two));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(declined, new InjuryRolled(A(1), B(1), made.Injury) { Cloaked = true }));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(held, new InjuryRolled(A(1), B(1), made.Injury)));   // not answered yet
        Reducer.Apply(declined, new InjuryRolled(A(1), B(1), made.Injury));
    }
}
