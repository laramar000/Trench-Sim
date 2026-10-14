using System.Xml.Linq;

namespace Rules.Tests;

// M0 acceptance: Rules.Core references no Godot package. Rules.Core must also stay free
// of networking frameworks and of other projects, so the rules can run without either.
public class RulesCoreDependencyTest
{
    [Fact]
    public void RulesCoreHasNoGodotNetworkingOrProjectReferences()
    {
        var project = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "Rules.Core", "Rules.Core.csproj"));
        var references = project.Descendants()
            .Where(e => e.Name.LocalName is "PackageReference" or "FrameworkReference" or "ProjectReference")
            .Select(e => $"{e.Name.LocalName} {(string?)e.Attribute("Include")}")
            .ToList();

        Assert.DoesNotContain(references, r => r.Contains("Godot", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, r => r.StartsWith("FrameworkReference"));
        Assert.DoesNotContain(references, r => r.StartsWith("ProjectReference"));
    }

    private static string FindRepositoryRoot()
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
