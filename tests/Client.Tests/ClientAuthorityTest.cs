using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Client.Tests;

// M7: the client holds no authority. The server resolves every command, rolls every die and decides every outcome; a
// client only applies the server's events (through MatchMirror, which checks each hash) and sends commands. These checks
// replace the plan's "Rules.Core.Resolution" check (that namespace does not exist): they look at the project files and
// at the compiled code of Match.Client and of the Godot client's scripts. A text scan of the Godot sources is kept only
// as a quick extra check; the compiled scan is the guard.
public class ClientAuthorityTest
{
    private static readonly HashSet<string> AllowedAssemblyReferences =
    [
        "System.Runtime",
        "System.Collections",
        "System.Collections.Concurrent",
        "System.Linq",
        "System.Memory",
        "System.Net.Http",
        "System.Net.Primitives",
        "System.Net.WebSockets",
        "System.Net.WebSockets.Client",
        "System.Security.Cryptography",
        "System.Text.Json",
        "System.Threading",
        "System.Threading.Channels",
        "System.Collections.Immutable",
        "Match.Protocol",
        "Rules.Core",
    ];

    // Types a client must never use: the resolver's dice, the reducer (only MatchMirror applies events), and the
    // record verifier (records are never loaded into a live match).
    private static readonly HashSet<string> ForbiddenTypes =
    [
        "Rules.Core.Dice.IDiceSource",
        "Rules.Core.Dice.RandomDice",
        "Rules.Core.Reducer",
        "Match.Protocol.RecordVerifier",
        "Match.Protocol.MatchRecordBody",
        "Sim.SeededDice",
    ];

    // Members a client must never call, by type and name.
    private static readonly HashSet<string> ForbiddenMembers =
    [
        "Rules.Core.TurnSequence.Decide",
    ];

    // The Godot client's scripts: Godot's own assembly, the shared client libraries and basic runtime assemblies only.
    // Never Match.Server, Sim or Match.Cli.
    private static readonly HashSet<string> AllowedGodotAssemblyReferences =
    [
        "GodotSharp",
        "Client.Core",
        "Match.Client",
        "Match.Protocol",
        "Rules.Core",
        "System.Runtime",
        "System.Runtime.InteropServices",
        "System.Collections",
        "System.Collections.Concurrent",
        "System.Console",
        "System.Linq",
        "System.Memory",
        "System.Threading",
    ];

    // On top of the rules for Match.Client: scripts must not feed the copy of the match themselves (only ClientSession
    // applies the server's events, after reading them in order), and must not reach forbidden code by reflection.
    private static readonly HashSet<string> ForbiddenGodotTypes =
    [
        "System.Activator",
        "System.Runtime.Loader.AssemblyLoadContext",
        "System.Reflection.Emit.DynamicMethod",
    ];

    private static readonly HashSet<string> ForbiddenGodotMembers =
    [
        "Match.Protocol.MatchMirror.Apply",
        "Match.Protocol.MatchMirror.Reset",
        "Match.Protocol.Codec.ParseServer",
        "System.Type.GetType",
        "System.Type.GetMethod",
        "System.Type.GetMethods",
        "System.Type.InvokeMember",
        "System.Reflection.MethodBase.Invoke",
        "System.Reflection.Assembly.Load",
        "System.Reflection.Assembly.LoadFrom",
        "System.Reflection.Assembly.LoadFile",
        "System.Reflection.Assembly.GetType",
    ];

    // The same rules, as text, for the Godot client's sources: a quick extra check, not the guard.
    private static readonly Regex ForbiddenSource = new(
        @"\bTurnSequence\s*\.\s*Decide\b|\bIDiceSource\b|\bRandomDice\b|\bSeededDice\b|\bReducer\s*\.|\bRecordVerifier\b|\bMatchRecordBody\b"
        + @"|\busing\s+Sim\b|\bSim\s*\.|\bMatch\.Server\b|\bMatch\.Cli\b",
        RegexOptions.Compiled);

    [Fact]
    public void MatchClientUsesThePlainSdkAndOnlyReferencesMatchProtocol()
    {
        var project = XDocument.Load(Path.Combine(Repository.Root, "src", "Match.Client", "Match.Client.csproj"));
        Assert.Equal("Microsoft.NET.Sdk", (string?)project.Root?.Attribute("Sdk"));
        var references = project.Descendants()
            .Where(e => e.Name.LocalName is "PackageReference" or "Reference" or "FrameworkReference" or "ProjectReference" or "Import")
            .Select(e => $"{e.Name.LocalName} {(string?)e.Attribute("Include") ?? (string?)e.Attribute("Project")}");
        Assert.Equal([@"ProjectReference ..\Match.Protocol\Match.Protocol.csproj"], references);
    }

