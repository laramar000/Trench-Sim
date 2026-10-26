using System.Collections.Immutable;

namespace Rules.Core.State;

// The Glorious Deeds of the scenario being played (pp. 149-150; U96). Each is worth 1 Victory Point to the first warband
// to complete it, once per game.
public enum GloriousDeed
{
    Bloodletting,          // Scenario I: an attack puts the sixth Blood Marker beside an enemy model
    HoldYourGround,        // Scenarios I and IV: the first warband to pass a Morale Check
    LordOfWar,             // Scenario I: one model takes two enemy models Out of Action with Melee Attacks in one Turn
    ResistAndBite,         // Scenario I: a model that began its activation Down takes an enemy Out of Action in it
    GoodHunting,           // Scenario IV: a Ranged Attack with the Long Range modifier takes an enemy Out of Action
    Headshot,              // Scenario IV: a model that retreated earlier in its activation takes Out of Action an enemy it retreated from
    IntoTheTrenches,       // Scenario IV: a model charges an enemy in a trench section, then takes it Out of Action in melee
    SurviveToTellTheTale,  // Scenario IV: a model has two mine Injury Rolls and is taken Out of Action by neither
}

// A deed and who completed it. Model is the model that did it; Hold Your Ground is a warband's, with no model.
public sealed record CompletedDeed(GloriousDeed Deed, Seat Seat, ModelId? Model);

// What the deeds need to know beyond the rest of the state, kept by the reducer from the events (Deeds.Track).
// Owed: deeds the last event completed, each announced by the next events (GloriousDeedCompleted), in order.
public sealed record DeedState
{
    public ImmutableArray<CompletedDeed> Completed { get; init; } = [];
    public ImmutableArray<CompletedDeed> Owed { get; init; } = [];

    // The Ranged Attack being made has the Long Range modifier (Good Hunting).
    public bool ShotAtLongRange { get; init; }

    // One entry per enemy model taken Out of Action by a Melee Attack this Turn: the attacker (Lord of War).
    public ImmutableArray<ModelId> MeleeTakedowns { get; init; } = [];

    // The active model stood up at the start of its activation, so it began it Down (Resist and Bite).
    public bool ActiveBeganDown { get; init; }

    // The enemy models engaged with the retreating model when its Retreat was declared, and, once it has moved away,
    // every enemy the active model has retreated from in this activation (Headshot).
    public ImmutableArray<ModelId> RetreatingFrom { get; init; } = [];
    public ImmutableArray<ModelId> RetreatedFrom { get; init; } = [];

    // The enemy model the active model reached with a charge while that enemy was in a trench section (Into the Trenches!).
    public ModelId? ChargedInTrench { get; init; }

    // One entry per mine Injury Roll a model came through without being taken Out of Action (Survive to Tell the Tale).
    public ImmutableArray<ModelId> MineSurvivals { get; init; } = [];

    public bool Done(GloriousDeed deed) => Completed.Any(c => c.Deed == deed) || Owed.Any(c => c.Deed == deed);
}
