using Client.Core;
using Rules.Core;
using Rules.Core.Events;
using Rules.Core.State;

namespace Client.Tests;

// Animation audit item T1: in Fast, the effects drawn for a set length (a shot's flash, a hit's red puff, a miss's dirt,
// a blow's sparks, a charge's impact dust) are shortened with their beat, so they do not linger into the next beats
// more than in Full; in Full they are drawn exactly as long as before (also in an update played faster to fit
// MaxSeconds); Off plays none. Over the simulated matches cut into updates as the client gets them.
public class FastEffectTimingTests
{
    private static readonly BeatKind[] SetLength = [BeatKind.MuzzleFlash, BeatKind.HitPuff, BeatKind.DirtPuff, BeatKind.Strike, BeatKind.Impact];

    private static IEnumerable<(List<LogLine> Lines, Dictionary<ModelId, TablePoint> Before)> Updates(int size)
    {
        // Ranged and melee matches (melee has the blows and charge impacts).
        foreach (var (_, log) in ClientCoreTests.Matches.Value.Where(m => m.Scenario == "ranged").Take(15).Concat(ClientCoreTests.Matches.Value.Where(m => m.Scenario == "melee").Take(15)))
        {
            var state = MatchState.None;
            var statuses = new StatusTracker();
            for (var i = 0; i < log.Count; i += size)
            {
                var before = state.Models.Where(m => m.Position is not null).ToDictionary(m => m.Id, m => TablePoint.Of(m.Position!.Value));
                var lines = new List<LogLine>();
                foreach (var (e, k) in log.Skip(i).Take(size).Select((e, k) => (e, k)))
                {
                    state = Reducer.Apply(state, e);
                    lines.Add(new LogLine(i + k + 1, "", null, Motion: Motions.Of(e, state), Models: EventModels.Of(e)) { Effects = EffectCues.Of(e), Changes = statuses.Next(state) });
                }
                yield return (lines, before);
            }
        }
    }

    private static Func<ModelId, TablePoint?> At(Dictionary<ModelId, TablePoint> before) => id => before.TryGetValue(id, out var p) ? p : null;

    // How long each set-length effect is drawn for: its Full length times its scale.
    private static double Drawn(Beat beat, AnimationSpeed speed) => Choreography.FullSeconds(beat.Kind)!.Value * Choreography.EffectScale(beat, speed);

    [Fact]
    public void FullDrawsEveryEffectAsLongAsBefore()
    {
        var seen = new HashSet<BeatKind>();
        var squeezed = 0;
        // Updates of up to 40 events: long ones are played faster to fit MaxSeconds.
        foreach (var (lines, before) in Updates(40))
        {
            var plan = Choreography.Of(lines, At(before), AnimationSpeed.Full);
            squeezed += plan.Beats.Any(b => SetLength.Contains(b.Kind) && b.Seconds < Choreography.FullSeconds(b.Kind)!.Value - 1e-9) ? 1 : 0;
            foreach (var beat in plan.Beats.Where(b => SetLength.Contains(b.Kind)))
            {
                Assert.Equal(1, Choreography.EffectScale(beat, AnimationSpeed.Full));
                seen.Add(beat.Kind);
            }
        }
        Assert.Equal(SetLength.Order(), seen.Order());
        // Some updates were played faster to fit MaxSeconds; Full still drew their effects at full length.
        Assert.True(squeezed > 0, "no update was long enough to be played faster");
    }

