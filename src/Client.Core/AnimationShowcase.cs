using Rules.Core.Geometry;
using Rules.Core.State;

namespace Client.Core;

// The animation showcase (Jason, 2026-11-22: "validate the animations visually"): staged scenes of the battlefield's
// animations, for a person to watch (Test Center, "Animation showcase") or for frame captures to be looked at. Each scene
// is the log lines the client would build from the server's events for that moment (the same shapes MatchController
// makes: a Motion ending where the server put the model, an Injury Roll's status change with the server's place, a
// blast's BattleFx), laid out by Choreography.Of and played through the real battlefield (BoardView.PlayForCheck): a shot
// that takes a model Down or Out of Action, a miss, a mine stopping a move and taking the model out on the spot, a charge
// that reaches and a blow, a charge that carries on, a grenade with its blast and gas, a scatter, overlapping blasts
// with fire, gas and shrapnel, a hot-seat seat switch mid-shot, the grenade again in Fast, the largest model going
// Down and standing back up, and a grenade putting two models Out of Action. Staged on the models of
// the table drawn now (nothing is sent; the server's state is never touched); display only.
public sealed record ShowcaseScene(
    string Title,
    string Watch,
    IReadOnlyList<LogLine> Lines,
    Func<ModelId, TablePoint?> DrawnAt,
    IReadOnlyList<ModelId> Hidden,
    IReadOnlyList<(ModelId Model, ModelStatus Status)> HoldAfter,
    bool SwitchSeat = false,
    AnimationSpeed? Speed = null)
{
    // Where the camera looks for this scene (null: on the actors), and from how far.
    public TablePoint? Focus { get; init; }

    public float FocusDistance { get; init; } = 8;

    // The scene's own speed (the Fast scene) unless the battlefield plays nothing (Off).
    public Choreography Plan(AnimationSpeed speed) => Choreography.Of(Lines, DrawnAt, speed == AnimationSpeed.Off ? speed : Speed ?? speed);
}

public static class AnimationShowcase
{
    public const double ChargeFrom = 4;
    public const double BlastRadius = 2;

