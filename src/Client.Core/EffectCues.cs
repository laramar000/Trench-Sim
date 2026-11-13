using Rules.Core.Events;
using Rules.Core.State;

namespace Client.Core;

// Graphics stage G5: the short visual effects an event shows on the battlefield, like its sound and its note: a muzzle
// flash at a shooter, a dirt puff by a target that was missed, a puff on a target that was hit. They come only from the
// server's events (the result the server rolled), never from a preview or the client's own guess, and a window that is
// catching up (rejoin, replay) plays none.
public enum EffectKind
{
    MuzzleFlash,
    DirtPuff,
    HitPuff,
}

public sealed record EffectCue(EffectKind Kind, ModelId At);

public static class EffectCues
{
    public static IReadOnlyList<EffectCue> Of(GameEvent e) => e switch
    {
        RangedSuccessRolled shot => [new(EffectKind.MuzzleFlash, shot.Attacker), new(Hit(e) ? EffectKind.HitPuff : EffectKind.DirtPuff, shot.Target)],
        MeleeSuccessRolled blow => [new(Hit(e) ? EffectKind.HitPuff : EffectKind.DirtPuff, blow.Target)],
        _ => [],
    };

    // A hit is a Success Roll the rules count as passed (or critical), as the dice and the sound show it.
    private static bool Hit(GameEvent e) => DiceRolls.Of(e)?.Verdict is RollVerdict.Pass or RollVerdict.Critical;
}
