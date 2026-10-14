using Rules.Core.State;

namespace Rules.Core.Events;

public sealed record MatchCreated : GameEvent
{
    public MatchCreated(MatchSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        Setup = setup;
    }

    public MatchSetup Setup { get; }
}
