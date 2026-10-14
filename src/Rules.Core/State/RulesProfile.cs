namespace Rules.Core.State;

// How Initiative counts models. The two rulebook chapters differ (Comprehensive p. 32 excludes Down
// and Out of Action models; Core p. 14 does not say so). Provisional rule R1.
public enum InitiativeCounting
{
    ExcludeDownAndOutOfAction,
    CountAllNotOutOfAction,
}

// How the two Morale Checks are ordered when both warbands must check. The rulebook does not say
// (provisional rule R5; the "two things at the same time" box on p. 32 is the nearest text).
public enum MoraleOrderRule
{
    InitiativeHolderChooses,
    InitiativeHolderFirst,
}

// Provisional rule choices, kept in the match state and the state hash so a match record always says
// which reading it was played under. Nothing here is an official ruling.
public sealed record RulesProfile(
    InitiativeCounting InitiativeCount = InitiativeCounting.ExcludeDownAndOutOfAction,
    MoraleOrderRule MoraleOrder = MoraleOrderRule.InitiativeHolderChooses)
{
    public static RulesProfile Default { get; } = new();
}
