using Rules.Core.Geometry;

namespace Rules.Core.State;

public enum ModelStatus
{
    Standing,
    Down,
    OutOfAction,
}

// One model. Position is null before deployment, in M2 setups (no battlefield) and once Out of Action (taken off
// the table). Out-of-Action models stay in the list: the Morale Phase counts them (p. 50).
public sealed record ModelState(
    ModelId Id,
    Seat Seat,
    ModelStatus Status = ModelStatus.Standing,
    bool Deployed = false,
    bool ActivatedThisTurn = false,
    Point2? Position = null,
    int BloodMarkers = 0,
    bool ToughSpent = false,
    bool WardSpent = false,
    Point2? DugInAt = null,
    bool MartyrdomSpent = false,
    int TalismansUsed = 0);

// Roster stage R5b (U106): ToughSpent, the model's TOUGH has already turned an Out of Action result into Down (p. 57).
// Roster stage R5e (U109): WardSpent, its Sacrificial Lamb's blood has already kept off the first BLOOD MARKER.
// Roster stage R5j (U115): MartyrdomSpent, its Martyrdom Device has detonated and cannot be used again.
// Roster stage R5k (U116): TalismansUsed, how many of its Talismans it has used (each once per game).
// Roster stage R5f (U110): DugInAt, where a model with a Shovel started the game on Open terrain, until it moves away.