    [Fact]
    public void FastDrawsEachEffectAtItsBeatsLengthHalfOfFullAndOverlapsHalfAsLong()
    {
        var seen = new HashSet<BeatKind>();
        foreach (var (lines, before) in Updates(4))
        {
            var full = Choreography.Of(lines, At(before), AnimationSpeed.Full);
            var fast = Choreography.Of(lines, At(before), AnimationSpeed.Fast);
            Assert.Equal(full.Beats.Select(b => (b.Kind, b.Seq, b.Model)), fast.Beats.Select(b => (b.Kind, b.Seq, b.Model)));
            var squeezed = full.Length > Choreography.MaxSeconds + 1e-9;
            foreach (var (f, q) in full.Beats.Zip(fast.Beats).Where(p => SetLength.Contains(p.First.Kind)))
            {
                seen.Add(f.Kind);
                var scale = Choreography.EffectScale(q, AnimationSpeed.Fast);
                Assert.InRange(scale, Choreography.FastEffectFloor, 0.5 + 1e-9);
                if (!squeezed)
                {
                    // An update short enough to play in full: every set-length effect is drawn exactly half as long, starts
                    // at half the time, so it ends at half the time of Full's and runs into what follows half as long.
                    Assert.Equal(0.5, scale, 9);
                    Assert.Equal(Drawn(f, AnimationSpeed.Full) / 2, Drawn(q, AnimationSpeed.Fast), 9);
                    Assert.Equal((f.Start + Drawn(f, AnimationSpeed.Full)) / 2, q.Start + Drawn(q, AnimationSpeed.Fast), 9);
                    Assert.Equal((full.Length - (f.Start + Drawn(f, AnimationSpeed.Full))) / 2, fast.Length - (q.Start + Drawn(q, AnimationSpeed.Fast)), 9);
                }
            }
        }
        Assert.Equal(SetLength.Order(), seen.Order());
    }

    [Fact]
    public void OffPlaysNoSetLengthEffectAndOtherBeatsAreNotScaled()
    {
        foreach (var (lines, before) in Updates(6).Take(400))
        {
            Assert.DoesNotContain(Choreography.Of(lines, At(before), AnimationSpeed.Off).Beats, b => SetLength.Contains(b.Kind));
            // Beats drawn over their own Seconds (tracers, explosions, bursts, glides...) take no extra scale.
            foreach (var beat in Choreography.Of(lines, At(before), AnimationSpeed.Fast).Beats.Where(b => !SetLength.Contains(b.Kind)))
            {
                Assert.Null(Choreography.FullSeconds(beat.Kind));
                Assert.Equal(1, Choreography.EffectScale(beat, AnimationSpeed.Fast));
            }
        }
    }

    [Fact]
    public void AVeryLongFastUpdateStillDrawsItsHitsLongEnoughToRead()
    {
        var hit = new Beat(1, BeatKind.HitPuff, 0, Choreography.HitPuffSeconds * 0.1, new ModelId("b1"), new ModelId("a1"), true);
        Assert.Equal(Choreography.FastEffectFloor, Choreography.EffectScale(hit, AnimationSpeed.Fast));
        Assert.Equal(1, Choreography.EffectScale(hit, AnimationSpeed.Full));
        Assert.Equal(1, Choreography.EffectScale(hit, AnimationSpeed.Off));
    }

    // Finishing at once mid-update (a command sent, Esc, a catch-up) works the same in Fast: every line still due comes
    // out in order, nothing is left playing, and what comes next starts at once.
    [Fact]
    public void FinishingAtOnceInFastLeavesNothing()
    {
        foreach (var (lines, before) in Updates(4).Where(u => u.Lines.Any(l => l.Effects.Count > 0)).Take(50))
        {
            var queue = new PlaybackQueue();
            var moves = new List<Beat>();
            var plan = Choreography.Of(lines, At(before), AnimationSpeed.Fast);
            queue.Add(plan, 0, moves);
            var taken = new List<PlaybackQueue.Item>();
            queue.TakeDue(plan.Length / 3, taken);
            queue.TakeAll(plan.Length / 3, taken);
            Assert.Equal(lines.Select(l => l.Seq), taken.Where(i => i.Line is not null).Select(i => i.Line!.Seq));
            Assert.Equal(0, queue.Count);
            Assert.Equal(1.0, queue.Add(Choreography.Empty, 1.0, moves), 9);
        }
    }
}
