using Rules.Core.Geometry;

namespace Rules.Tests.Geometry;

// Short builders so scenes read as inches. Every value is parsed exactly (at most three decimals).
internal static class Geo
{
    public static Length L(string inches) => Length.Parse(inches);

    public static Point2 P(string x, string y) => new(L(x), L(y));

    public static Rect R(string x1, string y1, string x2, string y2) => new(P(x1, y1), P(x2, y2));

    public static Footprint F(string x1, string y1, string x2, string y2) => Footprint.Of(R(x1, y1, x2, y2));

    public static ModelBody Body(string id, string x, string y, string radius = "0.5") => new(id, P(x, y), L(radius));

    // A Wall on the ground (not walkable) unless a surface is given.
    public static TerrainBlock Wall(string id, string x1, string y1, string x2, string y2) =>
        new(id, F(x1, y1, x2, y2), TerrainProfile.Wall);

    public static TerrainBlock Barricade(string id, string x1, string y1, string x2, string y2, TerrainKind kind = TerrainKind.Open) =>
        new(id, F(x1, y1, x2, y2), TerrainProfile.Cover, kind);

    public static TerrainBlock Platform(string id, string x1, string y1, string x2, string y2) =>
        new(id, F(x1, y1, x2, y2), TerrainProfile.Wall, TerrainKind.Open, SurfaceLevel.High);

    public static TerrainBlock Tower(string id, string x1, string y1, string x2, string y2) =>
        new(id, F(x1, y1, x2, y2), TerrainProfile.Wall, TerrainKind.Open, SurfaceLevel.High, walkable: false);

    public static TerrainBlock Rough(string id, string x1, string y1, string x2, string y2) =>
        new(id, F(x1, y1, x2, y2), TerrainProfile.Low, TerrainKind.Difficult);

    public static Battlefield Field(string size = "24", params TerrainBlock[] terrain) =>
        new(L(size), L(size), terrain, []);

    public static Battlefield Field(string size, IEnumerable<TerrainBlock> terrain, IEnumerable<DeploymentZone> zones) =>
        new(L(size), L(size), terrain, zones);

    public static Obstacle Enemy(ModelBody b) => new(b, Relation.Enemy);

    public static Obstacle Friend(ModelBody b) => new(b, Relation.Friendly);

    // Deterministic shuffle so order-independence tests are repeatable.
    public static List<T> Shuffled<T>(IEnumerable<T> items, int seed)
    {
        var list = items.ToList();
        var rng = new Random(seed);
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }
}
