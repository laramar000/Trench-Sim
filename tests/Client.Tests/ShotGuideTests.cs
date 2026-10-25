using Client.Core;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Client.Tests;

// Line-of-sight stage L5: the shot guide pictures the rules' own rays, and every Shoot preview says what the server will
// declare (plan test 11).
public class ShotGuideTests
{
    private static List<MatchState> States => PreviewAgreementTests.States.Value;

    private static PreviewRay Ray(bool clear, double h = 0.7) =>
        new(new TablePoint(0, 0), new TablePoint(10, 0), clear ? null : "wall", 1.1, h);

    private static BoardScene Scene() =>
        new(24, 24, [], [new TerrainShape("wall", new TableRect(4, -1, 5, 1), TerrainProfile.Wall, 3, false, false, "wall")], []);

    [Fact]
    public void TheShotLineIsSolidUntilItEntersTheBlockingPiece()
    {
        var rays = Enumerable.Range(0, 9).Select(i => Ray(i != 4)).ToList();
        var line = ShotGuide.Line(rays, Scene());
        Assert.Equal(2, line.Count);
        Assert.True(line[0].Clear);
        Assert.False(line[1].Clear);
        Assert.Equal(4, line[0].To.X, 6);
        Assert.Equal(1.1 - 0.4 * 0.4, line[0].ToHeight, 6);
        Assert.Equal(new TablePoint(10, 0), line[1].To);

        var clear = ShotGuide.Line([.. Enumerable.Range(0, 9).Select(_ => Ray(true))], Scene());
        Assert.True(Assert.Single(clear).Clear);
        Assert.Empty(ShotGuide.Line([Ray(true)], Scene()));
    }

    [Fact]
    public void TheFigureAndItsWordsShowWhichPointsAreSeen()
    {
        // Legs hidden, body: left seen only, head all seen.
        bool[] seen = [false, false, false, true, false, false, true, true, true];
        var rays = seen.Select(s => Ray(s)).ToList();
        Assert.Equal("Guide: sight points seen, head 3 of 3, body 1 of 3, legs 0 of 3", ShotGuide.Words(rays));
        var figure = ShotGuide.Figure(rays);
        Assert.Equal(SightIcons.Size, figure.Length);
        Assert.Equal('#', figure[1][1]);   // head, left
        Assert.Equal('#', figure[6][1]);   // body, left
        Assert.Equal('X', figure[6][6]);   // body, middle
        Assert.Equal('X', figure[11][11]); // legs, right
        Assert.Equal('.', figure[0][0]);
        Assert.Null(ShotGuide.Words([Ray(true)]));
    }

    // Plan test 11: for every Shoot the previews accept, the server's declaration has exactly the preview's sight, cover,
    // long range and elevation, and the preview's rays are the rules' rays.
    [Fact]
    public void EveryLegalShootPreviewMatchesTheServersDeclaration()
    {
        var shots = 0;
        foreach (var state in States.Where(s => s.Phase == Phase.Activation && s.Pending is null && s.ActiveModel is not null))
        {
            var actor = state.FindModel(state.ActiveModel!.Value)!;
            foreach (var target in state.Models.Where(m => m.Seat != actor.Seat && m.Position is not null))
            {
                var preview = Previews.Target(state, actor.Seat, PreviewKind.Shoot, actor.Id, target.Id);
                var decided = TurnSequence.Decide(state, new Shoot(actor.Seat, actor.Id, target.Id), new RandomDice());
                Assert.Equal(preview.Legal, decided.IsAccepted);
                if (!preview.Legal)
                {
                    continue;
                }
                var declared = decided.Events.OfType<RangedAttackDeclared>().Single();
                Assert.Equal(declared.CoverTerrainId, preview.CoverId);
                Assert.Equal(declared.PenaltyDice, preview.Dice!.HitPenalty);
                Assert.Equal(declared.BonusDice, preview.Dice.HitBonus);
                Assert.Equal(declared.LongRange, preview.Facts.Any(f => f.StartsWith("Long range", StringComparison.Ordinal)));
                Assert.Equal(9, preview.Rays.Count);
                shots++;
            }
        }
        Assert.True(shots > 100, $"only {shots} legal shots checked");
    }

    [Fact]
    public void TheCardSaysRulesAndGuideApart()
    {
        var state = States.First(s => s.Phase == Phase.Activation && s.Pending is null && s.ActiveModel is { } a
            && s.Models.Any(m => m.Seat != s.FindModel(a)!.Seat && m.Position is not null
                && Previews.Target(s, s.FindModel(a)!.Seat, PreviewKind.Shoot, a, m.Id).Legal));
        var actor = state.FindModel(state.ActiveModel!.Value)!;
        var target = state.Models.First(m => m.Seat != actor.Seat && m.Position is not null && Previews.Target(state, actor.Seat, PreviewKind.Shoot, actor.Id, m.Id).Legal);
        var lines = PreviewCard.Lines(Previews.Target(state, actor.Seat, PreviewKind.Shoot, actor.Id, target.Id));
        var rules = lines.ToList().IndexOf(PreviewCard.RulesHeading);
        var guide = lines.ToList().FindIndex(l => l.StartsWith("Guide: ", StringComparison.Ordinal));
        Assert.True(rules > 0 && guide > rules, string.Join(" | ", lines));
    }
}
