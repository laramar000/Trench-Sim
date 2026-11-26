using Client.Core;
using Rules.Core;
using Rules.Core.State;

namespace Client.Tests;

// The animation showcase's scenes, staged on a real table (a simulated match once at least three models are drawn), lay
// out through Choreography.Of exactly what each title promises, name only models on the table, and play nothing in Off.
public class AnimationShowcaseTests
{
    private static BoardScene Table()
    {
        var state = MatchState.None;
        foreach (var e in ClientCoreTests.Matches.Value.First(m => m.Scenario == "melee").Log)
        {
            state = Reducer.Apply(state, e);
            if (BoardScene.Of(state, Seat.One, ContentNames.None) is { Models.Count: >= 4 } scene)
            {
                return scene;
            }
        }
        throw new InvalidOperationException("no table with four models");
    }

    private static IReadOnlyList<ShowcaseScene> Scenes() => AnimationShowcase.Scenes(Table());

    private static List<BeatKind> Kinds(ShowcaseScene scene) => [.. scene.Plan(AnimationSpeed.Full).Beats.Select(b => b.Kind)];

    private static ShowcaseScene Titled(string start) => Scenes().Single(s => s.Title.StartsWith(start, StringComparison.Ordinal));

    [Fact]
    public void EighteenScenesEachNamingOnlyModelsOnTheTable()
    {
        var table = Table();
        var scenes = AnimationShowcase.Scenes(table);
        Assert.Equal(18, scenes.Count(s => s.Focus is null));
        Assert.Equal(scenes.Count, scenes.Select(s => s.Title).Distinct().Count());
        var ids = table.Models.Select(m => m.Id).ToHashSet();
        foreach (var scene in scenes)
        {
            var plan = scene.Plan(AnimationSpeed.Full);
            Assert.NotEmpty(plan.Beats);
            Assert.All(plan.Beats, b => Assert.Contains(b.Model, ids));
            Assert.All(plan.Beats.Where(b => b.Other is not null), b => Assert.Contains(b.Other!.Value, ids));
            Assert.All(scene.Hidden.Concat(scene.HoldAfter.Select(h => h.Model)), id => Assert.Contains(id, ids));
            Assert.InRange(plan.Length, 0.3, Choreography.MaxSeconds + 1e-9);
            // Off: no flash, puff, explosion, burst, fall or casualty (only instant turns).
            Assert.All(scene.Plan(AnimationSpeed.Off).Beats, b => Assert.Equal(BeatKind.Turn, b.Kind));
        }
    }

    [Fact]
    public void ShotsHitMissAndPutDownOrOutOfAction()
    {
        Assert.Superset(new HashSet<BeatKind> { BeatKind.Turn, BeatKind.MuzzleFlash, BeatKind.Tracer, BeatKind.HitPuff, BeatKind.Flinch, BeatKind.Fall }, Kinds(Titled("A shot hits and takes")).ToHashSet());
        var out_ = Titled("A shot hits and puts");
        var casualty = Assert.Single(out_.Plan(AnimationSpeed.Full).Beats, b => b.Kind == BeatKind.Casualty);
        Assert.True(casualty.Hit);
        Assert.Contains(casualty.Model, out_.Hidden);
        var miss = Kinds(Titled("A shot misses"));
        Assert.Contains(BeatKind.DirtPuff, miss);
        Assert.DoesNotContain(BeatKind.Flinch, miss);
    }

    [Fact]
    public void TheMineSceneWalksInThenFallsWhereItHalted()
    {
        var scene = Titled("A mine");
        var plan = scene.Plan(AnimationSpeed.Full);
        var glide = Assert.Single(plan.Beats, b => b.Kind == BeatKind.Glide);
        var casualty = Assert.Single(plan.Beats, b => b.Kind == BeatKind.Casualty);
        Assert.Equal(glide.Motion!.Path[^1], casualty.At);
        Assert.True(casualty.Start >= glide.End - 1e-9);
        Assert.Contains(plan.Beats, b => b.Kind == BeatKind.MineBlast && b.At == casualty.At);
        Assert.Contains(plan.Beats, b => b.Kind == BeatKind.Shrapnel);
        Assert.Equal([casualty.Model], scene.Hidden);
    }

