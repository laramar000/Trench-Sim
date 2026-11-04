using Client.Core;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;
using Sim;

namespace Client.Tests;

// Mines stage M3 (Trench Warfare, p. 162, U93): what the client shows of Mine Markers. The defender lays them on its own
// screen (MineLaying) and sends the whole layout once; the board draws the markers, marked dud or live only for a seat
// that knows the duds; a path preview warns where the move would halt; a halted move travels only to the halt.
public class MineClientTests
{
    private static readonly Lazy<MatchState> AtLaying = new(() =>
    {
        var setup = ScenarioSetups.FromFolder(Path.Combine(Repository.Root, "content"))["ranged-2v2" + ScenarioSetups.TrenchWarfareSuffix];
        var state = Reducer.Apply(MatchState.None, new MatchCreated(setup));
        var bot = new Bot(new Random(3));
        var dice = new SeededDice(7);
        for (var step = 0; step < 200 && state.Pending is not { Kind: DecisionKind.LayMines }; step++)
        {
            var next = bot.Propose(state).Select(c => TurnSequence.Decide(state, c, dice)).First(r => r.IsAccepted);
            foreach (var e in next.Events)
            {
                state = Reducer.Apply(state, e);
            }
        }
        Assert.Equal(DecisionKind.LayMines, state.Pending?.Kind);
        return state;
    });

    private static Seat Defender => AtLaying.Value.Pending!.Seat;

    private static MatchState Laid(MineLaying laying)
    {
        var result = TurnSequence.Decide(AtLaying.Value, laying.Command, new SeededDice(1));
        Assert.True(result.IsAccepted);
        var state = AtLaying.Value;
        foreach (var e in result.Events)
        {
            state = Reducer.Apply(state, e);
        }
        return state;
    }

    private static MineLaying Filled()
    {
        var laying = new MineLaying(AtLaying.Value, Defender);
        laying.FillAtRandom(new Spread("mines"));
        return laying;
    }

    [Fact]
    public void ClicksPlaceAMarkerMakeItADudAndTakeItBackUp()
    {
        var laying = new MineLaying(AtLaying.Value, Defender);
        var area = laying.Area;
        Assert.True(laying.Click(area.CentreX, area.CentreY));
        Assert.Single(laying.Placed);
        Assert.False(laying.IsDud(0));
        // A click on the marker makes it a dud; another takes it back up.
        Assert.True(laying.Click(area.CentreX + 0.2, area.CentreY));
        Assert.True(laying.IsDud(0));
        Assert.Equal(1, laying.DudCount);
        Assert.True(laying.Click(area.CentreX, area.CentreY));
        Assert.Empty(laying.Placed);
        Assert.Equal(0, laying.DudCount);
        // Outside the strip the rules refuse it, and the note says why.
        Assert.False(laying.Click(area.X0 - 3, area.CentreY));
        Assert.Empty(laying.Placed);
        Assert.NotEmpty(laying.Note);
        Assert.False(laying.Ready);
    }

    [Fact]
    public void FillingInAtRandomGivesALayoutTheRulesAccept()
    {
        var laying = Filled();
        Assert.Equal(Mines.Count, laying.Placed.Count);
        Assert.Equal(Mines.Duds, laying.DudCount);
        Assert.True(laying.Ready);
        Assert.Null(TurnSequence.Check(AtLaying.Value, laying.Command));
        Assert.True(Laid(laying).MinesLaid);
        // Undo takes the last marker back up; Clear takes them all.
        Assert.True(laying.Undo());
        Assert.False(laying.Ready);
        laying.Clear();
        Assert.Empty(laying.Placed);
    }

    [Fact]
    public void OnlyTheDefendersScreenLaysTheMines()
    {
        var state = AtLaying.Value;
        Assert.Throws<ArgumentException>(() => new MineLaying(state, Defender.Opponent()));
        var mine = new Interaction();
        mine.Update(state, Defender);
        Assert.NotNull(mine.Mines);
        Assert.True(mine.UsesCursor);
        var theirs = new Interaction();
        theirs.Update(state, Defender.Opponent());
        Assert.Null(theirs.Mines);
        // The legal command is offered to the decision card only through the laying screen, never as a plain hint.
        Assert.DoesNotContain(ActionHints.For(state, Defender, ContentNames.None), h => h.Command is LayMines);
        Assert.Contains("Mine Markers", ActionHints.NotYetOffered(state, Defender) ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void TheBoardShadesTheAreaAndMarksDudsOnlyForTheDefender()
    {
        var names = ContentNames.None;
        Assert.NotNull(BoardScene.Of(AtLaying.Value, Defender, names)!.MineArea);
        var laying = Filled();
        var state = Laid(laying);
        var defender = BoardScene.Of(state, Defender, names)!;
        Assert.Null(defender.MineArea);
        Assert.Equal(Mines.Count, defender.Mines.Count);
        Assert.Equal(Mines.Duds, defender.Mines.Count(m => m.Known == MineKnown.Dud));
        Assert.Contains(defender.Mines, m => m.Mark.EndsWith(" dud", StringComparison.Ordinal));
        // The attacker's copy of the match is told no duds (the server sends MinesLaid.Secret()).
        var laid = TurnSequence.Decide(AtLaying.Value, laying.Command, new SeededDice(1)).Events.OfType<MinesLaid>().Single();
        var secret = Reducer.Apply(AtLaying.Value, laid.Secret());
        var attacker = BoardScene.Of(secret, Defender.Opponent(), names)!;
        Assert.All(attacker.Mines, m => Assert.Equal(MineKnown.Unknown, m.Known));
        Assert.DoesNotContain(attacker.Mines, m => m.Mark.Contains("dud", StringComparison.Ordinal) || m.Mark.Contains("live", StringComparison.Ordinal));
    }

    [Fact]
    public void APathThroughAMarkerSaysWhereItHalts()
    {
        var state = Laid(Filled());
        var marker = state.Mines[0];
        var mover = state.Models.First(m => m.Seat == Defender.Opponent());
        var fact = Previews.MineFact(state with { Models = [.. state.Models.Select(m => m.Id == mover.Id ? m with { Position = new Point2(marker.At.X - Length.FromInches(6), marker.At.Y) } : m)] },
            mover with { Position = new Point2(marker.At.X - Length.FromInches(6), marker.At.Y) }, [new Point2(marker.At.X + Length.FromInches(6), marker.At.Y)]);
        Assert.NotNull(fact);
        Assert.StartsWith(Previews.MineFactStart, fact, StringComparison.Ordinal);
        Assert.Contains(marker.Id, fact, StringComparison.Ordinal);
        Assert.Null(Previews.MineFact(AtLaying.Value, mover, [marker.At]));
    }

    [Fact]
    public void AHaltedMoveTravelsOnlyToTheHalt()
    {
        var id = new ModelId("a1");
        Point2 P(double x, double y) => Previews.PointAt(x, y);
        var path = System.Collections.Immutable.ImmutableArray.Create(P(10, 10), P(20, 10));
        var moved = new ModelMoved(id, MoveKind.Move, path, Length.FromInches(10));
        var stop = new MineStopPoint("mine-1", P(14, 10), 1);
        var after = MatchState.None with { MineStop = new MineStop(id, path, [stop]) };
        Assert.Equal([new TablePoint(10, 10), new TablePoint(14, 10)], Motions.Of(moved, after)!.Path);
        Assert.Equal(2, Motions.Of(moved, MatchState.None)!.Path.Count);
        Assert.Equal([new TablePoint(20, 10)], Motions.Of(new MoveResumed(id, [P(20, 10)]))!.Path);
    }
}
