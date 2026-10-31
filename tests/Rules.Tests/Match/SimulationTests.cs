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

    // Regression pins of this implementation's own behaviour under StateHash version 5, not an independent oracle.
    // Re-pinned for version 5 (Glorious Deeds, U96, 2026-10-26): every match is event-for-event the same as under
    // version 4 apart from the GloriousDeedCompleted events added, and only the Victory Points those deeds give changed.
    private static readonly Dictionary<int, string> PinnedFinalHashes = new()
    {
        [1] = "18c8d034078d404416464a7513b00563e6a8b06469e9db83871bd7c4046c2e42",
        [2] = "3eb4529e82aa7de59ce4657796c81e6006ac09d1080d11c8d60a1bf673a04423",
        [3] = "74382aaa9ee4b45bb073ac7b99a3aecc68fef85f9d43951fca38978e0c4935c8",
        [4] = "565d7bf52e9ff7984e131b1cb51a7de3a64077eab7158232930cb4f901559fb9",
        [5] = "0582a3c150283de612981273a5cb7740cb86108ced227adf6575726897156e85",
        [6] = "283aa5ac4d243acec31473de85f6c8257315bd8df050663967aa7c3ffb778df3",
        [7] = "b0eab94dddb24079612326690226087e164ebc7dcd72286abee30e7cc3be365c",
        [8] = "1b6a7e4f051ede38bbe22a3b4cf0ba57ce337f6223b74b95d6aee90dcb62c9cb",
        [9] = "71c3ac8d58fefae664b856e6f7fdb78101790227ffa19707e89fe4b83c3a2973",
        [10] = "99d069fd3273ba507cfc965c3b53298e20b2c0890bb9a7d4c51595b2532c14b6",
        [11] = "6e9978880b31b2a4ccb4e9a784252045ffe2810bcf070cfc65a19cb34ff29fd5",
        [12] = "af912a0c6bcb9417ad2cc6ebc6459bb35478e5fe825ac79a1af4c89d2f46ac51",
        [13] = "203df5e9e9a735ecbb53f06e266bbfc1cb1507e0aa4edc17d0b2741493de27ab",
        [14] = "8db78723fe081619e9ca747025cc7cf9b79cdd3f4b071075ccd28a2fc15ce172",
        [15] = "55c60ec87b98c7d5f63cc8d9826055cc6ef6eccca663fe38a36529e3e09a97a3",
        [16] = "ff7e246f935979a93a38111869fe23366e2c34fe1def3c18c858f799339de91d",
        [17] = "3d274d55bec2735a38d9160cae8b51642387c632f80905933d242d85c485c048",
        [18] = "82a3d36fe60746d1b8c4422379d110e2a7ab987d6b6609904bd26bfff81b0c5a",
        [19] = "ab6b983ff6f1c0cb3fdc4e5cc4db51a995b4d1f85a2c941c6d4aac05b32ef57f",
        [20] = "e42e456f12ae40b84b84265e5eb8ca80ade1ae2d64e4ec1f2700f83717aee855"
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
