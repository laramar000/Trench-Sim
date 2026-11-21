using Client.Core;
using Rules.Core.State;

namespace Client.Tests;

// Animation stage A1: the battlefield's queue of what is still to play. Lines and one-off beats come due in time order at
// their time; glides and flinches are handed back at once with their absolute start; a second update plays after the
// first; TakeAll shows everything at once (a command sent, Esc, a catch-up, a new match) and leaves nothing playing;
// Off queues lines that are all due at once.
public class PlaybackQueueTests
{
    private static readonly ModelId A = new("a1");
    private static readonly ModelId B = new("b1");

    private static LogLine Shot(int seq, bool hit) => new(seq, "", null, Models: [A, B])
    {
        Effects = [new(EffectKind.MuzzleFlash, A), new(hit ? EffectKind.HitPuff : EffectKind.DirtPuff, B)],
    };

    private static LogLine Injury(int seq) => new(seq, "", null, Models: [A, B]);

    private static LogLine Move(int seq) => new(seq, "", null, Motion: new Motion(A, [new TablePoint(20, 0)]), Models: [A]);

    private static Choreography Plan(AnimationSpeed speed, params LogLine[] lines) =>
        Choreography.Of(lines, id => id == A ? new TablePoint(0, 0) : new TablePoint(10, 10), speed);

    private static string Describe(PlaybackQueue.Item item) => item.Line is { } line ? $"line {line.Seq}" : $"{item.Beat!.Kind} {item.Beat.Seq}";

