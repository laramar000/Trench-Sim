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
    // Audit item T3: Find is the one place for "where is this model drawn": on the table now, else as last drawn. Played
    // in the battlefield's own order (each update remembers the table drawn so far, then draws the new one), it answers
    // exactly what the removed point-and-radius copy (_lastSeen) and the shape cache answered together: the same point,
    // height and base for every model either knew, and nothing for a model neither knew.
    [Fact]
    public void FindAnswersWhatTheTwoOldCopiesAnsweredTogether()
    {
        var checkedModels = 0;
        var offTable = 0;
        foreach (var (_, log) in ClientCoreTests.Matches.Value.Take(12))
        {
            var memory = new LastDrawn();
            var oldSeen = new Dictionary<ModelId, (double X, double Standing, double Y, double Radius)>();
            var oldShapes = new Dictionary<ModelId, ModelShape>();
            BoardScene? drawn = null;
            var ever = new HashSet<ModelId>();
            foreach (var scene in Scenes(log))
            {
                // The battlefield's order in Show: remember the table drawn so far, then draw the new one.
                memory.Remember(drawn);
                foreach (var m in drawn?.Models ?? [])
                {
                    oldSeen[m.Id] = (m.X, m.Standing, m.Y, m.Radius);
                    oldShapes[m.Id] = m;
                }
                drawn = scene;
                ever.UnionWith(scene.Models.Select(m => m.Id));
                foreach (var id in ever)
                {
                    var found = memory.Find(drawn, id);
                    // Old: Where took the table, else _lastSeen; falls and casualties took the table, else _lastShapes.
                    var oldWhere = drawn.Find(id) is { } t ? (t.X, t.Standing, t.Y, t.Radius) : oldSeen.TryGetValue(id, out var seen) ? seen : ((double, double, double, double)?)null;
                    var oldShape = drawn.Find(id) ?? (oldShapes.TryGetValue(id, out var s) ? s : null);
                    Assert.Equal(oldWhere, found is { } f ? (f.X, f.Standing, f.Y, f.Radius) : null);
                    Assert.Equal(oldShape, found);
                    offTable += found is not null && drawn.Find(id) is null ? 1 : 0;
                    checkedModels++;
                }
            }
        }
        Assert.True(offTable > 0, "no model was looked up after leaving the table");
        Assert.True(checkedModels > 1000, $"only {checkedModels} look-ups");
    }

    [Fact]
    public void FindTakesTheTableFirstAndForgetsTheLastMatchWhenCleared()
    {
        var (ranged, melee) = TwoMatches();
        var first = Scenes(ranged);
        var memory = new LastDrawn();
        foreach (var scene in first)
        {
            memory.Remember(scene);
        }
        var table = Scenes(melee).First(s => s.Models.Count > 0);
        // On the table: the table's shape, whatever was remembered under the same id.
        foreach (var model in table.Models)
        {
            Assert.Equal(model, memory.Find(table, model.Id));
        }
        // A new match: only what is on its table is found.
        memory.Clear();
        foreach (var id in first.SelectMany(s => s.Models).Select(m => m.Id).Distinct())
        {
            Assert.Equal(table.Find(id), memory.Find(table, id));
        }
        Assert.Null(memory.Find(null, table.Models[0].Id));
    }
}
