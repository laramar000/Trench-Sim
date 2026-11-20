using Client.Core;
using Rules.Core;
using Rules.Core.Events;
using Rules.Core.State;

namespace Client.Tests;

// Animation stage A1: the battlefield plays each update's events in order and only what they say. Over the simulated
// matches, cut into updates of a few events each: every line is shown once, in order; every beat names only models of its
// own event; a glide follows the event's own path from where the model was drawn; a shot's flash and tracer come before
// its hit or miss, which comes with the roll's line and follows the rules' verdict; what follows a move waits for it to
// arrive; an update never plays longer than MaxSeconds; Fast takes half the time and Off plays nothing.
public class ChoreographyTests
{
    private const double Eps = 1e-9;

    // The matches as updates: each a run of up to `size` events with the lines the client makes of them, and where each
    // model stood before the update.
    private static IEnumerable<(List<LogLine> Lines, List<GameEvent> Events, Dictionary<ModelId, TablePoint> Before)> Updates(int size)
    {
        foreach (var (_, log) in ClientCoreTests.Matches.Value)
        {
            var state = MatchState.None;
            var statuses = new StatusTracker();
            for (var i = 0; i < log.Count; i += size)
            {
                var before = state.Models.Where(m => m.Position is not null).ToDictionary(m => m.Id, m => TablePoint.Of(m.Position!.Value));
                var lines = new List<LogLine>();
                var events = log.Skip(i).Take(size).ToList();
                foreach (var (e, k) in events.Select((e, k) => (e, k)))
                {
                    state = Reducer.Apply(state, e);
                    lines.Add(new LogLine(i + k + 1, "", null, Motion: Motions.Of(e, state), Models: EventModels.Of(e)) { Effects = EffectCues.Of(e), Changes = statuses.Next(state) });
                }
                yield return (lines, events, before);
            }
        }
    }

    private static Func<ModelId, TablePoint?> At(Dictionary<ModelId, TablePoint> before) => id => before.TryGetValue(id, out var p) ? p : null;

