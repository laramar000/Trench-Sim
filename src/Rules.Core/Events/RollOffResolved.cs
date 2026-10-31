using System.Collections.Immutable;
using Rules.Core.State;

namespace Rules.Core.Events;

public enum RollOffPurpose
{
    DeploymentZone,
    DeploymentOrder,
    Initiative,
    Roles, // Trench Warfare (p. 161): the winner chooses who attacks
}

public sealed record RollOffRound
{
    public RollOffRound(int seatOne, int seatTwo)
    {
        if (seatOne is < 1 or > 6 || seatTwo is < 1 or > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(seatOne), "Every die must be 1 to 6.");
        }
        SeatOne = seatOne;
        SeatTwo = seatTwo;
    }

    public int SeatOne { get; }
    public int SeatTwo { get; }
    public bool IsTie => SeatOne == SeatTwo;
}

// A roll-off (rulebook 1.0.2 p. 31): each player rolls a D6 and the highest wins; a tie is rolled again.
// Every round is recorded; all rounds but the last were ties.
public sealed record RollOffResolved : GameEvent
{
    public RollOffResolved(RollOffPurpose purpose, IEnumerable<RollOffRound> rounds, Seat winner)
    {
        ArgumentNullException.ThrowIfNull(rounds);
        var list = rounds.ToImmutableArray();
        if (!Enum.IsDefined(purpose) || !Enum.IsDefined(winner))
        {
            throw new ArgumentException("Unknown roll-off purpose or winner.");
        }
        if (list.IsEmpty || list.Any(r => r is null))
        {
            throw new ArgumentException("A roll-off has at least one round.", nameof(rounds));
        }
        if (list.Take(list.Length - 1).Any(r => !r.IsTie) || list[^1].IsTie)
        {
            throw new ArgumentException("Only the last round of a roll-off may decide it.", nameof(rounds));
        }
        var last = list[^1];
        if (winner != (last.SeatOne > last.SeatTwo ? Seat.One : Seat.Two))
        {
            throw new ArgumentException("The winner must be the seat with the higher last die.", nameof(winner));
        }
        Purpose = purpose;
        Rounds = list;
        Winner = winner;
    }

    public RollOffPurpose Purpose { get; }
    public ImmutableArray<RollOffRound> Rounds { get; }
    public Seat Winner { get; }
}
