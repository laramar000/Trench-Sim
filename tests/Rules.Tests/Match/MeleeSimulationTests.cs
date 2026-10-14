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

    // Regression pins of this implementation's own behaviour under StateHash version 3, not an independent oracle.
    private static readonly Dictionary<int, string> PinnedFinalHashes = new()
    {
        [1] = "fd3356e8948966b1e1855643fdb6a8ae8c93b1251acdfbf2010422b0c2df0727",
        [2] = "a549e42a699ba1d90c4e2e1bbdbe858ede9b29255a3ee61fc11ec2b2c059580b",
        [3] = "f6a9e3da6669985fc134044d9bf51de12947475508f498b442bf53543e2b75fa",
        [4] = "0dbcad72ce944e4223c6394f95d55834d253f01e75e2a50fdf9a6d7e44b7dae2",
        [5] = "75cb961ffb007a72a1db9cead3a84f72ae98e2172f7eb9b32fcd3149649afd7d",
        [6] = "c79b603647e56d3389e11d2c1aee201fe18130cb23bbe8d8c129a99bfe3c9eec",
        [7] = "edec0b11df3f45fbededaa6bc76134c449e623180f01519faf77730a7554657b",
        [8] = "318c1b1b25e2f838b5dea0f069c61e7aef267a872634281e16cb52ef58f7a755",
        [9] = "8726ec1b1c7adb58961cc5bb35dcbf788d65286eb58300a8ca96524feaad56e6",
        [10] = "7514f7d72a0efcf206d11fd5dc75c9bb19e2e7e9c1f66b4a25c4abc3ce963a70",
        [11] = "c16ae08fe00eedd1ba7bd9f6d8beadeb5b6f2409fb1cba625e7b280b6c98872e",
        [12] = "8559333fb48b4b8f3b2e3ab1f604c45b4a3c87bee759de8f72c52546b20e3c8d",
        [13] = "e1ea1cc2750f122dac8c5f77236701fb92adaae439bd2fe2ec03d9e9b9283f20",
        [14] = "5f478383b856eecb4091229a6668a05f6823143f41b3261afe2ac0c7fd87b4c9",
        [15] = "994eae6fe4fc5fdaf3e3d034a885bf292fb9db3f6cec69d3a65dd96fb2b8bad7",
        [16] = "0583013f22d605489d651bfc19a771cc651be3fa9c3a42a5445560c7429c3f7d",
        [17] = "1c157a219d6eae96fc9ce541f50ddc4d55f438226ec788f6349e1da42d31befa",
        [18] = "37813431ae0d92584967c7f863363f1601ac1aa8ca42c9cab2fc1c434637af86",
        [19] = "73cdc561f4e96a19885e107de6ce6222a9d789192b2ae015d63a9b86aece6f82",
        [20] = "b585a49914d5bdc8d266a49e440328c8f0d58370999e4e3df684ffa75a4d5141",
        [21] = "ef76994b465d4b01f9d9ad2682585db2f3b0bb2d8b1683bd05bf0ec507d186f3",
        [22] = "fcf17bac654bbd9c275b92dd592e23ad695a08fa328424f344317c3be673b43c",
        [23] = "8ddf2a1c838cd7ff53408334da759c91e296d9e89cafa95e2c667193b5831875",
        [24] = "4ca36fdda97de0e97532b27dbcf1cd91cbff89ef8b58cb853eb5ed319cea80a5",
        [25] = "76c896c9cc9ab8b706238a45e49308526248aeac3b98caad907db613054b9236",
        [26] = "9513dd1ddfa29954d4fcfe54757a24a04172007ffd3545dab19faf1080ebd11e",
        [27] = "fe22e639c99fce42cbc80df3de7f9b45ffbaa5e800dd22a0a4ba9b2e654fece9",
        [28] = "6cafd4c0c538694591701a522f129021805df02879965bfe6bfc74c7f19d115c",
        [29] = "8180f181741d015bdf09983f0f6ae203d2e43aa0b2cc12492280b7fc468982c7",
        [30] = "22fd95511103ce6065dcc3488b431e2b46299023b82adbb6829034ed15004f22"
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
