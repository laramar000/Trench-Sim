using System.Collections.Concurrent;
using System.Reflection;
using Rules.Core.Commands;
using Rules.Core.State;

namespace Match.Server;

// The seat a command is given for, or null for a command no player may send (StartMatch). Every Rules.Core command but
// StartMatch has a Seat property.
internal static class CommandSeats
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> SeatProperty = new();

    public static Seat? Of(Command command)
    {
        var property = SeatProperty.GetOrAdd(command.GetType(), t => t.GetProperty("Seat", typeof(Seat)));
        return property is null ? null : (Seat)property.GetValue(command)!;
    }
}
