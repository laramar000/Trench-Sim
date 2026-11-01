namespace Rules.Core.State;

// Roster stage R5b (U106): the model keywords the engine plays, from the model's own entry and its battlekit. Left off
// the wire and out of the state hash when None, so every setup without them is written exactly as before.
[Flags]
public enum ModelTraits
{
    None = 0,

    // FEAR (p. 54): -1 DICE to a Melee Attack that targets the model; models with FEAR are immune to it.
    Fear = 1,

    // NEGATE FEAR (p. 56): the model is not affected by FEAR.
    NegateFear = 2,

    // TOUGH (p. 57): the first Out of Action result on the Injury Table is a Down result instead.
    Tough = 4,

    // NEGATE HEAVY (p. 56; STRONG has it, p. 57): HEAVY does not limit the model.
    NegateHeavy = 8,

    // LEADER (p. 55): +1 DICE to the warband's Morale Checks while one is on the battlefield, not Down or Out of Action.
    Leader = 16,

    // Roster stage R6a (U117): keywords other rules look for. PRINCIPALITY and CULT are faction keywords (the
    // Trench Cleric's aura, Finish the Fallen); DEMONIC is also NEGATE FIRE (ModelProfile.Negates).
    Principality = 32,
    Cult = 64,
    Demonic = 128,
}
