using System.Diagnostics;
using Client.Core;
using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;
using Sim;

namespace Client.Tests;

// The battlefield's movement area, the decision bar (Blood Markers), the notes above models and the small test rosters.
// Every state comes from simulated matches; TurnSequence.Decide is called only here, never by the client.
public class BattlefieldUxTests
{
    private static List<MatchState> States => PreviewAgreementTests.States.Value;

    private static IEnumerable<MatchState> ActivationStates() =>
        States.Where(s => s.Phase == Phase.Activation && s.Pending is null && s.ActiveModel is { } a && s.FindModel(a)?.Position is not null);

    private static bool Accepted(MatchState state, Command command) => TurnSequence.Decide(state, command, new SeededDice(1)).IsAccepted;

    // The area is exactly the grid points the server would accept as the end of one straight leg: every point in it is
    // accepted, and every grid point near it that is left out is refused.
    [Fact]
    public void TheMovementAreaIsWhereTheServerAcceptsTheMove()
    {
        var checkedStates = 0;
        var watch = Stopwatch.StartNew();
        var areas = 0L;
        foreach (var state in ActivationStates().Where((_, i) => i % 23 == 0).Take(40))
        {
            var model = state.FindModel(state.ActiveModel!.Value)!;
            foreach (var kind in new[] { PreviewKind.Move, PreviewKind.Dash, PreviewKind.Retreat })
            {
                var area = Previews.Reachable(state, model.Seat, kind, model.Id, []);
                areas++;
                var inArea = area.ToHashSet();
                var at = TablePoint.Of(model.Position!.Value);
                var reach = Previews.Reach(state, model.Seat, model.Id) ?? 0;
                // Every point in the area, and a ring of grid points around the edge of the allowance.
                foreach (var p in area.Where((_, i) => i % 7 == 0))
                {
                    Assert.True(Accepted(state, Command(kind, model, p)), $"{kind} area point {p} refused by the server");
                }
                for (var x = Math.Max(0, Math.Round((at.X - reach - 1) * 2) / 2); x <= Math.Min(36, at.X + reach + 1); x += 1.5)
                {
                    for (var y = Math.Max(0, Math.Round((at.Y - reach - 1) * 2) / 2); y <= Math.Min(36, at.Y + reach + 1); y += 1.5)
                    {
                        var p = new TablePoint(x, y);
                        if (p == at)
                        {
                            continue;
                        }
                        Assert.True(inArea.Contains(p) == Accepted(state, Command(kind, model, p)), $"{kind} at {p}: area {(inArea.Contains(p) ? "has" : "leaves out")} it");
                    }
                }
            }
            checkedStates++;
        }
        Assert.True(checkedStates >= 20, $"only {checkedStates} states");
        // Worked out once per action (not per mouse movement); it must stay quick enough for that.
        Assert.True(watch.Elapsed.TotalSeconds / areas < 2, $"{watch.Elapsed.TotalSeconds / areas:0.00} s per area (with the checks)");
    }

    [Fact]
    public void TheAreaFollowsTheWaypointsAndWhatIsLeftOfTheAllowance()
    {
        var state = ActivationStates().First(s => s.Actions == ActionsTaken.None && !s.MovementHalved
            && Previews.ActionOpen(s, s.FindModel(s.ActiveModel!.Value)!.Seat, s.ActiveModel!.Value, PreviewKind.Move) is null);
        var model = state.FindModel(state.ActiveModel!.Value)!;
        var interaction = new Interaction();
        interaction.Update(state, model.Seat);
        interaction.Start(InteractionMode.Move);
        var full = interaction.ReachArea();
        Assert.NotEmpty(full);
        Assert.Same(full, interaction.ReachArea());

        var at = TablePoint.Of(model.Position!.Value);
        var first = full.OrderBy(p => Math.Pow(Math.Abs(p.X - at.X) - 2, 2) + Math.Pow(p.Y - at.Y, 2)).First();
        Assert.Equal(ClickResult.Waypoint, interaction.ClickAt(first.X, first.Y, waypoint: true));
        var rest = interaction.ReachArea();
        Assert.NotSame(full, rest);
        // After the first leg less is left: no point of the rest is further from the waypoint than the allowance left.
        var used = Previews.Path(state, model.Seat, PreviewKind.Move, model.Id, [Previews.PointAt(first.X, first.Y)]).Path[^1].Inches;
        var left = Previews.Reach(state, model.Seat, model.Id)!.Value - used;
        Assert.All(rest, p => Assert.True(Math.Sqrt(Math.Pow(p.X - first.X, 2) + Math.Pow(p.Y - first.Y, 2)) <= left + 1e-9));
        Assert.True(left < Previews.Reach(state, model.Seat, model.Id));
    }