    [Fact]
    public void CompiledMatchClientUsesOnlyAllowedAssemblies()
    {
        var assembly = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Match.Client.dll"));
        var disallowed = assembly.GetReferencedAssemblies().Select(a => a.Name ?? "").Where(n => !AllowedAssemblyReferences.Contains(n));
        Assert.Empty(disallowed);
    }

    [Fact]
    public void CompiledMatchClientNeverResolvesRollsDiceOrReplaysRecords()
    {
        var (types, members) = References(Path.Combine(AppContext.BaseDirectory, "Match.Client.dll"));
        Assert.Empty(types.Intersect(ForbiddenTypes));
        Assert.Empty(members.Intersect(ForbiddenMembers));
        // It does use the mirror, which is how a client applies the server's events.
        Assert.Contains("Match.Protocol.MatchMirror", types);
    }

    // The scan itself works: the server, which does resolve commands and roll dice, is caught by it.
    [Fact]
    public void TheScanFindsResolutionAndDiceInTheServer()
    {
        var (types, members) = References(Path.Combine(AppContext.BaseDirectory, "Match.Server.dll"));
        Assert.Contains("Rules.Core.Dice.IDiceSource", types);
        Assert.Contains("Rules.Core.TurnSequence.Decide", members);
    }

    // Client.Core (M7.3): what the Godot client shows, as plain C#. It reads state and rules queries; it never resolves,
    // rolls, feeds the copy of the match itself or loads code by reflection.
    [Fact]
    public void ClientCoreUsesThePlainSdkAndOnlyReferencesMatchClient()
    {
        var project = XDocument.Load(Path.Combine(Repository.Root, "src", "Client.Core", "Client.Core.csproj"));
        Assert.Equal("Microsoft.NET.Sdk", (string?)project.Root?.Attribute("Sdk"));
        var references = project.Descendants()
            .Where(e => e.Name.LocalName is "PackageReference" or "Reference" or "FrameworkReference" or "ProjectReference" or "Import")
            .Select(e => $"{e.Name.LocalName} {(string?)e.Attribute("Include") ?? (string?)e.Attribute("Project")}");
        Assert.Equal([@"ProjectReference ..\Match.Client\Match.Client.csproj"], references);
    }

    [Fact]
    public void CompiledClientCoreUsesOnlyAllowedAssemblies()
    {
        var references = ReferencedAssemblies(Path.Combine(AppContext.BaseDirectory, "Client.Core.dll"));
        Assert.DoesNotContain(references, n => !AllowedAssemblyReferences.Contains(n) && n != "Match.Client");
    }

    [Fact]
    public void CompiledClientCoreNeverResolvesRollsDiceOrFeedsTheCopy()
    {
        var (types, members) = References(Path.Combine(AppContext.BaseDirectory, "Client.Core.dll"));
        Assert.Empty(types.Intersect(ForbiddenTypes));
        Assert.Empty(types.Intersect(ForbiddenGodotTypes));
        Assert.Empty(members.Intersect(ForbiddenMembers));
        Assert.Empty(members.Intersect(ForbiddenGodotMembers));
        // Hints come from the read-only rules query, and connections from Match.Client.
        Assert.Contains("Rules.Core.TurnSequence.LegalCommands", members);
        Assert.Contains("Match.Client.Reconnector", types);
    }

    // The scan sees member references too: Match.Client applies events through the mirror, which a Godot script may not.
    [Fact]
    public void TheScanFindsMirrorUpdatesInMatchClient()
    {
        var (_, members) = References(Path.Combine(AppContext.BaseDirectory, "Match.Client.dll"));
        Assert.Contains("Match.Protocol.MatchMirror.Apply", members);
        Assert.Contains("Match.Protocol.MatchMirror.Reset", members);
    }

    // The Godot client is built here, outside the solution, with the Godot .NET SDK from NuGet (no Godot install is
    // needed), and its compiled scripts are scanned like Match.Client. If it cannot be built, the test fails.
    [Fact]
    public void CompiledGodotClientUsesOnlyAllowedAssemblies()
    {
        var assembly = GodotClient.Dll;
        var references = ReferencedAssemblies(assembly);
        Assert.Contains("GodotSharp", references);
        Assert.Contains("Match.Client", references);
        Assert.DoesNotContain(references, n => !AllowedGodotAssemblyReferences.Contains(n));
    }

