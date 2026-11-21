using Client.Core;
using Rules.Core;
using Rules.Core.Events;
using Rules.Core.State;

namespace Client.Tests;

// XCOM-style UX, improvement 3: a model travels the path the server's event gives, and the path ends exactly where the
// server's state has the model after that event. Events that move nothing give no motion.
public class MotionTests
{
    [Fact]
    public void EveryMotionEndsWhereTheStateHasTheModel()
    {
        var kinds = new Dictionary<string, int>();
        foreach (var (_, log) in ClientCoreTests.Matches.Value)
        {
            var state = MatchState.None;
            foreach (var e in log)
            {
                var before = state;
                state = Reducer.Apply(state, e);
                if (Motions.Of(e) is not { } motion)
                {
                    Assert.False(e is ModelMoved || e is ChargeMoved { Path.IsEmpty: false } || e is RetreatResolved { Moved: true }, e.ToString());
                    continue;
                }
                Assert.NotEmpty(motion.Path);
                Assert.NotNull(before.FindModel(motion.Model)!.Position);
                var at = state.FindModel(motion.Model)!.Position!.Value;
                Assert.Equal(TablePoint.Of(at), motion.Path[^1]);
                kinds[e.GetType().Name] = kinds.GetValueOrDefault(e.GetType().Name) + 1;
            }
        }
        Assert.True(kinds.GetValueOrDefault(nameof(ModelMoved)) > 100 && kinds.GetValueOrDefault(nameof(ChargeMoved)) > 10, string.Join(", ", kinds));
    }
}
