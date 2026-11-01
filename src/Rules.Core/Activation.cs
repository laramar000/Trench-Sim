using System.Collections.Immutable;
using Rules.Core.Commands;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Rules.Core;

// Read-only, for the client (battlefield UI 2, stage N2, decision D3 approved by Jason on 2026-10-21): whether the
// activating model has nothing left to do. Nothing new in the rules: it asks only TurnSequence.Check, the very check
// Decide makes, and it says true only when
// - no roll, decision or free attack is waiting (Pending, InProgress, Retreat) and the rules are not about to end the
//   activation themselves (MustEndActivation, after a failed Risky roll);
// - the rules refuse a Move, a Dash and a Retreat for a reason no path can change (the action already taken or excluded,
//   a model not standing or not on the table, not engaged for a Retreat); a refusal about the path itself never counts,
//   as another path might be accepted;
// - the rules refuse a Shoot, a Charge and a Fight at every model of the match.
// Then the only command the rules accept from that seat is EndActivation; the client may send it, and the server decides.
public static class Activation
{
    // The refusals of a Move, Dash or Retreat that hold whatever the path.
    private static readonly ImmutableHashSet<ReasonCode> Closed =
    [
        ReasonCode.ActionAlreadyTaken,
        ReasonCode.ActionExcluded,
        ReasonCode.ModelNotStanding,
        ReasonCode.ModelNotOnTable,
        ReasonCode.ModelOutOfAction,
        ReasonCode.NotEngaged,
    ];

    public static bool Finished(MatchState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Phase != Phase.Activation || state.ActiveModel is not { } active || state.Pending is not null || state.InProgress is not null
            || state.Retreat is not null || state.MustEndActivation || state.FindModel(active) is not { } model)
        {
            return false;
        }
        var seat = model.Seat;
        if (TurnSequence.Check(state, new EndActivation(seat)) is not null)
        {
            return false;
        }
        // A one-point path to where the model stands: enough to reach every check that does not depend on the path.
        ImmutableArray<Point2> here = model.Position is { } at ? [at] : [];
        foreach (var move in new Command[] { new MoveModel(seat, active, here), new DashModel(seat, active, here), new RetreatModel(seat, active, here) })
        {
            if (TurnSequence.Check(state, move) is not { } refused || !Closed.Contains(refused.Code))
            {
                return false;
            }
        }
        foreach (var other in state.Models)
        {
            // Roster stage R3: with each of the model's weapons (a Fight with attacks to come goes on with its next weapon).
            var profile = state.Setup!.ProfileOf(active)!;
            var attacks = new List<Command> { new Shoot(seat, active, other.Id), new ChargeModel(seat, active, other.Id), new FightModel(seat, active, other.Id) };
            if (profile.Weapons(AttackKind.Ranged) == 2)
            {
                attacks.Add(new Shoot(seat, active, other.Id) { Weapon = 1 });
            }
            if (profile.Has(AttackKind.Ranged, ModelProfile.GrenadeWeapon))
            {
                attacks.Add(new Shoot(seat, active, other.Id) { Weapon = ModelProfile.GrenadeWeapon });
            }
            if (profile.Weapons(AttackKind.Melee) == 2)
            {
                attacks.Add(new FightModel(seat, active, other.Id) { Weapon = 1 });
            }
            // Roster stage R5h (U113): a Medi-kit's Treat ACTION on the model itself or a friend.
            if (profile.Treats && other.Seat == seat)
            {
                attacks.Add(new TreatModel(seat, active, other.Id, TreatChoice.RemoveMarker));
                attacks.Add(new TreatModel(seat, active, other.Id, TreatChoice.StandUp));
            }
            foreach (var attack in attacks)
            {
                if (TurnSequence.Check(state, attack) is null)
                {
                    return false;
                }
            }
        }
        return true;
    }
}
