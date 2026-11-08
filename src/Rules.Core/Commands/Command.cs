using System.Collections.Immutable;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Rules.Core.Commands;

// A player's (or the host's) intent. A command is only a request: TurnSequence.Decide checks it
// against the state and either rejects it or turns it into events.
public abstract record Command;

public sealed record StartMatch : Command;

public sealed record ChooseDeploymentZone(Seat Seat, string ZoneId) : Command;

public sealed record DeployModel(Seat Seat, ModelId Model) : Command;

public sealed record ChooseFirstActivation(Seat Seat, Seat First) : Command;

public sealed record ActivateModel(Seat Seat, ModelId Model) : Command;

public sealed record EndActivation(Seat Seat) : Command;

public enum MoraleFailureChoice
{
    BecomeShaken,
    SoundRetreat,
}

public sealed record ResolveFailedMorale(Seat Seat, MoraleFailureChoice Choice) : Command;

public sealed record ChooseMoraleOrder(Seat Seat, Seat First) : Command;

// Trench Warfare (p. 161): the roles roll-off winner chooses which seat attacks; the other defends.
public sealed record ChooseAttacker(Seat Seat, Seat Attacker) : Command;

// Trench Warfare (p. 162): the defender sets up the Mine Markers (ids Mines.IdOf(0) onwards, in this order) and names
// the duds among them. Only the defender's seat sees the duds.
public sealed record LayMines(Seat Seat, ImmutableArray<Point2> Markers, ImmutableArray<string> Duds) : Command;

// Field Shrine (Battlekit p. 82; U127): after a model that carries one is deployed, its player may deploy the shrine anywhere
// wholly within the deployment zone, or decline.
public sealed record PlaceShrine(Seat Seat, ModelId Carrier, Point2 At) : Command;

public sealed record DeclineShrine(Seat Seat, ModelId Carrier) : Command;

// ---- M4: commands that need a battlefield (a MatchSetup with a Scenario) ----

// Sets a model up at a point inside its deployment zone (Scenario I, p. 152). Replaces DeployModel when there is a battlefield.
public sealed record PlaceModel(Seat Seat, ModelId Model, Point2 At) : Command
{
    // Roster stage R8d (U133): which table axis an oval base lies along (Y for a round base, which has none).
    public BaseAxis Axis { get; init; }
}

// A Move ACTION (p. 34): the active model's centre follows the waypoints in order. At most MaxWaypoints of them.
public sealed record MoveModel(Seat Seat, ModelId Model, ImmutableArray<Point2> Path) : Command;

// A Dash ACTION: a Risky Success Roll first, then a move like an ordinary Move (p. 34).
public sealed record DashModel(Seat Seat, ModelId Model, ImmutableArray<Point2> Path) : Command;

// A Shoot ACTION: a Ranged Attack by the active model against one enemy model (pp. 42-44).
public sealed record Shoot(Seat Seat, ModelId Attacker, ModelId Target) : Command
{
    // Roster stage R3 (U100): which of the model's weapons of that kind attacks: 0, or 1 for its second weapon.
    // Left off the wire when 0.
    public int Weapon { get; init; }
}

// Answers a pending Blood Marker decision (p. 28): spend this many markers, or none (Count 0).
public sealed record SpendMarkers(Seat Seat, int Count) : Command
{
    // Bloodbath Roll (p. 47, U124): on a pending Injury Roll decision, Count includes the 6 markers (3 if the target is Down)
    // that turn the roll into a Bloodbath Roll; any more are +1 INJURY DICE each. Left off the wire when false.
    public bool Bloodbath { get; init; }
}

// ---- M4 Slice B: Charge, Retreat, Fight and the choices around them ----

// A Charge ACTION (pp. 34, 36-37): declare the target; the Charge Bonus D6 and the charge move follow.
public sealed record ChargeModel(Seat Seat, ModelId Model, ModelId Target) : Command
{
    // Carry-on past contact (p. 37, U126): if the charge stops in contact with allowance to spare, it may slide round the
    // target to the nearest point within 1" of another enemy. Left off the wire when false.
    public bool CarryOn { get; init; }
}

// A Retreat ACTION (pp. 35, 37): the path is fixed now, the move happens after the opponent's free attacks.
public sealed record RetreatModel(Seat Seat, ModelId Model, ImmutableArray<Point2> Path) : Command;

// A Fight ACTION (pp. 34, 45-46): one Melee Attack with the model's Melee Weapon against one enemy within 1".
public sealed record FightModel(Seat Seat, ModelId Model, ModelId Target) : Command
{
    // Roster stage R3 (U100): which of the model's weapons of that kind attacks: 0, or 1 for its second weapon.
    // Left off the wire when 0.
    public int Weapon { get; init; }
}

// The opponent of a retreating model attacks it with one engaged model (p. 37). Answers the FreeFight decision.
public sealed record FreeFight(Seat Seat, ModelId Attacker) : Command
{
    // Roster stage R3 (U100): which of the model's weapons of that kind attacks: 0, or 1 for its second weapon.
    // Left off the wire when 0.
    public int Weapon { get; init; }
}

