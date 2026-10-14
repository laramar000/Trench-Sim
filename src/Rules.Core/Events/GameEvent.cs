namespace Rules.Core.Events;

// An event is a fact: something that happened, with every die it needed. The reducer applies events
// one at a time and holds no rules of its own; the rules live in TurnSequence.Decide.
// Events validate their own contents on construction, so one loaded from a log is as trustworthy as a fresh one.
public abstract record GameEvent;
