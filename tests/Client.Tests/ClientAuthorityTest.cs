using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Client.Tests;

// M7: the client holds no authority. The server resolves every command, rolls every die and decides every outcome; a
// client only applies the server's events (through MatchMirror, which checks each hash) and sends commands. These checks
// replace the plan's "Rules.Core.Resolution" check (that namespace does not exist): they look at the project file, the
// compiled Match.Client assembly and the Godot client's C# sources.
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

    // The same rules, as text, for the Godot client's sources (which are not built in this solution).
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