// The opponent declines the remaining free attacks (p. 37). Answers the FreeFight decision.
public sealed record DeclineFreeFights(Seat Seat) : Command;

// A shot into melee rolled 1-3: the shooter picks which of its own models within 1" of the target is shot (p. 42).
public sealed record ChooseFriendlyTarget(Seat Seat, ModelId Victim) : Command;

// Roster stage R4c (U103): a SCATTER attack missed; the attacker's opponent picks the point it lands at, exactly the
// scatter distance from the target and in Line of Sight of it (p. 57). Answers the ChooseScatterPoint decision.
public sealed record ScatterAttack(Seat Seat, Point2 At) : Command;

// Roster stage R5a (U105): a Shoot ACTION with a BLAST weapon aimed at a visible point on the battlefield instead of a
// model (p. 53). Weapon as for Shoot.
public sealed record ShootPoint(Seat Seat, ModelId Attacker, Point2 At) : Command
{
    public int Weapon { get; init; }
}

// Roster stage R5h (U113): a Treat ACTION with a Medi-kit (p. 84): a Risky Success Roll, and on a Success, Choice: remove
// 1 BLOOD MARKER from the model itself or a friendly model within 1" (Target), or stand up a Down friendly model within 1".
public sealed record TreatModel(Seat Seat, ModelId Model, ModelId Target, TreatChoice Choice) : Command;

// Roster stage R5j (U115): a Trigger Martyrdom Device ACTION (Warbands p. 58): the device detonates, once per game.
public sealed record TriggerMartyrdom(Seat Seat, ModelId Model) : Command;

// Roster stage R5k (U116): answers the UseTalisman decision after the active model's Risky Success Roll failed: use one of
// its Talismans (a Blessed Icon or an Unholy Trinket) so the activation does not end, or let the activation end.
public sealed record UseTalisman(Seat Seat) : Command;
public sealed record DeclineTalisman(Seat Seat) : Command;

// Roster stage R6b (U118): an ACTION the model takes on itself alone, a Risky Success Roll: Aim (+2 DICE to the roll; on
// a Success its Ranged Attacks get +2 DICE for the rest of the activation) or Fortify (on a Success it has COVER until it
// moves).
// Roster stage R6e (U121): Target, the friend a Laying on of Hands ACTION is taken on (the model itself included); no other
// ACTION has one.
public sealed record TakeSelfAction(Seat Seat, ModelId Model, SelfActionKind Action) : Command
{
    public ModelId? Target { get; init; }

    // Roster stage R9a (U134): a Puppet Master ACTION's Target is the model it moves (any other model in sight within 12") and
    // Direction a point on the straight line it is moved along, from the target's centre through that point.
    public Point2? Direction { get; init; }
}

// Roster stage R7d (U131): answers the UseBodyguard decision after an attack hit a model with a friendly Bodyguard within
// 1": Guard takes the hit instead (its Injury Roll is made), or null leaves it on the model that was hit.
public sealed record ChooseBodyguard(Seat Seat, ModelId? Guard) : Command;

// Roster stage R8d (U133): the active model's base is turned in place to lie along the other table axis. Free, not an ACTION.
public sealed record TurnBase(Seat Seat, ModelId Model, BaseAxis Axis) : Command;

// Roster stage R11 (U136): casts a spell the model knows (a "Cast Spell ACTION"), paying its cost in BLOOD MARKERS from models
// without CULT or DEMONIC. Target is the model in sight a spell works on; TakeBlessing (What is Yours is Mine) takes a
// BLESSING MARKER instead of a BLOOD MARKER.
public sealed record CastSpell(Seat Seat, ModelId Model, SpellKind Spell, ImmutableArray<MarkerPayment> Payment) : Command
{
    public ModelId? Target { get; init; }

    public bool TakeBlessing { get; init; }

    // October 2026 Review (U147): Raining Blood's targets, one enemy model per SIN MARKER paid; left off the wire when empty.
    public ImmutableArray<ModelId> Targets { get; init; } = [];
}

// October 2026 Review (U144): the Break Vessel ACTION of a model with a Sin Vessel, once per game: 2 SIN MARKERS next to the model.
public sealed record BreakVessel(Seat Seat, ModelId Model) : Command;

// Roster stage R16 (U141): answers UseBlessing or UseInjuryReaction by casting nothing. (Casting is a CastSpell by the model the
// Injury Roll is for.)
public sealed record DeclineReaction(Seat Seat) : Command;

// October 2026 Review (U145): answers ConvertMarkers: turn Converted of the BLOOD MARKERS the Injury Roll would place into SIN MARKERS next
// to the friendly DEMONIC model To (Mortal Suffering) and keep Stripped off in exchange for removing as many SIN MARKERS from the target
// (Perversion of God's Laws). All 0 changes nothing.
public sealed record ConvertMarkers(Seat Seat, int Converted, ModelId? To, int Stripped) : Command;
