using Client.Core;
using Rules.Core;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.State;
using Sim;

namespace Client.Tests;

// Animation audit item B2: a model put Out of Action during its own move (a live Mine Marker halts the move and takes it
// out) goes where the server halted it, not where its move started. Over bot-played Trench Warfare matches, each command's
// events are one update, as the client gets them: the status change names the server's last place for the model (its
// position just before the event that took it out), the move's glide ends exactly there, and the casualty starts falling
// there only once the glide has arrived; Fast keeps that order, Off plays no casualty, and finishing at once (a command
// sent, Esc, a catch-up) still hands back every line in order with nothing left. Display only: every place is the state's.
public class CasualtyPlaceTests
{
    private sealed record Update(List<LogLine> Lines, List<GameEvent> Events, List<MatchState> After, MatchState Before);

    private static readonly Lazy<List<Update>> Updates = new(() =>
    {
        var updates = new List<Update>();
        var setups = ScenarioSetups.FromFolder(Path.Combine(Repository.Root, "content"));
        foreach (var name in new[] { "ranged-trench-warfare", "melee-trench-warfare" })
        {
            for (var seed = 0; seed < 12; seed++)
            {
                var state = Reducer.Apply(MatchState.None, new MatchCreated(setups[name]));
                var statuses = new StatusTracker();
                var effects = new BattleEffectsTracker();
                var resolutions = new ResolutionLog();
                statuses.Next(state);
                var bot = new Bot(new Random(seed));
                var dice = new SeededDice(seed * 17 + 3);
                var seq = 1;
                for (var step = 0; step < 3000 && state.Phase != Phase.Ended; step++)
                {
                    var next = bot.Propose(state).Select(c => TurnSequence.Decide(state, c, dice)).FirstOrDefault(r => r.IsAccepted);
                    Assert.NotNull(next);
                    var update = new Update([], [], [], state);
                    foreach (var e in next.Events)
                    {
                        state = Reducer.Apply(state, e);
                        seq++;
                        resolutions.Add(seq, e, state.Setup);
                        var (kind, weapon) = resolutions.AttackAt(seq);
                        update.Lines.Add(new LogLine(seq, "", null, Motion: Motions.Of(e, state), Models: EventModels.Of(e))
                        {
                            Effects = EffectCues.Of(e),
                            Changes = statuses.Next(state),
                            Fx = effects.Next(e, state, kind, weapon),
                        });
                        update.Events.Add(e);
                        update.After.Add(state);
                    }
                    updates.Add(update);
                }
            }
        }
        return updates;
    });

    // The updates in which a model's own move of that update ends at a live mine that puts it Out of Action.
    private static List<(Update Update, int Exploded, ModelId Model)> MinedMidMove() =>
        [.. Updates.Value.SelectMany(u => u.Events.Select((e, i) => (e, i))
            .Where(x => x.e is MineExploded { Result: InjuryOutcome.OutOfAction } m && u.Lines.Take(x.i).Any(l => l.Motion?.Model == m.Model))
            .Select(x => (u, x.i, ((MineExploded)x.e).Model)))];

    private static Func<ModelId, TablePoint?> DrawnAt(MatchState state) =>
        id => state.FindModel(id)?.Position is { } p ? TablePoint.Of(p) : null;

    private static TablePoint Point(MatchState state, ModelId id) => TablePoint.Of(state.FindModel(id)!.Position!.Value);

