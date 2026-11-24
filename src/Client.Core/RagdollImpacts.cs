using Rules.Core.State;

namespace Client.Core;

// The last hit a model took in an update (Jason, 2026-11-24: "the impact influences how far they ragdoll; seamless, no
// hesitation"): when it landed (seconds into the update's playback), which way it threw the model (away from the attacker
// or the blast's centre, on the table), how hard (a share of the figure's height per second: a shot is light, a blow in
// melee harder, a charge harder still, a blast hardest the closer it stood) and how much it lifted it (blasts, a mine
// most). A figure put Out of Action goes limp from this moment, thrown this way and this hard. Display only.
public readonly record struct RagdollImpact(double At, double DirX, double DirY, double Strength, double Lift)
{
    // The push for Ragdoll.Fall: across the table (X, Y on the table are X, Z in the world), and up.
    public Joint3 Push => new(DirX * Strength, Lift, DirY * Strength);
}

public static class RagdollImpacts
{
    public const double Shot = 0.5;
    public const double Blow = 0.8;
    public const double Charge = 1.0;

    // A fall with no hit in sight (a model that crumples): a light push, backwards.
    public const double Crumple = 0.5;

    // For every model hit in this update, its last hit. place: where a model stands (the last place drawn); a casualty's
    // own beat names the server's place for it (a mine halt), which is used first.
    public static IReadOnlyDictionary<ModelId, RagdollImpact> Of(Choreography plan, Func<ModelId, TablePoint?> place)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(place);
        var beats = plan.Beats;
        TablePoint? Where(ModelId id) =>
            beats.FirstOrDefault(b => b.Kind == BeatKind.Casualty && b.Model == id && b.At is not null)?.At ?? place(id);
        // Who could be hit: every model a beat of this update names as hit, falling or burnt.
        var hit = beats.Where(b => b.Kind is BeatKind.Flinch or BeatKind.Casualty or BeatKind.Fall or BeatKind.Shrapnel or BeatKind.Gas or BeatKind.Fire)
            .Select(b => b.Model).Distinct().ToList();
        var impacts = new Dictionary<ModelId, RagdollImpact>();
        void Take(ModelId id, RagdollImpact impact)
        {
            if (!impacts.TryGetValue(id, out var was) || impact.At >= was.At)
            {
                impacts[id] = impact;
            }
        }
        foreach (var beat in beats.OrderBy(b => b.Start))
        {
            switch (beat.Kind)
            {
                case BeatKind.Flinch when beat.Other is { } by:
                {
                    // The kind of attack from its event's other beats: a blow (a strike on it) or a charge striking home
                    // (an impact on it); else a shot.
                    var same = beats.Where(b => b.Seq == beat.Seq && b.Model == beat.Model).ToList();
                    var strength = same.Any(b => b.Kind == BeatKind.Impact) ? Charge : same.Any(b => b.Kind == BeatKind.Strike) ? Blow : Shot;
                    var (dx, dy) = Away(Where(beat.Model), Where(by), beat.Model);
                    Take(beat.Model, new RagdollImpact(beat.Start, dx, dy, strength, 0));
                    break;
                }
                case BeatKind.Explosion or BeatKind.MartyrBlast or BeatKind.MineBlast when beat.At is { } centre:
                {
                    var radius = Math.Max(0.5, beat.Size);
                    foreach (var id in hit)
                    {
                        if (Where(id) is not { } p)
                        {
                            continue;
                        }
                        var d = Math.Sqrt((p.X - centre.X) * (p.X - centre.X) + (p.Y - centre.Y) * (p.Y - centre.Y));
                        if (d > radius + 0.5)
                        {
                            continue;
                        }
                        var near = 1 - Math.Min(1, d / radius);
                        var (strength, lift) = beat.Kind switch
                        {
                            BeatKind.MineBlast => (0.8 + 0.8 * near, 0.9 + 0.6 * near),
                            BeatKind.MartyrBlast => (1.3 + 2.0 * near, 0.4 + 0.7 * near),
                            _ => (1.0 + 1.8 * near, 0.3 + 0.6 * near),
                        };
                        var (dx, dy) = Away(p, centre, id);
                        Take(id, new RagdollImpact(beat.Start, dx, dy, strength, lift));
                    }
                    break;
                }
            }
        }
        return impacts;
    }

    // The way from `from` to `at` (away from it), or, when they are the same place or unknown, a way of the model's own
    // (Spread on its id, so always the same).
    private static (double X, double Y) Away(TablePoint? at, TablePoint? from, ModelId id)
    {
        if (at is { } a && from is { } f)
        {
            var dx = a.X - f.X;
            var dy = a.Y - f.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (length > 0.05)
            {
                return (dx / length, dy / length);
            }
        }
        var angle = new Spread("impact:" + id.Value).Next() * 2 * Math.PI;
        return (Math.Cos(angle), Math.Sin(angle));
    }
}
