using Rules.Core.State;

namespace Client.Core;

// Animation stage A1: how fast the battlefield plays what the server resolved. Off draws every result at once (models
// jump to the server's position, no effects); Fast plays everything in half the time.
public enum AnimationSpeed
{
    Full,
    Fast,
    Off,
}

public enum BeatKind
{
    Glide,
    MuzzleFlash,
    Tracer,
    HitPuff,
    DirtPuff,
    Flinch,

    // Animation stage A2: the attacker turns to face its target (Model turns towards Other; From is where it stands when
    // that is not where the server has it now, as at the start of a charge).
    Turn,

    // A short lunge at the target and back (Model lunges towards Other); the blow lands at its peak.
    Lunge,

    // A charger leans into its charge for the length of the glide and the drive into contact.
    Lean,

    // Where a blow lands: a brief pale flash between attacker (Other) and target (Model).
    Strike,

    // Where a charge strikes home: dust kicked up between charger (Other) and target (Model).
    Impact,

    // Animation stage A3: a model taken Down falls over backwards, away from the model that downed it (Other, when the
    // event names one); a model standing up rises the other way; a model put Out of Action falls (unless already Down),
    // then sinks and fades away.
    Fall,
    Rise,
    Casualty,

    // Animation stage A4: a grenade lobbed in an arc (Model throws at Other, or at At); an explosion at At, Size inches
    // across in radius (a weapon's BLAST, a Mine, a Martyrdom Device); a scattered attack's flight from where it was aimed
    // (From) to where it landed (At); dust where an attack at a point fell short (At); and the burst of a SHRAPNEL, GAS or
    // FIRE marker on a model.
    Lob,
    Explosion,
    MineBlast,
    MartyrBlast,
    Scatter,
    Dust,
    Shrapnel,
    Gas,
    Fire,

    // Animation suite (2026-11-25): the shooter's recoil as it fires (Model kicks back from Other, its target); an ACTION
    // a model takes: bracing (taking aim, working a RELOAD weapon: a crouch and lean), digging in (Fortify: a crouch with
    // spade strokes, dust at its feet), rallying (Enforced Orthodoxy: it straightens and rises), reaching to a friend it
    // tends (Treat, Laying on of Hands: Model leans to Other and back).
    Recoil,

    // Hit reactions (2026-11-26): a model that took a BLOOD MARKER and stays standing reels from the blow (Model reels away
    // from Other, the model that hurt it, when the event names one) and recovers its stance.
    Wound,
    Brace,
    DigIn,
    Rally,
    Reach,
}

// One thing the battlefield draws for an event, Start seconds after the update arrived, for Seconds. Model is the model it
// happens to (the shooter of a flash or tracer, the target of a puff or flinch, the model that glides); Other is the model
// at the far end (the target of a tracer, the attacker a flinch moves away from). Hit says whether the roll counted as
// passed (a tracer that hits ends on its target). Motion and From are a glide's path and the point it starts from. At is
// a point the beat happens at (a turn's or an attack's point, an explosion; audit item B2: a casualty's place, the server's
// last place for the model).
public sealed record Beat(int Seq, BeatKind Kind, double Start, double Seconds, ModelId Model, ModelId? Other = null, bool Hit = false, Motion? Motion = null, TablePoint? From = null,
    TablePoint? At = null, double Size = 0)
{
    public double End => Start + Seconds;

    // Beats that move or turn a model for a while (the battlefield poses it each frame) rather than draw an effect once.
    public bool PosesModel => Kind is BeatKind.Glide or BeatKind.Flinch or BeatKind.Turn or BeatKind.Lunge or BeatKind.Lean
        or BeatKind.Fall or BeatKind.Rise or BeatKind.Casualty or BeatKind.Recoil or BeatKind.Wound or BeatKind.Brace or BeatKind.DigIn or BeatKind.Rally or BeatKind.Reach;

    // Effects drawn once, that fade on their own (an update is not busy while they fade).
    public bool FadesOnItsOwn => Kind is BeatKind.HitPuff or BeatKind.DirtPuff or BeatKind.Strike or BeatKind.Impact
        or BeatKind.Explosion or BeatKind.MineBlast or BeatKind.MartyrBlast or BeatKind.Dust or BeatKind.Shrapnel or BeatKind.Gas or BeatKind.Fire;
}