    // The rules (1.0.2 p. 34) give a Move and a Dash each the model's whole Movement, halved after standing up; a Shoot
    // does not use movement. So after shooting the Move area is the full one, and a Dash's range is the Move's.
    [Fact]
    public void TheRangeIsTheRulesAllowanceForMoveDashAndAfterShooting()
    {
        var afterShot = ActivationStates().First(s => s.Actions == ActionsTaken.Shoot);
        var shooter = afterShot.FindModel(afterShot.ActiveModel!.Value)!;
        var movement = TableRect.Inches(afterShot.Setup!.ProfileOf(shooter.Id)!.Movement);
        Assert.Equal(afterShot.MovementHalved ? movement / 2 : movement, Previews.Reach(afterShot, shooter.Seat, shooter.Id));
        Assert.Null(Previews.ActionOpen(afterShot, shooter.Seat, shooter.Id, PreviewKind.Move));

        // After a Move, the Move is closed (no area) and the Dash has its own full area from where the model now stands.
        var afterMove = ActivationStates().First(s => s.Actions == ActionsTaken.Move
            && Previews.ActionOpen(s, s.FindModel(s.ActiveModel!.Value)!.Seat, s.ActiveModel!.Value, PreviewKind.Dash) is null);
        var mover = afterMove.FindModel(afterMove.ActiveModel!.Value)!;
        Assert.Empty(Previews.Reachable(afterMove, mover.Seat, PreviewKind.Move, mover.Id, []));
        var dash = Previews.Reachable(afterMove, mover.Seat, PreviewKind.Dash, mover.Id, []);
        Assert.NotEmpty(dash);
        var now = TablePoint.Of(mover.Position!.Value);
        var reach = Previews.Reach(afterMove, mover.Seat, mover.Id)!.Value;
        Assert.All(dash, p => Assert.True(Math.Sqrt(Math.Pow(p.X - now.X, 2) + Math.Pow(p.Y - now.Y, 2)) <= reach + 1e-9));
    }

    [Fact]
    public void BloodMarkerDecisionsAreOnABarNextToTheModelHoldingThem()
    {
        var found = 0;
        foreach (var state in States.Where(s => s.Pending is { Kind: DecisionKind.SpendMarkersOnSuccessRoll or DecisionKind.SpendMarkersOnInjuryRoll }).Take(30))
        {
            var pending = state.Pending!;
            var interaction = new Interaction();
            interaction.Update(state, pending.Seat);
            var bar = interaction.Bar(null);
            var holder = pending.Kind == DecisionKind.SpendMarkersOnSuccessRoll ? state.InProgress!.Actor : state.InProgress!.Target!.Value;
            Assert.Equal(holder, bar.Anchor);
            var legal = TurnSequence.LegalCommands(state).Where(c => c is SpendMarkers m && m.Seat == pending.Seat).ToList();
            Assert.Equal(legal, bar.Items.Select(i => i.Command));
            Assert.All(bar.Items, i => Assert.Equal(BarKind.Decide, i.Kind));
            Assert.Equal("No Blood", bar.Items[0].Label);
            Assert.All(bar.Items.Skip(1), i => Assert.Matches("^Spend [1-6] Blood$", i.Label));
            Assert.All(bar.Items, i => Assert.True(Accepted(state, i.Command!)));
            // The other player sees no such bar.
            interaction.Update(state, pending.Seat.Opponent());
            Assert.Empty(interaction.Bar(null).Items);
            found++;
        }
        Assert.True(found > 0, "no Blood Marker decision in the sample");
    }

    [Fact]
    public void OtherDecisionsAboutAModelAreOnItsBarToo()
    {
        var seen = new HashSet<DecisionKind>();
        foreach (var state in States.Where(s => s.Pending is { Kind: DecisionKind.FreeFight or DecisionKind.ChooseFriendlyTarget }).Take(30))
        {
            var interaction = new Interaction();
            interaction.Update(state, state.Pending!.Seat);
            var bar = interaction.Bar(null);
            Assert.NotEmpty(bar.Items);
            Assert.Equal(TurnSequence.LegalCommands(state).Where(c => Match.Protocol.CommandSeats.Of(c) == state.Pending.Seat), bar.Items.Select(i => i.Command));
            seen.Add(state.Pending.Kind);
        }
        Assert.Contains(DecisionKind.FreeFight, seen);
    }

    [Fact]
    public void EventsGiveShortNotesAboveTheRightModel()
    {
        var a = new ModelId("a1");
        var b = new ModelId("b1");
        Assert.Empty(UnitNotes.Of(new TurnStarted(1)));
        Assert.Equal([new UnitNote(a, "STANDS UP", NoteTone.Info)], UnitNotes.Of(new ModelStoodUp(a)));
        Assert.Equal([new UnitNote(a, "2 BLOOD SPENT", NoteTone.Info)], UnitNotes.Of(new MarkersSpent(Seat.Two, a, 2, MarkerSpendPurpose.ReduceSuccessRoll)));
        Assert.Empty(UnitNotes.Of(new MarkersSpent(Seat.Two, a, 0, MarkerSpendPurpose.ReduceSuccessRoll)));
        Assert.Equal([new UnitNote(a, "CHARGE +4\"", NoteTone.Info)], UnitNotes.Of(new ChargeRolled(a, b, 4)));

        // Every roll event of whole simulated matches gives notes about the models it names, and the words match the dice.
        var events = Play(ScenarioSetups.FromFolder(Path.Combine(Repository.Root, "content"))["melee"], 3).Log;
        var kinds = new HashSet<string>();
        foreach (var e in events)
        {
            foreach (var note in UnitNotes.Of(e))
            {
                kinds.Add(note.Text.Split(' ')[0]);
                switch (e)
                {
                    case InjuryRolled i:
                        Assert.Equal(i.Target, note.Model);
                        Assert.StartsWith(i.Injury.Outcome switch
                        {
                            InjuryOutcome.NoEffect => "NO EFFECT",
                            InjuryOutcome.MinorHit => "MINOR HIT",
                            InjuryOutcome.Down => "DOWN",
                            _ => "OUT OF ACTION",
                        }, note.Text);
                        Assert.Equal(i.Injury.BloodMarkers > 0, note.Text.Contains("BLOOD"));
                        break;
                    case MeleeSuccessRolled m:
                        Assert.Equal(m.Attacker, note.Model);
                        Assert.Equal(m.Check.Outcome == SuccessOutcome.Failure, note.Text == "MISSED");
                        break;
                }
            }
        }
        Assert.Contains("MISSED", kinds);
    }

