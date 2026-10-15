namespace Match.Server;

// The server's data folder for its lifetime: the single-instance lock, the signing key, the record store (recovered at
// start-up) and the record writer.
public sealed class ServerData : IDisposable
{
    private readonly FileStream _lock;

    private ServerData(FileStream lockFile, SigningKey key, RecordStore store, RecoveryReport recovery, RecordWriter writer)
    {
        _lock = lockFile;
        Key = key;
        Store = store;
        Recovery = recovery;
        Writer = writer;
    }

    public SigningKey Key { get; }
    public RecordStore Store { get; }
    public RecoveryReport Recovery { get; }
    public RecordWriter Writer { get; }

    public static string DefaultDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".trench-sim", "server");

    public static ServerData Open(string directory, bool newSigningKey = false, IWriteFaults? faults = null, Action<string>? log = null, TimeProvider? time = null)
    {
        SigningKey.CreateDirectory(directory);
        FileStream lockFile;
        try
        {
            lockFile = new FileStream(Path.Combine(directory, "server.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException e)
        {
            throw new ServerDataException($"Another server is using {directory}.", e);
        }
        SigningKey? key = null;
        try
        {
            key = SigningKey.Load(directory, newSigningKey);
            var store = new RecordStore(directory, key, faults, log, time);
            var recovery = store.Recover();
            return new ServerData(lockFile, key, store, recovery, new RecordWriter(store, log));
        }
        catch
        {
            key?.Dispose();
            lockFile.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        Writer.CompleteAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
        Key.Dispose();
        _lock.Dispose();
    }
}