    [Fact]
    public void AModelMinedDuringItsMoveGoesWhereTheServerHaltedIt()
    {
        var cases = MinedMidMove();
        var travelled = 0;
        var halts = new HashSet<string>();
        foreach (var (update, exploded, model) in cases)
        {
            // The server's place for it: where the state had it just before the mine went off (the halt).
            var halt = Point(exploded == 0 ? update.Before : update.After[exploded - 1], model);
            var change = Assert.Single(update.Lines[exploded].Changes, c => c.Model == model);
            Assert.Equal((ModelStatus.OutOfAction, (TablePoint?)halt), (change.To, change.At));
            Assert.Null(update.After[exploded].FindModel(model)!.Position);
            var motion = update.Lines.Take(exploded).Last(l => l.Motion?.Model == model).Motion!;
            Assert.Equal(halt, motion.Path[^1]);
            halts.Add(string.Join(" ", update.Events.Select(e => e.GetType().Name)));

            var plan = Choreography.Of(update.Lines, DrawnAt(update.Before));
            var casualty = Assert.Single(plan.Beats, b => b.Kind == BeatKind.Casualty && b.Model == model);
            Assert.Equal(halt, casualty.At);
            Assert.True(casualty.Hit, "a standing model hit by a mine falls before it goes");
            var fast = Choreography.Of(update.Lines, DrawnAt(update.Before), AnimationSpeed.Fast);
            var fastCasualty = Assert.Single(fast.Beats, b => b.Kind == BeatKind.Casualty && b.Model == model);
            Assert.Equal(halt, fastCasualty.At);
            if (Point(update.Before, model) == halt)
            {
                // Halted where it stood (its base already came within 3" of the marker): nothing to travel, it falls there.
                Assert.DoesNotContain(plan.Beats, b => b.Kind == BeatKind.Glide && b.Model == model);
            }
            else
            {
                // The move took it there: its glide ends at the halt and the casualty falls only once it has arrived.
                travelled++;
                var glide = plan.Beats.Last(b => b.Kind == BeatKind.Glide && b.Model == model);
                Assert.Equal(halt, glide.Motion!.Path[^1]);
                Assert.True(casualty.Start >= glide.End - 1e-9, $"the casualty starts at {casualty.Start} before its glide ends at {glide.End}");
                var fastGlide = fast.Beats.Last(b => b.Kind == BeatKind.Glide && b.Model == model);
                Assert.True(fastCasualty.Start >= fastGlide.End - 1e-9);
            }

            Assert.DoesNotContain(Choreography.Of(update.Lines, DrawnAt(update.Before), AnimationSpeed.Off).Beats, b => b.Kind == BeatKind.Casualty);
        }
        // Moves, Dashes, charges and moves resumed after a dud or a survived mine all came up.
        Assert.True(travelled >= 5, $"only {travelled} of {cases.Count} mined models travelled first");
        Assert.Contains(halts, h => h.StartsWith("DashDeclared", StringComparison.Ordinal));
        Assert.Contains(halts, h => h.StartsWith("ChargeDeclared", StringComparison.Ordinal));
        Assert.Contains(halts, h => h.Contains("MoveResumed", StringComparison.Ordinal));
    }

    // Finishing at once mid-move (a command sent, Esc, a catch-up): every line still due comes out in order and nothing is
    // left playing; what is handed out to pose models (the glide and the casualty) still names the halt.
    [Fact]
    public void FinishingAtOnceMidMoveShowsEverythingAndLeavesNothing()
    {
        foreach (var (update, exploded, model) in MinedMidMove())
        {
            var queue = new PlaybackQueue();
            var moves = new List<Beat>();
            var start = queue.Add(Choreography.Of(update.Lines, DrawnAt(update.Before)), 10, moves);
            if (moves.LastOrDefault(b => b.Kind == BeatKind.Glide && b.Model == model) is not { } glide)
            {
                continue;
            }
            var casualty = Assert.Single(moves, b => b.Kind == BeatKind.Casualty && b.Model == model);
            Assert.True(casualty.Start >= glide.End - 1e-9);
            Assert.True(start <= glide.Start);
            var taken = new List<PlaybackQueue.Item>();
            var midMove = (glide.Start + glide.End) / 2;
            queue.TakeDue(midMove, taken);
            queue.TakeAll(midMove, taken);
            Assert.Equal(update.Lines.Select(l => l.Seq), taken.Where(i => i.Line is not null).Select(i => i.Line!.Seq));
            Assert.Equal(0, queue.Count);
            Assert.Equal(0, queue.Behind(midMove));
        }
    }

    // Every other model put Out of Action (shots, blows, blasts, a mine that went off where the model stood) goes where
    // the state had it just before: for a model that did not move in that update, where it was drawn.
    [Fact]
    public void EveryCasualtyIsAtTheServersLastPlaceForIt()
    {
        var seen = 0;
        foreach (var update in Updates.Value)
        {
            for (var i = 0; i < update.Lines.Count; i++)
            {
                foreach (var change in update.Lines[i].Changes.Where(c => c.To == ModelStatus.OutOfAction))
                {
                    var before = i == 0 ? update.Before : update.After[i - 1];
                    Assert.Equal(before.FindModel(change.Model)!.Position is { } p ? TablePoint.Of(p) : null, change.At);
                    if (!update.Lines.Take(i).Any(l => l.Motion?.Model == change.Model))
                    {
                        Assert.Equal(DrawnAt(update.Before)(change.Model), change.At);
                    }
                    seen++;
                }
            }
        }
        Assert.True(seen > 10, $"only {seen} models put Out of Action");
    }
}
