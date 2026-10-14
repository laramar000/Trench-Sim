using Rules.Core;
using Rules.Core.State;
using Sim;

namespace Rules.Tests.Match;

// Whole matches between two simulated players, with every rule enforced by Rules.Core. The simulator's own checks
// (no dice on a rejection, an invariant after every event, replay of the log) run inside Runner; these tests
// require that every seeded match really ends.
public class SimulationTests
{
    private static readonly IReadOnlyList<Warband> Warbands = Content.Load(Content.DefaultPath());

    // Regression pins of this implementation's own behaviour under StateHash version 3, not an independent oracle:
    // a rule change that alters any of these matches must re-pin them on purpose.
    private static readonly Dictionary<int, string> PinnedFinalHashes = new()
    {
        [1] = "6f6306c975ed27c733c1ef7e55bc8b0fd07c71cfe859f63bf7b14df4cab9da73",
        [2] = "807be46212c9a1920f7efa6d7fc22cb1c5a7c1f95cb2b75bcd10481c69c8de73",
        [3] = "5c91b33835d1ed66f54a06da051aafc4a1e192fb3b462764012f9c88a4bdde20",
        [4] = "cfd64bc73e7edb9dcf24666733a4e5110e530f8d7a4501fb9c59ed181af126c4",
        [5] = "0cd0654b38f5f8bbdb3a690ac80f5c7bb2d71f95606a3595a7270a4953c5dbee",
        [6] = "9f97edb88bce828ff9cbf501c35671c37ea2c66110c7d317f183d6247832fe50",
        [7] = "4893431b3d226eb988d993055820aa382ed0f9fd7b19e8c772640a3249ae3170",
        [8] = "657029844a42271441b9f8886359e93a951114273a95349b2738c03736aee380",
        [9] = "2d9d9b493927098e976bbdd4565a3506cca5aa7c3b1ceddbec6cdf230646244b",
        [10] = "20541de3bb7106f4a64e0120e578cde109783134ac2fafd3d26d15c46e89a393",
        [11] = "b26b3586bbad09f39218fbc02a097c50f93739ad7829675f9a02950ccd7ff122",
        [12] = "bf1f89d1947cc749c003c19a24344f705d239d4b9975abfe10107a1f3f5ba882",
        [13] = "f23cf02bc531fec1253dfe0c7f1b9dfbe9bc116362aaa06fdc371d106ac2f1e3",
        [14] = "258a9e81f1a4b64d2bb4bf6014f6ae8e519e4a7b6a75c7e065bcea0cd8ca9d89",
        [15] = "b2506bbe54a7d287b2a18e2bf9c00e5c773686d47407fc1e0555bcef8ab37790",
        [16] = "8d87f68b883d64b80c83b8a7054c440eb78a3faca32ba2305799e61066009f64",
        [17] = "09ef8ce365279cc6946e84c488d13b30720703e98a5c9c779bdcefecbcf67141",
        [18] = "e180cc67a78a412308e1883ce2508d7af8bba4dbe3f8421aaa40fa689d2f52be",
        [19] = "eaad0e347cf463d2e683646bddab95388a8057499e10277582c41347e9517272",
        [20] = "232e8ebcc28308aa64ef50be6b202b881041cc47807a602ae3f9adead0309826"
    };

    public static TheoryData<int> Seeds() => [.. Enumerable.Range(1, 20)];

    [Theory]
    [MemberData(nameof(Seeds))]
    [Trait("Rule", "Gate 1: a full simulated match reaches a terminal state")]
    public void EverySeededMatchReachesATerminalState(int seed)
    {
        var result = Runner.Run(seed, Warbands);

        Assert.True(result.Terminal, result.Describe());
        Assert.Equal("terminal", result.Status);
        Assert.NotNull(result.Outcome);
        Assert.True(result.Commands < Runner.DefaultMaxCommands);
        Assert.InRange(result.Turns, 1, ClaimNoMansLandTurns);
        Assert.Equal(PinnedFinalHashes[seed], result.FinalHash);
    }

    private const int ClaimNoMansLandTurns = 4;

