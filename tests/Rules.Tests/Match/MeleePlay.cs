using Rules.Core;
using Rules.Core.Geometry;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;

namespace Rules.Tests.Match;

// Profiles and set-ups shared by the Slice B tests (Charge, Retreat, Fight, shooting into melee).
public static class MeleePlay
{
    // Movement 6", 1" base, a Melee Weapon and no ranged weapon.
    public static readonly ModelProfile Brawler = new(L("6"), L("0.5"), Length.Zero, hasMeleeWeapon: true);

    // The same, with an 18" ranged weapon as well.
    public static readonly ModelProfile Gunman = new(L("6"), L("0.5"), L("18"), hasMeleeWeapon: true);

    public static Func<ModelId, ModelProfile> Everyone(ModelProfile profile) => _ => profile;

    // A game with Brawlers on both sides, A1 at the given place and activated, B1 (and B2) wherever the test puts them.
    public static BoardPlay Skirmish(string ax, string ay, string bx, string by, ModelProfile? profile = null, int perSide = 2)
    {
        var play = new BoardPlay(perSide, Everyone(profile ?? Brawler)).ToActivation();
        play.Put(A(1), ax, ay).Put(B(1), bx, by);
        play.Do(new Rules.Core.Commands.ActivateModel(Seat.One, BoardPlay.A(1)));
        return play;
    }

    // Claim No Man's Land with extra pieces of terrain added to the table.
    public static Scenario WithTerrain(params TerrainBlock[] extra)
    {
        var plain = Rules.Core.Scenarios.ClaimNoMansLand.Create();
        var field = plain.Battlefield;
        var battlefield = new Battlefield(field.Width, field.Height, field.Terrain.Concat(extra), field.Zones);
        return new Scenario(battlefield, plain.ObjectiveIds);
    }

    // Like Skirmish, on a table with extra terrain. Impassable low wire stops a charge without blocking sight.
    public static BoardPlay SkirmishWith(Scenario scenario, string ax, string ay, string bx, string by, ModelProfile? profile = null, int perSide = 2)
    {
        var play = new BoardPlay(perSide, Everyone(profile ?? Brawler), scenario: scenario).ToActivation();
        play.Put(A(1), ax, ay).Put(B(1), bx, by);
        play.Do(new Rules.Core.Commands.ActivateModel(Seat.One, BoardPlay.A(1)));
        return play;
    }

    public static TerrainBlock Wire(string id, string x1, string y1, string x2, string y2) =>
        new(id, F(x1, y1, x2, y2), TerrainProfile.Low, TerrainKind.Impassable);

    public static ModelBody BodyOf(BoardPlay play, ModelId id) =>
        new(id.Value, play.M(id).Position!.Value, play.Setup.ProfileOf(id)!.BaseRadius);

    public static Length Gap(BoardPlay play, ModelId a, ModelId b) => Measure.Gap(BodyOf(play, a), BodyOf(play, b));

    public static bool Within(BoardPlay play, ModelId a, ModelId b, string inches) => Measure.IsWithin(BodyOf(play, a), BodyOf(play, b), L(inches));
}
