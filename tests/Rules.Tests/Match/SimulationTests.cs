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

    // Regression pins of this implementation's own behaviour under StateHash version 2, not an independent oracle:
    // a rule change that alters any of these matches must re-pin them on purpose.
    private static readonly Dictionary<int, string> PinnedFinalHashes = new()
    {
        [1] = "73058a39a4347776911aa60d0ead96606ce88d01d2f8ab8ca5c56c4e8a8b8a98",
        [2] = "b27d322e80d9ba56a4c0e0f433b0cebb2f39c081d6356a983ee693a7ce1f2384",
        [3] = "2dbf25fb83fcd1e3bb3f6f1af2f34e7e3bab75bbf9005ea450b6a11293462516",
        [4] = "21d79dd3680f01ae5248074120c8b1993965ccaa550edb60ab55ba6eb9d4a5fe",
        [5] = "13d6a21cea41959fc3fbf299d6c2ac4b25abb0b9fee45b47c3a48b3ef8ff0114",
        [6] = "8a32e4f6ccae8f024fa2a032213c058dd4262588bb6e330d97550208fa3d95a6",
        [7] = "a76898d01b98b1e239278fb9d72cd7a07522abdc03462c541531ed0126801bb2",
        [8] = "68825954023a7f0e41cdca412fccce156b1d66f08d180d227bd62b50c7c8b236",
        [9] = "a85724395e45e0c24e4dc6560a8545cb60950e0de270f11bb1efd19d66c623a1",
        [10] = "6b468376ec881f2408320c7bdc3de728ff5564e176cf013540a0a54e1db5accf",
        [11] = "7780559c9edd69b003b099f18145a7fb9bd0fd523afd706fb765e97d4ac24c6a",
        [12] = "0bac6bfaa120df00cdf12506bc7f0bf5ccc2bab9bfd8161e3da173dadc5bd81d",
        [13] = "efd9118c09ed80a9b4ec50bdaa0f945d3cfd246bce9bb037adb0ece56a5ef92d",
        [14] = "a535064a0dcd085660572d7d0e9d8446786884168242de9d30025d681d6cd8c5",
        [15] = "f7e73556bdcc014612ff9e4458f385ba8041a77be7049ebf6e77488acda6b717",
        [16] = "15dec218d44cf054d91d5f4d0d259cb0bd67ab7bf953c9b8ebb4c09320796593",
        [17] = "51dd716b1b224a781ef17e9153f67611085be416937107bf1389ad1669598462",
        [18] = "762475e5c4298df0a17bc0ef453c263f23e3dfe79a8037d9815a02fd06891ee9",
        [19] = "5f50de20e5922a18b6d42935763e6e00547abe3e11c5730ac56a7331cf5563bd",
        [20] = "959c1340fb35ee0fe691433ede194438ad131be0ff75d3c9938a42124ec95dff"
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