    [Fact]
    public void CompiledGodotClientNeverResolvesRollsDiceFeedsTheCopyOrReachesTheServer()
    {
        var (types, members) = References(GodotClient.Dll);
        Assert.Empty(types.Intersect(ForbiddenTypes));
        Assert.Empty(types.Intersect(ForbiddenGodotTypes));
        Assert.DoesNotContain(types, t => t.StartsWith("Match.Server.", StringComparison.Ordinal) || t.StartsWith("Sim.", StringComparison.Ordinal));
        Assert.Empty(members.Intersect(ForbiddenMembers));
        Assert.Empty(members.Intersect(ForbiddenGodotMembers));
        // It does use the client libraries, so the scan is looking at the real scripts.
        Assert.Contains("Client.Core.MatchController", types);
    }

    [Fact]
    public void GodotClientSourcesNeverResolveRollDiceOrReachTheServer()
    {
        var folder = Path.Combine(Repository.Root, "client");
        var files = Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + ".godot" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(files);
        var offending = files
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: Path.GetRelativePath(Repository.Root, f), Line: i + 1, Text: line)))
            .Where(l => !l.Text.TrimStart().StartsWith("//", StringComparison.Ordinal) && ForbiddenSource.IsMatch(l.Text))
            .Select(l => $"{l.File}:{l.Line}: {l.Text.Trim()}");
        Assert.Empty(offending);
    }

    [Theory]
    [InlineData("var result = TurnSequence.Decide(state, command, dice);")]
    [InlineData("IDiceSource dice = new RandomDice();")]
    [InlineData("var state = Reducer.Apply(state, e);")]
    [InlineData("using Sim;")]
    [InlineData("var record = RecordVerifier.Verify(bytes, keys);")]
    [InlineData("using Match.Server;")]
    public void TheSourceScanCatchesForbiddenCode(string line) => Assert.Matches(ForbiddenSource, line);

    private static List<string> ReferencedAssemblies(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        return reader.AssemblyReferences.Select(h => reader.GetString(reader.GetAssemblyReference(h).Name)).ToList();
    }

    // Every type and member the assembly references from other assemblies, as "Namespace.Type" and "Namespace.Type.Member".
    private static (HashSet<string> Types, HashSet<string> Members) References(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var types = new HashSet<string>(StringComparer.Ordinal);
        foreach (var handle in reader.TypeReferences)
        {
            types.Add(Name(reader, handle));
        }
        var members = new HashSet<string>(StringComparer.Ordinal);
        foreach (var handle in reader.MemberReferences)
        {
            var member = reader.GetMemberReference(handle);
            var parent = member.Parent.Kind switch
            {
                HandleKind.TypeReference => Name(reader, (TypeReferenceHandle)member.Parent),
                HandleKind.TypeSpecification => "",
                _ => "",
            };
            if (parent.Length > 0)
            {
                members.Add($"{parent}.{reader.GetString(member.Name)}");
            }
        }
        return (types, members);
    }

    private static string Name(MetadataReader reader, TypeReferenceHandle handle)
    {
        var type = reader.GetTypeReference(handle);
        var name = reader.GetString(type.Name);
        if (type.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            return Name(reader, (TypeReferenceHandle)type.ResolutionScope) + "+" + name;
        }
        var ns = reader.GetString(type.Namespace);
        return ns.Length == 0 ? name : $"{ns}.{name}";
    }
}

// Builds client/Client.csproj once per test run, in this test project's configuration, into a temporary folder. The
// shared libraries it references are taken as the solution build left them (BuildProjectReferences=false), so this
// build never rewrites the solution's own outputs.
internal static class GodotClient
{
    private static readonly Lazy<string> Built = new(Build, LazyThreadSafetyMode.ExecutionAndPublication);

    public static string Dll => Built.Value;

    private static string Build()
    {
        var configuration = typeof(GodotClient).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Debug";
        var output = Directory.CreateTempSubdirectory("trench-godot-client-").FullName;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Directory.Delete(output, recursive: true);
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[]
        {
            "build", Path.Combine(Repository.Root, "client", "Client.csproj"), "-c", configuration, "-o", output,
            "-warnaserror", "-nodeReuse:false", "-p:UseSharedCompilation=false", "-p:BuildProjectReferences=false",
        })
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(5)))
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException("Building the Godot client took more than five minutes.");
        }
        var log = stdout.Result + stderr.Result;
        var dll = Path.Combine(output, "Client.dll");
        if (process.ExitCode != 0 || !File.Exists(dll))
        {
            throw new InvalidOperationException($"The Godot client did not build (exit {process.ExitCode}):\n{log[Math.Max(0, log.Length - 4000)..]}");
        }
        return dll;
    }
}

internal static class Repository
{
    public static string Root { get; } = Find();

    private static string Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TrenchSim.sln")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("TrenchSim.sln not found above the test output directory.");
    }
}
