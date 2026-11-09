using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.State;

namespace Client.Core;

// October 2026 Review, stage C6: the spells the activating model can cast now, as complete commands the rules accept (checked with
// TurnSequence.Check, never worked out here). Each spell is offered at its lowest cost, paid the cheapest way. Not offered (cast with the
// CLI): Raining Blood, Burning Inferno, Left-Hand Path and Slavemaster, which need a list of targets, an attack, a move or an order,
// and the three reactions, which are answered when an Injury Roll is held.
public static class SpellPicker
{
    private static readonly SpellKind NotOffered = SpellKind.BlessingOfTheSerpentMoon | SpellKind.OracleBeastCloak | SpellKind.TooProudToFall
        | SpellKind.RainingBlood | SpellKind.BurningInferno | SpellKind.LeftHandPath | SpellKind.Slavemaster;

    public static IReadOnlyList<(SpellKind Spell, IReadOnlyList<CastSpell> Casts)> Choices(MatchState state, Seat seat)
    {
        if (state.Pending is not null || state.Setup is null || state.ActiveModel is not { } caster || state.FindModel(caster) is not { Seat: var owner } || owner != seat
            || state.Setup.ProfileOf(caster) is not { } profile || profile.Spells == SpellKind.None)
        {
            return [];
        }
        var found = new List<(SpellKind, IReadOnlyList<CastSpell>)>();
        var targets = state.Models.Where(m => m.Position is not null && m.Status != ModelStatus.OutOfAction).Select(m => (ModelId?)m.Id).Prepend(null).ToList();
        foreach (var spell in Enum.GetValues<SpellKind>().Where(k => Spells.IsOne(k) && (profile.Spells & k) != 0 && (NotOffered & k) == 0))
        {
            if (TurnSequence.CheapestPayment(state, seat, Spells.Cost(spell).Min) is not { } payment)
            {
                continue;
            }
            var pieces = spell == SpellKind.UncaringGluttony ? Equipment.All : [EquipmentPiece.None];
            var casts = new List<CastSpell>();
            foreach (var target in targets)
            {
                foreach (var blessing in spell == SpellKind.WhatIsYoursIsMine ? new[] { false, true } : [false])
                {
                    foreach (var piece in pieces)
                    {
                        var cast = new CastSpell(seat, caster, spell, payment) { Target = target, TakeBlessing = blessing, Piece = piece };
                        if (TurnSequence.Check(state, cast) is null)
                        {
                            casts.Add(cast);
                        }
                    }
                }
            }
            if (casts.Count > 0)
            {
                found.Add((spell, casts));
            }
        }
        return found;
    }
}
