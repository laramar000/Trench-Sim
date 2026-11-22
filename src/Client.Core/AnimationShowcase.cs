using Rules.Core.State;

namespace Client.Core;

// The animation showcase (Jason, 2026-11-22: "validate the animations visually"): staged scenes of the battlefield's
// animations, for a person to watch (Test Center, "Animation showcase") or for frame captures to be looked at. Each scene
// is the log lines the client would build from the server's events for that moment (the same shapes MatchController
// makes: a Motion ending where the server put the model, an Injury Roll's status change with the server's place, a
// blast's BattleFx), laid out by Choreography.Of and played through the real battlefield (BoardView.PlayForCheck): a shot
// that takes a model Down or Out of Action, a miss, a mine stopping a move and taking the model out on the spot, a charge
// that reaches and a blow, a charge that carries on, a grenade with its blast and gas, a scatter, overlapping blasts
// with fire, gas and shrapnel, a hot-seat seat switch mid-shot, and the grenade again in Fast. Staged on the models of
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
        return scenes;
    }
}