    [Fact]
    public void AHitComesDueAsFlashTracerThenImpactWithItsLineThenTheInjuryLine()
    {
        var queue = new PlaybackQueue();
        var moves = new List<Beat>();
        var start = queue.Add(Plan(AnimationSpeed.Full, Shot(1, hit: true), Injury(2)), 100, moves);
        Assert.Equal(100, start);
        // The turn (stage A2) and the flinch move models from their start on; they are not queued.
        Assert.Equal([BeatKind.Turn, BeatKind.Flinch], moves.Select(m => m.Kind));
        Assert.Equal((A, (ModelId?)B, 100.0), (moves[0].Model, moves[0].Other, moves[0].Start));
        var flinch = moves[1];
        var fire = 100 + Choreography.TurnSeconds;
        Assert.Equal((B, (ModelId?)A), (flinch.Model, flinch.Other));
        Assert.Equal(fire + Choreography.ImpactAt, flinch.Start, 9);
        var taken = new List<PlaybackQueue.Item>();
        queue.TakeDue(fire - 0.001, taken);
        Assert.Empty(taken);
        queue.TakeDue(fire + 1e-9, taken);
        Assert.Equal(["MuzzleFlash 1"], taken.Select(Describe));
        taken.Clear();
        queue.TakeDue(fire + Choreography.ImpactAt - 0.001, taken);
        Assert.Equal(["Tracer 1"], taken.Select(Describe));
        taken.Clear();
        queue.TakeDue(fire + Choreography.ImpactAt + 1e-9, taken);
        Assert.Equal(["line 1", "HitPuff 1"], taken.Select(Describe));
        taken.Clear();
        queue.TakeDue(fire + Choreography.ImpactAt + Choreography.AfterImpact + 1e-9, taken);
        Assert.Equal(["line 2"], taken.Select(Describe));
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void AMissHasNoFlinchAndEndsInADirtPuff()
    {
        var queue = new PlaybackQueue();
        var moves = new List<Beat>();
        queue.Add(Plan(AnimationSpeed.Full, Shot(1, hit: false)), 0, moves);
        Assert.Equal([BeatKind.Turn], moves.Select(m => m.Kind));
        var taken = new List<PlaybackQueue.Item>();
        queue.TakeAll(0, taken);
        Assert.Equal(["MuzzleFlash 1", "Tracer 1", "line 1", "DirtPuff 1"], taken.Select(Describe));
        Assert.False(taken.Single(i => i.Beat?.Kind == BeatKind.Tracer).Beat!.Hit);
    }

    [Fact]
    public void ASecondUpdatePlaysAfterTheFirst()
    {
        var queue = new PlaybackQueue();
        var moves = new List<Beat>();
        queue.Add(Plan(AnimationSpeed.Full, Move(1)), 10, moves);
        // Stage A5: the model turns along its way as it sets off, then glides.
        Assert.Equal([BeatKind.Turn, BeatKind.Glide], moves.Select(m => m.Kind));
        Assert.All(moves, m => Assert.Equal(10, m.Start, 9));
        Assert.Equal(10 + Choreography.GlideMaxSeconds, queue.End, 9);
        // The next update arrives while the model still travels: its shot waits for the end of the move.
        var start = queue.Add(Plan(AnimationSpeed.Full, Shot(2, hit: true)), 10.1, moves);
        Assert.Equal(10 + Choreography.GlideMaxSeconds, start, 9);
        Assert.Equal(start, moves[2].Start, 9);
        Assert.Equal(start + Choreography.TurnSeconds + Choreography.ImpactAt, moves[3].Start, 9);
        var taken = new List<PlaybackQueue.Item>();
        queue.TakeDue(start - 0.001, taken);
        Assert.Equal(["line 1"], taken.Select(Describe));
    }

    [Fact]
    public void TakeAllShowsEverythingAtOnceAndLeavesNothingPlaying()
    {
        var queue = new PlaybackQueue();
        var moves = new List<Beat>();
        queue.Add(Plan(AnimationSpeed.Full, Move(1), Shot(2, hit: true), Injury(3)), 0, moves);
        Assert.True(queue.Behind(0.1) > 0);
        var taken = new List<PlaybackQueue.Item>();
        queue.TakeDue(0.1, taken);
        Assert.Equal(["line 1"], taken.Select(Describe));
        taken.Clear();
        // A command sent mid-playback: everything still queued, in order, at once.
        queue.TakeAll(0.2, taken);
        Assert.Equal(["MuzzleFlash 2", "Tracer 2", "line 2", "HitPuff 2", "line 3"], taken.Select(Describe));
        Assert.Equal(0, queue.Count);
        Assert.Equal(0, queue.Behind(0.2));
        // What comes next starts at once, not after the cut playback.
        Assert.Equal(0.3, queue.Add(Plan(AnimationSpeed.Full, Injury(4)), 0.3, moves), 9);
    }

    [Fact]
    public void OffQueuesEveryLineDueAtOnceAndOnlyInstantTurns()
    {
        var queue = new PlaybackQueue();
        var moves = new List<Beat>();
        queue.Add(Plan(AnimationSpeed.Off, Move(1), Shot(2, hit: true), Injury(3)), 5, moves);
        // Stage A5: only instant turns (the mover along its way, the shooter to its target), all at once.
        Assert.Equal([(BeatKind.Turn, A, 5.0, 0.0), (BeatKind.Turn, A, 5.0, 0.0)], moves.Select(m => (m.Kind, m.Model, m.Start, m.Seconds)));
        var taken = new List<PlaybackQueue.Item>();
        queue.TakeDue(5, taken);
        Assert.Equal(["line 1", "line 2", "line 3"], taken.Select(Describe));
        Assert.Equal(0, queue.Behind(5));
    }

    [Fact]
    public void FastPlaysTheSameBeatsInHalfTheTime()
    {
        var full = new PlaybackQueue();
        var fast = new PlaybackQueue();
        var moves = new List<Beat>();
        full.Add(Plan(AnimationSpeed.Full, Move(1), Shot(2, hit: true), Injury(3)), 0, moves);
        fast.Add(Plan(AnimationSpeed.Fast, Move(1), Shot(2, hit: true), Injury(3)), 0, moves);
        Assert.Equal(full.End / 2, fast.End, 9);
        var a = new List<PlaybackQueue.Item>();
        var b = new List<PlaybackQueue.Item>();
        full.TakeAll(0, a);
        fast.TakeAll(0, b);
        Assert.Equal(a.Select(Describe), b.Select(Describe));
        Assert.All(a.Zip(b), p => Assert.Equal(p.First.At / 2, p.Second.At, 9));
    }

    [Fact]
    public void ACaughtUpShotQueuesItsLineButNoEffects()
    {
        var queue = new PlaybackQueue();
        var moves = new List<Beat>();
        queue.Add(Plan(AnimationSpeed.Full, Shot(1, hit: true) with { Replayed = true }), 0, moves);
        Assert.Empty(moves);
        var taken = new List<PlaybackQueue.Item>();
        queue.TakeDue(0, taken);
        Assert.Equal(["line 1"], taken.Select(Describe));
    }
}
