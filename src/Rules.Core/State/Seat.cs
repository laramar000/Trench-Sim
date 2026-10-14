namespace Rules.Core.State;

// The two players. Seat One and seat Two are only names; neither has an advantage.
public enum Seat
{
    One,
    Two,
}

public static class SeatExtensions
{
    public static Seat Opponent(this Seat seat) => seat == Seat.One ? Seat.Two : Seat.One;
}