    [Fact]
    public void EveryUpdatePlaysItsOwnEventsInOrder()
    {
        var seen = new Dictionary<BeatKind, int>();
        var waitedForAMove = 0;
        var charges = 0;
        var reachedCharges = 0;
        var blows = 0;
        var statusBeats = new Dictionary<BeatKind, int>();
        foreach (var size in new[] { 3, 8 })
        {
            foreach (var (lines, events, before) in Updates(size))
            {
                var plan = Choreography.Of(lines, At(before));
                // Every line once, in order, never earlier than the one before it.
                Assert.Equal(lines.Select(l => l.Seq), plan.Lines.Select(c => c.Line.Seq));
                Assert.True(plan.Lines.Zip(plan.Lines.Skip(1)).All(p => p.First.At <= p.Second.At + Eps));
                Assert.True(plan.Length <= Choreography.MaxSeconds + Eps, $"length {plan.Length}");
                var reached = new Dictionary<ModelId, TablePoint>(before);
                foreach (var (line, e) in lines.Zip(events))
                {
                    var mine = plan.Beats.Where(b => b.Seq == line.Seq).ToList();
                    var models = line.Models ?? [];
                    Assert.All(mine, b => Assert.True(b.Seconds > 0 && b.Start >= 0, b.ToString()));
                    foreach (var b in mine)
                    {
                        seen[b.Kind] = seen.GetValueOrDefault(b.Kind) + 1;
                        Assert.True(b.Kind == BeatKind.Glide ? b.Model == line.Motion!.Model : models.Contains(b.Model), $"{b} not a model of {e}");
                        Assert.True(b.Other is null || models.Contains(b.Other.Value), $"{b} not a model of {e}");
                    }
                    var at = plan.Lines.Single(c => c.Line.Seq == line.Seq).At;
                    if (line.Motion is { } motion && reached.TryGetValue(motion.Model, out var from))
                    {
                        Assert.Equal(e switch { ChargeMoved c => c.Target, ChargeCarriedOn c => c.Target, _ => (ModelId?)null }, motion.Charging);
                        var glide = mine.SingleOrDefault(b => b.Kind == BeatKind.Glide);
                        if (glide is not null)
                        {
                            Assert.Equal(motion, glide.Motion);
                            Assert.Equal(from, glide.From);
                            // (Below the least seconds only when a long update is played faster to fit.)
                            Assert.True(glide.Seconds <= (motion.Charging is null ? Choreography.GlideMaxSeconds : Choreography.ChargeMaxSeconds) + Eps, glide.ToString());
                            if (motion.Charging is not { } target)
                            {
                                Assert.Equal(at, glide.Start, 9);
                                Assert.DoesNotContain(mine, b => b.Kind != BeatKind.Glide);
                            }
                            else
                            {
                                // A charge: (a turn to the target first, unless it already faces it,) a glide with a
                                // lean; one that reaches drives into contact, and its line comes with the impact.
                                charges++;
                                var turn = mine.SingleOrDefault(b => b.Kind == BeatKind.Turn);
                                Assert.True(turn is null || (turn.Model, turn.Other, turn.From) == (motion.Model, target, from) && turn.End <= glide.Start + Eps, $"{turn} {glide}");
                                var lean = mine.Single(b => b.Kind == BeatKind.Lean);
                                Assert.Equal((motion.Model, (ModelId?)target), (lean.Model, lean.Other));
                                Assert.Equal(glide.Start, lean.Start, 9);
                                if (motion.Reached)
                                {
                                    var lunge = mine.Single(b => b.Kind == BeatKind.Lunge);
                                    var hitHome = mine.Single(b => b.Kind == BeatKind.Impact);
                                    var knock = mine.Single(b => b.Kind == BeatKind.Flinch);
                                    Assert.Equal((motion.Model, (ModelId?)target), (lunge.Model, lunge.Other));
                                    Assert.Equal((target, (ModelId?)motion.Model), (hitHome.Model, hitHome.Other));
                                    Assert.Equal((target, (ModelId?)motion.Model), (knock.Model, knock.Other));
                                    Assert.Equal(glide.End, lunge.Start, 9);
                                    Assert.True(hitHome.Start > lunge.Start && hitHome.Start < lunge.End && knock.Start == hitHome.Start, $"{lunge} {hitHome}");
                                    Assert.Equal(at, hitHome.Start, 9);
                                    Assert.Equal(lunge.End, lean.End, 9);
                                    reachedCharges++;
                                }
                                else
                                {
                                    Assert.DoesNotContain(mine, b => b.Kind is BeatKind.Lunge or BeatKind.Impact or BeatKind.Flinch);
                                    Assert.Equal(at, glide.End, 9);
                                    Assert.Equal(glide.End, lean.End, 9);
                                }
                            }
                            // Whatever comes after the move in this update waits for the model to arrive.
                            foreach (var later in plan.Lines.Where(c => c.Line.Seq > line.Seq))
                            {
                                Assert.True(later.At >= glide.End - Eps && later.At >= at - Eps, $"[{later.Line.Seq}] at {later.At} before the move [{line.Seq}] ends at {glide.End}");
                                waitedForAMove += later.Line.Effects.Count > 0 ? 1 : 0;
                            }
                        }
                        else
                        {
                            Assert.Empty(mine);
                        }
                        reached[motion.Model] = motion.Path[^1];
                        continue;
                    }
                    if (line.Effects.Count == 0)
                    {
                        // Stage A3: one beat per status change, starting with the line; what follows waits for a fall or
                        // a rise (an Out of Action model sinks meanwhile).
                        Assert.Equal(line.Changes.Count, mine.Count);
                        foreach (var (change, b) in line.Changes.Zip(mine))
                        {
                            var kind = change switch
                            {
                                { From: ModelStatus.Standing, To: ModelStatus.Down } => BeatKind.Fall,
                                { From: ModelStatus.Down, To: ModelStatus.Standing } => BeatKind.Rise,
                                _ => BeatKind.Casualty,
                            };
                            Assert.Equal((kind, change.Model), (b.Kind, b.Model));
                            Assert.Equal(at, b.Start, 9);
                            Assert.True(b.Other is null || b.Other != change.Model, b.ToString());
                            var falls = kind != BeatKind.Casualty || change.From == ModelStatus.Standing;
                            Assert.Equal(kind == BeatKind.Casualty && falls, kind == BeatKind.Casualty && b.Hit);
                            var fallEnd = b.Start + (falls ? b.Seconds - (kind == BeatKind.Casualty ? b.Seconds * Choreography.SinkSeconds / (Choreography.FallSeconds + Choreography.SinkSeconds) : 0) : 0);
                            foreach (var later in plan.Lines.Where(c => c.Line.Seq > line.Seq))
                            {
                                Assert.True(later.At >= fallEnd - Eps, $"[{later.Line.Seq}] at {later.At} before the fall of [{line.Seq}] ends at {fallEnd}");
                            }
                            statusBeats[kind] = statusBeats.GetValueOrDefault(kind) + 1;
                        }
                        continue;
                    }
                    // A shot or a blow: the rules' verdict on the roll decides hit or miss; the flash and tracer come
                    // first, the hit or miss and the roll's line together, and a flinch only on a hit.
                    var hit = DiceRolls.Of(e)?.Verdict is RollVerdict.Pass or RollVerdict.Critical;
                    var impact = mine.Single(b => b.Kind is BeatKind.HitPuff or BeatKind.DirtPuff);
                    Assert.Equal(hit ? BeatKind.HitPuff : BeatKind.DirtPuff, impact.Kind);
                    Assert.All(mine, b => Assert.Equal(hit, b.Hit));
                    Assert.Equal(at, impact.Start, 9);
                    Assert.Equal(hit ? 1 : 0, mine.Count(b => b.Kind == BeatKind.Flinch));
                    // The attacker turns to the target first, unless it already faces it in this update.
                    var attackTurn = mine.SingleOrDefault(b => b.Kind == BeatKind.Turn);
                    Assert.True(attackTurn is null || attackTurn.Model == impact.Other && attackTurn.Other == impact.Model
                        && mine.Where(b => b != attackTurn).All(b => b.Start >= attackTurn.End - Eps), $"{attackTurn}");
                    if (e is RangedSuccessRolled shot)
                    {
                        var flash = mine.Single(b => b.Kind == BeatKind.MuzzleFlash);
                        var tracer = mine.Single(b => b.Kind == BeatKind.Tracer);
                        Assert.Equal((shot.Attacker, shot.Target), (flash.Model, flash.Other));
                        Assert.Equal((shot.Attacker, shot.Target), (tracer.Model, tracer.Other));
                        Assert.True(flash.Start <= tracer.Start && tracer.End <= impact.Start + Eps, $"{flash} {tracer} {impact}");
                        Assert.Equal(shot.Target, impact.Model);
                    }
                    else
                    {
                        var blow = Assert.IsType<MeleeSuccessRolled>(e);
                        Assert.DoesNotContain(mine, b => b.Kind is BeatKind.MuzzleFlash or BeatKind.Tracer);
                        Assert.Equal((blow.Target, (ModelId?)blow.Attacker), (impact.Model, impact.Other));
                        // A lunge at the target, the blow landing at its peak with a strike between them.
                        var lunge = mine.Single(b => b.Kind == BeatKind.Lunge);
                        var strike = mine.Single(b => b.Kind == BeatKind.Strike);
                        Assert.Equal((blow.Attacker, (ModelId?)blow.Target), (lunge.Model, lunge.Other));
                        Assert.Equal((blow.Target, (ModelId?)blow.Attacker), (strike.Model, strike.Other));
                        Assert.True(impact.Start > lunge.Start && impact.Start < lunge.End && strike.Start == impact.Start, $"{lunge} {strike} {impact}");
                        blows++;
                    }
                }
            }
        }
        // Stage A4's kinds (grenades, blasts, markers) need armed models: BattleEffectsTests covers them.
        Assert.All(Enum.GetValues<BeatKind>().Where(k => k < BeatKind.Lob), k => Assert.True(seen.GetValueOrDefault(k) > 0, $"{k}: {string.Join(", ", seen)}"));
        Assert.True(waitedForAMove > 0, "no shot or blow came after a move in the same update");
        Assert.True(charges > 0 && reachedCharges > 0 && blows > 0, $"charges {charges}, reached {reachedCharges}, blows {blows}");
    }