    [Fact]
    public void ChargesReachAndCarryOn()
    {
        var reach = Kinds(Titled("A charge reaches"));
        Assert.Superset(new HashSet<BeatKind> { BeatKind.Glide, BeatKind.Lean, BeatKind.Lunge, BeatKind.Impact, BeatKind.Strike, BeatKind.Fall }, reach.ToHashSet());
        var carry = Titled("A charge carries on").Plan(AnimationSpeed.Full).Beats;
        var glides = carry.Where(b => b.Kind == BeatKind.Glide).ToList();
        Assert.Equal(2, glides.Count);
        Assert.NotEqual(glides[0].Other, glides[1].Other);
        Assert.Single(carry, b => b.Kind == BeatKind.Impact);
        // The first leg ends in base contact with its target, as a charge that reaches does.
        var table = Table();
        var (charger, target) = (table.Find(glides[0].Model)!, table.Find(glides[0].Other)!);
        var end = glides[0].Motion!.Path[^1];
        Assert.Equal(charger.Radius + target.Radius, Math.Sqrt((end.X - target.X) * (end.X - target.X) + (end.Y - target.Y) * (end.Y - target.Y)), 6);
        Assert.True(glides[1].Start >= glides[0].End - 1e-9);
    }

    [Fact]
    public void GrenadeScatterAndOverlappingBlasts()
    {
        var grenade = Kinds(Titled("A grenade"));
        Assert.Contains(BeatKind.Lob, grenade);
        Assert.DoesNotContain(BeatKind.Tracer, grenade);
        Assert.Contains(BeatKind.Explosion, grenade);
        Assert.Equal(2, grenade.Count(k => k == BeatKind.Gas));
        var scatter = Titled("A scattered").Plan(AnimationSpeed.Full).Beats;
        var flight = Assert.Single(scatter, b => b.Kind == BeatKind.Scatter);
        Assert.Contains(scatter, b => b.Kind == BeatKind.Explosion && b.At == flight.At && b.Start >= flight.End - 1e-9);
        var overlap = Titled("Overlapping").Plan(AnimationSpeed.Full).Beats;
        var blasts = overlap.Where(b => b.Kind is BeatKind.Explosion or BeatKind.MartyrBlast).OrderBy(b => b.Start).ToList();
        Assert.Equal(3, blasts.Count);
        Assert.Contains(blasts, b => b.Kind == BeatKind.MartyrBlast);
        Assert.Equal(Choreography.BlastStagger, blasts[1].Start - blasts[0].Start, 9);
        Assert.Superset(new HashSet<BeatKind> { BeatKind.Fire, BeatKind.Gas, BeatKind.Shrapnel }, overlap.Select(b => b.Kind).ToHashSet());
    }

    [Fact]
    public void TheSwitchSceneSwitchesAndTheFastSceneIsHalfTheGrenade()
    {
        Assert.True(Titled("Hot-seat").SwitchSeat);
        var fast = Titled("Fast");
        var grenade = Titled("A grenade");
        Assert.Equal(grenade.Plan(AnimationSpeed.Full).Length / 2, fast.Plan(AnimationSpeed.Full).Length, 9);
        Assert.Equal(Kinds(grenade), Kinds(fast));
    }

    [Fact]
    public void TheLargestModelFallsThenRises()
    {
        var table = Table();
        var scene = Titled("The largest model");
        var beats = scene.Plan(AnimationSpeed.Full).Beats;
        var fall = Assert.Single(beats, b => b.Kind == BeatKind.Fall);
        var rise = Assert.Single(beats, b => b.Kind == BeatKind.Rise);
        Assert.Equal(fall.Model, rise.Model);
        Assert.Equal(table.Models.Max(m => m.Radius), table.Find(fall.Model)!.Radius);
        Assert.True(rise.Start >= fall.End - 1e-9);
        Assert.Empty(scene.Hidden);
        Assert.Empty(scene.HoldAfter);
    }

    [Fact]
    public void AGrenadePutsTwoOutOfActionTheNearerThrownHarder()
    {
        var scene = Titled("Two models put");
        var plan = scene.Plan(AnimationSpeed.Full);
        var casualties = plan.Beats.Where(b => b.Kind == BeatKind.Casualty).ToList();
        Assert.Equal(2, casualties.Count);
        Assert.Equal(casualties.Select(c => c.Model).ToHashSet(), scene.Hidden.ToHashSet());
        var impacts = RagdollImpacts.Of(plan, scene.DrawnAt);
        var blast = plan.Beats.Single(b => b.Kind == BeatKind.Explosion);
        // Both are hit by the blast itself, before their Injury Rolls' beats.
        Assert.All(casualties, c => Assert.Equal(blast.Start, impacts[c.Model].At, 9));
        Assert.All(casualties, c => Assert.True(impacts[c.Model].At < c.Start));
        var (centre, edge) = (impacts[casualties[0].Model], impacts[casualties[1].Model]);
        Assert.True(centre.Strength > edge.Strength && centre.Lift > edge.Lift, $"{centre} / {edge}");
    }

    [Fact]
    public void ATableWithFewerThanThreeModelsHasNoScenes()
    {
        var table = Table();
        Assert.Empty(AnimationShowcase.Scenes(table with { Models = [.. table.Models.Take(2)] }));
    }

