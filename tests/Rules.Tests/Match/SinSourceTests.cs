using Rules.Core;
using Rules.Core.Commands;
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
}
