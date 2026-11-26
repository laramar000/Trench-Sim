using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;
using static Rules.Tests.Geometry.Geo;
using static Rules.Tests.Match.BoardPlay;
using static Rules.Tests.Match.MeleePlay;

namespace Rules.Tests.Match;

// Roster stage R5f (U110): the COVER keyword (p. 54) gives a model the Cover and Defended Obstacle modifiers: always
// with a shield that has it, and with a Shovel while the model stands where it started the game on Open terrain (p. 85).
public class CoverKeywordTests
{
    private static readonly ModelProfile Shielded = Rifleman with { HasCover = true };

    // A1 (4, 2) activated; B1 at (10, 2) in the open.
    private static BoardPlay Range(Func<ModelId, ModelProfile> profiles)
    {
        var play = new BoardPlay(2, profiles).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), "10", "2");
        play.Do(new ActivateModel(Seat.One, A(1)));
        return play;
    }

    private static DecideResult Shot(BoardPlay play) => TurnSequence.Decide(play.State, new Shoot(Seat.One, A(1), B(1)), new FixedDice([1, 1, 1, 1, 1]));

    [Fact]
    [Trait("Rule", "COVER p. 54: a model with the keyword has the Cover modifier against Ranged Attacks")]
    public void AShieldWithCoverGivesCoverInTheOpen()
    {
        var shot = Shot(Range(id => id == B(1) ? Shielded : Rifleman));
        var declared = shot.Events.OfType<RangedAttackDeclared>().Single();
        Assert.Equal((true, TurnSequence.CoverKeyword, 1), (declared.InCover, declared.CoverTerrainId, declared.PenaltyDice));
        Assert.Equal(-1, shot.Events.OfType<RangedSuccessRolled>().Single().Check.NetDice);
        // Without it: no cover. IGNORE COVER ignores it; a Cloak makes it -2.
        Assert.False(Shot(Range(_ => Rifleman)).Events.OfType<RangedAttackDeclared>().Single().InCover);
        var ignoring = Shot(Range(id => id == B(1) ? Shielded : Rifleman with { RangedWeaponKeywords = WeaponKeywords.IgnoreCover }));
        Assert.Equal(0, ignoring.Events.OfType<RangedSuccessRolled>().Single().Check.NetDice);
        Assert.Equal(-2, Shot(Range(id => id == B(1) ? Shielded with { BlendsIn = true } : Rifleman)).Events.OfType<RangedSuccessRolled>().Single().Check.NetDice);
        // A forged declaration without the cover is refused.
        var play = Range(id => id == B(1) ? Shielded : Rifleman);
        Assert.Throws<InvalidEventException>(() => Reducer.Apply(play.State, new RangedAttackDeclared(A(1), B(1), false, false, null, declared.AttackerLevel, declared.TargetLevel, 0)));
    }

    [Fact]
    [Trait("Rule", "COVER p. 54: a piece of terrain that gives cover is named first; the keyword does not add a second die")]
    public void TerrainCoverIsNamedFirst()
    {
        // B1 against the south edge of the rubble obj-1 (as in RangedAttackTests): in cover.
        var play = new BoardPlay(2, id => id == B(1) ? Shielded : Rifleman).ToActivation();
        play.Put(A(1), "4", "6").Put(B(1), "12", "3.6");
        play.Do(new ActivateModel(Seat.One, A(1)));
        var declared = Shot(play).Events.OfType<RangedAttackDeclared>().Single();
        Assert.Equal(("obj-1", 1), (declared.CoverTerrainId, declared.PenaltyDice));
    }

    [Fact]
    [Trait("Rule", "COVER p. 54: a model with the keyword has the Defended Obstacle modifier against Melee Attacks")]
    public void CoverIsADefendedObstacleInMelee()
    {
        var play = new BoardPlay(2, id => id == B(1) ? Brawler with { HasCover = true } : Brawler).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), "5.1", "2");
        play.Do(new ActivateModel(Seat.One, A(1)));
        var result = play.Do(new FightModel(Seat.One, A(1), B(1)), 1, 1, 1);
        var declared = result.Events.OfType<MeleeAttackDeclared>().Single();
        Assert.Equal((TurnSequence.CoverKeyword, 1), (declared.DefendedObstacleId, declared.PenaltyDice));
        Assert.Equal(-1, result.Events.OfType<MeleeSuccessRolled>().Single().Check.NetDice);
    }

    // B1 has a Shovel; B models are set up at x 34, y 3 and 11; a Difficult patch may lie under B1's spot.
    private static BoardPlay Dug(bool mud)
    {
        var scenario = mud ? WithTerrain(new TerrainBlock("mud", F("33", "2", "35", "4"), TerrainProfile.Low, TerrainKind.Difficult)) : null;
        return new BoardPlay(2, id => id == B(1) ? Rifleman with { DigsIn = true } : Rifleman, scenario: scenario).ToActivation();
    }

    [Fact]
    [Trait("Rule", "Shovel p. 85: starting the game on Open terrain the model has COVER until it moves away from its starting position")]
    public void AShovelDigsInUntilTheModelMoves()
    {
        var play = Dug(mud: false);
        Assert.Equal(P("34", "3"), play.M(B(1)).DugInAt);
        Assert.True(TurnSequence.HasCover(play.State, B(1)));
        Assert.Contains("dugIn=", StateHash.Canonical(play.State), StringComparison.Ordinal);
        // A shot at it: cover.
        play.Put(A(1), "28", "3");
        play.Do(new ActivateModel(Seat.One, A(1)));
        var declared = TurnSequence.Decide(play.State, new Shoot(Seat.One, A(1), B(1)), new FixedDice([1, 1, 1, 1, 1])).Events.OfType<RangedAttackDeclared>().Single();
        Assert.Equal(TurnSequence.CoverKeyword, declared.CoverTerrainId);

        // Once it moves away it has it no more, even back where it started.
        var moved = Dug(mud: false);
        moved.Do(new ActivateModel(Seat.One, A(1)));
        moved.Do(new EndActivation(Seat.One));
        moved.Do(new ActivateModel(Seat.Two, B(1)));
        moved.Do(new MoveModel(Seat.Two, B(1), Path(("32", "3"), ("34", "3"))));
        Assert.Equal(P("34", "3"), moved.M(B(1)).Position);
        Assert.Null(moved.M(B(1)).DugInAt);
        Assert.False(TurnSequence.HasCover(moved.State, B(1)));

        // Set up on Difficult terrain: not dug in. Without a Shovel: never.
        Assert.Null(Dug(mud: true).M(B(1)).DugInAt);
        Assert.Null(Dug(mud: false).M(B(2)).DugInAt);
    }

    [Fact]
    [Trait("Rule", "Records: matches with these profiles play to the end and replay; the fields are hashed only when set")]
    public void SimulatedMatchesReplay()
    {
        var plain = StateHash.Canonical(Range(_ => Rifleman).State);
        Assert.DoesNotContain("cover=yes", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("digsIn", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("dugIn", plain, StringComparison.Ordinal);
        Assert.Contains("cover=yes", StateHash.Canonical(Range(_ => Shielded).State), StringComparison.Ordinal);
        var covered = 0;
        foreach (var path in new[] { Sim.Content.DefaultPath(), Sim.Content.MeleePath() })
        {
            var warbands = Sim.Content.Load(path).Select(w => w with
            {
                Models = w.Models.Select((m, i) => m with { Profile = m.Profile with { HasCover = i == 0, DigsIn = i % 2 == 1 } }).ToList(),
            }).ToList();
            for (var seed = 1; seed <= 8; seed++)
            {
                var run = Sim.Runner.Run(seed, warbands);
                Assert.True(run.Terminal, run.Status);
                Assert.Equal(run.FinalHash, StateHash.Compute(Reducer.Replay(run.Log)));
                covered += run.Log.OfType<RangedAttackDeclared>().Count(r => r.CoverTerrainId == TurnSequence.CoverKeyword)
                    + run.Log.OfType<MeleeAttackDeclared>().Count(m => m.DefendedObstacleId == TurnSequence.CoverKeyword);
            }
        }
        Assert.True(covered > 0);
    }
}