    [Fact]
    public void FastTakesHalfTheTimeAndOffPlaysNothing()
    {
        foreach (var (lines, _, before) in Updates(8))
        {
            var full = Choreography.Of(lines, At(before));
            var fast = Choreography.Of(lines, At(before), AnimationSpeed.Fast);
            var off = Choreography.Of(lines, At(before), AnimationSpeed.Off);
            Assert.Equal(full.Length / 2, fast.Length, 9);
            Assert.Equal(full.Beats.Select(b => (b.Seq, b.Kind, b.Model)), fast.Beats.Select(b => (b.Seq, b.Kind, b.Model)));
            Assert.Empty(off.Beats);
            Assert.Equal(lines.Select(l => l.Seq), off.Lines.Select(c => c.Line.Seq));
            Assert.All(off.Lines, c => Assert.Equal(0, c.At));
        }
    }

    [Fact]
    public void ACaughtUpEventPlaysNoEffectsAndAnUndrawnModelDoesNotTravel()
    {
        var a = new ModelId("a1");
        var b = new ModelId("b1");
        var shot = new LogLine(5, "", null, Models: [a, b], Replayed: true) { Effects = [new(EffectKind.MuzzleFlash, a), new(EffectKind.HitPuff, b)] };
        var move = new LogLine(6, "", null, Motion: new Motion(a, [new TablePoint(20, 20)]), Models: [a]);
        var plan = Choreography.Of([shot, move], _ => null);
        Assert.Empty(plan.Beats);
        Assert.Equal([0.0, 0.0], plan.Lines.Select(c => c.At));
        // Live, the same shot plays (the shooter turns first), and the move of a model drawn at (0, 0) waits for the hit's pause.
        var live = Choreography.Of([shot with { Replayed = false }, move], id => id == a ? new TablePoint(0, 0) : null);
        var glide = live.Beats.Single(x => x.Kind == BeatKind.Glide);
        Assert.Equal(Choreography.TurnSeconds + Choreography.ImpactAt + Choreography.AfterImpact, glide.Start, 9);
        Assert.Equal(Choreography.GlideMaxSeconds, glide.Seconds, 9);
    }

