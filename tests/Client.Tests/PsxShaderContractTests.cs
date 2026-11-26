using System.Text.RegularExpressions;

namespace Client.Tests;

// Animation audit item T5: the art pipeline's PSX figure shader (client/shaders/psx_figure.gdshader) carries uniforms the
// client sets and reads by name. A shader edit that drops or renames one would not fail anything in Godot (setting a
// missing uniform does nothing), so this checks the contract on every build: the uniforms are there with the defaults
// that leave a figure drawn as before (glow 1, fade 1: drawn whole), the casualty figure's ordered-dither dissolve reads
// fade, and every uniform the client scripts set or read is one the shader declares (docs/art-pipeline.md, section 6).
public class PsxShaderContractTests
{
    private static string Shader => File.ReadAllText(Path.Combine(Repository.Root, "client", "shaders", "psx_figure.gdshader"));

    private static IEnumerable<string> ClientScripts => Directory.GetFiles(Path.Combine(Repository.Root, "client", "scripts"), "*.cs")
        .Select(File.ReadAllText);

    [Theory]
    [InlineData("sampler2D", "albedo_tex", null)]
    [InlineData("vec4", "albedo_colour", "vec4(1.0)")]
    [InlineData("float", "glow", "1.0")]
    [InlineData("float", "fade", "1.0")]
    public void TheShaderDeclaresWhatTheClientUses(string type, string name, string? initial)
    {
        var declared = Regex.Match(Shader, $@"^uniform\s+{type}\s+{name}\b[^=;]*(=\s*([^;]+))?;", RegexOptions.Multiline);
        Assert.True(declared.Success, $"psx_figure.gdshader has no `uniform {type} {name}`");
        if (initial is not null)
        {
            Assert.Equal(initial, declared.Groups[2].Value.Trim());
        }
    }

    [Fact]
    public void FadeDissolvesInAnOrderedDitherAndLeavesAWholeFigureAlone()
    {
        // Only below 1 is anything left out, so a model's own figure (fade never set) is drawn whole.
        Assert.Matches(new Regex(@"if\s*\(\s*fade\s*<\s*1\.0\s*&&\s*bayer4\(FRAGCOORD\.xy\)\s*\+\s*0\.5\s*>=\s*fade\s*\)\s*\{\s*discard;"), Shader);
    }

    [Fact]
    public void EveryUniformTheClientSetsOrReadsIsDeclared()
    {
        var used = ClientScripts.SelectMany(s => Regex.Matches(s, @"(?:Set|Get)ShaderParameter\(""([a-z_]+)""").Select(m => m.Groups[1].Value)).ToHashSet();
        Assert.Contains("fade", used);
        var declared = Regex.Matches(Shader, @"^uniform\s+\w+\s+([a-z_]+)", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToHashSet();
        Assert.Empty(used.Except(declared));
    }
}
