using System.Reflection;
using Client.Core;
using Rules.Core.Events;
using Rules.Core.State;

namespace Client.Tests;

// QoL I1: a combat-log row points out exactly the models its event names: every model id the event carries (found
// here by reflection over the event's properties, so a new event field is not missed), no other, actor first.
public class EventModelsTests
{
    [Fact]
    public void EveryEventNamesExactlyTheModelsItCarries()
    {
        var kinds = new HashSet<string>();
        foreach (var (_, log) in ClientCoreTests.Matches.Value)
        {
            foreach (var e in log)
            {
                var carried = e.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.PropertyType == typeof(ModelId) || p.PropertyType == typeof(ModelId?))
                    .Select(p => p.GetValue(e)).OfType<ModelId>().Distinct().ToList();
                var named = EventModels.Of(e);
                Assert.Equal(carried.OrderBy(m => m.Value).ToList(), named.OrderBy(m => m.Value).ToList());
                if (named.Count > 0)
                {
                    kinds.Add(e.GetType().Name);
                    Assert.Equal(carried[0], named[0]);
                }
            }
        }
        foreach (var kind in new[] { nameof(ModelMoved), nameof(ActivationStarted), nameof(ChargeDeclared), nameof(MeleeAttackDeclared), nameof(InjuryRolled), nameof(RangedAttackDeclared) })
        {
            Assert.Contains(kind, kinds);
        }
    }
}
