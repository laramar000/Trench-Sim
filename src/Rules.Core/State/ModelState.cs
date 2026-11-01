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
    bool ToughSpent = false);

// Roster stage R5b (U106): ToughSpent, the model's TOUGH has already turned an Out of Action result into Down (p. 57).
