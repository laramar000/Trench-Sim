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
    int TalismansUsed = 0,
    bool Hidden = false,
    BaseAxis Axis = BaseAxis.Y,
    int InfectionMarkers = 0,
    int BlessingMarkers = 0)
{
    // Roster stage R10 (U135): INFECTION and BLESSING MARKERS are BLOOD MARKERS in another colour: up to 6 of each, and for
    // spending (an opponent's -DICE on a Success Roll, +INJURY DICE on an Injury Roll) every kind counts. Spending takes Blood
    // first, then Infection, then Blessing. Rules that remove BLOOD MARKERS (Treat, Laying on of Hands) remove only those.
    public int Markers => BloodMarkers + InfectionMarkers + BlessingMarkers;

    // The model after `count` of its markers were spent, in that order.
    public ModelState WithoutMarkers(int count)
    {
        var blood = Math.Min(count, BloodMarkers);
        var infection = Math.Min(count - blood, InfectionMarkers);
        var blessing = count - blood - infection;
        return this with { BloodMarkers = BloodMarkers - blood, InfectionMarkers = InfectionMarkers - infection, BlessingMarkers = BlessingMarkers - blessing };
    }
}

// Roster stage R8d (U132): Axis, which table axis an oval base's long side lies along (Y for every round base, and until the player turns it).

// Roster stage R7c (U130): Hidden, a Hide ACTION succeeded; it lasts until the model's next activation (or an enemy within 1.5").

// Roster stage R5b (U106): ToughSpent, the model's TOUGH has already turned an Out of Action result into Down (p. 57).
// Roster stage R5e (U109): WardSpent, its Sacrificial Lamb's blood has already kept off the first BLOOD MARKER.
// Roster stage R5j (U115): MartyrdomSpent, its Martyrdom Device has detonated and cannot be used again.
// Roster stage R5k (U116): TalismansUsed, how many of its Talismans it has used (each once per game).
// Roster stage R5f (U110): DugInAt, where a model with a Shovel started the game on Open terrain, until it moves away.
// Roster stage R6b (U118): or where a Fortify ACTION succeeded, until it moves away.