    // Ragdoll terrain (2026-11-25): a scene per kind of terrain on the table, each a blast on the open side throwing a
    // staged casualty at the piece (off a tower's platform: outwards from inside its edge).
    [Fact]
    public void TerrainScenesThrowABodyAtEachKindOfTerrain()
    {
        var table = Table();
        var cx = table.Models.Average(m => m.X);
        var cy = table.Models.Average(m => m.Y);
        var wall = new TerrainShape("wall", new TableRect(cx + 3, cy - 4, cx + 4, cy + 4), Rules.Core.Geometry.TerrainProfile.Wall, 3, false, false, "wall");
        var bags = new TerrainShape("bags", new TableRect(cx - 5, cy - 4, cx - 3, cy + 4), Rules.Core.Geometry.TerrainProfile.Cover, 0.75, false, false, "bags");
        var trench = new TerrainShape("trench", new TableRect(cx - 4, cy + 3, cx + 4, cy + 5), Rules.Core.Geometry.TerrainProfile.Low, 0, false, false, "trench") { Depth = 1 };
        var tower = new TerrainShape("tower", new TableRect(cx - 2, cy - 8, cx + 2, cy - 5), Rules.Core.Geometry.TerrainProfile.Wall, 3, true, false, "tower")
        {
            Ladders = [new LadderShape("l", new TablePoint(cx, cy - 5), Rules.Core.Geometry.Side.South)],
        };
        var scenes = AnimationShowcase.Scenes(table with { Terrain = [wall, bags, trench, tower] }).Where(s => s.Focus is not null).ToList();
        Assert.Equal(5, scenes.Count);
        foreach (var (scene, piece, inside) in new[] { (scenes[0], wall, false), (scenes[1], bags, false), (scenes[2], trench, false), (scenes[3], tower, false), (scenes[4], tower, true) })
        {
            var plan = scene.Plan(AnimationSpeed.Full);
            var blast = Assert.Single(plan.Beats, b => b.Kind == BeatKind.Explosion);
            var casualty = Assert.Single(plan.Beats, b => b.Kind == BeatKind.Casualty);
            var at = casualty.At!.Value;
            Assert.Equal(inside, piece.Holds(at.X, at.Y));
            Assert.Equal(scene.Focus, at);
            // Thrown at the piece (or, inside, out over its edge): a step on from the body along the blast's way crosses it.
            var (dx, dy) = (at.X - blast.At!.Value.X, at.Y - blast.At.Value.Y);
            var d = Math.Sqrt(dx * dx + dy * dy);
            Assert.InRange(d, 0.5, AnimationShowcase.TerrainBlastRadius);
            Assert.NotEqual(inside, piece.Holds(at.X + dx / d * 0.8, at.Y + dy / d * 0.8));
            Assert.Contains(casualty.Model, scene.Hidden);
        }
    }

    [Fact]
    public void TheActionsScenePlaysEachAction()
    {
        var kinds = Kinds(Titled("Actions:"));
        Assert.Superset(new HashSet<BeatKind> { BeatKind.Brace, BeatKind.Recoil, BeatKind.DigIn, BeatKind.Dust, BeatKind.Reach, BeatKind.Rally }, kinds.ToHashSet());
        Assert.Equal(2, kinds.Count(k => k == BeatKind.Brace));
    }

    [Fact]
    public void AWoundingShotReelsItsTarget()
    {
        var scene = Titled("A shot wounds");
        var wound = Assert.Single(scene.Plan(AnimationSpeed.Full).Beats, b => b.Kind == BeatKind.Wound);
        Assert.NotNull(wound.Other);
        Assert.DoesNotContain(scene.Plan(AnimationSpeed.Full).Beats, b => b.Kind is BeatKind.Fall or BeatKind.Casualty);
    }

    [Fact]
    public void APistolHitIsLighterThanAShotgunHit()
    {
        var wounds = Titled("A pistol hit").Plan(AnimationSpeed.Full).Beats.Where(b => b.Kind == BeatKind.Wound).ToList();
        Assert.Equal([HitPower.Light, HitPower.Heavy], wounds.Select(Choreography.PowerOf));
        Assert.True(wounds[0].Seconds < wounds[1].Seconds);
    }

    [Fact]
    public void AFlamethrowerSceneBurnsItsTarget()
    {
        var kinds = Kinds(Titled("A flamethrower"));
        Assert.Superset(new HashSet<BeatKind> { BeatKind.Flame, BeatKind.Fire, BeatKind.Wound }, kinds.ToHashSet());
        Assert.DoesNotContain(BeatKind.Tracer, kinds);
    }
}
