using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TestCenter;

// Every program the test center starts goes through here, so nothing is left behind: each one is stopped (with
// everything it started) when its step ends, when the test center quits, and on Windows even when its window is closed
// or it crashes (a job object that ends with this process). Output goes to a log file per program.
public sealed class Processes : IDisposable
{
    private readonly List<Child> _children = [];
    private readonly IntPtr _job = CreateKillOnCloseJob();

    public Child Start(string name, string file, IEnumerable<string> args, string logFolder, IReadOnlyDictionary<string, string>? environment = null,
        string? workingDirectory = null, Action<string>? echo = null)
    {
        var info = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }
        info.Environment["DOTNET_NOLOGO"] = "1";
        info.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        foreach (var (key, value) in environment ?? new Dictionary<string, string>())
        {
            info.Environment[key] = value;
        }
        if (workingDirectory is not null)
        {
            info.WorkingDirectory = workingDirectory;
        }
        Process process;
        try
        {
            process = Process.Start(info) ?? throw new FriendlyException($"Could not start {Path.GetFileName(file)}.");
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            throw new FriendlyException($"Could not start {file}: {e.Message}");
        }
        if (_job != IntPtr.Zero)
        {
            AssignProcessToJobObject(_job, process.Handle);
        }
        Directory.CreateDirectory(logFolder);
        var log = Path.Combine(logFolder, $"{name}.txt");
        File.WriteAllText(log, $"{file} {string.Join(' ', info.ArgumentList.Select(Quote))}\n\n");
        var child = new Child(name, process, log, echo);
        lock (_children)
        {
            _children.Add(child);
        }
        return child;
    }

    // Runs a program to the end (or the time limit, after which it is stopped). Exit is null when it was stopped.
    public async Task<(int? Exit, List<string> Lines)> Run(string name, string file, IEnumerable<string> args, string logFolder, double seconds,
        Action<string>? echo = null, IReadOnlyDictionary<string, string>? environment = null)
    {
        var child = Start(name, file, args, logFolder, environment, echo: echo);
        var exit = await child.Exit(seconds);
        return (exit, child.Lines);
    }

    public void StopAll()
    {
        List<Child> children;
        lock (_children)
        {
            children = [.. _children];
        }
        foreach (var child in children)
        {
            child.Kill();
        }
    }

    public void Dispose()
    {
        StopAll();
        if (_job != IntPtr.Zero)
        {
            CloseHandle(_job);
        }
    }

    // Running programs whose command line contains the given text (a test folder). Linux reads /proc; Windows asks WMI.
    public static List<(int Pid, string CommandLine)> Using(string text) =>
        [.. All().Where(p => p.Pid != Environment.ProcessId
            && p.CommandLine.Contains(text, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            .Select(p => (p.Pid, p.CommandLine))];

    // Every program a process started, and what those started in turn (for example the game the Godot editor starts
    // when Play is pressed).
    public static List<(int Pid, string CommandLine)> Descendants(int root)
    {
        var all = All();
        var ours = new HashSet<int> { root };
        bool added;
        do
        {
            added = false;
            foreach (var p in all)
            {
                if (!ours.Contains(p.Pid) && ours.Contains(p.Parent))
                {
                    ours.Add(p.Pid);
                    added = true;
                }
            }
        }
        while (added);
        return [.. all.Where(p => p.Pid != root && ours.Contains(p.Pid)).Select(p => (p.Pid, p.CommandLine))];
    }

    public static void Stop(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: true);
            process.WaitForExit(10_000);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    // Lines a PowerShell command prints (Windows only).
    public static List<string> PowerShell(string command)
    {
        var info = new ProcessStartInfo("powershell", ["-NoProfile", "-NonInteractive", "-Command", command])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var ps = Process.Start(info)!;
        var text = ps.StandardOutput.ReadToEnd();
        ps.WaitForExit();
        return [.. text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0)];
    }

    private static List<(int Pid, int Parent, string CommandLine)> All()
    {
        var all = new List<(int, int, string)>();
        if (OperatingSystem.IsWindows())
        {
            foreach (var line in PowerShell("Get-CimInstance Win32_Process | ForEach-Object { \"$($_.ProcessId)`t$($_.ParentProcessId)`t$($_.CommandLine)\" }"))
            {
                var parts = line.Split('\t', 3);
                if (parts.Length == 3 && int.TryParse(parts[0], out var pid) && int.TryParse(parts[1], out var parent))
                {
                    all.Add((pid, parent, parts[2]));
                }
            }
        }
        else if (Directory.Exists("/proc"))
        {
            foreach (var dir in Directory.EnumerateDirectories("/proc"))
            {
                if (!int.TryParse(Path.GetFileName(dir), out var pid))
                {
                    continue;
                }
                try
                {
                    var stat = File.ReadAllText(Path.Combine(dir, "stat"));
                    var parent = int.Parse(stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[1]);
                    all.Add((pid, parent, File.ReadAllText(Path.Combine(dir, "cmdline")).Replace('\0', ' ').Trim()));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or IndexOutOfRangeException)
                {
                }
            }
        }
        return all;
    }

    private static string Quote(string arg) => arg.Contains(' ') || arg.Length == 0 ? $"\"{arg}\"" : arg;

    public sealed class Child
    {
        private readonly object _gate = new();
        private readonly List<string> _lines = [];
        private readonly StreamWriter _log;
        private readonly Action<string>? _echo;

        public Child(string name, Process process, string log, Action<string>? echo)
        {
            Name = name;
            Process = process;
            LogPath = log;
            _echo = echo;
            _log = new StreamWriter(log, append: true) { AutoFlush = true };
            process.OutputDataReceived += (_, e) => Add(e.Data);
            process.ErrorDataReceived += (_, e) => Add(e.Data);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        public string Name { get; }

        public Process Process { get; }

        public string LogPath { get; }

        public bool HasExited
        {
            get
            {
                try
                {
                    return Process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    return true;
                }
            }
        }

        public List<string> Lines
        {
            get
            {
                lock (_gate)
                {
                    return [.. _lines];
                }
            }
        }

        private void Add(string? line)
        {
            if (line is null)
            {
                return;
            }
            lock (_gate)
            {
                _lines.Add(line);
                try
                {
                    _log.WriteLine(line);
                }
                catch (ObjectDisposedException)
                {
                }
            }
            _echo?.Invoke(line);
        }

        // The first output line that matches, or null when the program ended or the time ran out first.
        public async Task<string?> WaitFor(Func<string, bool> condition, double seconds)
        {
            var deadline = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < deadline)
            {
                if (Lines.FirstOrDefault(condition) is { } line)
                {
                    return line;
                }
                if (HasExited)
                {
                    await Task.Delay(200);
                    return Lines.FirstOrDefault(condition);
                }
                await Task.Delay(100);
            }
            return null;
        }

        public async Task<int?> Exit(double seconds)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
            try
            {
                await Process.WaitForExitAsync(timeout.Token);
                Process.WaitForExit();
                return Process.ExitCode;
            }
            catch (OperationCanceledException)
            {
                Kill();
                return null;
            }
        }

        public void Kill()
        {
            try
            {
                if (!Process.HasExited)
                {
                    Process.Kill(entireProcessTree: true);
                    Process.WaitForExit(10_000);
                }
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
        }
    }

    // ---- Windows: a job object that stops every program started here when this process ends, even by a crash ----

    private const uint KillOnJobClose = 0x2000;
    private const int ExtendedLimitInformation = 9;

    private static IntPtr CreateKillOnCloseJob()
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
            if (!SetInformationJobObject(job, ExtendedLimitInformation, buffer, (uint)size))
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

// A problem explained in plain words for the person running the tests (no stack trace).
public sealed class FriendlyException(string message) : Exception(message);