// When an event's own line is shown (its dice, notes and sound), seconds after the update arrived. The combat log does not
// wait for this: it always shows every event as soon as it arrives.
public sealed record LineCue(LogLine Line, double At);

// Animation stage A1: what the battlefield plays for the new events of one update, in order: a model travels its path and
// what follows waits for it to arrive; a shot is a flash at the shooter, a tracer to the target, then the hit or the miss
// (a puff, and a flinch when it hit) with the roll's dice and notes. Stage A2: the attacker first turns to face its target;
// a blow in melee is a lunge landing at its peak with a strike; a charge turns, glides in faster leaning forward, and one
// that reaches drives into contact with an impact and a flinch (its line, with "REACHED", comes on the impact). Stage A3:
// a model taken Down falls with its line, one standing up rises, one put Out of Action falls (Hit: it was standing), sinks
// and fades; what follows waits for the fall. Everything comes from the events (the server's
// results, the rules' verdicts on the rolls as EffectCues reads them); nothing here decides or changes an outcome.
// Display only, and timing only: the state the battlefield and every check use is always the server's latest.
public sealed record Choreography(IReadOnlyList<LineCue> Lines, IReadOnlyList<Beat> Beats)
{
    public const double GlideInchesPerSecond = 24;
    public const double GlideMinSeconds = 0.25;
    public const double GlideMaxSeconds = 0.7;
    public const double FlashSeconds = 0.08;
    public const double TracerDelay = 0.04;
    public const double TracerSeconds = 0.12;
    public const double ImpactAt = TracerDelay + TracerSeconds;
    public const double HitPuffSeconds = 0.7;
    public const double DirtPuffSeconds = 0.9;
    public const double FlinchSeconds = 0.2;

    // The pause after a hit or a miss before the next event's line, so an Injury Roll is read after the hit it follows.
    public const double AfterImpact = 0.3;

    // Animation stage A2: facing, melee and charges. A turn comes before an attack (and is left out when the attacker
    // already faces that target in this update); a blow lands at the peak of the lunge; a charge glides faster with the
    // charger leaning in, and one that reaches drives into contact with an impact and a flinch.
    public const double TurnSeconds = 0.15;
    public const double LungeSeconds = 0.25;
    public const double LungePeak = 0.12;
    public const double StrikeSeconds = 0.15;
    public const double ImpactSeconds = 0.5;
    public const double ChargeInchesPerSecond = 36;
    public const double ChargeMinSeconds = 0.25;
    public const double ChargeMaxSeconds = 0.6;

    // Animation stage A3: a fall and a rise take FallSeconds; an Out of Action model then sinks and fades for SinkSeconds.
    // What follows waits for the fall (not the sink).
    public const double FallSeconds = 0.45;
    public const double SinkSeconds = 0.5;

    // Animation stage A4: a grenade's flight, a scattered attack's flight, the pause after an explosion before the Injury
    // Rolls it causes are read, and how long each effect shows (they fade on their own; what follows does not wait).
    public const double LobSeconds = 0.45;
    public const double ScatterSeconds = 0.3;
    public const double BlastHold = 0.35;
    public const double ThrownSettle = 0.05;
    public const double ExplosionSeconds = 0.9;
    public const double DustSeconds = 0.6;
    public const double ShrapnelSeconds = 0.35;
    public const double GasSeconds = 1.2;
    public const double FireSeconds = 0.7;

    // Animation suite (2026-11-25): a shooter's recoil, and how long each ACTION plays (what follows waits for it).
    public const double RecoilSeconds = 0.18;

