using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Server.Tests;

// M7 (D6): the Godot client in client/ stays outside TrenchSim.sln, so the solution builds and tests without Godot, and
// it may reference only the shared client-side libraries. It never references the server, the simulator (which rolls
// dice) or the terminal client. The deeper authority guard over compiled client code arrives with Client.Core (M7.3).
public class ClientProjectTest
{
    private static readonly HashSet<string> AllowedProjectReferences =
    [
        @"..\src\Match.Protocol\Match.Protocol.csproj",
    ];

    [Fact]
    public void TheGodotClientIsOutsideTheSolution()
    {
        var root = RepositoryRoot();
        Assert.True(File.Exists(Path.Combine(root, "client", "Client.csproj")));
        var solution = File.ReadAllText(Path.Combine(root, "TrenchSim.sln"));
        Assert.DoesNotContain("Client.csproj", solution, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(new Regex(@"""client\\", RegexOptions.IgnoreCase), solution);
    }

    [Fact]
    public void TheGodotClientReferencesOnlySharedClientLibraries()
    {
        var project = XDocument.Load(Path.Combine(RepositoryRoot(), "client", "Client.csproj"));
        var sdk = (string?)project.Root?.Attribute("Sdk") ?? "";
        Assert.Equal($"Godot.NET.Sdk/{GodotVersion()}", sdk);
        Assert.Equal("net10.0", project.Descendants().Single(e => e.Name.LocalName == "TargetFramework").Value);

        var references = project.Descendants()
            .Where(e => e.Name.LocalName is "PackageReference" or "Reference" or "FrameworkReference" or "ProjectReference" or "Import")
            .Select(e => (Kind: e.Name.LocalName, Include: (string?)e.Attribute("Include") ?? (string?)e.Attribute("Project") ?? ""))
            .ToList();
        Assert.All(references, r => Assert.Equal("ProjectReference", r.Kind));
        Assert.All(references, r => Assert.Contains(r.Include, AllowedProjectReferences));
    }

    // The Godot build recorded in GODOT_VERSION is the one the SDK pins, so the two cannot drift apart.
    private static string GodotVersion()
    {
        var recorded = File.ReadAllText(Path.Combine(RepositoryRoot(), "client", "GODOT_VERSION")).Trim();
        var match = Regex.Match(recorded, @"^(\d+\.\d+\.\d+)\.stable\.mono\.");
        Assert.True(match.Success, $"GODOT_VERSION should name a stable .NET (mono) build, not '{recorded}'.");
        return match.Groups[1].Value;
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
}
