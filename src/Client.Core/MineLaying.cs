using System.Collections.Immutable;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Client.Core;

// Trench Warfare (p. 162; U93): the defender sets up 12 Mine Markers in the Mine Placement Area and picks 4 of them as
// duds, before anyone deploys. This keeps the markers the player has put down so far on this screen only: nothing is
// sent until Lay sends the whole layout, and the server checks it like any other command (TurnSequence.Check here
// first, so the button is offered only when the server would accept it). Click on an empty spot in the area to put a
// marker there; click on a marker to make it a dud, or a dud to take the marker back up.
public sealed class MineLaying
{
    private readonly List<Point2> _placed = [];
    private readonly HashSet<int> _duds = [];
    private readonly Battlefield _field;

    public MineLaying(MatchState state, Seat seat)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Pending is not { Kind: DecisionKind.LayMines } pending || pending.Seat != seat || state.Setup?.Scenario is not { } scenario
            || state.SeatFor(seat).ZoneId is not { } own || state.SeatFor(seat.Opponent()).ZoneId is not { } theirs
            || Mines.PlacementArea(scenario.Battlefield, own, theirs) is not { } area)
        {
            throw new ArgumentException("No Mine Markers are waiting on this seat.", nameof(state));
        }
        State = state;
        Seat = seat;
        _field = scenario.Battlefield;
        AreaRect = area;
        Area = TableRect.Of(area);
    }

    public MatchState State { get; }
    public Seat Seat { get; }
    public Rect AreaRect { get; }
    public TableRect Area { get; }

    public IReadOnlyList<Point2> Placed => _placed;

    public bool IsDud(int index) => _duds.Contains(index);

    public int DudCount => _duds.Count;

    // What the last click did, in words.
    public string Note { get; private set; } = "";

    // The markers as the board draws them: id, centre and whether it is a dud.
    public IReadOnlyList<(string Id, TablePoint At, bool Dud)> Shapes =>
        [.. _placed.Select((p, i) => (Mines.IdOf(i), TablePoint.Of(p), _duds.Contains(i)))];

    public bool Ready => _placed.Count == Mines.Count && _duds.Count == Mines.Duds && TurnSequence.Check(State, Command) is null;

    public LayMines Command => new(Seat, [.. _placed], [.. _duds.Order().Select(Mines.IdOf)]);

    public string Status =>
        $"Mine Markers {_placed.Count} of {Mines.Count} placed; duds {_duds.Count} of {Mines.Duds} chosen. " +
        (_placed.Count < Mines.Count ? "Click in the shaded strip in front of your zone to place a marker. " : "") +
        (_duds.Count < Mines.Duds ? "Click a marker to make it a dud (only you see which). " : "") +
        "Click a dud to take it back up.";

    // A click at a table point (inches): on a marker, toggles it between live and dud (a dud is taken back up); else
    // places a new marker there if the rules allow one.
    public bool Click(double x, double y)
    {
        var at = Previews.PointAt(x, y);
        var hit = _placed.FindIndex(p => ExactDistanceWithin(p, at, Mines.MarkerRadius));
        if (hit >= 0)
        {
            if (_duds.Remove(hit))
            {
                RemoveAt(hit);
                Note = $"{Mines.IdOf(hit)} taken back up.";
            }
            else if (_duds.Count < Mines.Duds)
            {
                _duds.Add(hit);
                Note = $"{Mines.IdOf(hit)} is a dud.";
            }
            else
            {
                Note = $"There are already {Mines.Duds} duds: click a dud to take it back up first.";
                return false;
            }
            return true;
        }
        if (_placed.Count >= Mines.Count)
        {
            Note = $"All {Mines.Count} Mine Markers are placed.";
            return false;
        }
        if (Mines.Problem(_field, AreaRect, at, _placed) is { } problem)
        {
            Note = problem;
            return false;
        }
        _placed.Add(at);
        Note = $"{Mines.IdOf(_placed.Count - 1)} placed at {TablePoint.Of(at)}.";
        return true;
    }

    // Takes the last marker back up.
    public bool Undo()
    {
        if (_placed.Count == 0)
        {
            return false;
        }
        RemoveAt(_placed.Count - 1);
        Note = "Last marker taken back up.";
        return true;
    }

    public void Clear()
    {
        _placed.Clear();
        _duds.Clear();
        Note = "All markers taken back up.";
    }

    // Places the markers still missing at random legal spots and picks the duds still missing at random among all.
    public void FillAtRandom(Spread spread)
    {
        ArgumentNullException.ThrowIfNull(spread);
        var r = Mines.MarkerRadius.Thousandths;
        var r0 = AreaRect;
        for (var tries = 0; tries < 20_000 && _placed.Count < Mines.Count; tries++)
        {
            var x = spread.Next((int)(r0.Min.X.Thousandths + r), (int)(r0.Max.X.Thousandths - r + 1));
            var y = spread.Next((int)(r0.Min.Y.Thousandths + r), (int)(r0.Max.Y.Thousandths - r + 1));
            var at = new Point2(Length.FromThousandths(x), Length.FromThousandths(y));
            if (Mines.Problem(_field, AreaRect, at, _placed) is null)
            {
                _placed.Add(at);
            }
        }
        // Should the area be too crowded for random spots, the rules' own grid fills the rest.
        foreach (var at in Mines.Grid(_field, AreaRect))
        {
            if (_placed.Count >= Mines.Count)
            {
                break;
            }
            if (Mines.Problem(_field, AreaRect, at, _placed) is null)
            {
                _placed.Add(at);
            }
        }
        while (_duds.Count < Mines.Duds && _duds.Count < _placed.Count)
        {
            _duds.Add(spread.Next(0, _placed.Count));
        }
        Note = _placed.Count == Mines.Count ? "Markers and duds filled in at random." : "There is no room for every marker.";
    }

    private void RemoveAt(int index)
    {
        _placed.RemoveAt(index);
        var later = _duds.Where(d => d > index).ToList();
        _duds.RemoveWhere(d => d >= index);
        foreach (var d in later)
        {
            _duds.Add(d - 1);
        }
    }

    private static bool ExactDistanceWithin(Point2 a, Point2 b, Length radius)
    {
        var dx = (double)(a.X.Thousandths - b.X.Thousandths);
        var dy = (double)(a.Y.Thousandths - b.Y.Thousandths);
        return dx * dx + dy * dy <= (double)radius.Thousandths * radius.Thousandths;
    }
}
