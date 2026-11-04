using Roster;

namespace Roster.Tests;

public class KeywordVocabularyTests
{
    [Fact]
    public void TheShippedVocabularyLoadsWithoutProblems()
    {
        var loaded = KeywordVocabulary.Load(KeywordVocabulary.DefaultPath());
        Assert.Empty(loaded.Problems);
        Assert.Equal(56, loaded.Value!.All.Count);
        Assert.Equal(KeywordValue.Number, loaded.Value.Find("AUTOMATIC")!.Value);
        Assert.Equal(KeywordValue.Inches, loaded.Value.Find("BLAST")!.Value);
        Assert.Equal(KeywordValue.Text, loaded.Value.Find("NEGATE")!.Value);
        Assert.Equal(KeywordValue.None, loaded.Value.Find("CRITICAL")!.Value);
        Assert.All(loaded.Value.All, k => Assert.InRange(k.Page, 52, 57));
    }

    [Fact]
    public void FactionKeywordsUseOurFactionNames()
    {
        var vocabulary = KeywordVocabulary.Load(KeywordVocabulary.DefaultPath()).Value!;
        foreach (var ours in new[] { "PRINCIPALITY", "PILGRIM", "SULTANATE", "HERETIC", "CULT", "COURT" })
            Assert.Equal(KeywordKind.Tag, vocabulary.Find(ours)?.Kind);
        foreach (var theirs in new[] { "NEW ANTIOCH", "BLACK GRAIL", "THE COURT" })
            Assert.Null(vocabulary.Find(theirs));
    }

    [Fact]
    public void ADuplicateOrMalformedKeywordIsRefusedWithReasons()
    {
        var loaded = KeywordVocabulary.Parse("""
            { "format": 1, "rulesVersion": "1.0.2", "keywords": [
              { "id": "HEAVY", "kind": "effect", "value": "none", "page": 55 },
              { "id": "HEAVY", "kind": "effect", "value": "none", "page": 55 },
              { "id": "Lower", "kind": "effect", "value": "none", "page": 55 },
              { "id": "ODD", "kind": "spell", "value": "none", "page": 55 },
              { "id": "NOPAGE", "kind": "tag", "value": "number", "page": 0 }
            ] }
            """);
        Assert.False(loaded.Ok);
        Assert.Contains(loaded.Problems, p => p.Contains("\"HEAVY\" is listed twice"));
        Assert.Contains(loaded.Problems, p => p.Contains("\"Lower\" must be written in capitals"));
        Assert.Contains(loaded.Problems, p => p.Contains("\"kind\" is \"spell\""));
        Assert.Contains(loaded.Problems, p => p.Contains("page must be 1 or more"));
    }

    [Theory]
    [InlineData("""{ "format": 2, "rulesVersion": "1.0.2", "keywords": [] }""", "this version reads format 1 only")]
    [InlineData("""{ "format": 1, "rulesVersion": "1.6", "keywords": [] }""", "supports 1.0.2 only")]
    [InlineData("""{ "format": 1, "rulesVersion": "1.0.2" }""", "has no \"keywords\" list")]
    [InlineData("""{ "format": 1, """, "not valid JSON")]
    public void AWrongHeaderIsRefused(string json, string expected)
    {
        var loaded = KeywordVocabulary.Parse(json);
        Assert.False(loaded.Ok);
        Assert.Contains(loaded.Problems, p => p.Contains(expected));
    }
}
