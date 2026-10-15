using Client.Core;
using Rules.Core;
using Rules.Core.Events;
using Rules.Core.Scenarios;
using Rules.Core.Geometry;
using Rules.Core.State;
using Sim;

namespace Client.Tests;

// M7.4 stage 4b: a preview must agree with the server. Over whole simulated matches (both content files), every
// preview's command is decided by the rules exactly as the server would decide it: a "legal" preview's command is
// accepted, and a refused preview's command is refused with the same reason, words and rule reference. Only this test
// calls TurnSequence.Decide; the client never does (ClientAuthorityTest).
public class PreviewAgreementTests
{
    // The states a player can act in: the state after each accepted command, as the server holds it between commands
    // (never the half-way states inside one command's events). Played by the simulator's bot with seeded dice.
    internal static readonly Lazy<List<MatchState>> States = new(() =>
    {
        var all = new List<MatchState>();
        foreach (var file in new[] { "test-warbands.json", "melee-warbands.json" })
        {
            var warbands = Content.Load(Path.Combine(Repository.Root, "content", file));
            for (var seed = 0; seed < 12; seed++)
            {
                var one = warbands[seed % 2];
                var two = warbands[(seed + 1) % 2];
                var models = one.Models.Select((m, i) => new SetupModel(new ModelId($"a{i + 1}"), Seat.One, m.Profile))
                    .Concat(two.Models.Select((m, i) => new SetupModel(new ModelId($"b{i + 1}"), Seat.Two, m.Profile)));
                var state = Reducer.Apply(MatchState.None, new MatchCreated(ClaimNoMansLand.CreateSetup(models)));
                var bot = new Bot(new Random(seed));
                var dice = new SeededDice(seed * 31 + 5);
                all.Add(state);
                for (var step = 0; step < 3000 && state.Phase != Phase.Ended; step++)
                {
                    var next = bot.Propose(state).Select(c => TurnSequence.Decide(state, c, dice)).FirstOrDefault(r => r.IsAccepted);
                    Assert.NotNull(next);
                    state = next.State!;
                    all.Add(state);
                }
                Assert.Equal(Phase.Ended, state.Phase);
            }
        }
        return all;
    });

    private static readonly Seat[] Seats = [Seat.One, Seat.Two];

    [Fact]
    public void PlacementPreviewsAgreeWithTheServer()
    {
        var tally = new Tally();
        foreach (var state in States.Value.Where(s => s.Phase == Phase.Deployment && s.Setup?.Scenario is not null).Take(400))
        {
            foreach (var seat in Seats)
            {
                foreach (var model in state.Models.Where(m => m.Seat == seat).Take(3).Append(state.Models.First(m => m.Seat != seat)))
                {
                    for (var x = -1.0; x <= 37; x += 4.5)
                    {
                        for (var y = -1.0; y <= 37; y += 4.5)
                        {
                            tally.Check(state, Previews.Place(state, seat, model.Id, Previews.PointAt(x, y)));
                        }
                    }
                }
            }
        }
        tally.Expect(PreviewKind.Place, ReasonCode.PlacementIllegal, ReasonCode.NotYourTurn, ReasonCode.NotYourModel, ReasonCode.ModelAlreadyDeployed);
    }

    [Fact]
    public void MovePreviewsAgreeWithTheServer()
    {
        var tally = new Tally();
        var random = new Random(7);
        foreach (var state in ActivationStates())
        {
            var active = state.FindModel(state.ActiveModel!.Value)!;
            var at = TablePoint.Of(active.Position!.Value);
            foreach (var kind in new[] { PreviewKind.Move, PreviewKind.Dash, PreviewKind.Retreat })
            {
                for (var i = 0; i < 4; i++)
                {
                    var path = new List<Point2>();
                    var (x, y) = (at.X, at.Y);
                    for (var leg = random.Next(0, 3); leg >= 0; leg--)
                    {
                        x = Math.Clamp(x + random.Next(-12, 13) * 0.5, -0.5, 36.5);
                        y = Math.Clamp(y + random.Next(-12, 13) * 0.5, -0.5, 36.5);
                        path.Add(Previews.PointAt(x, y));
                    }
                    if (i == 0)
                    {
                        // A short step: more often legal.
                        path = [Previews.PointAt(Math.Clamp(at.X + 1, 0, 36), at.Y)];
                    }
                    foreach (var seat in Seats)
                    {
                        tally.Check(state, Previews.Path(state, seat, kind, active.Id, path));
                    }
                }
                tally.Check(state, Previews.Path(state, active.Seat, kind, active.Id, []));
            }
        }
        tally.Expect(PreviewKind.Move, ReasonCode.MoveIllegal, ReasonCode.NotYourModel, ReasonCode.ActionAlreadyTaken);
        tally.Expect(PreviewKind.Dash, ReasonCode.MoveIllegal, ReasonCode.ActionAlreadyTaken);
        tally.Expect(PreviewKind.Retreat, ReasonCode.NotEngaged, ReasonCode.MoveIllegal, ReasonCode.RetreatEndsEngaged);
    }

