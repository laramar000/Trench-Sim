using Rules.Core.Geometry;
using Rules.Core.State;

namespace Sim;

// Facts that must hold after every accepted command, whatever the rules did. A violation fails the simulation.
public static class Invariants
{
    public static string? Check(MatchState before, MatchState after)
    {
        var setup = after.Setup!;
        var field = setup.Scenario!.Battlefield;
        var bodies = new List<ModelBody>();
        foreach (var m in after.Models)
        {
            if (m.BloodMarkers is < 0 or > BloodMarkers.Max)
            {
                return $"{m.Id} has {m.BloodMarkers} Blood Markers";
            }
            if (m.Status == ModelStatus.OutOfAction && m.Position is not null)
            {
                return $"{m.Id} is Out of Action but still on the table";
            }
            if (m.Status != ModelStatus.OutOfAction && m.Deployed && m.Position is null)
            {
                return $"{m.Id} is deployed and in play but has no position";
            }
            if (m.Position is { } at)
            {
                var body = new ModelBody(m.Id.Value, at, setup.ProfileOf(m.Id)!.BaseRadius);
                if (field.Area.Bounds.Min.X > at.X || field.Area.Bounds.Max.X < at.X || field.Area.Bounds.Min.Y > at.Y || field.Area.Bounds.Max.Y < at.Y)
                {
                    return $"{m.Id} is off the battlefield at {at}";
                }
                bodies.Add(body);
            }
            var previous = before.FindModel(m.Id);
            if (previous is not null && previous.Status == ModelStatus.OutOfAction && m.Status != ModelStatus.OutOfAction)
            {
                return $"{m.Id} came back from Out of Action";
            }
        }
        for (var i = 0; i < bodies.Count; i++)
        {
            for (var j = i + 1; j < bodies.Count; j++)
            {
                if (Measure.Overlap(bodies[i], bodies[j]))
                {
                    return $"{bodies[i].Id} and {bodies[j].Id} overlap";
                }
            }
        }
        if (after.InProgress is not null && after.Pending is null)
        {
            return "a roll is in progress with no decision pending";
        }
        if (after.One.VictoryPoints < before.One.VictoryPoints || after.Two.VictoryPoints < before.Two.VictoryPoints)
        {
            return "Victory Points went down";
        }
        if (after.Turn < before.Turn || after.Turn > setup.TurnLimit)
        {
            return $"turn {after.Turn} is out of order";
        }
        if (after.Phase == Phase.Ended && after.Outcome is null)
        {
            return "the match ended without an outcome";
        }
        return null;
    }
}