    [Fact]
    [Trait("Rule", "Gate 1: a stalled or unfinished simulation is a failure")]
    public void AMatchThatHitsTheStepLimitIsAFailureNotAResult()
    {
        var result = Runner.Run(1, Warbands, maxCommands: 25);

        Assert.False(result.Terminal);
        Assert.StartsWith("step-limit", result.Status);
        Assert.Null(result.Outcome);
    }

    [Fact]
    [Trait("Rule", "Architecture: deterministic replay")]
    public void RunningASeedTwiceGivesTheSameMatchEventForEvent()
    {
        foreach (var seed in new[] { 1, 6, 13, 20 })
        {
            var first = Runner.Run(seed, Warbands);
            var second = Runner.Run(seed, Warbands);
            Assert.Equal(first.FinalHash, second.FinalHash);
            Assert.Equal(first.Log.Count, second.Log.Count);
            Assert.Equal(
                first.Log.Select(e => e.GetType().Name),
                second.Log.Select(e => e.GetType().Name));
        }
    }

    [Fact]
    [Trait("Rule", "Architecture: replaying any prefix of the log gives the state at that moment")]
    public void EveryPrefixOfALogReplaysToTheHashSeenWhileItWasPlayed()
    {
        var result = Runner.Run(7, Warbands);
        var state = MatchState.None;
        var seen = new List<string>();
        foreach (var e in result.Log)
        {
            state = Reducer.Apply(state, e);
            seen.Add(StateHash.Compute(state));
        }
        Assert.Equal(result.FinalHash, seen[^1]);
        for (var length = 1; length <= result.Log.Count; length += 7)
        {
            Assert.Equal(seen[length - 1], StateHash.Compute(Reducer.Replay(result.Log.Take(length))));
        }
    }

    [Fact]
    [Trait("Rule", "Architecture: the rules engine knows no warband content")]
    public void RenamingEveryWarbandAndModelChangesNothing()
    {
        var json = File.ReadAllText(Content.DefaultPath());
        var renamed = Content.Parse(
            json.Replace("Ash Company", "Group Nine").Replace("Lantern Guard", "Group Ten")
                .Replace("Sharpshooter", "Type A").Replace("Skirmisher", "Type B").Replace("Bulwark", "Type C").Replace("Banner Carrier", "Type D")
                .Replace("Marksman", "Type E").Replace("Runner", "Type F").Replace("Shieldbearer", "Type G").Replace("Lamp Keeper", "Type H"));
        Assert.Equal("Group Nine", renamed[0].Name);

        foreach (var seed in new[] { 2, 9, 17 })
        {
            var original = Runner.Run(seed, Warbands);
            var other = Runner.Run(seed, renamed);
            Assert.Equal(original.FinalHash, other.FinalHash);
            Assert.NotEqual(original.WarbandOne, other.WarbandOne);
        }
    }

    [Fact]
    [Trait("Rule", "Architecture: the rules engine knows no warband content")]
    public void RulesCoreSourceNamesNoWarbandOrModel()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Rules.Core"));
        Assert.True(Directory.Exists(root), root);
        var names = Warbands.SelectMany(w => new[] { w.Name }.Concat(w.Models.Select(m => m.Name))).ToList();
        Assert.NotEmpty(names);
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            var text = File.ReadAllText(file);
            foreach (var name in names)
            {
                Assert.DoesNotContain(name, text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    [Trait("Rule", "Gate 1: coverage of match endings")]
    public void ManySeedsEndInEveryKindOfWayTheScenarioAllows()
    {
        var results = Enumerable.Range(1, 150).Select(s => Runner.Run(s, Warbands)).ToList();

        Assert.All(results, r => Assert.True(r.Terminal, r.Describe()));
        var reasons = results.Select(r => r.Outcome!.Reason).Distinct().ToList();
        Assert.Contains(OutcomeReason.VictoryPoints, reasons);
        Assert.Contains(OutcomeReason.OpponentFled, reasons);
        Assert.Contains(results, r => r.Outcome!.Winner is null);
        Assert.Contains(results, r => r.Rejections > 0);
        Assert.Contains(results, r => r.Hits > 0 && r.OutOfAction > 0);
    }
}
