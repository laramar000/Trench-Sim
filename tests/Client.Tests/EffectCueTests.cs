using Client.Core;
using Rules.Core.Events;

namespace Client.Tests;

// Graphics stage G5: effects come only from the server's events: a flash at the shooter of every Ranged Success Roll,
// a puff on its target (hit when the rules count the roll passed, dirt when failed), and a puff on the target of every
// Melee Success Roll. No other event has effects.
public class EffectCueTests
{
    [Fact]
    public void EffectsFollowTheShotsAndBlowsAndTheRulesVerdicts()
    {
        var seen = new Dictionary<EffectKind, int>();
        foreach (var (_, log) in ClientCoreTests.Matches.Value)
        {
            foreach (var e in log)
            {
                var cues = EffectCues.Of(e);
                var hit = DiceRolls.Of(e)?.Verdict is RollVerdict.Pass or RollVerdict.Critical;
                var puff = hit ? EffectKind.HitPuff : EffectKind.DirtPuff;
                switch (e)
                {
                    case RangedSuccessRolled shot:
                        Assert.Equal([new EffectCue(EffectKind.MuzzleFlash, shot.Attacker), new EffectCue(puff, shot.Target)], cues);
                        break;
                    case MeleeSuccessRolled blow:
                        Assert.Equal([new EffectCue(puff, blow.Target)], cues);
                        break;
                    default:
                        Assert.Empty(cues);
                        break;
                }
                foreach (var cue in cues)
                {
                    seen[cue.Kind] = seen.GetValueOrDefault(cue.Kind) + 1;
                }
            }
        }
        Assert.All(Enum.GetValues<EffectKind>(), k => Assert.True(seen.GetValueOrDefault(k) > 0, k.ToString()));
    }
}
