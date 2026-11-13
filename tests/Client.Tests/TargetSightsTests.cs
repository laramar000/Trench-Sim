using Client.Core;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Client.Tests;

// Line-of-sight stage L4: the target icons are the rules' own sight and cover, for every enemy, in the target modes only.
public class TargetSightsTests
{
    private static List<MatchState> States => PreviewAgreementTests.States.Value;

    private static ModelBody Body(MatchState state, ModelState m) =>
        new(m.Id.Value, m.Position!.Value, state.Setup!.ProfileOf(m.Id)!.BaseRadius, Towers.ElevationAt(state.Setup.Scenario!.Battlefield, m.Position.Value));

    [Fact]
    public void EveryIconIsTheRulesAnswer()
    {
        var marks = new HashSet<SightMark>();
        var covered = 0;
        foreach (var state in States.Where(s => s.Phase == Phase.Activation && s.ActiveModel is not null).Take(400))
        {
            var actor = state.FindModel(state.ActiveModel!.Value)!;
            var sights = TargetSights.Of(state, actor.Id);
            var enemies = state.Models.Where(m => m.Seat != actor.Seat && m.Position is not null).OrderBy(m => m.Id.Value, StringComparer.Ordinal).ToList();
            Assert.Equal(enemies.Select(e => e.Id), sights.Select(s => s.Model));
            foreach (var (enemy, sight) in enemies.Zip(sights))
            {
                var cover = Cover.Of(state.Setup!.Scenario!.Battlefield, Body(state, actor), Body(state, enemy));
                var want = cover.Sight.Visibility switch { Visibility.Full => SightMark.Clear, Visibility.Partial => SightMark.Partial, _ => SightMark.Blocked };
                Assert.Equal(new TargetSight(enemy.Id, want, cover.InCover, cover.Sight.Visible, cover.Sight.Total), sight);
                marks.Add(sight.Mark);
                covered += sight.Cover ? 1 : 0;
            }
        }
        Assert.True(marks.Count == 3 && covered > 0, $"marks seen: {string.Join(", ", marks)}; in cover {covered}");
    }

    [Fact]
    public void IconsAppearOnlyInTheTargetModes()
    {
        var state = States.First(s => s.Phase == Phase.Activation && s.Pending is null && s.Actions == ActionsTaken.None && s.ActiveModel is { } a
            && s.FindModel(a)!.Status == ModelStatus.Standing && s.Setup!.ProfileOf(a)!.HasRangedWeapon);
        var seat = state.FindModel(state.ActiveModel!.Value)!.Seat;
        var interaction = new Interaction();
        interaction.Update(state, seat);
        Assert.Empty(interaction.TargetSights());
        Assert.True(interaction.Start(InteractionMode.Shoot));
        Assert.Equal(TargetSights.Of(state, state.ActiveModel!.Value), interaction.TargetSights());
        interaction.Cancel();
        Assert.Empty(interaction.TargetSights());
    }

    [Fact]
    public void TheWordsAndIconsForEachMark()
    {
        Assert.Equal("seen", TargetSights.Words(new(new("b1"), SightMark.Clear, false, 9, 9)));
        Assert.Equal("partly seen, in cover", TargetSights.Words(new(new("b1"), SightMark.Partial, true, 3, 9)));
        Assert.Equal("hidden", TargetSights.Words(new(new("b1"), SightMark.Blocked, false, 0, 9)));
        foreach (var icon in new[] { SightIcons.Eye, SightIcons.HalfEye, SightIcons.StruckEye, SightIcons.Shield })
        {
            Assert.Equal(SightIcons.Size, icon.Length);
            Assert.All(icon, row => Assert.Equal(SightIcons.Size, row.Length));
            Assert.All(icon, row => Assert.All(row, c => Assert.Contains(c, ".#X")));
        }
        Assert.NotEqual(SightIcons.Eye, SightIcons.StruckEye);
        Assert.Same(SightIcons.HalfEye, SightIcons.For(SightMark.Partial));
    }
}

// Line-of-sight stage L6: while choosing a destination, the icons say how the model would see each enemy from there.
public class DestinationSightsTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static List<MatchState> States => PreviewAgreementTests.States.Value;

    private static Point2 PointOf(TablePoint p) =>
        new(Length.FromThousandths((long)Math.Round(p.X * 1000)), Length.FromThousandths((long)Math.Round(p.Y * 1000)));

    [Fact]
    public void TheIconsAtTheEndOfAnAcceptedPathAreTheRulesSightFromThere()
    {
        var looked = 0;
        foreach (var state in States.Where(s => s.Phase == Phase.Activation && s.Pending is null && s.Actions == ActionsTaken.None && s.ActiveModel is { } a
                     && s.FindModel(a)!.Status == ModelStatus.Standing).Take(60))
        {
            var seat = state.FindModel(state.ActiveModel!.Value)!.Seat;
            var interaction = new Interaction();
            interaction.Update(state, seat);
            Assert.Empty(interaction.DestinationSights());
            if (!interaction.Start(InteractionMode.Move))
            {
                continue;
            }
            Assert.Empty(interaction.DestinationSights()); // no path yet
            foreach (var (dx, dy) in new[] { (2, 0), (-2, 0), (0, 2), (0, -2) })
            {
                interaction.MoveCursor(dx, dy);
                var preview = interaction.Preview;
                var before = preview;
                var sights = interaction.DestinationSights();
                if (preview is not { Legal: true })
                {
                    Assert.Empty(sights);
                    interaction.MoveCursor(-dx, -dy);
                    continue;
                }
                Assert.Equal(TargetSights.Of(state, state.ActiveModel!.Value, PointOf(preview.Path[^1].At)), sights);
                Assert.Same(before, interaction.Preview); // asking changes nothing about the move
                looked++;
                interaction.MoveCursor(-dx, -dy);
            }
            interaction.Cancel();
            Assert.Empty(interaction.DestinationSights());
        }
        Assert.True(looked > 20, $"only {looked} accepted paths looked at");
    }

    [Fact]
    public void ItIsQuickEnoughToFollowThePointer()
    {
        var state = States.Last(s => s.Phase == Phase.Activation && s.ActiveModel is not null);
        var actor = state.ActiveModel!.Value;
        var at = state.FindModel(actor)!.Position!.Value;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var n = 0;
        for (var x = 0; x < 20; x++)
        {
            for (var y = 0; y < 20; y++)
            {
                TargetSights.Of(state, actor, new Point2(at.X + Length.FromThousandths(x * 50), at.Y + Length.FromThousandths(y * 50)));
                n++;
            }
        }
        watch.Stop();
        output.WriteLine($"{n} destinations, {state.Models.Count(m => m.Position is not null)} models on the table: {watch.Elapsed.TotalMilliseconds / n:0.000} ms each");
        // Generous bound: the pointer is sampled every 80 ms (BoardView.HoverSeconds).
        Assert.True(watch.Elapsed.TotalMilliseconds / n < 20, $"{watch.Elapsed.TotalMilliseconds / n:0.000} ms per destination");
    }
}
