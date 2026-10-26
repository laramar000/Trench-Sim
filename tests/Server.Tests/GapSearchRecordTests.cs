using Match.Protocol;
using Rules.Core;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Server.Tests;

// Line-of-sight stage 2b-1 (2026-10-26): the gap search (HeightSight.WithGapSearch) is not used by the rules yet. Replay
// re-checks sight only for attacks that were made (Reducer: Ranged Attack, Charge, Melee Attack and free attack), and
// those were legal, so the nine rays saw the target and the gap search leaves their answer alone. This test plays whole
// matches through the server, as the golden records are made, and checks that at every attack in the record.
public class GapSearchRecordTests
{
    [Theory]
    [InlineData("ranged", 1)] // as golden/ranged-1.json
    [InlineData("melee", 3)] // as golden/melee-3.json
    [InlineData("ranged", 5)]
    [InlineData("melee-towers", 2)]
    [InlineData("ranged-towers", 4)]
    [InlineData("melee-2v2-trench-warfare", 1)]
    public void EveryRecordedAttackKeepsItsSightUnderTheGapSearch(string scenario, int seed)
    {
        var record = TestRecords.Completed(scenario, seed);
        var state = MatchState.None;
        var attacks = 0;
        foreach (var recorded in record.Events)
        {
            var pair = recorded.Event switch
            {
                RangedAttackDeclared e => (e.Attacker, e.Target),
                ChargeDeclared e => (e.Model, e.Target),
                MeleeAttackDeclared e => (e.Attacker, e.Target),
                _ => ((ModelId, ModelId)?)null,
            };
            if (pair is var (attackerId, targetId) && state.Setup?.Scenario is { } map)
            {
                // As Board.BodyOf builds a base: its point, its profile's radius and the elevation of that point.
                ModelBody BodyOf(ModelId id)
                {
                    var at = state.FindModel(id)!.Position!.Value;
                    return new ModelBody(id.Value, at, state.Setup!.ProfileOf(id)!.BaseRadius, Towers.ElevationAt(map.Battlefield, at));
                }
                var attacker = BodyOf(attackerId);
                var target = BodyOf(targetId);
                var nine = HeightSight.Between(map.Battlefield, attacker, target);
                var searched = HeightSight.WithGapSearch(map.Battlefield, attacker, target);
                Assert.NotEqual(Visibility.None, nine.Visibility);
                Assert.Equal(nine.Visibility, searched.Visibility);
                Assert.Empty(searched.Extra);
                attacks++;
            }
            state = Reducer.Apply(state, recorded.Event);
        }
        Assert.True(attacks > 0, $"{scenario} {seed}: no attack in the record");
    }
}
