using System.Collections.Immutable;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.State;
using static Rules.Tests.Match.BoardPlay;
using static Rules.Tests.Match.MeleePlay;

namespace Rules.Tests.Match;

// Roster stage R5g (U112): a Crown of Hellfire (when its bearer ends an activation, 1 BLOOD MARKER on each enemy model
// within 1", Warbands p. 150) and a Damned Pact (the same when its bearer is taken Out of Action, before it is removed,
// p. 83). NEGATE FIRE models are not affected; a Sacrificial Lamb keeps the marker off (U109).
public class BurnTests
{
    private static readonly ModelProfile Crowned = Brawler with { BurnsAtActivationEnd = true };
    private static readonly ModelProfile Pact = Brawler with { BurnsWhenTakenOut = true };

    // A1 (4, 2) is activated beside B1 (5.1, 2); B2 and A2 stand where given.
    private static BoardPlay Near(Func<ModelId, ModelProfile> profiles, string b2x = "30", string b2y = "30", string a2x = "2", string a2y = "30", int perSide = 2)
    {
        var play = new BoardPlay(perSide, profiles).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), "5.1", "2");
        if (perSide > 1)
        {
            play.Put(B(2), b2x, b2y).Put(A(2), a2x, a2y);
        }
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    [Fact]
    [Trait("Rule", "Crown of Hellfire: when the bearer ends an Activation, each enemy within 1\" gets 1 BLOOD MARKER")]
    public void TheCrownBurnsEnemiesWithinOneInch()
    {
        // B2 exactly 1" from A1's base (centres 2" apart); A2, a friend, beside it too.
        var play = Near(id => id == A(1) ? Crowned : Brawler, "4", "4", "3", "2.9");
        var events = play.Do(new EndActivation(Seat.One)).Events;
        var placed = events.OfType<BloodMarkersPlaced>().Single();
        Assert.IsType<ActivationEnded>(events[events.IndexOf(placed) - 1]);
        Assert.Equal((A(1), "B1,B2", 0), (placed.Source, string.Join(",", placed.Targets), placed.Warded.Length));
        Assert.Equal((1, 1, 0), (play.M(B(1)).BloodMarkers, play.M(B(2)).BloodMarkers, play.M(A(2)).BloodMarkers));

        // Just beyond 1", or no Crown: nothing.
        var far = Near(id => id == A(1) ? Crowned : Brawler, "4", "4.01");
        Assert.Equal([B(1)], far.Do(new EndActivation(Seat.One)).Events.OfType<BloodMarkersPlaced>().Single().Targets.ToArray());
        Assert.Empty(Near(_ => Brawler, "4", "4").Do(new EndActivation(Seat.One)).Events.OfType<BloodMarkersPlaced>());

        // NEGATE FIRE models are not affected; with nobody left, no event at all.
        var fireproof = Near(id => id == A(1) ? Crowned : Brawler with { Negates = WeaponKeywords.Fire });
        Assert.Empty(fireproof.Do(new EndActivation(Seat.One)).Events.OfType<BloodMarkersPlaced>());
        Assert.DoesNotContain("markersOwed", StateHash.Canonical(fireproof.State), StringComparison.Ordinal);

        // A Down bearer still burns; a Sacrificial Lamb keeps the first marker off, and only once.
        var down = Near(id => id == A(1) ? Crowned : Brawler with { WardedByBlood = true }).SetStatus(B(1), ModelStatus.Down);
        var warded = down.Do(new EndActivation(Seat.One)).Events.OfType<BloodMarkersPlaced>().Single();
        Assert.Equal([B(1)], warded.Warded.ToArray());
        Assert.Equal((0, true), (down.M(B(1)).BloodMarkers, down.M(B(1)).WardSpent));
    }

    [Fact]
    [Trait("Rule", "Damned Pact: when the bearer is taken Out of Action, before it is removed, each enemy within 1\" gets 1 BLOOD MARKER")]
    public void ThePactBurnsWhenItsBearerIsTakenOut()
    {
        // B1 (the bearer) is fought Out of Action (hit 4 + 4, injury 6 + 6); A1 and A2 are within 1" of where it stood,
        // B2 (its friend) is too but is not burned.
        var play = Near(id => id == B(1) ? Pact : Brawler, "5.1", "3.9", "5.1", "0.1");
        var events = play.Do(new FightModel(Seat.One, A(1), B(1)), 4, 4, 6, 6).Events;
        var injury = events.OfType<InjuryRolled>().Single();
        Assert.Equal(InjuryOutcome.OutOfAction, injury.Result);
        var placed = events.OfType<BloodMarkersPlaced>().Single();
        Assert.Equal(events.IndexOf(injury) + 1, events.IndexOf(placed));
        Assert.Equal((B(1), "A1,A2"), (placed.Source, string.Join(",", placed.Targets)));
        Assert.Equal((1, 1, 0), (play.M(A(1)).BloodMarkers, play.M(A(2)).BloodMarkers, play.M(B(2)).BloodMarkers));

        // Down is not Out of Action: nothing.
        var down = Near(id => id == B(1) ? Pact : Brawler);
        Assert.Empty(down.Do(new FightModel(Seat.One, A(1), B(1)), 4, 4, 4, 4).Events.OfType<BloodMarkersPlaced>());

        // The last enemy model: its markers are placed before the match ends.
        var last = Near(id => id == B(1) ? Pact : Brawler, perSide: 1);
        var end = last.Do(new FightModel(Seat.One, A(1), B(1)), 4, 4, 6, 6).Events;
        Assert.True(end.ToList().FindIndex(e => e is BloodMarkersPlaced) < end.ToList().FindIndex(e => e is MatchEnded));
        Assert.Equal(1, last.M(A(1)).BloodMarkers);
    }

    [Fact]
    [Trait("Rule", "Records: owed BLOOD MARKERS come next, exactly as owed; none can be placed when none are owed")]
    public void ForgedBurnsAreRefused()
    {
        var play = Near(id => id == A(1) ? Crowned : Brawler with { WardedByBlood = true });
        var events = TurnSequence.Decide(play.State, new EndActivation(Seat.One), new FixedDice()).Events;
        var ended = Reducer.Apply(play.State, events.OfType<ActivationEnded>().Single());
        Assert.Contains("markersOwed", StateHash.Canonical(ended), StringComparison.Ordinal);
        var placed = events.OfType<BloodMarkersPlaced>().Single();
        // Anything else first, the wrong source, or a ward claimed wrongly.
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(ended, new ActivationStarted(Seat.Two, B(1))));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(ended, new BloodMarkersPlaced(B(2), [B(1)], [B(1)])));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(ended, new BloodMarkersPlaced(A(1), [B(1)], [])));
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(play.State, placed));
        Assert.DoesNotContain("markersOwed", StateHash.Canonical(Reducer.Apply(ended, placed)), StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => new BloodMarkersPlaced(A(1), [], []));
        Assert.Throws<ArgumentException>(() => new BloodMarkersPlaced(A(1), [B(1)], [B(2)]));
    }

    [Fact]
    [Trait("Rule", "Records: matches with Crowns and Pacts play to the end and replay; the fields are hashed only when set")]
    public void SimulatedMatchesReplay()
    {
        var plain = StateHash.Canonical(Near(_ => Brawler).State);
        Assert.DoesNotContain("burns", plain, StringComparison.Ordinal);
        var kitted = StateHash.Canonical(Near(_ => Brawler with { BurnsAtActivationEnd = true, BurnsWhenTakenOut = true }).State);
        Assert.Contains("burnsAtEnd=yes", kitted, StringComparison.Ordinal);
        Assert.Contains("burnsWhenOut=yes", kitted, StringComparison.Ordinal);
        var (crowns, pacts) = (0, 0);
        foreach (var path in new[] { Sim.Content.DefaultPath(), Sim.Content.MeleePath() })
        {
            var warbands = Sim.Content.Load(path).Select(w => w with
            {
                Models = w.Models.Select((m, i) => m with { Profile = m.Profile with { BurnsAtActivationEnd = i % 2 == 0, BurnsWhenTakenOut = i % 2 == 1, WardedByBlood = i == 1 } }).ToList(),
            }).ToList();
            for (var seed = 1; seed <= 8; seed++)
            {
                var run = Sim.Runner.Run(seed, warbands);
                Assert.True(run.Terminal, run.Status);
                Assert.Equal(run.FinalHash, StateHash.Compute(Reducer.Replay(run.Log)));
                var log = run.Log.ToList();
                foreach (var (placed, i) in log.Select((e, i) => (e, i)).Where(x => x.e is BloodMarkersPlaced))
                {
                    var before = log[i - 1];
                    crowns += before is ActivationEnded ? 1 : 0;
                    pacts += before is InjuryRolled or MineExploded ? 1 : 0;
                }
            }
        }
        Assert.True(crowns > 0 && pacts > 0, $"{crowns} crowns, {pacts} pacts");
    }
}
