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

    // Regression pins of this implementation's own behaviour under StateHash version 4, not an independent oracle:
    // a rule change that alters any of these matches must re-pin them on purpose.
    // Re-pinned for version 4 (reducer hardening adds the roll-off result and the forced follow-ups to the canonical
    // text): every match is event-for-event the same as under version 3, and its final state with the new lines
    // removed hashes to the old version-3 pin.
    private static readonly Dictionary<int, string> PinnedFinalHashes = new()
    {
        [1] = "280d57a7d67f5fe24d05b6b74bed908511772480fab8bd7904682d78101a8a2e",
        [2] = "e6ffcfc9464cfa4ce3de8635f9b431de03d922c6b798b80837b59bc50e0f2f84",
        [3] = "9269c6a9616d310fb26f47faee25ce30e0ed6cb15631398dbd7a1b1e74776744",
        [4] = "abe48f25ed0247bd8d2daaf8ad2f93c2e453e894816de9c02fae0a3e2a14ebcb",
        [5] = "4355dcfc03968d1468657045876f9e0454255fbd039947c7d6a078edf5962414",
        [6] = "72de46df8a24cef9bdc275e5f0d95c61c5de060a1cd02298d09b38ce76cc0988",
        [7] = "a0e3550a2ce427ed0ac64f5aa0684ea47c04a5f7639962544cd0dc27f2bb5f36",
        [8] = "40a4407a7d36aae36bc10dc8d63be7cec3bc1adb75726360665c84880549f7f8",
        [9] = "f598b0c1d20442108272c3dc94c7590d48ba27fc314edb0d45aed5ffbf0515fd",
        [10] = "ab25439c50b362e970458566b7f89ef19ae22b8a33adbae84f17e4730dc58d5b",
        [11] = "c393c98ba4b0f51b50e99e66a5d0d9b930511ef8f5e0a22c4c08c757e42fd8ea",
        [12] = "c090bfb1852a7adadcdfe35379ab43b7dc95222e567327c0a59343cd629ceb48",
        [13] = "72bf3f3e7ea568e90f2789a65f6c9f65f532ce35e47ed1d1987f5693a0a322fd",
        [14] = "237aa37daaec748de76a736c08f486956cafdee24f156a7b2a32279bff998955",
        [15] = "73c456ba993a3999a52254537d37ddb70764699e8cb0032a24064e313373e7bd",
        [16] = "9ff750e903af85e7f6986c8ad6176faf5cb85de194f9825fd15d685fe59f84b9",
        [17] = "639eb6bbd20469dc930fc58497e6f7ed76cfbe82004fe3026bf436b00be1af43",
        [18] = "848e390e67874e1f5368e1160b7d5b42e1291afc23a7a37d0b495352fc6cb3b8",
        [19] = "bb9e26d2ba9363a3adacb3811a4142cf1e0cfbbb09d4f22ae06c6918b8ae474b",
        [20] = "cd84c7326529b5fe956516252cac6418720890c69718effa867516bd83a7f5c7"
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
