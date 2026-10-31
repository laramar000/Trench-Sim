using System.Collections.Immutable;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.Scenarios;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;

namespace Rules.Tests.Match;

// Line-of-sight stage L3: Elevated position, +1 DICE to a Ranged Attack when the attacker's base is at least 3" higher
// than the target's (p. 43, U79).
public class ElevatedPositionTests
{
    private static TerrainBlock Roof(string x1, string y1, string x2, string y2) =>
        new("roof", F(x1, y1, x2, y2), TerrainProfile.Wall, TerrainKind.Open, SurfaceLevel.High, walkable: true);

    private static TerrainBlock TowerOf(string height) =>
        new("tower", F("2", "10", "5", "13"), TerrainProfile.Wall, TerrainKind.Open, SurfaceLevel.High, walkable: true)
        {
            Climb = new Climb(L(height), [new Ladder("tower-w", P("2", "11.5"))]),
        };

    [Fact]
    [Trait("Rule", "p. 43 Elevated position: at least 3\" higher, measured by the bases")]
    public void ExactlyThreeInchesHigherCountsAndLessDoesNot()
    {
        // A 3" roof (a Wall-high platform) is exactly 3" above the ground.
        var roof = Field("24", Roof("2", "10", "5", "13"));
        Assert.True(SightHeights.IsElevated(roof, Body("a", "3.5", "11.5"), Body("t", "12", "11.5")));
        // A 2.999" tower is a thousandth short.
        var tower = Field("24", TowerOf("2.999"));
        Assert.False(SightHeights.IsElevated(tower, new ModelBody("a", P("3.5", "11.5"), L("0.5"), L("2.999")), Body("t", "12", "11.5")));
        // Looking up never counts, and two models at one height do not either.
        Assert.False(SightHeights.IsElevated(roof, Body("t", "12", "11.5"), Body("a", "3.5", "11.5")));
        Assert.False(SightHeights.IsElevated(Field(), Body("a", "3.5", "11.5"), Body("t", "12", "11.5")));
    }

    private static BoardPlay OnRoof()
    {
        var template = ClaimNoMansLand.Create();
        var field = template.Battlefield;
        var scenario = new Scenario(
            new Battlefield(field.Width, field.Height, field.Terrain.Add(Roof("2", "0", "6", "4")), field.Zones),
            template.ObjectiveIds);
        var play = new BoardPlay(scenario: scenario).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), "12", "2");
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    [Fact]
    [Trait("Rule", "p. 43: Elevated position is +1 DICE on the Success Roll")]
    public void AShotFromThreeInchesUpRollsOneBonusDie()
    {
        var play = OnRoof();
        var result = play.Do(new Shoot(Seat.One, A(1), B(1)), 4, 3, 1, 1, 1);
        var declared = result.Events.OfType<RangedAttackDeclared>().Single();
        Assert.True(declared.Elevated);
        Assert.Equal(1, declared.BonusDice);
        Assert.Equal(0, declared.PenaltyDice);
        var roll = result.Events.OfType<RangedSuccessRolled>().Single().Check;
        Assert.Equal(1, roll.NetDice);
        Assert.Equal(3, roll.Dice.Rolled.Count);
        Assert.Equal(7, roll.Dice.Total); // the highest two of 4, 3, 1
    }

    [Fact]
    [Trait("Rule", "p. 43: the bonus follows the positions; a declaration that disagrees is refused by the reducer")]
    public void TheReducerRefusesADeclarationWhoseElevationDisagrees()
    {
        var play = OnRoof();
        var honest = TurnSequence.Decide(play.State, new Shoot(Seat.One, A(1), B(1)), new FixedDice(4, 3, 1, 1, 1));
        var declared = honest.Events.OfType<RangedAttackDeclared>().Single();
        var lie = new RangedAttackDeclared(declared.Attacker, declared.Target, declared.LongRange, declared.InCover, declared.CoverTerrainId,
            declared.AttackerLevel, declared.TargetLevel, declared.PenaltyDice) { IntoMelee = declared.IntoMelee, Elevated = false };
        Assert.ThrowsAny<Exception>(() => Reducer.Apply(play.State, lie));
    }

    [Fact]
    public void AShotOnTheGroundHasNoBonus()
    {
        var play = new BoardPlay().ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), "10", "2");
        play.Do(new ActivateModel(Seat.One, A(1)));
        var declared = play.Do(new Shoot(Seat.One, A(1), B(1)), 4, 3, 1, 1).Events.OfType<RangedAttackDeclared>().Single();
        Assert.False(declared.Elevated);
        Assert.Equal(0, declared.BonusDice);
    }
}
