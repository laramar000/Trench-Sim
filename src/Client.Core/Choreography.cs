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
}

// One thing the battlefield draws for an event, Start seconds after the update arrived, for Seconds. Model is the model it
// happens to (the shooter of a flash or tracer, the target of a puff or flinch, the model that glides); Other is the model
// at the far end (the target of a tracer, the attacker a flinch moves away from). Hit says whether the roll counted as
// passed (a tracer that hits ends on its target). Motion and From are a glide's path and the point it starts from.
public sealed record Beat(int Seq, BeatKind Kind, double Start, double Seconds, ModelId Model, ModelId? Other = null, bool Hit = false, Motion? Motion = null, TablePoint? From = null)
{
    public double End => Start + Seconds;

    // Beats that move or turn a model for a while (the battlefield poses it each frame) rather than draw an effect once.
    public bool PosesModel => Kind is BeatKind.Glide or BeatKind.Flinch or BeatKind.Turn or BeatKind.Lunge or BeatKind.Lean;

    // Effects drawn once, that fade on their own (an update is not busy while they fade).
    public bool FadesOnItsOwn => Kind is BeatKind.HitPuff or BeatKind.DirtPuff or BeatKind.Strike or BeatKind.Impact;
}

// When an event's own line is shown (its dice, notes and sound), seconds after the update arrived. The combat log does not
// wait for this: it always shows every event as soon as it arrives.
public sealed record LineCue(LogLine Line, double At);

// Animation stage A1: what the battlefield plays for the new events of one update, in order: a model travels its path and
// what follows waits for it to arrive; a shot is a flash at the shooter, a tracer to the target, then the hit or the miss
// (a puff, and a flinch when it hit) with the roll's dice and notes. Stage A2: the attacker first turns to face its target;
// a blow in melee is a lunge landing at its peak with a strike; a charge turns, glides in faster leaning forward, and one
// that reaches drives into contact with an impact and a flinch (its line, with "REACHED", comes on the impact). Everything comes from the events (the server's
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

    // One update plays for at most this long; a longer one is played faster to fit.
    public const double MaxSeconds = 2.5;

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
            return new([.. lines.Select(l => new LineCue(l, 0))], []);
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
                cues.Add(new(line, t));
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
            if (flash is not null)
            {
                beats.Add(new(line.Seq, BeatKind.MuzzleFlash, strikeAt, FlashSeconds, flash.At, puff.At, hit));
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
            t = landed + AfterImpact;
        }
        var plan = new Choreography(cues, beats);
        var scale = (speed == AnimationSpeed.Fast ? 0.5 : 1) * Math.Min(1, MaxSeconds / Math.Max(plan.Length, 1e-9));
        return scale >= 1 ? plan : new(
            [.. cues.Select(c => c with { At = c.At * scale })],
            [.. beats.Select(b => b with { Start = b.Start * scale, Seconds = b.Seconds * scale })]);
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
