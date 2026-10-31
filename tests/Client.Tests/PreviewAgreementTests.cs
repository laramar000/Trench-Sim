using System.Collections.Immutable;
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
        // Each content file on the plain layout (12 seeds), then on the tower layout (towers proposal stage T3, 6 seeds).
        foreach (var (file, towers, seeds) in new[] { ("test-warbands.json", false, 12), ("melee-warbands.json", false, 12), ("test-warbands.json", true, 6), ("melee-warbands.json", true, 6) })
        {
            var warbands = Content.Load(Path.Combine(Repository.Root, "content", file));
            for (var seed = 0; seed < seeds; seed++)
            {
                var one = warbands[seed % 2];
                var two = warbands[(seed + 1) % 2];
                var models = one.Models.Select((m, i) => new SetupModel(new ModelId($"a{i + 1}"), Seat.One, m.Profile))
                    .Concat(two.Models.Select((m, i) => new SetupModel(new ModelId($"b{i + 1}"), Seat.Two, m.Profile)));
                var state = Reducer.Apply(MatchState.None, new MatchCreated(towers ? ClaimNoMansLand.CreateTowersSetup(models) : ClaimNoMansLand.CreateSetup(models)));
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
                // On the tower layout: the ladders themselves, up from the foot or down from the top.
                foreach (var path in LadderPaths(state, active))
                {
                    tally.Check(state, Previews.Path(state, active.Seat, kind, active.Id, path));
                }
            }
        }
        tally.Expect(PreviewKind.Move, ReasonCode.MoveIllegal, ReasonCode.NotYourModel, ReasonCode.ActionAlreadyTaken);
        tally.Expect(PreviewKind.Dash, ReasonCode.MoveIllegal, ReasonCode.ActionAlreadyTaken);
        tally.Expect(PreviewKind.Retreat, ReasonCode.NotEngaged, ReasonCode.MoveIllegal, ReasonCode.RetreatEndsEngaged);
    }

    // Towers proposal, stage T3: for each ladder of each tower, to its foot and up, and to its top and down.
    private static IEnumerable<List<Point2>> LadderPaths(MatchState state, ModelState model)
    {
        var field = state.Setup!.Scenario!.Battlefield;
        var radius = state.Setup.ProfileOf(model.Id)!.BaseRadius;
        foreach (var tower in field.Terrain.Where(t => t.IsTower))
        {
            foreach (var ladder in tower.Climb!.Ladders)
            {
                var foot = Climb.Foot(tower.Footprint.Bounds, ladder, radius);
                var top = Climb.Top(tower.Footprint.Bounds, ladder, radius);
                yield return [foot, top];
                yield return [top, foot];
            }
        }
    }

    // The simulated tower matches do use the towers: models climb, stand on a platform, and come down.
    [Fact]
    public void SimulatedTowerMatchesClimbTheTowers()
    {
        var on = 0;
        var maxOn = 0;
        foreach (var state in States.Value.Where(s => s.Setup?.Scenario?.Battlefield.Terrain.Any(t => t.IsTower) == true))
        {
            var field = state.Setup!.Scenario!.Battlefield;
            var count = state.Models.Count(m => m.Position is { } p && Towers.ElevationAt(field, p) > Length.Zero);
            on += count > 0 ? 1 : 0;
            maxOn = Math.Max(maxOn, count);
        }
        Console.WriteLine($"{on} states with a model on a tower; at most {maxOn} at once");
        Assert.True(on > 20, $"only {on} states have a model on a tower");
        Assert.True(maxOn >= 2, $"at most {maxOn} models were on towers at once");
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

    // XCOM-style UX, improvement 1: the dice an attack preview shows are the dice the rules then use. For every legal
    // Shoot and Fight preview, the rules decide its command with a few seeds: the declared penalty dice, the Success
    // Roll's dice and Risky, the Injury Roll's dice and modifier, and who is asked to spend Blood Markers all match.
    [Fact]
    public void AttackPreviewDiceAreTheRulesOwn()
    {
        var seen = new Dictionary<string, int>();
        void Saw(string what) => seen[what] = seen.GetValueOrDefault(what) + 1;
        foreach (var state in ActivationStates())
        {
            var active = state.FindModel(state.ActiveModel!.Value)!;
            foreach (var kind in new[] { PreviewKind.Shoot, PreviewKind.Fight })
            {
                foreach (var target in state.Models.Where(m => m.Seat != active.Seat))
                {
                    CheckDice(state, kind, active, target, Saw);
                }
            }
        }
        var summary = string.Join(", ", seen.OrderBy(p => p.Key).Select(p => $"{p.Key}: {p.Value}"));
        Assert.True(seen.GetValueOrDefault("Shoot penalty 0") > 0 && seen.Keys.Any(k => k.StartsWith("Shoot penalty ", StringComparison.Ordinal) && k != "Shoot penalty 0"), summary);
        Assert.True(seen.GetValueOrDefault("Fight penalty 0") > 0 && seen.GetValueOrDefault("markers on hit asked") > 0 && seen.GetValueOrDefault("markers on hit not asked") > 0, summary);
        Assert.True(seen.Keys.Count(k => k.StartsWith("injury modifier ", StringComparison.Ordinal)) > 1 && seen.GetValueOrDefault("injury bonus 0") > 0, summary);
        Console.WriteLine(summary);
    }

    // The checks for one Shoot or Fight preview against the rules' decisions of its command with a few seeds.
    private static void CheckDice(MatchState state, PreviewKind kind, ModelState active, ModelState target, Action<string> Saw)
    {
        var preview = Previews.Target(state, active.Seat, kind, active.Id, target.Id);
        if (!preview.Legal)
        {
            Assert.Null(preview.Dice);
            return;
        }
        var dice = preview.Dice!;
        Assert.Contains(dice.Lines(active.Id, target.Id), l => l.StartsWith("Hit roll: ", StringComparison.Ordinal));
        for (var seed = 1; seed <= 4; seed++)
        {
            var events = TurnSequence.Decide(state, preview.Command, new SeededDice(seed)).Events;
            var intoMelee = events.OfType<RangedAttackDeclared>().SingleOrDefault()?.IntoMelee == true;
            Assert.Equal(dice.HitPenalty, kind == PreviewKind.Shoot
                ? events.OfType<RangedAttackDeclared>().Single().PenaltyDice
                : events.OfType<MeleeAttackDeclared>().Single().PenaltyDice);
            Saw($"{kind} penalty {dice.HitPenalty}");
            var askedToSpend = events.OfType<DecisionRequested>().Any(d => d.Kind == DecisionKind.SpendMarkersOnSuccessRoll);
            if (!intoMelee)
            {
                Assert.Equal(dice.AttackerMarkers > 0, askedToSpend);
                Saw(askedToSpend ? "markers on hit asked" : "markers on hit not asked");
            }
            if (events.OfType<RangedSuccessRolled>().Select(r => r.Check).Concat(events.OfType<MeleeSuccessRolled>().Select(m => m.Check)).SingleOrDefault() is { } check
                && !intoMelee)
            {
                Assert.Equal(dice.HitNetDice, check.NetDice);
                Saw($"hit dice {dice.HitNetDice}");
                Assert.Equal(dice.Risky, check.Risky);
                Saw(dice.Risky ? "risky" : "not risky");
                var askedForInjury = events.OfType<DecisionRequested>().Any(d => d.Kind == DecisionKind.SpendMarkersOnInjuryRoll);
                if (check.Outcome != Rules.Core.Dice.SuccessOutcome.Failure && !check.RiskyFailure)
                {
                    Assert.Equal(dice.TargetMarkers > 0, askedForInjury);
                }
            }
            if (events.OfType<InjuryRolled>().SingleOrDefault() is { } injury && !intoMelee)
            {
                Assert.Equal(dice.InjuryModifier, injury.Injury.Modifier);
                var critical = events.Any(e => e is RangedSuccessRolled { Check.Outcome: Rules.Core.Dice.SuccessOutcome.CriticalSuccess }
                    or MeleeSuccessRolled { Check.Outcome: Rules.Core.Dice.SuccessOutcome.CriticalSuccess });
                Assert.Equal(dice.InjuryNetDice + (critical ? 1 : 0), injury.Injury.NetDice);
                Saw($"injury dice {dice.InjuryNetDice}");
                Saw($"injury bonus {dice.InjuryBonus}");
                Saw($"injury modifier {dice.InjuryModifier}");
            }
        }
    }

    // The same with the cases a simulated match rarely reaches: the attacker's warband Shaken (Risky rolls) and a Down
    // target in melee (+1 INJURY DICE). Blood Markers are cleared so the rolls are made without stopping to ask.
    [Fact]
    public void AttackPreviewDiceCoverShakenAndDownTargets()
    {
        var seen = new Dictionary<string, int>();
        void Saw(string what) => seen[what] = seen.GetValueOrDefault(what) + 1;
        foreach (var original in ActivationStates().Where((_, i) => i % 3 == 0))
        {
            var active = original.FindModel(original.ActiveModel!.Value)!;
            foreach (var target in original.Models.Where(m => m.Seat != active.Seat && m.Status == ModelStatus.Standing && m.Position is not null))
            {
                var models = original.Models.Select(m => m with { BloodMarkers = 0 }).Select(m => m.Id == target.Id ? m with { Status = ModelStatus.Down } : m).ToImmutableArray();
                var state = original with { Models = models };
                state = active.Seat == Seat.One ? state with { One = state.One with { Shaken = true } } : state with { Two = state.Two with { Shaken = true } };
                foreach (var kind in new[] { PreviewKind.Shoot, PreviewKind.Fight })
                {
                    CheckDice(state, kind, state.FindModel(active.Id)!, state.FindModel(target.Id)!, Saw);
                }
            }
        }
        var summary = string.Join(", ", seen.OrderBy(p => p.Key).Select(p => $"{p.Key}: {p.Value}"));
        Assert.True(seen.GetValueOrDefault("risky") > 50 && seen.GetValueOrDefault("injury bonus 1") > 10 && seen.GetValueOrDefault("Shoot penalty 0") + seen.GetValueOrDefault("Shoot penalty 1") > 0, summary);
        Console.WriteLine(summary);
    }

    // Roster stage R1: the same with the attacker's own dice. Every model's profile gets Ranged and Melee dice and
    // weapon DICE and INJURY DICE (some positive, some negative); the preview's dice are still the rules' own.
    [Fact]
    public void AttackPreviewDiceIncludeTheModelsOwnDice()
    {
        var seen = new Dictionary<string, int>();
        void Saw(string what) => seen[what] = seen.GetValueOrDefault(what) + 1;
        var index = 0;
        foreach (var original in ActivationStates().Where((_, i) => i % 4 == 0))
        {
            var setup = original.Setup!;
            var shift = index++ % 3 - 1; // -1, 0, +1 in turn
            var models = setup.Models.Select(m => m with { Profile = WithDice(m.Profile!, shift) });
            var state = original with
            {
                Setup = new MatchSetup(setup.RulesVersion, setup.ScenarioId, setup.TurnLimit, setup.Profile, setup.ZoneIds, models, setup.Scenario),
                Models = original.Models.Select(m => m with { BloodMarkers = 0 }).ToImmutableArray(),
            };
            var active = state.FindModel(state.ActiveModel!.Value)!;
            foreach (var kind in new[] { PreviewKind.Shoot, PreviewKind.Fight })
            {
                foreach (var target in state.Models.Where(m => m.Seat != active.Seat))
                {
                    CheckDice(state, kind, active, target, Saw);
                }
            }
        }
        var summary = string.Join(", ", seen.OrderBy(p => p.Key).Select(p => $"{p.Key}: {p.Value}"));
        Assert.True(seen.Keys.Count(k => k.StartsWith("hit dice ", StringComparison.Ordinal) && k != "hit dice 0") >= 2
            && seen.Keys.Count(k => k.StartsWith("injury dice ", StringComparison.Ordinal) && k != "injury dice 0") >= 2, summary);
        Console.WriteLine(summary);
    }

    internal static ModelProfile WithDice(ModelProfile p, int shift) =>
        new(p.Movement, p.BaseRadius, p.RangedRange, p.RangedInjuryModifier, p.Armour, p.HasMeleeWeapon, p.MeleeInjuryModifier)
        {
            RangedAlsoMelee = p.RangedAlsoMelee,
            RangedDice = 1 + shift,
            MeleeDice = 2 * shift,
            RangedWeaponDice = p.HasRangedWeapon ? shift : 0,
            RangedWeaponInjuryDice = p.HasRangedWeapon ? 1 : 0,
            MeleeWeaponDice = p.HasMeleeWeapon ? -shift : 0,
            MeleeWeaponInjuryDice = p.HasMeleeWeapon ? shift + 1 : 0,
        };

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
