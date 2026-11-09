using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;

namespace Rules.Tests.Match;

// October 2026 Rules Review, stage C2 (U144): where SIN MARKERS come from: Beacon of Corruption, the Sin Vessel.
public class SinSourceTests
{
    private static BoardPlay Court(ModelProfile caster)
    {
        var scenario = new Scenario(Field("36", [], [new DeploymentZone("west", F("0", "0", "6", "36")), new DeploymentZone("east", F("30", "0", "36", "36"))]), []);
        var play = new BoardPlay(2, id => id == A(1) ? caster : Rifleman, scenario: scenario).ToActivation();
        play.Put(A(1), "10", "10").Put(A(2), "2", "30").Put(B(1), "16", "10").Put(B(2), "30", "30");
        return play;
    }

    [Fact]
    [Trait("Rule", "Beacon of Corruption: when the Praetor Activates, place 1 SIN MARKER next to it")]
    public void ABeaconAddsASinMarkerEachTimeItsModelActivatesUpToSix()
    {
        var play = Court(Rifleman with { SinBeacon = true });
        play.Do(new ActivateModel(Seat.One, A(1)));
        Assert.Equal(1, play.M(A(1)).SinMarkers);
        play.Do(new EndActivation(Seat.One));
        Assert.Equal(0, play.M(A(2)).SinMarkers);

        var capped = Court(Rifleman with { SinBeacon = true });
        capped.Sin(A(1), 6);
        capped.Do(new ActivateModel(Seat.One, A(1)));
        Assert.Equal(6, capped.M(A(1)).SinMarkers);

        var plain = Court(Rifleman);
        plain.Do(new ActivateModel(Seat.One, A(1)));
        Assert.Equal(0, plain.M(A(1)).SinMarkers);
    }

