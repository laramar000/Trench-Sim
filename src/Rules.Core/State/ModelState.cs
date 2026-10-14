namespace Rules.Core.State;

public enum ModelStatus
{
    Standing,
    Down,
    OutOfAction,
}

// One model. M2 tracks only what the turn sequence needs; positions (M3) and markers (M4) come later.
// Out-of-Action models stay in the list: the Morale Phase counts them (p. 50).
public sealed record ModelState(
    ModelId Id,
    Seat Seat,
    ModelStatus Status = ModelStatus.Standing,
    bool Deployed = false,
    bool ActivatedThisTurn = false);
