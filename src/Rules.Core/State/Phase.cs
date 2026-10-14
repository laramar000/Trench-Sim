namespace Rules.Core.State;

// Where a match is. The turn is Initiative, Activation, then Morale (rulebook 1.0.2 p. 32).
// Deployment happens once before the first turn (Scenario Format and Claim No Man's Land, pp. 145-152).
public enum Phase
{
    None,        // no match has been created yet
    PreGame,     // created, not started
    Deployment,
    Initiative,
    Activation,
    Morale,
    Ended,
}