    [Fact]
    [Trait("Rule", "Sin Vessel: once per game a Break Vessel ACTION places 2 SIN MARKERS next to the model")]
    public void BreakingAVesselGivesTwoSinMarkersOncePerGame()
    {
        var play = Court(Rifleman with { SinVessel = true });
        play.Do(new ActivateModel(Seat.One, A(1)));
        play.Refused(new BreakVessel(Seat.One, A(2)), ReasonCode.ActivationInProgress);
        play.Do(new BreakVessel(Seat.One, A(1)));
        Assert.Equal((2, true), (play.M(A(1)).SinMarkers, play.M(A(1)).VesselBroken));
        Assert.True(play.State.Actions.HasFlag(ActionsTaken.Vessel));
        play.Refused(new BreakVessel(Seat.One, A(1)), ReasonCode.ActionAlreadyTaken);

        var none = Court(Rifleman);
        none.Do(new ActivateModel(Seat.One, A(1)));
        none.Refused(new BreakVessel(Seat.One, A(1)), ReasonCode.NoSuchAction);

        // Never past 6, and a forged second break is refused.
        var full = Court(Rifleman with { SinVessel = true });
        full.Do(new ActivateModel(Seat.One, A(1)));
        full.Sin(A(1), 5);
        full.Do(new BreakVessel(Seat.One, A(1)));
        Assert.Equal(6, full.M(A(1)).SinMarkers);
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(full.State, new VesselBroken(A(1))));
    }

    [Fact]
    public void TheHashSeesTheNewState()
    {
        var play = Court(Rifleman with { SinVessel = true });
        play.Do(new ActivateModel(Seat.One, A(1)));
        var before = StateHash.Compute(play.State);
        play.Do(new BreakVessel(Seat.One, A(1)));
        Assert.NotEqual(before, StateHash.Compute(play.State));
        Assert.Contains("vessel=broken", StateHash.Canonical(play.State), StringComparison.Ordinal);
    }

    // ---- C2b (U145): Mortal Suffering and Perversion of God's Laws ----

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

    // A1 (DEMONIC, `attacker` flags) shoots B1 (`target` flags); A2 is a DEMONIC friend. 4 + 4 = 8 on the Injury Roll: Down, 1 BLOOD MARKER.
    private static BoardPlay Duel(ModelProfile attacker, ModelProfile target)
    {
        var scenario = new Scenario(Field("36", [], [new DeploymentZone("west", F("0", "0", "6", "36")), new DeploymentZone("east", F("30", "0", "36", "36"))]), []);
        var play = new BoardPlay(2, id => id == A(1) ? attacker : id == A(2) ? Rifleman with { Traits = ModelTraits.Demonic } : id == B(1) ? target : Rifleman, scenario: scenario).ToActivation();
        play.Put(A(1), "10", "10").Put(A(2), "2", "30").Put(B(1), "16", "10").Put(B(2), "30", "30");
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    private static readonly ModelProfile Sufferer = Rifleman with { Traits = ModelTraits.Demonic, MortalSuffering = true };
    private static readonly ModelProfile Pervert = Rifleman with { Traits = ModelTraits.Demonic, Perverse = true };

    [Fact]
    [Trait("Rule", "Mortal Suffering: a BLOOD MARKER a friendly DEMONIC model would place next to an enemy may be a SIN MARKER next to a friendly model")]
    public void MortalSufferingTurnsABloodMarkerIntoASinMarkerOnAFriend()
    {
        var play = Duel(Sufferer, Rifleman);
        var made = GoScripted(play, new Shoot(Seat.One, A(1), B(1)), 4).Events;
        Assert.Contains(made, e => e is InjuryRollMade);
        Assert.Equal(new PendingDecision(DecisionKind.ConvertMarkers, Seat.One), play.State.Pending);
        Assert.Equal(ModelStatus.Standing, play.M(B(1)).Status);   // not applied yet
        play.Refused(new ConvertMarkers(Seat.One, 2, A(2), 0), ReasonCode.InvalidChoice);   // only 1 would be placed
        play.Refused(new ConvertMarkers(Seat.One, 1, B(2), 0), ReasonCode.InvalidChoice);   // not a friend
        play.Refused(new ConvertMarkers(Seat.One, 0, null, 1), ReasonCode.InvalidChoice);   // no Perversion on the target
        play.Refused(new ConvertMarkers(Seat.One, 0, A(2), 0), ReasonCode.InvalidChoice);   // a model but nothing converted
        var applied = play.Do(new ConvertMarkers(Seat.One, 1, A(2), 0)).Events.OfType<InjuryRolled>().Single();
        Assert.Equal((1, A(2)), (applied.Converted, applied.ConvertedTo!.Value));
        Assert.Equal((0, 1, ModelStatus.Down), (play.M(B(1)).BloodMarkers, play.M(A(2)).SinMarkers, play.M(B(1)).Status));
        Assert.Null(play.State.Held);
    }

    [Fact]
    public void MortalSufferingMayBeDeclinedAndIsNotAskedWithoutIt()
    {
        var kept = Duel(Sufferer, Rifleman);
        GoScripted(kept, new Shoot(Seat.One, A(1), B(1)), 4);
        kept.Do(new ConvertMarkers(Seat.One, 0, null, 0));
        Assert.Equal((1, 0), (kept.M(B(1)).BloodMarkers, kept.M(A(2)).SinMarkers));

        var plain = Duel(Rifleman with { Traits = ModelTraits.Demonic }, Rifleman);
        var events = GoScripted(plain, new Shoot(Seat.One, A(1), B(1)), 4).Events;
        Assert.DoesNotContain(events, e => e is InjuryRollMade);
        Assert.Null(plain.State.Pending);
        Assert.Equal(1, plain.M(B(1)).BloodMarkers);
    }

    [Fact]
    [Trait("Rule", "Perversion of God's Laws: when a friendly DEMONIC model would have a BLOOD MARKER placed next to it, its opponent can instead remove a SIN MARKER from it")]
    public void PerversionStripsTheTargetsSinMarkerInsteadOfAddingBlood()
    {
        var play = Duel(Rifleman, Pervert);
        play.Sin(B(1), 3);
        GoScripted(play, new Shoot(Seat.One, A(1), B(1)), 4);
        Assert.Equal(new PendingDecision(DecisionKind.ConvertMarkers, Seat.One), play.State.Pending);
        play.Refused(new ConvertMarkers(Seat.One, 0, null, 2), ReasonCode.InvalidChoice);   // only 1 would be placed
        var applied = play.Do(new ConvertMarkers(Seat.One, 0, null, 1)).Events.OfType<InjuryRolled>().Single();
        Assert.Equal(1, applied.Stripped);
        Assert.Equal((0, 2), (play.M(B(1)).BloodMarkers, play.M(B(1)).SinMarkers));

        // No SIN MARKER to lose: not asked.
        var bare = Duel(Rifleman, Pervert);
        Assert.DoesNotContain(GoScripted(bare, new Shoot(Seat.One, A(1), B(1)), 4).Events, e => e is InjuryRollMade);
    }

    [Fact]
    public void ForgedLogsCannotChangeMarkersWithoutTheDecision()
    {
        var play = Duel(Sufferer, Rifleman);
        var before = play.State;
        var events = GoScripted(play, new Shoot(Seat.One, A(1), B(1)), 4).Events;
        var state = before;
        foreach (var e in events.TakeWhile(x => x is not InjuryRollMade))
        {
            state = Reducer.Apply(state, e);
        }
        var made = events.OfType<InjuryRollMade>().Single();
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(state, new InjuryRolled(A(1), B(1), made.Injury)));   // skipped the hold
        var held = Reducer.Apply(Reducer.Apply(state, made), events.OfType<DecisionRequested>().Last());
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(held, new InjuryRolled(A(1), B(1), made.Injury)));   // not answered
        var answered = Reducer.Apply(held, new MarkersConverted(Seat.One, 1, A(2), 0));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(answered, new InjuryRolled(A(1), B(1), made.Injury)));   // forgot the conversion
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(held, new MarkersConverted(Seat.One, 1, B(2), 0)));      // not a friend
        Reducer.Apply(answered, new InjuryRolled(A(1), B(1), made.Injury) { Converted = 1, ConvertedTo = A(2) });
    }

    // ---- C5b (U156): the Wrath, Envy and Gluttony variants earn SIN MARKERS from their attacks ----

    private static readonly ModelProfile Demon = Rifleman with { Traits = ModelTraits.Demonic };

    [Fact]
    [Trait("Rule", "Gluttony variant: a DEMONIC model that takes an enemy Down earns 1 SIN MARKER")]
    public void TakingAModelDownEarnsAGluttonyModelASinMarker()
    {
        var play = Duel(Demon with { DownSins = true }, Rifleman);
        GoScripted(play, new Shoot(Seat.One, A(1), B(1)), 4);
        Assert.Equal((ModelStatus.Down, 1), (play.M(B(1)).Status, play.M(A(1)).SinMarkers));

        var plain = Duel(Demon, Rifleman);
        GoScripted(plain, new Shoot(Seat.One, A(1), B(1)), 4);
        Assert.Equal(0, plain.M(A(1)).SinMarkers);

        var miss = Duel(Demon with { DownSins = true }, Rifleman);
        GoScripted(miss, new Shoot(Seat.One, A(1), B(1)), 1);   // 1 + 1: no effect
        Assert.Equal((ModelStatus.Standing, 0), (miss.M(B(1)).Status, miss.M(A(1)).SinMarkers));
    }

    [Fact]
    [Trait("Rule", "Envy variant: when a model takes an enemy Down, each other friendly DEMONIC model within 6in earns 1 SIN MARKER")]
    public void ATakedownEarnsNearbyDemonicFriendsASinMarker()
    {
        var play = Duel(Demon with { TakedownSins = true }, Rifleman);
        play.Put(A(2), "12", "10");
        GoScripted(play, new Shoot(Seat.One, A(1), B(1)), 4);
        Assert.Equal((0, 1), (play.M(A(1)).SinMarkers, play.M(A(2)).SinMarkers));

        var far = Duel(Demon with { TakedownSins = true }, Rifleman);   // A2 stands 20in away
        GoScripted(far, new Shoot(Seat.One, A(1), B(1)), 4);
        Assert.Equal(0, far.M(A(2)).SinMarkers);
    }

    [Fact]
    [Trait("Rule", "Wrath variant: a DEMONIC model's FIRE hit on an enemy earns it 1 SIN MARKER")]
    public void AFireHitEarnsAWrathModelASinMarker()
    {
        var play = Duel(Demon with { FireSins = true, RangedWeaponKeywords = WeaponKeywords.Fire }, Rifleman);
        GoScripted(play, new Shoot(Seat.One, A(1), B(1)), 4);
        Assert.Equal(1, play.M(A(1)).SinMarkers);

        var noFire = Duel(Demon with { FireSins = true }, Rifleman);
        GoScripted(noFire, new Shoot(Seat.One, A(1), B(1)), 4);
        Assert.Equal(0, noFire.M(A(1)).SinMarkers);
    }
}
