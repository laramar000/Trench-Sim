using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;

namespace Rules.Tests;

// M0 acceptance: Rules.Core references no Godot package. Rules.Core must also stay free
// of networking, UI and other frameworks, and of other projects, so the rules can run
// without any of them. Three checks, because each catches what the others miss:
// the project file, what restore actually resolved, and what the compiled assembly uses.
public class RulesCoreDependencyTest
{
    // Base-library assemblies the rules engine may use. Anything else (System.Net.*,
    // ASP.NET, Godot, UI) fails the test; add to this list only on purpose.
    private static readonly HashSet<string> AllowedAssemblyReferences =
    [
        "System.Runtime",
        "System.Collections",
        "System.Linq",
        "System.Memory", // span helpers, e.g. comparing dice arrays in RollRecord
        "System.Security.Cryptography",
    ];

    [Fact]
    public void RulesCoreProjectUsesThePlainSdkAndDeclaresNoReferences()
    {
        var project = XDocument.Load(Path.Combine(RulesCoreDirectory(), "Rules.Core.csproj"));

        // A different project SDK (Microsoft.NET.Sdk.Web, Godot.NET.Sdk) brings its own frameworks.
        Assert.Equal("Microsoft.NET.Sdk", (string?)project.Root?.Attribute("Sdk"));
        Assert.DoesNotContain(project.Descendants(), e => e.Name.LocalName == "Sdk");

        // No packages, assemblies, frameworks, projects or imported files at all.
        var references = project.Descendants()
            .Where(e => e.Name.LocalName is "PackageReference" or "Reference" or "FrameworkReference"
                or "ProjectReference" or "Import")
            .Select(e => $"{e.Name.LocalName} {(string?)e.Attribute("Include") ?? (string?)e.Attribute("Project")}");
        Assert.Empty(references);
    }

    [Fact]
    public void NoMsBuildFilesAreImportedIntoRulesCore()
    {
        // Directory.Build.props/.targets and Directory.Packages.props in any folder above the
        // project are imported automatically and could add references the project file doesn't show.
        var root = RepositoryRoot();
        var found = new List<string>();
        for (var dir = new DirectoryInfo(RulesCoreDirectory()); dir is not null; dir = dir.Parent)
        {
            found.AddRange(dir.GetFiles("Directory.*.props").Select(f => f.FullName));
            found.AddRange(dir.GetFiles("Directory.*.targets").Select(f => f.FullName));
            if (dir.FullName == root)
            {
                break;
            }
        }
        Assert.Empty(found);
    }

    [Fact]
    public void RestoreResolvedOnlyTheBaseFrameworkAndNoPackages()
    {
        var assetsPath = Path.Combine(RulesCoreDirectory(), "obj", "project.assets.json");
        Assert.True(File.Exists(assetsPath), $"{assetsPath} not found; build the solution first.");
        using var assets = JsonDocument.Parse(File.ReadAllText(assetsPath));

        var packages = assets.RootElement.GetProperty("libraries").EnumerateObject().Select(p => p.Name);
        Assert.Empty(packages);

        var frameworks = assets.RootElement.GetProperty("project").GetProperty("frameworks")
            .EnumerateObject()
            .SelectMany(tfm => tfm.Value.TryGetProperty("frameworkReferences", out var refs)
                ? refs.EnumerateObject().Select(r => r.Name)
                : [])
            .Distinct();
        Assert.Equal(["Microsoft.NETCore.App"], frameworks);
    }

    [Fact]
    public void CompiledRulesCoreUsesOnlyAllowedAssemblies()
    {
        var assembly = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Rules.Core.dll"));

        var disallowed = assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? "")
            .Where(name => !AllowedAssemblyReferences.Contains(name));

        Assert.Empty(disallowed);
    }

    private static string RulesCoreDirectory() => Path.Combine(RepositoryRoot(), "src", "Rules.Core");

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
