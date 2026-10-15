using System.Text.Json;
using Match.Protocol;
using Rules.Core.State;

namespace Match.Cli;

// A seat this client holds: enough to rejoin after the process is gone. The token is a secret: it is never printed.
public sealed record SavedSeat(string Server, string Code, string MatchId, Seat Seat, string Token);

public enum SavedSeatStatus
{
    Found,
    Missing,

    // The file could not be read as a saved seat. It is left in place for the user to look at.
    Corrupt,

    // The file or its folder can be read by other users (Unix). It is not used.
    Insecure,

    // Both seats of this match are saved here; say which one.
    Ambiguous,
}

public sealed record SavedSeatLookup(SavedSeatStatus Status, SavedSeat? Seat, string? Path);

// Saved seats in ~/.trench-sim/sessions (or $TRENCH_SIM_HOME/sessions): one file per seat, named <matchId>-<seat>.json.
// The folder is created owner-only (0700) and each file owner-only (0600); a file or folder that is readable by others is
// refused. A file is replaced atomically, so it always holds either the old token or the new one.
public sealed class SessionStore(string directory)
{
    private const UnixFileMode OwnerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode OwnerOnlyFolder = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode Others = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 4,
    };

    public string Directory { get; } = directory;

    public static string DefaultDirectory()
    {
        var home = Environment.GetEnvironmentVariable("TRENCH_SIM_HOME");
        if (string.IsNullOrEmpty(home))
        {
            home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".trench-sim");
        }
        return Path.Combine(home, "sessions");
    }

    public string PathOf(string matchId, Seat seat) => Path.Combine(Directory, $"{MatchIds.Check(matchId, nameof(matchId))}-{seat}.json");

    // Saves the seat. Returns false (and the reason) when it could not be saved durably; the caller must then not
    // confirm the token to the server.
    public bool TrySave(SavedSeat seat, out string? problem)
    {
        problem = null;
        try
        {
            EnsureFolder();
            if (!FolderIsPrivate())
            {
                problem = $"{Directory} can be read by other users; not saving the seat there.";
                return false;
            }
            var path = PathOf(seat.MatchId, seat.Seat);
            var temp = path + "." + Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4)) + ".tmp";
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = OwnerOnlyFile;
            }
            try
            {
                using (var stream = new FileStream(temp, options))
                {
                    JsonSerializer.Serialize(stream, new SeatFile(seat.Server, seat.Code, seat.MatchId, seat.Seat.ToString(), seat.Token), Json);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temp, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problem = $"Could not save the seat in {Directory} ({e.GetType().Name}).";
            return false;
        }
    }

    // The saved seat for this server and match code (and seat, when both are saved here).
    public SavedSeatLookup Find(string server, string code, Seat? seat = null)
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return new SavedSeatLookup(SavedSeatStatus.Missing, null, null);
        }
        if (!FolderIsPrivate())
        {
            return new SavedSeatLookup(SavedSeatStatus.Insecure, null, Directory);
        }
        var matches = new List<SavedSeatLookup>();
        SavedSeatLookup? unusable = null;
        foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "*.json").Order(StringComparer.Ordinal))
        {
            var lookup = Load(path);
            if (lookup.Status != SavedSeatStatus.Found)
            {
                // A damaged or exposed file is only a problem if it might be the one asked for.
                if (unusable is null && MightBe(path, code))
                {
                    unusable = lookup;
                }
                continue;
            }
            if (lookup.Seat!.Code == code && lookup.Seat.Server == server && (seat is null || lookup.Seat.Seat == seat))
            {
                matches.Add(lookup);
            }
        }
        return matches.Count switch
        {
            1 => matches[0],
            > 1 => new SavedSeatLookup(SavedSeatStatus.Ambiguous, null, null),
            _ => unusable ?? new SavedSeatLookup(SavedSeatStatus.Missing, null, null),
        };
    }

    public SavedSeatLookup Load(string path)
    {
        try
        {
            if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & Others) != 0)
            {
                return new SavedSeatLookup(SavedSeatStatus.Insecure, null, path);
            }
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length > 4096)
            {
                return new SavedSeatLookup(SavedSeatStatus.Corrupt, null, path);
            }
            var file = JsonSerializer.Deserialize<SeatFile>(bytes, Json);
            if (file is null || file.Server is null || file.Code is null || file.MatchId is null || file.Seat is null || file.Token is null
                || !Enum.TryParse<Seat>(file.Seat, ignoreCase: false, out var seat) || !Enum.IsDefined(seat) || seat.ToString() != file.Seat
                || !Uri.TryCreate(file.Server, UriKind.Absolute, out _))
            {
                return new SavedSeatLookup(SavedSeatStatus.Corrupt, null, path);
            }
            Ids.Check(file.Code, "code");
            MatchIds.Check(file.MatchId, "matchId");
            SeatTokens.Check(file.Token, "token");
            return new SavedSeatLookup(SavedSeatStatus.Found, new SavedSeat(file.Server, file.Code, file.MatchId, seat, file.Token), path);
        }
        catch (Exception e) when (e is JsonException or ArgumentException or NotSupportedException)
        {
            return new SavedSeatLookup(SavedSeatStatus.Corrupt, null, path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new SavedSeatLookup(SavedSeatStatus.Corrupt, null, path);
        }
    }

    public void Delete(string matchId, Seat seat)
    {
        try
        {
            File.Delete(PathOf(matchId, seat));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void EnsureFolder()
    {
        if (OperatingSystem.IsWindows())
        {
            System.IO.Directory.CreateDirectory(Directory);
        }
        else
        {
            System.IO.Directory.CreateDirectory(Directory, OwnerOnlyFolder);
        }
    }

    private bool FolderIsPrivate() =>
        OperatingSystem.IsWindows() || (File.GetUnixFileMode(Directory) & Others) == 0;

    // Whether an unusable file could be the seat for this code. Only a file that plainly names a different code is ruled
    // out; unreadable or unparsable files might be the one asked for.
    private static bool MightBe(string path, string code)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            return document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("code", out var named)
                || named.ValueKind != JsonValueKind.String
                || named.GetString() == code;
        }
        catch (JsonException)
        {
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private sealed record SeatFile(string? Server, string? Code, string? MatchId, string? Seat, string? Token);
}
