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

    // Regression pins of this implementation's own behaviour under StateHash version 5, not an independent oracle.
    // Re-pinned for version 5 (Glorious Deeds, U96, 2026-10-26): every match is event-for-event the same as under
    // version 4 apart from the GloriousDeedCompleted events added, and only the Victory Points those deeds give changed.
    private static readonly Dictionary<int, string> PinnedFinalHashes = new()
    {
        [1] = "4195beb878aa9df19ee1fccbd9dd537d60dbd0b6073671133ce92ecdb508d512",
        [2] = "4fb44dbac25f7bc99c2e8bbb93fa49d8cac80f9f2c1238bcc1123d0e3b500686",
        [3] = "dc3942169f148a387837165cca36105bff33cc32dd70e833122aa11665ca2e52",
        [4] = "4a87ea067af29fbbbfd2b0f0e5b5cafe700f79b326a9890fbb0c17fc46a76b19",
        [5] = "5c200ce6e8e062743b0ed52adb34066631f70e0d5b8ad922ea9477c687f10fd3",
        [6] = "2cb9fc584c9177f189cc659638e0317b5cd4106356712324a90bd74c40a52ffb",
        [7] = "6d0613c1a1637fe0497aa90f9e25e83eaf9476e17a0a1bf9d6a79c5bb32afbcf",
        [8] = "cfae15737e261dbcc536f85a2a7be0079f84f8ffc987f730f9d225ffd549c67d",
        [9] = "9066e1f571e6b2cd12309ac92ebf9659a5fe489f1bea793b129e6e85640c37d6",
        [10] = "aa2ba70df66fb8370a66b49072d0dc4d78205f15e84ed88d3f8ae5abacfa91a2",
        [11] = "1e0cdfdfc6ebd1c9df7010f36244387c8f06539705d5b10132fd8829f1f243cf",
        [12] = "854ff0805dca2c5a8bab3ef9ab89c791eae62eaec1bf1d5626068599543f00c8",
        [13] = "d3e10c0ed979399f8e43e6e36a2c6ee96d54cf59f0ea9dd80bb0d9e4a9742823",
        [14] = "62fdba5a6cfafcb1e35c84bda240a7f0fe64b4e7e0b2200f2a1101bfada6b358",
        [15] = "4554efec00818722d34073e7024767ccfb2a40564c960c96d623eaecc0f0c3df",
        [16] = "543ccd124e707bf166278fd4fe6adb98248111642d2c088f26220f7c37376849",
        [17] = "a1cb2b496c4bec782f7af52fc9357a07bdec67b8381f62d07d2098c64d6c0dd5",
        [18] = "8952634584dc76f0123e630ccdf28d62031572eba6f30697a594b05f979c37d0",
        [19] = "788da775ec0feb283023f19225c599b3810b7bdf97b26fd834e91b3fa96256ea",
        [20] = "81f4357f809b35a7dc4c54a9b91d40f1dbd753656a854408ac5135e428ec550a",
        [21] = "8eec11abab0ad5880933602ec28deefdf03b14ccc66ea26efda1b9f76ce9e9b1",
        [22] = "c03a38834e513e5ffcb7c81e2ff7da8feab5c9c096631a426024f603030cc54c",
        [23] = "b8d5c7398120d003958cdf1bda61e1223d81133de8dc52384b47d0dd999a1ffb",
        [24] = "7c236546307be3ce6284e4d3dec30d2335817372bd63a7d0e44b27f573e21143",
        [25] = "89c654643c5e3a209a5555e7d68367636ceb88ef44f6236c79339972434e4114",
        [26] = "19be8087888a80352bccaf74a0810cf794d176e34e92b3db914109c99eab8c45",
        [27] = "95443a6dcc9e72dc59559326c73865a9dfc82aa5253320d81c79502a2b9d0331",
        [28] = "08897f41c11d65b2a8db8186f642088b1aacf47c344c46cbd9b9daa56485efe0",
        [29] = "7632d4288e575b99fab5fc4790960bd31722c1572f0768e65c6f0f579f88932f",
        [30] = "37b13701197fa6df2a5e26243bb06cec6e4a92d9609377cc784fd36b83248199"
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