    [Theory]
    [InlineData("ranged", 4)]
    [InlineData("melee", 4)]
    [InlineData("ranged-1v1", 1)]
    [InlineData("ranged-2v2", 2)]
    [InlineData("ranged-3v3", 3)]
    [InlineData("melee-1v1", 1)]
    [InlineData("melee-2v2", 2)]
    [InlineData("melee-3v3", 3)]
    [InlineData("ranged-towers", 4)]
    [InlineData("melee-towers", 4)]
    [InlineData("ranged-1v1-towers", 1)]
    [InlineData("melee-3v3-towers", 3)]
    public void SmallTestRostersHaveTheFirstModelsOfEachSideAndPlayToTheEnd(string scenario, int size)
    {
        var all = ScenarioSetups.FromFolder(Path.Combine(Repository.Root, "content"));
        Assert.Equal(ContentNames.Choices.Order(), all.Keys.Order());
        var setup = all[scenario];
        Assert.Equal(size, setup.Models.Count(m => m.Seat == Seat.One));
        Assert.Equal(size, setup.Models.Count(m => m.Seat == Seat.Two));
        // The normal roster is the same setup as before the test sizes existed; a small one is its first models.
        var normal = all[scenario.Split('-')[0]];
        Assert.Equal(StateHash.Compute(Reducer.Apply(MatchState.None, new MatchCreated(ScenarioSetups.Setup(Path.Combine(Repository.Root, "content",
            scenario.StartsWith("ranged", StringComparison.Ordinal) ? "test-warbands.json" : "melee-warbands.json"))))), StateHash.Compute(Reducer.Apply(MatchState.None, new MatchCreated(normal))));
        Assert.All(setup.Models, m => Assert.Equal(normal.Models.Single(n => n.Id == m.Id).Profile, m.Profile));
        // The client names the models from its content for every size.
        var names = ContentNames.For(scenario, setup);
        Assert.Null(names.Problem);
        Assert.DoesNotContain("(a1)", names.Of(new ModelId("a1"))[..1]);
        // Whole matches with the simulator's bot reach their end.
        for (var seed = 0; seed < 4; seed++)
        {
            Assert.Equal(Phase.Ended, Play(setup, seed).State.Phase);
        }
    }

    // The create-match list names every scenario in words, the tower layouts included.
    [Theory]
    [InlineData("ranged", "ranged (normal roster)")]
    [InlineData("melee", "melee (shooting and close combat)")]
    [InlineData("melee-towers", "melee with towers (shooting and close combat)")]
    [InlineData("melee-2v2", "melee, 2 v 2 (test roster)")]
    [InlineData("ranged-towers", "ranged with towers (normal roster)")]
    [InlineData("melee-1v1-towers", "melee with towers, 1 v 1 (test roster)")]
    public void EveryScenarioHasALabel(string scenario, string label)
    {
        Assert.Equal(label, ContentNames.Label(scenario));
        Assert.All(ContentNames.Choices, c => Assert.DoesNotContain("-", ContentNames.Label(c)));
    }

    // A whole match between two simulator bots with seeded dice, as PreviewAgreementTests plays them.
    private static (MatchState State, List<GameEvent> Log) Play(MatchSetup setup, int seed)
    {
        var created = new MatchCreated(setup);
        var state = Reducer.Apply(MatchState.None, created);
        var log = new List<GameEvent> { created };
        var bot = new Bot(new Random(seed));
        var dice = new SeededDice(seed * 31 + 5);
        for (var step = 0; step < 3000 && state.Phase != Phase.Ended; step++)
        {
            var next = bot.Propose(state).Select(c => TurnSequence.Decide(state, c, dice)).FirstOrDefault(r => r.IsAccepted);
            Assert.NotNull(next);
            log.AddRange(next.Events);
            state = next.State!;
        }
        return (state, log);
    }

    private static Command Command(PreviewKind kind, ModelState model, TablePoint p) =>
        Previews.Path(MatchState.None, model.Seat, kind, model.Id, [Previews.PointAt(p.X, p.Y)]).Command;
}