    [Fact]
    public void TargetPreviewsAgreeWithTheServer()
    {
        var tally = new Tally();
        foreach (var state in ActivationStates())
        {
            var active = state.FindModel(state.ActiveModel!.Value)!;
            foreach (var kind in new[] { PreviewKind.Shoot, PreviewKind.Charge, PreviewKind.Fight })
            {
                foreach (var target in state.Models)
                {
                    tally.Check(state, Previews.Target(state, active.Seat, kind, active.Id, target.Id));
                }
                tally.Check(state, Previews.Target(state, active.Seat.Opponent(), kind, active.Id, state.Models[0].Id));
            }
        }
        tally.Expect(PreviewKind.Shoot, ReasonCode.TargetNotEnemy, ReasonCode.NoLineOfSight, ReasonCode.TargetOutOfRange, ReasonCode.AttackerEngaged, ReasonCode.NoRangedWeapon);
        tally.Expect(PreviewKind.Charge, ReasonCode.TargetNotEnemy, ReasonCode.TargetOutOfRange, ReasonCode.NoLineOfSight, ReasonCode.ChargeRouteBlocked, ReasonCode.AlreadyEngaged);
        tally.Expect(PreviewKind.Fight, ReasonCode.TargetNotEnemy, ReasonCode.TargetNotInMelee, ReasonCode.NoMeleeWeapon);
    }

    [Fact]
    public void PreviewsOutsideTheirMomentAgreeWithTheServer()
    {
        var tally = new Tally();
        var someModel = States.Value.Last().Models[0].Id;
        foreach (var state in States.Value.Where((_, i) => i % 9 == 0))
        {
            foreach (var seat in Seats)
            {
                tally.Check(state, Previews.Place(state, seat, someModel, Previews.PointAt(18, 18)));
                tally.Check(state, Previews.Path(state, seat, PreviewKind.Move, someModel, [Previews.PointAt(18, 18)]));
                tally.Check(state, Previews.Target(state, seat, PreviewKind.Shoot, someModel, someModel));
            }
        }
        tally.ExpectAny(ReasonCode.WrongPhase, ReasonCode.DecisionPending, ReasonCode.MatchEnded, ReasonCode.NoActivationInProgress, ReasonCode.ActivationInProgress);
    }

    // States where a model is activating and no decision is pending: the moments the action previews are for.
    private static IEnumerable<MatchState> ActivationStates() =>
        States.Value.Where(s => s.Phase == Phase.Activation && s.Pending is null && s.ActiveModel is { } a && s.FindModel(a)?.Position is not null);

    private sealed class Tally
    {
        private readonly Dictionary<PreviewKind, (int Legal, HashSet<ReasonCode> Refused)> _seen = [];
        private readonly HashSet<ReasonCode> _all = [];

        public int Total { get; private set; }

        public void Check(MatchState state, Preview preview)
        {
            Total++;
            var decided = TurnSequence.Decide(state, preview.Command, new SeededDice(1));
            Assert.True(decided.IsAccepted == preview.Legal, $"{preview.Command}: preview {(preview.Legal ? "legal" : preview.Refusal!.Text)}, server {(decided.IsAccepted ? "accepted" : decided.Rejection!.Text)}");
            Assert.Equal(decided.Rejection, preview.Refusal);
            Assert.Contains(Preview.Caveat, preview.Headline);
            var entry = _seen.TryGetValue(preview.Kind, out var e) ? e : (Legal: 0, Refused: new HashSet<ReasonCode>());
            if (preview.Refusal is { } refusal)
            {
                entry.Refused.Add(refusal.Code);
                _all.Add(refusal.Code);
            }
            else
            {
                entry.Legal++;
            }
            _seen[preview.Kind] = entry;
        }

        // The sample really tried legal commands and the refusals that matter for this kind.
        public void Expect(PreviewKind kind, params ReasonCode[] reasons)
        {
            Assert.True(_seen.TryGetValue(kind, out var entry), $"no {kind} previews");
            Assert.True(Total >= 1000, $"only {Total} previews checked");
            Assert.True(entry.Legal > 0, $"no legal {kind} preview in the sample");
            Assert.All(reasons, r => Assert.Contains(r, entry.Refused));
        }

        public void ExpectAny(params ReasonCode[] reasons) => Assert.All(reasons, r => Assert.Contains(r, _all));
    }
}
