using System.Reflection;
using System.Xml.Linq;

namespace Server.Tests;

// Match.Protocol is shared by the server and every client (terminal now, Godot in M7), so it stays as plain as
// Rules.Core: the plain SDK, no packages, one project reference (Rules.Core), and only base-library assemblies.
public class ProtocolDependencyTest
{
    private static readonly HashSet<string> AllowedAssemblyReferences =
    [
        "System.Runtime",
        "System.Collections",
        "System.Collections.Concurrent",
        "System.Collections.Immutable",
        "System.Linq",
        "System.Memory",
        "System.Text.Json",
        // M6: ECDSA signatures and SHA-256 for match records.
        "System.Security.Cryptography",
        "Rules.Core",
    ];

    [Fact]
    public void ProtocolProjectUsesThePlainSdkAndOnlyReferencesRulesCore()
    {
        var project = XDocument.Load(Path.Combine(RepositoryRoot(), "src", "Match.Protocol", "Match.Protocol.csproj"));
        Assert.Equal("Microsoft.NET.Sdk", (string?)project.Root?.Attribute("Sdk"));
        var references = project.Descendants()
            .Where(e => e.Name.LocalName is "PackageReference" or "Reference" or "FrameworkReference" or "ProjectReference" or "Import")
            .Select(e => $"{e.Name.LocalName} {(string?)e.Attribute("Include") ?? (string?)e.Attribute("Project")}");
        Assert.Equal([@"ProjectReference ..\Rules.Core\Rules.Core.csproj"], references);
    }

    [Fact]
    public void CompiledProtocolUsesOnlyAllowedAssemblies()
    {
        var assembly = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Match.Protocol.dll"));
        var disallowed = assembly.GetReferencedAssemblies().Select(a => a.Name ?? "").Where(n => !AllowedAssemblyReferences.Contains(n));
        Assert.Empty(disallowed);
    }

    private static string RepositoryRoot()
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

    // D6: records are for checking only. Nothing public in Match.Server takes a record or a record body, so there is no
    // way to load one into a live match.
    [Fact]
    public void TheServerHasNoPublicWayToLoadARecordIntoAMatch()
    {
        var recordTypes = new[] { typeof(Match.Protocol.MatchRecordBody), typeof(Match.Protocol.RecordedEvent), typeof(Match.Protocol.RecordVerdict) };
        var offenders = typeof(Match.Server.Lobby).Assembly.GetExportedTypes()
            // The store and the writer take a record only to write it to disk.
            .Where(t => t != typeof(Match.Server.RecordStore) && !typeof(Match.Server.IMatchRecorder).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Cast<MethodBase>()
                .Concat(t.GetConstructors())
                .Where(m => m.GetParameters().Any(p => recordTypes.Contains(p.ParameterType)))
                .Select(m => $"{t.Name}.{m.Name}"))
            .ToList();
        Assert.Empty(offenders);
    }
}
