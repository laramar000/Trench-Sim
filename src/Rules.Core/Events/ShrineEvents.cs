using Rules.Core.Geometry;
using Rules.Core.State;

namespace Rules.Core.Events;

// Field Shrine (Battlekit p. 82; U127).

// The player deployed the Field Shrine of a model that was just deployed. The carrier no longer carries it.
public sealed record ShrinePlaced(ModelId Carrier, Point2 At) : GameEvent;

// The player chose not to deploy it.
public sealed record ShrineDeclined(ModelId Carrier) : GameEvent;
