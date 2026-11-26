using Match.Client;

namespace Client.Core;

// The seats saved for one server, for the connect screen's Rejoin list. Unreadable or exposed files are reported with
// the existing wording and never used; tokens are never shown.
public static class SavedSeats
{
    public static (IReadOnlyList<SavedSeat> Seats, string? Problem) For(SessionStore store, string server)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (!Directory.Exists(store.Directory))
        {
            return ([], null);
        }
        const UnixFileMode others = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(store.Directory) & others) != 0)
        {
            return ([], $"{store.Directory} can be read by other users; fix its permissions (folder 700, files 600) before rejoining.");
        }
        var seats = new List<SavedSeat>();
        string? problem = null;
        foreach (var path in Directory.EnumerateFiles(store.Directory, "*.json").Order(StringComparer.Ordinal))
        {
            var lookup = store.Load(path);
            switch (lookup.Status)
            {
                case SavedSeatStatus.Found when lookup.Seat!.Server == server:
                    seats.Add(lookup.Seat);
                    break;
                case SavedSeatStatus.Insecure:
                    problem = $"{path} can be read by other users; it was not used.";
                    break;
                case SavedSeatStatus.Corrupt:
                    problem ??= $"{path} cannot be read; it was left as it is.";
                    break;
            }
        }
        return (seats, problem);
    }
}
