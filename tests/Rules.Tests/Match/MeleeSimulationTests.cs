using Rules.Core;
using Rules.Core.Events;
using Rules.Core.State;
using Sim;

namespace Rules.Tests.Match;

// Whole matches between two simulated players whose warbands have Melee Weapons, so that the bots charge, fight,
// retreat and shoot into melee. Runner enforces the same checks as for Slice A (no dice on a rejection, an invariant
// after every command, replay of the log); these tests require every match to end and every action to occur.
public class MeleeSimulationTests
{
    private static readonly IReadOnlyList<Warband> Warbands = Content.Load(Content.MeleePath());

    // Regression pins of this implementation's own behaviour under StateHash version 4, not an independent oracle.
    // Re-pinned for version 4 (reducer hardening adds the roll-off result and the forced follow-ups to the canonical
    // text): every match is event-for-event the same as under version 3, and its final state with the new lines
    // removed hashes to the old version-3 pin.
    private static readonly Dictionary<int, string> PinnedFinalHashes = new()
    {
        [1] = "fc0730b4083a0f3838e2c6ca4f3000a173fed2e2454732593f93ae30b826b6e4",
        [2] = "828b28cc4364da05073a10e96ec1ac7092b42cb5be9a0f72173dc091ef4877fa",
        [3] = "4dc46db9e1d3fb4d9e89306eb4c5a0bfb1e671f6029f0884ddcd34cacf31b025",
        [4] = "269aadf8b82756d1a033d08aa39bacdd4b2c65514d1684406f7f8bb56f4494ef",
        [5] = "5fe9843bc28fc7b25ac9b7c6151f5e184661b3a122cd9048d24913d17db317a0",
        [6] = "5315629f2c140e3b66c00f659d643fa42deba0dbe881c2e69f1fd9b0cb1b6703",
        [7] = "666907a9206beaebc97d987a3f119079bd4a694208ec640f668c98bdb07b2b80",
        [8] = "e3a9856ebe5a510c4769445e6fa540884b528fbd722b7fb8e7c0b621b72ab58f",
        [9] = "d01c7fba51ce42834c66529289dd8c0ae83e14581f364c8632d8695a5d5d8dd7",
        [10] = "c6a463af7de37465e57cf1339d61e064f98c4bdf17a3015fbf608057ba6d87cc",
        [11] = "8852499b8c88adbb329117f2dced52b59fc6e34af99d863477b4170eb11dd65f",
        [12] = "8ba9d29839ee10508ec62731cdd69465951b97c628e755163ced2c5ec9a1f91b",
        [13] = "146c2cf9cec236ef7c3b2d94bf83409f9f1fca36a8e134b00198cea70c32f670",
        [14] = "1c41ff0720571fd7526425b97481e4482978a713fcd4a19a50210be64fecbd50",
        [15] = "526ff1b2a4702395c3fb8e0bae6d9e81068fb813d00912d34de55760e4090fe2",
        [16] = "970a42071e15ba655c30f78d51907c7f69ffad85e4852209a51370fd2f0d35af",
        [17] = "5c6b78f918f3dcf026e0defd8776ee26bea4d1f789393cc85f0c24b714b7a7f0",
        [18] = "1b618273bf50b1b78668bc2ed47b9b3ce3fe59b4587419ca4b919adf963e6647",
        [19] = "2ecbc70bc8f300b603c5d0717c6fe392987e6a54ccd003bca5d0c225ea9b6598",
        [20] = "24f8b5c1dcb6a2f2b5343e616bff0f7fd74e642a0de2fd6305299f0aa393b548",
        [21] = "5a2a430e45f75b6743f572b24890dd5fcd7ad7533794f91a7dd601faa5b1847a",
        [22] = "ebd7136aac857baa19bd3196ec9810980e079ef719a28c9650e3b9470330860b",
        [23] = "9ecf2feda677dd9090f1094a36f8d4b7eb6c79215029bcb9d32f5b38dd4c1a64",
        [24] = "c6b44f9a553762183ea37441e03d10fadc728af5bd80f3756847422fe0d64db8",
        [25] = "70e9be43f25001576596e8983e7b2ed7b54f3e0063aec232b435bfb8209854f8",
        [26] = "ab2dc5abb4251f6bd1707bda8ef38584d9cf87cdcdaae7d55a6ee00fd3614c21",
        [27] = "ba47f3069f56d045feed90db6cfab10cb41b02c2b99e215fc88795522a8c9859",
        [28] = "484d7da95286392912a4add0bb22faa0dfc3b3242494dcdd73d3acf30560bcd8",
        [29] = "fc8b5f8cc1581b43351dbb0b6d0d86d989951d310ea987c0408811f5888527a1",
        [30] = "df991c8609cd4d7ee91cca9b9941c23b4c7e31bbc8cdd0c86b011f1f90c08215"
    };

    public static TheoryData<int> Seeds() => [.. Enumerable.Range(1, 30)];

    [Theory]
    [MemberData(nameof(Seeds))]
    [Trait("Rule", "Gate 1: a full simulated melee match reaches a terminal state")]
    public void EverySeededMeleeMatchReachesATerminalState(int seed)
    {
        var result = Runner.Run(seed, Warbands);

        Assert.True(result.Terminal, result.Describe());
        Assert.NotNull(result.Outcome);
        Assert.True(result.Commands < Runner.DefaultMaxCommands);
        Assert.Equal(PinnedFinalHashes[seed], result.FinalHash);
    }

