using System.Collections.Immutable;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;

namespace Rules.Tests.Match;

// Roster stage R7b (U129): Defuse Mine. A model with it whose move halts at a Mine Marker first rolls a Risky Success
// Roll; a Success removes the marker (live or dud, and nothing is revealed), a Failure leaves the usual reveal and the
// Risky failure ends the activation.
public class DefuseMineTests
{
    private static readonly ModelProfile Sapper = Rifleman with { DefusesMines = true };

    private static BoardPlay Play(bool live, ModelProfile? a1 = null)
    {
        // mine-1 stands at (20, 25) on the way; mine-2 to mine-12 along x 29.5, out of the way.
        var markers = ImmutableArray.CreateRange(new[] { P("20", "25") }.Concat(Enumerable.Range(0, Mines.Count - 1).Select(k => P("29.5", (1.5 + 3 * k).ToString(System.Globalization.CultureInfo.InvariantCulture)))));
        ImmutableArray<string> duds = live ? ["mine-2", "mine-3", "mine-4", "mine-5"] : ["mine-1", "mine-2", "mine-3", "mine-4"];
        var play = TrenchWarfare(1, id => id == A(1) ? a1 ?? Sapper : Rifleman).ToTrenchActivation(markers, duds);
        play.Put(A(1), "14", "25");
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    private static Type[] Kinds(DecideResult result) => [.. result.Events.Select(e => e.GetType())];

    [Fact]
    [Trait("Rule", "Defuse Mine: a Risky roll at the halt removes the Mine Marker; a Success needs no reveal")]
    public void ASuccessRemovesTheMarker()
    {
        foreach (var live in new[] { true, false })
        {
            var play = Play(live);
            var result = play.Do(new MoveModel(Seat.One, A(1), Path(("20", "25"))), 4, 4);
            Assert.Equal([typeof(ModelMoved), typeof(MineDefuseRolled), typeof(MoveResumed)], Kinds(result));
            Assert.True(((MineDefuseRolled)result.Events[1]).Defused);
            Assert.DoesNotContain(play.State.Mines, m => m.Id == "mine-1");
            Assert.Null(play.State.MineStop);
            Assert.Equal(ModelStatus.Standing, play.M(A(1)).Status);
        }
    }

    [Fact]
    [Trait("Rule", "Defuse Mine: a Failure leaves the marker to be revealed as usual, and the Risky failure ends the activation")]
    public void AFailureRevealsTheMarkerAndEndsTheActivation()
    {
        var dud = Play(live: false);
        var result = dud.Do(new MoveModel(Seat.One, A(1), Path(("20", "25"))), 1, 2);
        Assert.Equal([typeof(ModelMoved), typeof(MineDefuseRolled), typeof(MineRevealed), typeof(MoveResumed), typeof(ActivationEnded)], Kinds(result));
        Assert.False(((MineDefuseRolled)result.Events[1]).Defused);

        var live = Play(live: true);
        var blown = live.Do(new MoveModel(Seat.One, A(1), Path(("20", "25"))), 1, 2, 3, 3);
        Assert.Contains(blown.Events, e => e is MineExploded);
    }

    [Fact]
    [Trait("Rule", "Defuse Mine: only a model with it rolls; the others reveal at once")]
    public void OthersRevealAtOnce()
    {
        var play = Play(false, Rifleman);
        Assert.Equal([typeof(ModelMoved), typeof(MineRevealed), typeof(MoveResumed)], Kinds(play.Do(new MoveModel(Seat.One, A(1), Path(("20", "25"))))));
    }
}
