using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Client.Core;

// Single-player (hot-seat) mode: the repository's own authoritative server (trench-server, src/Match.Server), started as
// a separate local process on a free port and stopped again when the session ends. The client never references the
// server's code: it builds the project with the .NET SDK, runs "dotnet Match.Server.dll --port 0 --data <folder>" and
// reads the "Listening on ws://..." line. On Windows the process is also put in a job object that is closed with the
// client's process, so the server stops even if the client crashes.
public sealed class LocalServer : IAsyncDisposable
{
    public const string Project = "src/Match.Server/Match.Server.csproj";

    private readonly Process _process;
    private readonly IntPtr _job;
    private int _disposed;

    private LocalServer(Process process, IntPtr job, Uri endpoint)
    {
        _process = process;
        _job = job;
        Endpoint = endpoint;
    }

    public Uri Endpoint { get; }

    public int ProcessId => _process.Id;

    // True once the server process has ended (after Stop or DisposeAsync, as checked before the handle was released).
    public bool HasExited => _exitedOnStop ?? _process.HasExited;

    private bool? _exitedOnStop;

    // The folder that holds TrenchSim.sln, at or above the given folder; null if there is none.
    public static string? FindRepository(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TrenchSim.sln")) && File.Exists(Path.Combine(dir.FullName, Project)))
            {
                return dir.FullName;
            }
        }
        return null;
    }

    // Where the hot-seat server keeps its signing key and match records (one hot-seat server at a time).
    // Under TRENCH_SIM_HOME when it is set (as for saved seats), else ~/.trench-sim.
    public static string DefaultDataDirectory()
    {
        var home = Environment.GetEnvironmentVariable("TRENCH_SIM_HOME");
        if (string.IsNullOrEmpty(home))
        {
            home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".trench-sim");
        }
        return Path.Combine(home, "hotseat-server");
    }

    // Builds the server (Debug; quick when already built) and starts it. Progress lines go to report.
    public static async Task<LocalServer> StartAsync(string repository, string dataDirectory, Action<string>? report = null,
        CancellationToken cancellationToken = default)
    {
        report?.Invoke("Building the local server (the first time takes a little while)…");
        var project = Path.Combine(repository, Project);
        // No build servers are left running afterwards (node reuse and the shared compiler are off).
        var build = await RunAsync("dotnet", ["build", project, "-c", "Debug", "-nologo", "-v", "q", "-nodeReuse:false", "-p:UseSharedCompilation=false"],
            TimeSpan.FromMinutes(5), cancellationToken);
        if (build.ExitCode != 0)
        {
            throw new LocalServerException($"Could not build the server (dotnet build exit {build.ExitCode}): {Tail(build.Output)}");
        }
        var dll = Path.Combine(repository, "src", "Match.Server", "bin", "Debug", "net10.0", "Match.Server.dll");
        return await StartBuiltAsync(dll, dataDirectory, report, cancellationToken);
    }

    // Starts an already built Match.Server.dll on a free port and waits until it listens.
    public static async Task<LocalServer> StartBuiltAsync(string serverDll, string dataDirectory, Action<string>? report = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(serverDll))
        {
            throw new LocalServerException($"The server was not found at {serverDll}.");
        }
        report?.Invoke("Starting the local server…");
        var info = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in new[] { serverDll, "--port", "0", "--data", dataDirectory })
        {
            info.ArgumentList.Add(arg);
        }
        var process = Process.Start(info) ?? throw new LocalServerException("The server process did not start.");
        var job = KillWithThisProcess(process);
        var listening = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new List<string>();
        void Line(string? line)
        {
            if (line is null)
            {
                return;
            }
            lock (output)
            {
                output.Add(line);
            }
            const string Marker = "Listening on ";
            var at = line.IndexOf(Marker, StringComparison.Ordinal);
            if (at >= 0)
            {
                var url = line[(at + Marker.Length)..].Split(' ')[0];
                if (Uri.TryCreate(url, UriKind.Absolute, out var endpoint))
                {
                    listening.TrySetResult(endpoint);
                }
            }
        }
        process.OutputDataReceived += (_, e) => Line(e.Data);
        process.ErrorDataReceived += (_, e) => Line(e.Data);
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => listening.TrySetException(new LocalServerException("The server stopped before it was listening."));
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            var endpoint = await listening.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
            return new LocalServer(process, job, endpoint);
        }
        catch (Exception e)
        {
            Stop(process, job);
            string text;
            lock (output)
            {
                text = Tail(output);
            }
            throw e is LocalServerException or TimeoutException
                ? new LocalServerException($"The local server did not start: {text}", e)
                : e;
        }
    }

    // Stops the server process (and anything it started) and waits for it to exit.
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }
        Stop(_process, _job);
        try
        {
            await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (Exception e) when (e is TimeoutException or InvalidOperationException)
        {
        }
        _exitedOnStop = _process.HasExited;
        _process.Dispose();
    }

    // The same, synchronously: for a window that is closing and cannot wait for a task.
    public void Stop()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }
        Stop(_process, _job);
        _process.WaitForExit(10_000);
        _exitedOnStop = _process.HasExited;
        _process.Dispose();
    }

    private static void Stop(Process process, IntPtr job)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
        }
        if (job != IntPtr.Zero)
        {
            CloseHandle(job);
        }
    }

    private static async Task<(int ExitCode, List<string> Output)> RunAsync(string file, string[] args, TimeSpan limit, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }
        using var process = Process.Start(info) ?? throw new LocalServerException($"{file} did not start.");
        var job = KillWithThisProcess(process);
        var output = new List<string>();
        process.OutputDataReceived += (_, e) => { if (e.Data is { } d) { lock (output) { output.Add(d); } } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is { } d) { lock (output) { output.Add(d); } } };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancellationToken).WaitAsync(limit, cancellationToken);
            process.WaitForExit();
        }
        catch
        {
            Stop(process, job);
            throw;
        }
        if (job != IntPtr.Zero)
        {
            CloseHandle(job);
        }
        lock (output)
        {
            return (process.ExitCode, [.. output]);
        }
    }

    private static string Tail(List<string> lines) =>
        string.Join(" | ", lines.Where(l => !string.IsNullOrWhiteSpace(l)).TakeLast(5).Select(l => l.Trim()));

    // ---- Windows: a job object that kills the server when this process ends, even by a crash ----

    private const uint KillOnJobClose = 0x2000;
    private const int ExtendedLimitInformation = 9;

    private static IntPtr KillWithThisProcess(Process process)
    {
        if (!OperatingSystem.IsWindows())
        {
            return IntPtr.Zero;
        }
        var job = CreateJobObjectW(IntPtr.Zero, null);
        if (job == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }
        var limits = new ExtendedLimits { Basic = new BasicLimits { LimitFlags = KillOnJobClose } };
        var size = Marshal.SizeOf<ExtendedLimits>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(limits, buffer, false);
            if (!SetInformationJobObject(job, ExtendedLimitInformation, buffer, (uint)size) || !AssignProcessToJobObject(job, process.Handle))
            {
                CloseHandle(job);
                return IntPtr.Zero;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return job;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public IoCounters Io;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}

public sealed class LocalServerException(string message, Exception? inner = null) : Exception(message, inner);
