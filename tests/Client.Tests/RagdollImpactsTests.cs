using Client.Core;
using Rules.Core;
using Rules.Core.State;

namespace Client.Tests;

// 2026-11-24 ("the impact influences how far they ragdoll; no hesitation"): each staged hit of the animation showcase
// gives the model hit its last impact: when it landed (before the casualty's own beat, so the fall starts on the hit),
// away from the attacker or the blast, and how hard: a shot lighter than a blow, a blow lighter than a blast, a blast
// harder the closer it stood, a mine lifting it most.
public class RagdollImpactsTests
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

    private static (ShowcaseScene Scene, Choreography Plan, IReadOnlyDictionary<ModelId, RagdollImpact> Impacts) Staged(BoardScene table, string title)
    {
        var scene = AnimationShowcase.Scenes(table).Single(s => s.Title.StartsWith(title, StringComparison.Ordinal));
        var plan = scene.Plan(AnimationSpeed.Full);
        return (scene, plan, RagdollImpacts.Of(plan, scene.DrawnAt));
    }

    [Fact]
    public void AShotLandsBeforeTheCasualtyAndThrowsItAwayFromTheShooter()
    {
        var table = Table();
        var (scene, plan, impacts) = Staged(table, "A shot hits and puts");
        var casualty = plan.Beats.Single(b => b.Kind == BeatKind.Casualty);
        var impact = impacts[casualty.Model];
        Assert.Equal(RagdollImpacts.Shot, impact.Strength);
        Assert.Equal(0, impact.Lift);
        Assert.True(impact.At < casualty.Start, $"hit at {impact.At:0.00} s, casualty at {casualty.Start:0.00} s");
        Assert.Equal(plan.Beats.Single(b => b.Kind == BeatKind.Flinch).Start, impact.At, 9);
        var shooter = scene.DrawnAt(casualty.Other!.Value)!.Value;
        var target = scene.DrawnAt(casualty.Model)!.Value;
        Assert.True((target.X - shooter.X) * impact.DirX + (target.Y - shooter.Y) * impact.DirY > 0, "not thrown away from the shooter");
    }

    [Fact]
    public void ABlowIsHarderThanAShotAndABlastHarderStill()
    {
        var table = Table();
        var blow = Staged(table, "A charge reaches");
        var target = blow.Plan.Beats.Single(b => b.Kind == BeatKind.Fall).Model;
        Assert.Equal(RagdollImpacts.Blow, blow.Impacts[target].Strength);
        var blasts = Staged(table, "A grenade");
        var aimed = blasts.Plan.Beats.Single(b => b.Kind == BeatKind.Explosion);
        var closest = blasts.Impacts.OrderByDescending(i => i.Value.Strength).First();
        Assert.True(closest.Value.Strength > RagdollImpacts.Blow);
        Assert.True(closest.Value.Lift > 0);
        // Closer to the blast's centre, thrown harder and higher; outside its reach, not at all.
        var (near, far, outside) = (new ModelId("n1"), new ModelId("f1"), new ModelId("o1"));
        var places = new Dictionary<ModelId, TablePoint> { [near] = new(10.3, 10), [far] = new(11.8, 10), [outside] = new(14, 10) };
        var plan = new Choreography([],
        [
            new Beat(1, BeatKind.Explosion, 0.2, 0.9, near, At: new TablePoint(10, 10), Size: 2),
            new Beat(2, BeatKind.Gas, 0.5, 1.2, near), new Beat(3, BeatKind.Gas, 0.5, 1.2, far), new Beat(4, BeatKind.Gas, 0.5, 1.2, outside),
        ]);
        var hits = RagdollImpacts.Of(plan, id => places.TryGetValue(id, out var p) ? p : null);
        Assert.True(hits[near].Strength > hits[far].Strength && hits[near].Lift > hits[far].Lift, $"{hits[near]} / {hits[far]}");
        Assert.False(hits.ContainsKey(outside));
        Assert.True(hits[far].DirX > 0.99, "not thrown away from the centre");
        Assert.Equal(0.2, hits[near].At, 9);
    }

    [Fact]
    public void AMineLiftsItsCasualtyMost()
    {
        var (_, plan, impacts) = Staged(Table(), "A mine");
        var casualty = plan.Beats.Single(b => b.Kind == BeatKind.Casualty);
        var impact = impacts[casualty.Model];
        Assert.True(impact.Lift >= 0.9 && impact.Strength >= 0.8, $"{impact}");
        Assert.True(impact.At <= casualty.Start + 1e-9);
        Assert.Equal(1, Math.Sqrt(impact.DirX * impact.DirX + impact.DirY * impact.DirY), 6);
    }

    [Fact]
    public void AMissHitsNobody()
    {
        Assert.Empty(Staged(Table(), "A shot misses").Impacts);
    }
}
