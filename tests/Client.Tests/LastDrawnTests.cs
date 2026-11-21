using Client.Core;
using Rules.Core;
using Rules.Core.Events;
using Rules.Core.State;

namespace Client.Tests;

// Audit item R1: what the battlefield remembers of each model's last drawn place and shape stays within one match. A
// ranged match then a melee match (other warbands: other bases, types and places under the same ids a1, b1, ...) are
// drawn scene by scene as the battlefield draws them: within a match a model put Out of Action keeps the shape it was
// last drawn with; once a new match starts (BoardView.NewMatch clears it) nothing of the first match is remembered, not
// even under an id the second match also uses; without that clearing the first match's shapes would leak.
public class LastDrawnTests
{
    private static List<BoardScene> Scenes(List<GameEvent> log, int? upTo = null)
    {
        var scenes = new List<BoardScene>();
        var state = MatchState.None;
        foreach (var e in log.Take(upTo ?? log.Count))
        {
            state = Reducer.Apply(state, e);
            if (BoardScene.Of(state, Seat.One, ContentNames.None) is { } scene)
            {
                scenes.Add(scene);
            }
        }
        return scenes;
    }

    private static (List<GameEvent> Ranged, List<GameEvent> Melee) TwoMatches() =>
        (ClientCoreTests.Matches.Value.First(m => m.Scenario == "ranged").Log, ClientCoreTests.Matches.Value.First(m => m.Scenario == "melee").Log);

    [Fact]
    public void WithinAMatchAModelThatLeavesTheTableKeepsItsLastShape()
    {
        var memory = new LastDrawn();
        var seen = 0;
        foreach (var (_, log) in ClientCoreTests.Matches.Value.Take(10))
        {
            memory.Clear();
            BoardScene? previous = null;
            foreach (var scene in Scenes(log))
            {
                memory.Remember(scene);
                foreach (var gone in previous?.Models.Where(m => scene.Find(m.Id) is null) ?? [])
                {
                    Assert.True(memory.TryGet(gone.Id, out var kept));
                    Assert.Equal(gone, kept);
                    seen++;
                }
                previous = scene;
            }
        }
        Assert.True(seen > 0, "no model left the table in these matches");
    }

    [Fact]
    public void ANewMatchRemembersNothingOfTheLastOne()
    {
        var (ranged, melee) = TwoMatches();
        var first = Scenes(ranged);
        var memory = new LastDrawn();
        foreach (var scene in first)
        {
            memory.Remember(scene);
        }
        var fromFirst = memory.Models.ToDictionary(id => id, id => memory.TryGet(id, out var s) ? s : null);
        Assert.NotEmpty(fromFirst);

        // The second match's first scenes with models on the table: other warbands, so the shared ids have other shapes.
        var second = Scenes(melee).Where(s => s.Models.Count > 0).Take(3).ToList();
        Assert.NotEmpty(second);
        Assert.Contains(second[0].Models, m => fromFirst.TryGetValue(m.Id, out var old) && old is not null && (old.Radius != m.Radius || old.X != m.X || old.Y != m.Y || old.Type != m.Type));

        // Without the new match's clearing, the first match's models would still be remembered.
        var leaking = new LastDrawn();
        foreach (var scene in first.Concat(second))
        {
            leaking.Remember(scene);
        }
        var secondIds = second.SelectMany(s => s.Models).Select(m => m.Id).ToHashSet();
        Assert.Contains(leaking.Models, id => !secondIds.Contains(id));

        memory.Clear();
        Assert.Equal(0, memory.Count);
        Assert.False(memory.TryGet(fromFirst.Keys.First(), out _));
        foreach (var scene in second)
        {
            memory.Remember(scene);
        }
        // Exactly the second match's models, each as the second match last drew it.
        Assert.True(secondIds.SetEquals(memory.Models), $"remembered {string.Join(", ", memory.Models.Select(m => m.Value))}");
        foreach (var id in secondIds)
        {
            Assert.True(memory.TryGet(id, out var shape));
            Assert.Equal(second.Last(s => s.Find(id) is not null).Find(id), shape);
        }
    }

    [Fact]
    public void NoSceneRemembersNothingAndClearingTwiceIsHarmless()
    {
        var memory = new LastDrawn();
        memory.Remember(null);
        Assert.Equal(0, memory.Count);
        memory.Clear();
        memory.Clear();
        Assert.Empty(memory.Models);
    }
}
