using Rules.Core.Geometry;

namespace Rules.Core.State;

// A Field Shrine on the table (Field Shrine, Battlekit p. 82; U127): the model that carried it, the seat it belongs to,
// and where its centre is. It is not a model. Its base is Radius, 40 mm.
public sealed record ShrineState(ModelId Carrier, Seat Seat, Point2 At)
{
    // A 40 mm base, in thousandths of an inch (U1): 20 mm is 0.7874".
    public static readonly Length Radius = Length.FromThousandths(787);

    // Each shrine adds this many to the owner's count of models that are not Down or Out of Action for the Morale half
    // test, up to MaxBonus in all (p. 82).
    public const int MoraleBonus = 3;
    public const int MaxBonus = 9;
}