    [Fact]
    [Trait("Rule", "Gate 1: the simulated players really use Charge, Retreat, Fight and shooting into melee")]
    public void ManySeedsExerciseEveryNewAction()
    {
        var results = Enumerable.Range(1, 150).Select(s => Runner.Run(s, Warbands)).ToList();

        Assert.All(results, r => Assert.True(r.Terminal, r.Describe()));
        Assert.Contains(results, r => r.Melee.Charges > 0);
        Assert.Contains(results, r => r.Melee.ChargesReached > 0);
        Assert.Contains(results, r => r.Melee.Charges > r.Melee.ChargesReached);   // a failed charge
        Assert.Contains(results, r => r.Melee.Fights > 0);
        Assert.Contains(results, r => r.Melee.FightHits > 0);
        Assert.Contains(results, r => r.Melee.Retreats > 0);
        Assert.Contains(results, r => r.Melee.FreeFights > 0);
        Assert.Contains(results, r => r.Melee.ShotsIntoMelee > 0);
        Assert.Contains(results, r => r.Melee.FriendlyTargets > 0);
        Assert.Contains(results, r => r.Melee.TakenDown > 0);
        Assert.Contains(results, r => r.Log.OfType<MarkersSpent>().Any(m => m.Purpose == MarkerSpendPurpose.RaiseInjuryRoll && m.Count > 0));
        Assert.Contains(results, r => r.Log.OfType<InjuryRolled>().Any(i => i.Injury.Outcome == Rules.Core.Dice.InjuryOutcome.OutOfAction));
        var reasons = results.Select(r => r.Outcome!.Reason).Distinct().ToList();
        Assert.Contains(OutcomeReason.VictoryPoints, reasons);
    }

    [Fact]
    [Trait("Rule", "Charge Moves p. 37: the simulated players take routes round terrain, and every such charge replays")]
    public void ManySeedsIncludeChargesThatGoRoundTerrain()
    {
        var results = Enumerable.Range(1, 150).Select(s => Runner.Run(s, Warbands)).ToList();

        var routed = results.SelectMany(r => r.Log.OfType<ChargeMoved>()).Where(c => c.Path.Length > 2).ToList();
        Assert.True(routed.Count >= 10, $"only {routed.Count} charges went round something");
        Assert.Contains(routed, c => c.Reached);
        Assert.Contains(routed, c => !c.Reached);
        Assert.All(routed, c => Assert.True(c.Path.Length <= Rules.Core.Geometry.Charging.MaxLegs));
        // Runner replays every log and compares hashes, so a routed charge the reducer did not agree with would have failed.
        Assert.All(results, r => Assert.True(r.Terminal, r.Describe()));
    }

    [Fact]
    [Trait("Rule", "Architecture: deterministic replay")]
    public void RunningAMeleeSeedTwiceGivesTheSameMatchEventForEvent()
    {
        foreach (var seed in new[] { 1, 7, 15 })
        {
            var first = Runner.Run(seed, Warbands);
            var second = Runner.Run(seed, Warbands);
            Assert.Equal(first.FinalHash, second.FinalHash);
            Assert.Equal(first.Log.Select(e => e.GetType().Name), second.Log.Select(e => e.GetType().Name));
        }
    }

    [Fact]
    [Trait("Rule", "Architecture: replaying any prefix of the log gives the state at that moment")]
    public void EveryPrefixOfAMeleeLogReplaysToTheHashSeenWhileItWasPlayed()
    {
        var result = Runner.Run(10, Warbands);
        Assert.True(result.Melee.Charges > 0 && result.Melee.Retreats > 0 && result.Melee.FreeFights > 0 && result.Melee.ShotsIntoMelee > 0 && result.Melee.TakenDown > 0);

        var state = MatchState.None;
        var seen = new List<string>();
        foreach (var e in result.Log)
        {
            state = Reducer.Apply(state, e);
            seen.Add(StateHash.Compute(state));
        }
        Assert.Equal(result.FinalHash, seen[^1]);
        for (var length = 1; length <= result.Log.Count; length++)
        {
            Assert.Equal(seen[length - 1], StateHash.Compute(Reducer.Replay(result.Log.Take(length))));
        }
    }

    [Fact]
    [Trait("Rule", "Gate 1: a stalled or unfinished melee simulation is a failure")]
    public void AMeleeMatchThatHitsTheStepLimitIsAFailure()
    {
        var result = Runner.Run(1, Warbands, maxCommands: 40);
        Assert.False(result.Terminal);
        Assert.StartsWith("step-limit", result.Status);
    }

    [Fact]
    [Trait("Rule", "Slice A is unchanged: warbands without Melee Weapons never charge, fight or retreat")]
    public void RangedOnlyWarbandsStillPlayExactlyAsInSliceA()
    {
        var ranged = Content.Load(Content.DefaultPath());
        foreach (var seed in Enumerable.Range(1, 40))
        {
            var result = Runner.Run(seed, ranged);
            Assert.True(result.Terminal, result.Describe());
            Assert.Equal(0, result.Melee.Total);
            Assert.DoesNotContain(result.Log, e => e is ChargeDeclared or RetreatDeclared or MeleeAttackDeclared or ShootingTargetRolled);
        }
    }

    [Fact]
    [Trait("Rule", "Architecture: the rules engine knows no warband content")]
    public void RulesCoreSourceNamesNoMeleeWarbandOrModel()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Rules.Core"));
        var names = Warbands.SelectMany(w => new[] { w.Name }.Concat(w.Models.Select(m => m.Name))).ToList();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            var text = File.ReadAllText(file);
            foreach (var name in names)
            {
                Assert.DoesNotContain(name, text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
