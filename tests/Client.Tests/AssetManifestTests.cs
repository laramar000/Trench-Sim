using Client.Core;

namespace Client.Tests;

// Graphics stage G0: the art folder's manifest, licences, limits and catalog (client/assets).
public class AssetManifestTests
{
    private static string Assets => Path.Combine(Repository.Root, "client", "assets");

    [Fact]
    public void TheRepositoryArtFolderHasNoProblems()
    {
        Assert.Empty(AssetManifest.Problems(Assets));
        Assert.True(AssetManifest.TotalBytes(Assets) <= AssetManifest.MaxTotalBytes);
        // The repository's catalog reads, and every file it names exists.
        var catalog = VisualCatalog.Parse(File.ReadAllText(Path.Combine(Assets, VisualCatalog.FileName)));
        Assert.All(catalog.Entries.Values, path => Assert.True(File.Exists(Path.Combine(Assets, path)), path));
    }

    private static string Folder(string manifestRows, params (string Path, byte[] Bytes)[] files)
    {
        var folder = Path.Combine(Path.GetTempPath(), "trench-assets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, AssetManifest.FileName),
            "# Art\n\n## Assets\n\n| File | Source | Author | Licence | Date | Changes |\n|---|---|---|---|---|---|\n" + manifestRows);
        foreach (var (path, bytes) in files)
        {
            var full = Path.Combine(folder, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, bytes);
        }
        return folder;
    }

    private static readonly byte[] Small = [1, 2, 3];

    [Fact]
    public void AListedFileWithAnAcceptedLicenceIsFine()
    {
        var folder = Folder("| units/a.glb | https://example.org/a | Someone | CC0-1.0 | 2026-10-22 | Recoloured |\n| units/b.png | - | Trench Sim | Original | 2026-10-22 | Made for the project |\n",
            ("units/a.glb", Small), ("units/b.png", Small), ("units/a.glb.import", Small), ("units/.gitkeep", []));
        Assert.Empty(AssetManifest.Problems(folder));
        Assert.Equal(2, AssetManifest.Parse(File.ReadAllText(Path.Combine(folder, AssetManifest.FileName))).Count);
    }

    [Fact]
    public void UnlistedFilesMissingFilesAndBadLicencesAreProblems()
    {
        var folder = Folder("| units/gone.glb | https://example.org/g | Someone | CC0-1.0 | 2026-10-22 | None |\n| units/nc.glb | https://example.org/n | Someone | CC-BY-NC-4.0 | 2026-10-22 | None |\n| units/blank.glb | https://example.org/b |  | CC0-1.0 | 2026-10-22 | None |\n| units/nosource.glb | example.org | Someone | CC-BY-4.0 | 2026-10-22 | None |\n",
            ("units/unlisted.glb", Small), ("units/nc.glb", Small), ("units/blank.glb", Small), ("units/nosource.glb", Small));
        var problems = AssetManifest.Problems(folder);
        Assert.Contains(problems, p => p.Contains("units/unlisted.glb is not listed", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("lists units/gone.glb, which is not in the folder", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("units/nc.glb: licence \"CC-BY-NC-4.0\"", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("units/blank.glb: source, author", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("units/nosource.glb: the source must be", StringComparison.Ordinal));
    }

    [Fact]
    public void LfsPointersAndOversizedFilesAreProblems()
    {
        var pointer = System.Text.Encoding.ASCII.GetBytes("version https://git-lfs.github.com/spec/v1\noid sha256:00\nsize 1\n");
        var big = new byte[AssetManifest.MaxFileBytes + 1];
        var folder = Folder("| a.glb | https://example.org/a | A | CC0-1.0 | 2026-10-22 | None |\n| b.png | https://example.org/b | B | CC0-1.0 | 2026-10-22 | None |\n",
            ("a.glb", pointer), ("b.png", big));
        var problems = AssetManifest.Problems(folder);
        Assert.Contains(problems, p => p.Contains("a.glb is a Git LFS pointer", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("b.png is ", StringComparison.Ordinal) && p.Contains("limit for one file", StringComparison.Ordinal));
    }

    [Fact]
    public void ACatalogEntryMustNameAListedFileOfAKnownKind()
    {
        var folder = Folder("| units/a.glb | https://example.org/a | A | CC0-1.0 | 2026-10-22 | None |\n", ("units/a.glb", Small),
            (VisualCatalog.FileName, System.Text.Encoding.UTF8.GetBytes("{ \"entries\": { \"unit:default\": \"units/a.glb\", \"ground\": \"ground/mud.png\" } }")));
        Assert.Contains(AssetManifest.Problems(folder), p => p.Contains("ground draws ground/mud.png, which is not listed", StringComparison.Ordinal));

        Assert.Throws<FormatException>(() => VisualCatalog.Parse("{ \"entries\": { \"wall\": \"terrain/w.glb\" } }"));
        Assert.Throws<FormatException>(() => VisualCatalog.Parse("{ \"entries\": { \"unit:default\": \"../secret.glb\" } }"));
        Assert.Throws<FormatException>(() => VisualCatalog.Parse("{ \"entries\": { \"unit:default\": \"units/a.fbx\" } }"));
        Assert.Throws<FormatException>(() => VisualCatalog.Parse("{ \"entries\": { \"unit:\": \"units/a.glb\" } }"));
    }

    [Fact]
    public void TheCatalogChoosesTheFirstKeyWithAnEntryAndOtherwiseNothing()
    {
        var catalog = VisualCatalog.Parse("{ \"entries\": { \"unit:default\": \"units/soldier.glb\", \"unit:Bulwark\": \"units/heavy.glb\" } }");
        Assert.Equal("units/heavy.glb", catalog.PathFor(VisualCatalog.UnitKey("Bulwark"), VisualCatalog.DefaultUnitKey));
        Assert.Equal("units/soldier.glb", catalog.PathFor(VisualCatalog.UnitKey("Runner"), VisualCatalog.DefaultUnitKey));
        Assert.Null(catalog.PathFor(VisualCatalog.GroundKey));
        Assert.Null(VisualCatalog.Empty.PathFor(VisualCatalog.DefaultUnitKey));
    }
}