    // Hit reactions (2026-11-26): how long a wounded model reels and recovers (what follows does not wait for it).
    public const double WoundSeconds = 0.5;
    public const double BraceSeconds = 0.5;
    public const double DigInSeconds = 0.7;
    public const double RallySeconds = 0.6;
    public const double ReachSeconds = 0.55;

    // Animation stage A5: several explosions of one event go off this far apart, so each can be read.
    public const double BlastStagger = 0.15;

    // A move's first leg shorter than this does not turn the model (a shuffle, not a march).
    public const double FaceTravelInches = 0.2;

    // One update plays for at most this long; a longer one is played faster to fit.
    public const double MaxSeconds = 2.5;

    // Audit item T1: the effects drawn for a set length rather than over their beat (a shot's flash, a hit's red puff, a
    // miss's dirt, a blow's sparks, a charge's impact dust) take that length times EffectScale. Full draws them as long
    // as ever (1, also in an update played faster to fit MaxSeconds), Fast as long as their beat now lasts (half, or less
    // in a long update), never under FastEffectFloor of it, so a hit still reads; Off plays no such beat.
    public const double FastEffectFloor = 0.35;

    // The length an effect beat is drawn for in Full, or null for a beat that is drawn over its own Seconds already.
    public static double? FullSeconds(BeatKind kind) => kind switch
    {
        BeatKind.MuzzleFlash => FlashSeconds,
        BeatKind.HitPuff => HitPuffSeconds,
        BeatKind.DirtPuff => DirtPuffSeconds,
        BeatKind.Strike => StrikeSeconds,
        BeatKind.Impact => ImpactSeconds,
        _ => null,
    };

    public static double EffectScale(Beat beat, AnimationSpeed speed)
    {
        ArgumentNullException.ThrowIfNull(beat);
        return speed != AnimationSpeed.Fast || FullSeconds(beat.Kind) is not { } full || full <= 0
            ? 1
            : Math.Clamp(beat.Seconds / full, FastEffectFloor, 1);
    }

    public static Choreography Empty { get; } = new([], []);

    // How long the update keeps the battlefield busy: until its last line is shown and its last model is back at rest
    // (puffs fade on their own and do not count).
    public double Length => Math.Max(Lines.Count == 0 ? 0 : Lines.Max(l => l.At),
        Beats.Count == 0 ? 0 : Beats.Max(b => b.FadesOnItsOwn ? b.Start : b.End));

