using System.Collections.Immutable;
using Rules.Core.Dice;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Rules.Core.Events;

// Trench Warfare's Mine Markers (p. 162; U93).

// The defender set up the Mine Markers. Duds names the 4 duds; it is empty in a copy of the event sent to a seat that may
// not know them (the attacker's), and the state then keeps the duds unknown.
public sealed record MinesLaid : GameEvent
{
    public MinesLaid(Seat seat, ImmutableArray<MineMarker> markers, ImmutableArray<string> duds)
    {
        if (!Enum.IsDefined(seat))
        {
            throw new ArgumentException("Unknown seat.", nameof(seat));
        }
        if (markers.IsDefault || markers.Length != Mines.Count)
        {
            throw new ArgumentException($"The defender sets up {Mines.Count} Mine Markers.", nameof(markers));
        }
        for (var i = 0; i < markers.Length; i++)
        {
            if (markers[i] is null || markers[i].Id != Mines.IdOf(i))
            {
                throw new ArgumentException($"The markers are {Mines.IdOf(0)} to {Mines.IdOf(Mines.Count - 1)}, in order.", nameof(markers));
            }
        }
        duds = duds.IsDefault ? [] : duds;
        if (!duds.IsEmpty && (duds.Length != Mines.Duds || duds.Distinct(StringComparer.Ordinal).Count() != Mines.Duds
            || duds.Any(d => !markers.Any(m => m.Id == d))))
        {
            throw new ArgumentException($"The duds are {Mines.Duds} different markers, or none in a copy that keeps them secret.", nameof(duds));
        }
        Seat = seat;
        Markers = markers;
        Duds = duds;
    }

    public Seat Seat { get; }
    public ImmutableArray<MineMarker> Markers { get; }
    public ImmutableArray<string> Duds { get; }

    // The copy for a seat that may not know the duds.
    public MinesLaid Secret() => new(Seat, Markers, []);
}

// A model's move halted at a Mine Marker; the defender reveals it: a dud does nothing, a live mine detonates. Either
// way the marker is removed.
public sealed record MineRevealed : GameEvent
{
    public MineRevealed(string marker, ModelId model, bool live)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(marker);
        Marker = marker;
        Model = model;
        Live = live;
    }

    public string Marker { get; }
    public ModelId Model { get; }
    public bool Live { get; }
}

// A live mine detonated: an Injury Roll with SHRAPNEL for the model (MINED, p. 56; SHRAPNEL, p. 57): no weapon
// modifier, its armour as usual, no Blood Markers spent (there is no attacker), and 1 extra Blood Marker afterwards
// whatever the result.
public sealed record MineExploded : GameEvent
{
    public const int ShrapnelMarkers = 1;

    public MineExploded(ModelId model, string marker, InjuryRollResult injury)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(marker);
        ArgumentNullException.ThrowIfNull(injury);
        Model = model;
        Marker = marker;
        Injury = injury;
    }

    public ModelId Model { get; }
    public string Marker { get; }
    public InjuryRollResult Injury { get; }
}

// The model carries on along its move after a halt, to the next halt or the end: Path is what it walks (may be empty).
public sealed record MoveResumed : GameEvent
{
    public MoveResumed(ModelId model, ImmutableArray<Point2> path)
    {
        Model = model;
        Path = path.IsDefault ? [] : path;
    }

    public ModelId Model { get; }
    public ImmutableArray<Point2> Path { get; }
}