    // Animation stage A2: a charge that reaches, then the blow in the same update: the charger turns once (it already faces
    // the target when it strikes), the blow waits for the charge's impact, and the lunge lands the blow at its peak.
    [Fact]
    public void AChargeThatReachesThenABlowTurnsOnceAndStrikesAfterTheImpact()
    {
        var a = new ModelId("a1");
        var b = new ModelId("b1");
        var charge = new LogLine(1, "", null, Motion: new Motion(a, [new TablePoint(9, 0)], b, Reached: true), Models: [a, b]);
        var blow = new LogLine(2, "", null, Models: [a, b]) { Effects = [new(EffectKind.HitPuff, b)] };
        var plan = Choreography.Of([charge, blow], id => id == a ? new TablePoint(0, 0) : null);
        Assert.Single(plan.Beats, x => x.Kind == BeatKind.Turn);
        var turn = plan.Beats.Single(x => x.Kind == BeatKind.Turn);
        Assert.Equal((0.0, a, (ModelId?)b, (TablePoint?)new TablePoint(0, 0)), (turn.Start, turn.Model, turn.Other, turn.From));
        var glide = plan.Beats.Single(x => x.Kind == BeatKind.Glide);
        Assert.Equal(Choreography.TurnSeconds, glide.Start, 9);
        Assert.Equal(Choreography.ChargeMinSeconds, glide.Seconds, 9);
        var impact = plan.Beats.Single(x => x.Kind == BeatKind.Impact);
        Assert.Equal(glide.End + Choreography.LungePeak, impact.Start, 9);
        var lunges = plan.Beats.Where(x => x.Kind == BeatKind.Lunge).ToList();
        Assert.Equal(2, lunges.Count);
        var strike = plan.Beats.Single(x => x.Kind == BeatKind.Strike);
        Assert.Equal(impact.Start + Choreography.AfterImpact + Choreography.LungePeak, strike.Start, 9);
        Assert.Equal(lunges[1].Start + Choreography.LungePeak, strike.Start, 9);
        Assert.Equal([impact.Start, strike.Start], plan.Lines.Select(c => c.At));
        // Two flinches of the target: the charge's impact and the blow.
        Assert.Equal(2, plan.Beats.Count(x => x.Kind == BeatKind.Flinch && x.Model == b));
    }

    [Fact]
    public void AChargeThatFallsShortLeansButStrikesNothing()
    {
        var a = new ModelId("a1");
        var b = new ModelId("b1");
        var charge = new LogLine(1, "", null, Motion: new Motion(a, [new TablePoint(9, 0)], b), Models: [a, b]);
        var plan = Choreography.Of([charge], id => id == a ? new TablePoint(0, 0) : null);
        Assert.Equal([BeatKind.Turn, BeatKind.Glide, BeatKind.Lean], plan.Beats.Select(x => x.Kind));
        Assert.Equal(plan.Beats[1].End, plan.Lines.Single().At, 9);
    }
}