    // The scenes on this table, or none when it has fewer than three models drawn. The actors: the two closest models on
    // the table (whichever seats, so the camera holds all of the action), a third model nearest the target.
    public static IReadOnlyList<ShowcaseScene> Scenes(BoardScene table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var models = table.Models;
        if (models.Count < 3)
        {
            return [];
        }
        static double Apart(ModelShape p, ModelShape q) => Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y));
        static double Apart2(TablePoint p, TablePoint q) => Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y));
        var pairs = models.SelectMany(p => models.Where(q => string.CompareOrdinal(p.Id.Value, q.Id.Value) < 0).Select(q => (p, q))).ToList();
        // The closest pair on the table, whichever seats (deployed sides stand far apart; a showcase wants both in view).
        var (a, b) = pairs.MinBy(x => Apart(x.p, x.q));
        var c = models.Where(m => m.Id != a.Id && m.Id != b.Id).MinBy(m => Apart(m, b))!;
        TablePoint At(ModelShape m) => new(m.X, m.Y);
        var (pa, pb, pc) = (At(a), At(b), At(c));
        var apart = Math.Max(0.01, Apart(a, b));
        // A point beyond a, away from b: where a charge or a move starts.
        var back = new TablePoint(a.X + (a.X - b.X) / apart * ChargeFrom, a.Y + (a.Y - b.Y) / apart * ChargeFrom);
        TablePoint? Drawn(ModelId id) => models.FirstOrDefault(m => m.Id == id) is { } m ? At(m) : null;
        TablePoint? MovedFromBack(ModelId id) => id == a.Id ? back : Drawn(id);
        var seq = 0;
        LogLine Line(IReadOnlyList<ModelId> involved, Motion? motion = null) => new(++seq, "", null, Motion: motion, Models: involved);
        LogLine Shot(bool hit, bool thrown = false) => Line([a.Id, b.Id]) with
        {
            Effects = [new(EffectKind.MuzzleFlash, a.Id), new(hit ? EffectKind.HitPuff : EffectKind.DirtPuff, b.Id)],
            Fx = thrown ? new BattleFx(true, null, [], []) : BattleFx.None,
        };
        LogLine Injury(ModelShape by, ModelShape on, ModelStatus? to, params MarkerStyle[] markers) => Line([by.Id, on.Id]) with
        {
            Changes = to is { } status ? [new StatusChange(on.Id, ModelStatus.Standing, status, At(on))] : [],
            Fx = markers.Length == 0 ? BattleFx.None : new BattleFx(false, null, [], [.. markers.Select(s => new MarkerCue(on.Id, s))]),
        };
        LogLine Blasts(params BlastCue[] blasts) => Line([a.Id]) with { Fx = new BattleFx(false, null, blasts, []) };

        var scenes = new List<ShowcaseScene>
        {
            new("A shot hits and takes its target Down",
                "turn to the target, flash, tracer, red puff and a flinch; the dice come with the hit; the target falls backwards away from the shooter and stays lying",
                [Shot(true), Injury(a, b, ModelStatus.Down)], Drawn, [], [(b.Id, ModelStatus.Down)]),
            new("A shot hits and puts its target Out of Action",
                "the casualty figure stands where the target stood, falls on the Injury Roll's beat, then sinks and dissolves with its base",
                [Shot(true), Injury(a, b, ModelStatus.OutOfAction)], Drawn, [b.Id], []),
            new("A shot misses",
                "the tracer ends in the ground beside the target with a dust puff; no flinch",
                [Shot(false)], Drawn, [], []),
        };
        seq = 0;
        scenes.Add(new("A mine stops a move and takes the model Out of Action on the spot",
            "the model turns along its way and walks in; it falls only where the server halted it, with the mine's column of earth and shrapnel; nothing jumps",
            [Line([a.Id], new Motion(a.Id, [pa])),
             Line([a.Id]) with
             {
                 Fx = new BattleFx(false, null, [new BlastCue(BlastStyle.Mine, pa, BattleEffectsTracker.MineBlastRadius)], [new MarkerCue(a.Id, MarkerStyle.Shrapnel)]),
                 Changes = [new StatusChange(a.Id, ModelStatus.Standing, ModelStatus.OutOfAction, pa)],
             }],
            MovedFromBack, [a.Id], []));
        seq = 0;
        scenes.Add(new("A charge reaches; the blow takes the target Down",
            "the charger turns, leans in and glides fast, drives into contact with an impact and a flinch; then a lunge, a strike, and the target falls",
            [Line([a.Id], new Motion(a.Id, [pa], b.Id, true)), Line([a.Id, b.Id]) with { Effects = [new(EffectKind.HitPuff, b.Id)] }, Injury(a, b, ModelStatus.Down)],
            MovedFromBack, [], [(b.Id, ModelStatus.Down)]));
        seq = 0;
        // The first leg ends in base contact with the target on the side it came from (a charge that reaches is in contact).
        var touch = a.Radius + b.Radius;
        var contact = new TablePoint(b.X + (back.X - b.X) / Math.Max(0.01, Apart2(back, pb)) * touch, b.Y + (back.Y - b.Y) / Math.Max(0.01, Apart2(back, pb)) * touch);
        scenes.Add(new("A charge carries on to a second enemy",
            "the first leg leans in and drives into contact with an impact; the carry-on turns to the second enemy and slides on without a new impact",
            [Line([a.Id], new Motion(a.Id, [contact], b.Id, true)), Line([a.Id], new Motion(a.Id, [pa], c.Id))],
            MovedFromBack, [], []));
        seq = 0;
        scenes.Add(new("A grenade: thrown, a blast, two caught in it",
            "an arc from the thrower's hand, the blast as it lands with its ring at the server's radius, then gas round each model with its Injury Roll; the target falls",
            [Shot(true, thrown: true), Blasts(new BlastCue(BlastStyle.Weapon, pb, BlastRadius)),
             Injury(a, b, ModelStatus.Down, MarkerStyle.Gas), Injury(a, c, null, MarkerStyle.Gas)],
            Drawn, [], [(b.Id, ModelStatus.Down)]));
        seq = 0;
        var landed = new TablePoint(pb.X + (pc.X - pb.X) * 0.6 + 1, pb.Y + (pc.Y - pb.Y) * 0.6 + 1);
        scenes.Add(new("A scattered shot lands off target",
            "the shot misses in dust; the round skims from the target to where it landed and explodes there; shrapnel on the model caught",
            [Shot(false), Blasts(new BlastCue(BlastStyle.Weapon, landed, BlastRadius, pb)), Injury(a, c, null, MarkerStyle.Shrapnel)],
            Drawn, [], []));
        seq = 0;
        scenes.Add(new("Overlapping blasts with fire, gas and shrapnel",
            "three explosions a moment apart (one redder and wider), rings spreading, then fire, gas and shrapnel bursts; every model stays visible through the smoke",
            [Blasts(new BlastCue(BlastStyle.Weapon, pb, BlastRadius), new BlastCue(BlastStyle.Weapon, pc, BlastRadius), new BlastCue(BlastStyle.Martyrdom, pa, BattleEffectsTracker.MartyrdomRadius)),
             Injury(a, b, ModelStatus.Down, MarkerStyle.Fire), Injury(a, c, null, MarkerStyle.Gas), Injury(b, a, null, MarkerStyle.Shrapnel)],
            Drawn, [], [(b.Id, ModelStatus.Down)]));
        seq = 0;
        scenes.Add(new("Hot-seat: the seat switches (F2) mid-shot",
            "the shot plays on to its end across the switch; nothing is cut short or shown twice; the banner names the seat now shown",
            [Shot(true), Injury(a, b, ModelStatus.Down)], Drawn, [], [(b.Id, ModelStatus.Down)], SwitchSeat: true));
        seq = 0;
        scenes.Add(new("Fast: the grenade again",
            "the same grenade in half the time; puffs, gas and smoke shorter too, nothing lingering into the next beat",
            [Shot(true, thrown: true), Blasts(new BlastCue(BlastStyle.Weapon, pb, BlastRadius)),
             Injury(a, b, ModelStatus.Down, MarkerStyle.Gas), Injury(a, c, null, MarkerStyle.Gas)],
            Drawn, [], [(b.Id, ModelStatus.Down)], Speed: AnimationSpeed.Fast));
        seq = 0;
        // The largest model on the table (a bigger figure falls and rises over its own height and base), shot by the
        // nearest other model.
        var big = models.MaxBy(m => (m.Radius, m.Standing))!;
        var by = models.Where(m => m.Id != big.Id).MinBy(m => Apart(m, big))!;
        scenes.Add(new("The largest model goes Down, then stands back up",
            "a bigger figure tips back over its feet and settles lying on its base, then rises back to standing; no jump between the fall and the rise",
            [Line([by.Id, big.Id]) with { Effects = [new(EffectKind.MuzzleFlash, by.Id), new(EffectKind.HitPuff, big.Id)] },
             Injury(by, big, ModelStatus.Down),
             Line([big.Id]) with { Changes = [new StatusChange(big.Id, ModelStatus.Down, ModelStatus.Standing, At(big))] }],
            Drawn, [], []));
        seq = 0;
        // 2026-11-24 ("the impact influences how far they ragdoll"): a grenade lands on the target; it and the model
        // nearest it go Out of Action, the one at the centre thrown harder and further than the one at the edge.
        scenes.Add(new("Two models put Out of Action by one grenade",
            "the blast lands on the target: both go limp at the blast itself, no pause; the one at its centre is thrown up and further than the one at the edge",
            [Shot(true, thrown: true), Blasts(new BlastCue(BlastStyle.Weapon, pb, Math.Max(BlastRadius, Apart(b, c) + 0.3))),
             Injury(a, b, ModelStatus.OutOfAction, MarkerStyle.Shrapnel), Injury(a, c, ModelStatus.OutOfAction, MarkerStyle.Shrapnel)],
            Drawn, [b.Id, c.Id], []));
        seq = 0;
        // Animation suite (2026-11-25): the ACTIONS a model takes, one after another, each from its event: taking aim,
        // firing (with recoil), working a RELOAD weapon, digging in, tending a friend, and a rally.
        LogLine Act(ModelShape who, ActionStyle style, ModelShape? friend = null) =>
            Line([who.Id]) with { Fx = BattleFx.None with { Action = new ActionCue(who.Id, style, friend?.Id) } };
        scenes.Add(new("Actions: aim, fire, reload, dig in, tend a friend, rally",
            "the shooter crouches a little and leans into its aim, fires with a small kick back, braces again to reload; "
                + "the next model crouches low and digs with dust at its feet; it turns to a friend and leans to it; then straightens up to rally; each settles exactly as it stood",
            [Act(a, ActionStyle.Aim), Shot(false), Act(a, ActionStyle.Reload), Act(c, ActionStyle.Fortify), Act(c, ActionStyle.Treat, b), Act(c, ActionStyle.Rally)],
            Drawn, [], []));
        scenes.AddRange(TerrainScenes(table, b, pb, Line));
        return scenes;
    }

    public const double TerrainBlastRadius = 1.2;

    // Ragdoll terrain (2026-11-25): for each kind of terrain drawn on this table, a body thrown at it by a blast beside it:
    // against a wall's side, over a sandbag ring, into a trench, against a tower, and off a tower's platform edge. The
    // casualty is staged at a point by the piece (its Injury Roll names that place, as the server's last place for a model
    // does), the model it stands for hidden; the blast goes off on the far side, so it is thrown at the terrain.
    private static IEnumerable<ShowcaseScene> TerrainScenes(BoardScene table, ModelShape victim, TablePoint near, Func<IReadOnlyList<ModelId>, Motion?, LogLine> line)
    {
        // The point of a piece nearest a spot (over its parts), and the way from the spot to it.
        static (TablePoint At, double X, double Y) Nearest(TerrainShape piece, TablePoint from)
        {
            var best = piece.Parts.Select(r => new TablePoint(Math.Clamp(from.X, r.X0, r.X1), Math.Clamp(from.Y, r.Y0, r.Y1)))
                .MinBy(q => (q.X - from.X) * (q.X - from.X) + (q.Y - from.Y) * (q.Y - from.Y));
            var (dx, dy) = (best.X - from.X, best.Y - from.Y);
            var d = Math.Sqrt(dx * dx + dy * dy);
            return d < 1e-6 ? (best, 1, 0) : (best, dx / d, dy / d);
        }
        // Out: how far its base's edge stands outside the piece (negative: its base reaches over the edge, at a trench's lip,
        // or stands that far inside it, on a platform); Blast: how far the blast goes off from it, away from the piece.
        var kinds = new (string Title, string Watch, Func<TerrainShape, bool> Is, double Out, double Blast)[]
        {
            ("A body thrown against a wall", "the blast is on the open side: the body is thrown at the wall, stops against its side and slides down it; nothing goes into the stone",
                t => t.Profile == TerrainProfile.Wall && !t.Tower && !t.Trench, 0.1, 0.9),
            ("A body thrown over sandbags", "thrown at the sandbag ring: it lands draped over the bags, lying on their tops, not sunk into them",
                t => t.Profile == TerrainProfile.Cover && !t.Trench, 0.1, 0.9),
            ("A body thrown into a trench", "at the trench's edge, thrown towards it: it tips over the lip and drops onto the trench floor, its blood there",
                t => t.Trench, -0.1, 0.9),
            ("A body thrown against a tower", "thrown at the tower's side: it stops against the timbers and slides down; nothing passes into the tower",
                t => t.Tower, 0.1, 0.9),
            ("A body thrown off a tower's platform", "standing on the platform by its edge, thrown outwards: it goes over the edge and falls to the ground below",
                t => t.Tower, -0.05, -0.9),
        };
        var seq = 0;
        foreach (var (title, watch, isKind, outside, blast) in kinds)
        {
            if (table.Terrain.Where(isKind).MinBy(t => Nearest(t, near) is var n ? (n.At.X - near.X) * (n.At.X - near.X) + (n.At.Y - near.Y) * (n.At.Y - near.Y) : 0) is not { } piece)
            {
                continue;
            }
            var (edge, nx, ny) = Nearest(piece, near);
            // Outside the piece (a positive offset) towards the spot, or (negative) inside it by its edge.
            var off = outside >= 0 || blast > 0 ? victim.Radius + outside : -(victim.Radius - outside);
            var at = new TablePoint(edge.X - nx * off, edge.Y - ny * off);
            var centre = new TablePoint(at.X - nx * blast, at.Y - ny * blast);
            seq = 0;
            LogLine Next(IReadOnlyList<ModelId> involved) => line(involved, null) with { Seq = ++seq };
            yield return new ShowcaseScene(title, watch,
                [Next([victim.Id]) with { Fx = new BattleFx(false, null, [new BlastCue(BlastStyle.Weapon, centre, TerrainBlastRadius)], []) },
                 Next([victim.Id]) with
                 {
                     Changes = [new StatusChange(victim.Id, ModelStatus.Standing, ModelStatus.OutOfAction, at)],
                     Fx = new BattleFx(false, null, [], [new MarkerCue(victim.Id, MarkerStyle.Shrapnel)]),
                 }],
                id => table.Models.FirstOrDefault(m => m.Id == id) is { } m ? new TablePoint(m.X, m.Y) : null, [victim.Id], [])
            {
                Focus = at,
                FocusDistance = 7,
            };
        }
    }
}