    // drawnAt: where a model was drawn before this update (its centre, inches), or null when it was not on the table; a
    // move of a model that was not drawn is not shown travelling.
    public static Choreography Of(IReadOnlyList<LogLine> lines, Func<ModelId, TablePoint?> drawnAt, AnimationSpeed speed = AnimationSpeed.Full)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(drawnAt);
        if (speed == AnimationSpeed.Off)
        {
            // Stage A5: Off draws every result at once, and every model still ends facing the way it would with the
            // animation (an instant turn: the attacker to its target, a moving model along its way, a downed model to
            // the model that downed it), so Full, Fast and Off leave the same picture.
            return new([.. lines.Select(l => new LineCue(l, 0))], OffFacings(lines, drawnAt));
        }
        var cues = new List<LineCue>();
        var beats = new List<Beat>();
        // Where a model is after the moves already planned in this update (a charge that carries on starts where it ended).
        var reached = new Dictionary<ModelId, TablePoint>();
        // Which target each model faces after the turns already planned in this update.
        var faced = new Dictionary<ModelId, ModelId>();
        var t = 0.0;
        // A turn of the attacker towards the target, unless it already faces it; returns when the attack can start.
        double Turn(int seq, ModelId attacker, ModelId target, double at, bool hit, TablePoint? from = null)
        {
            if (faced.TryGetValue(attacker, out var facing) && facing == target)
            {
                return at;
            }
            faced[attacker] = target;
            beats.Add(new(seq, BeatKind.Turn, at, TurnSeconds, attacker, target, hit, From: from));
            return at + TurnSeconds;
        }
        foreach (var line in lines)
        {
            var start = line.Motion is { Path.Count: > 0 } moving
                ? reached.TryGetValue(moving.Model, out var was) ? was : drawnAt(moving.Model)
                : null;
            if (line.Motion is { } motion && start is { } from)
            {
                reached[motion.Model] = motion.Path[^1];
                var length = PathLength(from, motion.Path);
                if (length < 0.01)
                {
                    cues.Add(new(line, t));
                    continue;
                }
                if (motion.Charging is not { } target)
                {
                    var seconds = Math.Clamp(length / GlideInchesPerSecond, GlideMinSeconds, GlideMaxSeconds);
                    cues.Add(new(line, t));
                    // Stage A5: it turns to face its way as it sets off (no extra time), so it marches rather than slides.
                    if (TravelPoint(from, motion.Path) is { } way)
                    {
                        beats.Add(new(line.Seq, BeatKind.Turn, t, Math.Min(TurnSeconds, seconds), motion.Model, From: from, At: way));
                        faced.Remove(motion.Model);
                    }
                    beats.Add(new(line.Seq, BeatKind.Glide, t, seconds, motion.Model, Motion: motion, From: from));
                    t += seconds;
                    continue;
                }
                // A charge: turn to the target, glide in leaning forward, and (in contact) drive into it.
                var go = Turn(line.Seq, motion.Model, target, t, motion.Reached, from);
                var glide = Math.Clamp(length / ChargeInchesPerSecond, ChargeMinSeconds, ChargeMaxSeconds);
                beats.Add(new(line.Seq, BeatKind.Glide, go, glide, motion.Model, target, motion.Reached, motion, from));
                var arrive = go + glide;
                if (motion.Reached)
                {
                    var impact = arrive + LungePeak;
                    beats.Add(new(line.Seq, BeatKind.Lean, go, glide + LungeSeconds, motion.Model, target, true));
                    beats.Add(new(line.Seq, BeatKind.Lunge, arrive, LungeSeconds, motion.Model, target, true));
                    beats.Add(new(line.Seq, BeatKind.Impact, impact, ImpactSeconds, target, motion.Model, true));
                    beats.Add(new(line.Seq, BeatKind.Flinch, impact, FlinchSeconds, target, motion.Model, true));
                    cues.Add(new(line, impact));
                    t = impact + AfterImpact;
                }
                else
                {
                    beats.Add(new(line.Seq, BeatKind.Lean, go, glide, motion.Model, target));
                    cues.Add(new(line, arrive));
                    t = arrive;
                }
                continue;
            }
            // A caught-up event plays no effects (as before this stage); its line is shown in its place.
            if (line.Replayed || line.Effects.Count == 0)
            {
                if (line.Replayed)
                {
                    cues.Add(new(line, t));
                    continue;
                }
                // Animation suite: an ACTION a model takes (a friend tended: it turns to the friend first).
                if (line.Fx.Action is { } act)
                {
                    var go = act.Target is { } friend && friend != act.Model ? Turn(line.Seq, act.Model, friend, t, true) : t;
                    var (kind, seconds) = act.Style switch
                    {
                        ActionStyle.Fortify => (BeatKind.DigIn, DigInSeconds),
                        ActionStyle.Rally => (BeatKind.Rally, RallySeconds),
                        ActionStyle.Treat when act.Target is { } tended && tended != act.Model => (BeatKind.Reach, ReachSeconds),
                        ActionStyle.Treat => (BeatKind.Brace, BraceSeconds),
                        _ => (BeatKind.Brace, BraceSeconds),
                    };
                    beats.Add(new(line.Seq, kind, go, seconds, act.Model, act.Target is { } other && other != act.Model ? other : null, true));
                    if (kind == BeatKind.DigIn && drawnAt(act.Model) is { } feet)
                    {
                        beats.Add(new(line.Seq, BeatKind.Dust, go + seconds * 0.3, DustSeconds, act.Model, At: feet));
                    }
                    cues.Add(new(line, go));
                    t = go + seconds;
                    continue;
                }
                // Stage A4: an attack at a point: the attacker turns to it, then fires (flash and tracer) or throws.
                if (line.Fx.Aim is { } aim)
                {
                    var fire = t + TurnSeconds;
                    beats.Add(new(line.Seq, BeatKind.Turn, t, TurnSeconds, aim.Attacker, Hit: aim.Hit, At: aim.At));
                    double down;
                    if (line.Fx.Thrown)
                    {
                        beats.Add(new(line.Seq, BeatKind.Lob, fire, LobSeconds, aim.Attacker, Hit: aim.Hit, At: aim.At));
                        down = fire + LobSeconds;
                    }
                    else
                    {
                        beats.Add(new(line.Seq, BeatKind.MuzzleFlash, fire, FlashSeconds, aim.Attacker, Hit: aim.Hit, At: aim.At));
                        beats.Add(new(line.Seq, BeatKind.Recoil, fire, RecoilSeconds, aim.Attacker, Hit: aim.Hit, At: aim.At));
                        beats.Add(new(line.Seq, BeatKind.Tracer, fire + TracerDelay, TracerSeconds, aim.Attacker, Hit: aim.Hit, At: aim.At));
                        down = fire + ImpactAt;
                    }
                    if (!aim.Hit)
                    {
                        beats.Add(new(line.Seq, BeatKind.Dust, down, DustSeconds, aim.Attacker, At: aim.At));
                    }
                    faced.Remove(aim.Attacker);
                    cues.Add(new(line, down));
                    t = down + ThrownSettle;
                    continue;
                }
                // Stage A4: explosions (a scattered attack first flies to where it landed); the line comes with the first.
                var at = t;
                if (line.Fx.Blasts.Count > 0)
                {
                    var go = t;
                    var first = -1.0;
                    var last = t;
                    foreach (var blast in line.Fx.Blasts)
                    {
                        if (blast.From is { } aimed)
                        {
                            beats.Add(new(line.Seq, BeatKind.Scatter, go, ScatterSeconds, line.Models?.FirstOrDefault() ?? default, From: aimed, At: blast.At, Size: blast.Radius));
                            go += ScatterSeconds;
                        }
                        var kind = blast.Style switch { BlastStyle.Mine => BeatKind.MineBlast, BlastStyle.Martyrdom => BeatKind.MartyrBlast, _ => BeatKind.Explosion };
                        beats.Add(new(line.Seq, kind, go, ExplosionSeconds, line.Models?.FirstOrDefault() ?? default, At: blast.At, Size: blast.Radius));
                        first = first < 0 ? go : first;
                        last = go;
                        // Stage A5: the next one goes off a moment later.
                        go += BlastStagger;
                    }
                    at = first;
                    t = last + BlastHold;
                }
                cues.Add(new(line, at));
                // Stage A4: the burst of each SHRAPNEL, GAS or FIRE marker the event put on a model, with its line.
                foreach (var marker in line.Fx.Markers)
                {
                    var (kind, seconds) = marker.Style switch
                    {
                        MarkerStyle.Gas => (BeatKind.Gas, GasSeconds),
                        MarkerStyle.Fire => (BeatKind.Fire, FireSeconds),
                        _ => (BeatKind.Shrapnel, ShrapnelSeconds),
                    };
                    beats.Add(new(line.Seq, kind, at, seconds, marker.Model));
                }
                if (line.Changes.Count > 0)
                {
                    t = Math.Max(t, Falls(line, at, beats));
                }
                // Hit reactions: each model this event wounded reels as its line (its dice) comes up.
                foreach (var hurt in line.Wounded)
                {
                    var hurter = line.Models?.FirstOrDefault(m => m != hurt);
                    beats.Add(new(line.Seq, BeatKind.Wound, at, WoundSeconds, hurt, hurter is { } h && h != default ? h : null, true));
                }
                continue;
            }
            var puff = line.Effects.FirstOrDefault(e => e.Kind is EffectKind.HitPuff or EffectKind.DirtPuff);
            var flash = line.Effects.FirstOrDefault(e => e.Kind == EffectKind.MuzzleFlash);
            if (puff is null)
            {
                cues.Add(new(line, t));
                continue;
            }
            var hit = puff.Kind == EffectKind.HitPuff;
            var attacker = flash?.At ?? line.Models?.FirstOrDefault(m => m != puff.At);
            var strikeAt = attacker is { } a ? Turn(line.Seq, a, puff.At, t, hit) : t;
            double landed;
            if (flash is not null && line.Fx.Thrown)
            {
                // Stage A4: a grenade is lobbed in an arc; it lands at the end of its flight.
                beats.Add(new(line.Seq, BeatKind.Lob, strikeAt, LobSeconds, flash.At, puff.At, hit));
                landed = strikeAt + LobSeconds;
            }
            else if (flash is not null)
            {
                beats.Add(new(line.Seq, BeatKind.MuzzleFlash, strikeAt, FlashSeconds, flash.At, puff.At, hit));
                beats.Add(new(line.Seq, BeatKind.Recoil, strikeAt, RecoilSeconds, flash.At, puff.At, hit));
                beats.Add(new(line.Seq, BeatKind.Tracer, strikeAt + TracerDelay, TracerSeconds, flash.At, puff.At, hit));
                landed = strikeAt + ImpactAt;
            }
            else if (attacker is { } blower)
            {
                // A blow in melee: a lunge at the target, landing at its peak with a strike between them.
                beats.Add(new(line.Seq, BeatKind.Lunge, strikeAt, LungeSeconds, blower, puff.At, hit));
                landed = strikeAt + LungePeak;
                beats.Add(new(line.Seq, BeatKind.Strike, landed, StrikeSeconds, puff.At, blower, hit));
            }
            else
            {
                landed = strikeAt;
            }
            beats.Add(new(line.Seq, hit ? BeatKind.HitPuff : BeatKind.DirtPuff, landed, hit ? HitPuffSeconds : DirtPuffSeconds, puff.At, attacker, hit));
            if (hit && attacker is { } by)
            {
                beats.Add(new(line.Seq, BeatKind.Flinch, landed, FlinchSeconds, puff.At, by, hit));
            }
            cues.Add(new(line, landed));
            // A grenade's blast (its next event) goes off where it lands, without the pause after a shot.
            t = landed + (line.Fx.Thrown ? ThrownSettle : AfterImpact);
        }
        var plan = new Choreography(cues, beats);
        var scale = (speed == AnimationSpeed.Fast ? 0.5 : 1) * Math.Min(1, MaxSeconds / Math.Max(plan.Length, 1e-9));
        return scale >= 1 ? plan : new(
            [.. cues.Select(c => c with { At = c.At * scale })],
            [.. beats.Select(b => b with { Start = b.Start * scale, Seconds = b.Seconds * scale })]);
    }

    // Animation stage A3: the falls, rises and removals of one event, all starting with its line; returns when what
    // follows may start (after the falls and rises; an Out of Action model goes on sinking meanwhile).
    private static double Falls(LogLine line, double at, List<Beat> beats)
    {
        var next = at;
        foreach (var change in line.Changes)
        {
            var by = line.Models?.FirstOrDefault(m => m != change.Model);
            ModelId? other = by is { } m && m != default ? m : null;
            switch (change)
            {
                case { From: ModelStatus.Standing, To: ModelStatus.Down }:
                    beats.Add(new(line.Seq, BeatKind.Fall, at, FallSeconds, change.Model, other));
                    next = Math.Max(next, at + FallSeconds);
                    break;
                case { From: ModelStatus.Down, To: ModelStatus.Standing }:
                    beats.Add(new(line.Seq, BeatKind.Rise, at, FallSeconds, change.Model, other));
                    next = Math.Max(next, at + FallSeconds);
                    break;
                case { To: ModelStatus.OutOfAction }:
                    var falls = change.From == ModelStatus.Standing;
                    // Audit item B2: where the server's table last had it (a mine halt during its move), not where it was drawn.
                    beats.Add(new(line.Seq, BeatKind.Casualty, at, (falls ? FallSeconds : 0) + SinkSeconds, change.Model, other, falls, At: change.At));
                    next = Math.Max(next, at + (falls ? FallSeconds : 0));
                    break;
            }
        }
        return next;
    }

    // The point a move heads for first (its first leg, if long enough; else its end), or null for a shuffle.
    private static TablePoint? TravelPoint(TablePoint from, IReadOnlyList<TablePoint> path)
    {
        foreach (var p in path)
        {
            if (Math.Sqrt((p.X - from.X) * (p.X - from.X) + (p.Y - from.Y) * (p.Y - from.Y)) >= FaceTravelInches)
            {
                return p;
            }
        }
        return null;
    }

    // Stage A5: the instant turns Off plays (zero seconds), in event order: each attacker to its target (or point), each
    // moving model along its way, each downed model to the model that downed it. Nothing moves or shows.
    private static List<Beat> OffFacings(IReadOnlyList<LogLine> lines, Func<ModelId, TablePoint?> drawnAt)
    {
        var turns = new List<Beat>();
        var reached = new Dictionary<ModelId, TablePoint>();
        foreach (var line in lines)
        {
            if (line.Replayed)
            {
                continue;
            }
            if (line.Motion is { Path.Count: > 0 } motion && (reached.TryGetValue(motion.Model, out var was) ? was : drawnAt(motion.Model)) is { } from)
            {
                reached[motion.Model] = motion.Path[^1];
                if (motion.Charging is { } target)
                {
                    turns.Add(new(line.Seq, BeatKind.Turn, 0, 0, motion.Model, target, motion.Reached, From: from));
                }
                else if (TravelPoint(from, motion.Path) is { } way)
                {
                    turns.Add(new(line.Seq, BeatKind.Turn, 0, 0, motion.Model, From: from, At: way));
                }
                continue;
            }
            if (line.Fx.Aim is { } aim)
            {
                turns.Add(new(line.Seq, BeatKind.Turn, 0, 0, aim.Attacker, Hit: aim.Hit, At: aim.At));
            }
            if (line.Effects.FirstOrDefault(e => e.Kind is EffectKind.HitPuff or EffectKind.DirtPuff) is { } puff
                && (line.Effects.FirstOrDefault(e => e.Kind == EffectKind.MuzzleFlash)?.At ?? line.Models?.FirstOrDefault(m => m != puff.At)) is { } attacker)
            {
                turns.Add(new(line.Seq, BeatKind.Turn, 0, 0, attacker, puff.At, puff.Kind == EffectKind.HitPuff));
            }
            foreach (var change in line.Changes)
            {
                if (change is { From: ModelStatus.Standing, To: ModelStatus.Down } && line.Models?.FirstOrDefault(m => m != change.Model) is { } by && by != default)
                {
                    turns.Add(new(line.Seq, BeatKind.Turn, 0, 0, change.Model, by));
                }
            }
        }
        return turns;
    }

    private static double PathLength(TablePoint from, IReadOnlyList<TablePoint> path)
    {
        var length = 0.0;
        var at = from;
        foreach (var p in path)
        {
            length += Math.Sqrt((p.X - at.X) * (p.X - at.X) + (p.Y - at.Y) * (p.Y - at.Y));
            at = p;
        }
        return length;
    }
}
