using System.Runtime.InteropServices;

namespace Match.Server;

// The steps of one durable write, in order. Tests inject a failure before any of them.
public enum WriteStep
{
    CreateTemp,
    Write,
    Flush,
    Rename,
    SyncDirectory,
}

// A hook called before each step of a durable write; it may throw to simulate a failure (an IOException) or a crash
// (any other exception, which the writer does not clean up after).
public interface IWriteFaults
{
    void Before(WriteStep step, string path);
}

// Writes a whole file so that after a crash it is either absent or complete, and never overwrites one that exists:
// write a temporary file in the same folder, flush it to disk, rename it into place (refusing an existing target), then
// flush the folder so the rename itself survives a power cut.
//
// The guarantee holds on Linux with a local ext4 or xfs disk. On macOS the same calls are used and the flush is a full
// flush. Windows has no folder flush; NTFS journals the rename. Both are best effort. Network file systems are not
// supported.
public static class DurableFile
{
    public const string TempSuffix = ".tmp";

    public static void Write(string path, ReadOnlySpan<byte> content, IWriteFaults? faults = null, UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(6))}{TempSuffix}");
        try
        {
            faults?.Before(WriteStep.CreateTemp, path);
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = mode;
            }
            using (var stream = new FileStream(temp, options))
            {
                faults?.Before(WriteStep.Write, path);
                stream.Write(content);
                faults?.Before(WriteStep.Flush, path);
                stream.Flush(flushToDisk: true);
            }
            faults?.Before(WriteStep.Rename, path);
            File.Move(temp, path, overwrite: false);
        }
        catch (IOException)
        {
            TryDelete(temp);
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            TryDelete(temp);
            throw;
        }
        faults?.Before(WriteStep.SyncDirectory, path);
        SyncDirectory(directory);
    }

    // Flushes a folder's entries (the names in it) to disk. A no-op on Windows.
    public static void SyncDirectory(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        var fd = Native.Open(directory, 0);
        if (fd < 0)
        {
            throw new IOException($"Could not open the folder {directory} to flush it (errno {Marshal.GetLastPInvokeError()}).");
        }
        try
        {
            if (Native.Fsync(fd) != 0)
            {
                throw new IOException($"Could not flush the folder {directory} (errno {Marshal.GetLastPInvokeError()}).");
            }
        }
        finally
        {
            _ = Native.Close(fd);
        }
    }

    // Deletes a file and flushes its folder, so the deletion is durable before anything depends on it.
    public static void DeleteDurably(string path)
    {
        File.Delete(path);
        SyncDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    }

    public static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static bool IsTemp(string path) =>
        Path.GetFileName(path).StartsWith('.') && path.EndsWith(TempSuffix, StringComparison.Ordinal);

    private static class Native
    {
        [DllImport("libc", EntryPoint = "open", SetLastError = true, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        public static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

        [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
        public static extern int Fsync(int fd);

        [DllImport("libc", EntryPoint = "close", SetLastError = true)]
        public static extern int Close(int fd);
    }
}
