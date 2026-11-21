using Client.Core;
using Rules.Core;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.Scenarios;
using Rules.Core.State;
using Sim;

namespace Client.Tests;

// Animation stage A4: explosions, grenades, scattered attacks and marker bursts come only from the server's events and
// state. Over bot-played matches with BLAST and SCATTER guns, SHRAPNEL, GAS and FIRE weapons, grenades (BLAST, GAS) and a
// Martyrdom Device: every blast is centred where the event says (on the model it hit, where a scattered attack landed, on
// the device's bearer) with its weapon's radius; every Injury Roll with extra markers bursts once per marker; every
// grenade attack is thrown; and the battlefield plays them in order (a grenade lobbed instead of fired, a scattered
// attack flying from its target to where it landed, the explosion with its line, the bursts with theirs).
public class BattleEffectsTests
{
    private static ModelProfile Armed(ModelProfile p, int i, int seed) =>
        new(p.Movement, p.BaseRadius, p.RangedRange, p.RangedInjuryModifier, p.Armour, p.HasMeleeWeapon, p.MeleeInjuryModifier)
        {
            RangedAlsoMelee = p.RangedAlsoMelee,
            RangedWeaponBlast = p.HasRangedWeapon && i % 2 == 0 ? 1 + (seed + i) % 3 : 0,
            RangedWeaponKeywords = !p.HasRangedWeapon ? WeaponKeywords.None
                : (i % 3 == 0 ? WeaponKeywords.Shrapnel : WeaponKeywords.None) | (seed % 2 == 0 && i % 2 == 0 ? WeaponKeywords.Scatter : WeaponKeywords.None),
            MeleeWeaponKeywords = p.HasMeleeWeapon && i % 2 == 1 ? WeaponKeywords.Fire : WeaponKeywords.None,
            Grenade = i % 3 != 2 ? new Weapon(Length.Parse("8"), keywords: WeaponKeywords.Gas | WeaponKeywords.IgnoreLongRange | WeaponKeywords.Assault) { Blast = i % 2 } : null,
            Martyrdom = i == 1,
        };

    private static readonly Lazy<List<(MatchState Before, GameEvent Event, MatchState After)>> Events = new(() =>
    {
        var all = new List<(MatchState, GameEvent, MatchState)>();
        foreach (var file in new[] { "melee-warbands.json", "test-warbands.json" })
        {
            var warbands = Content.Load(Path.Combine(Repository.Root, "content", file));
            for (var seed = 0; seed < 8; seed++)
            {
                var one = warbands[seed % 2];
                var two = warbands[(seed + 1) % 2];
                var models = one.Models.Select((m, i) => new SetupModel(new ModelId($"a{i + 1}"), Seat.One, Armed(m.Profile, i, seed)))
                    .Concat(two.Models.Select((m, i) => new SetupModel(new ModelId($"b{i + 1}"), Seat.Two, Armed(m.Profile, i + 1, seed))));
                var created = new MatchCreated(ClaimNoMansLand.CreateSetup(models));
                var state = Reducer.Apply(MatchState.None, created);
                all.Add((MatchState.None, created, state));
                var bot = new Bot(new Random(seed));
                var dice = new SeededDice(seed * 31 + 7);
                for (var step = 0; step < 3000 && state.Phase != Phase.Ended; step++)
                {
                    var next = bot.Propose(state).Select(c => TurnSequence.Decide(state, c, dice)).FirstOrDefault(r => r.IsAccepted);
                    Assert.NotNull(next);
                    foreach (var e in next.Events)
                    {
                        var before = state;
                        state = Reducer.Apply(state, e);
                        all.Add((before, e, state));
                    }
                }
            }
        }
        return all;
    });

    // The match as the client's log lines, with the attack each event belongs to, cut into updates of a few events.
    private static List<(LogLine Line, GameEvent Event, MatchState Before, MatchState After)> Lines()
    {
        var lines = new List<(LogLine, GameEvent, MatchState, MatchState)>();
        ResolutionLog? resolutions = null;
        BattleEffectsTracker? tracker = null;
        var seq = 0;
        foreach (var (before, e, after) in Events.Value)
        {
            if (e is MatchCreated)
            {
                resolutions = new ResolutionLog();
                tracker = new BattleEffectsTracker();
                seq = 0;
            }
            seq++;
            resolutions!.Add(seq, e, after.Setup);
            var (kind, weapon) = resolutions.AttackAt(seq);
            var line = new LogLine(seq, "", null, Motion: Motions.Of(e, after), Models: EventModels.Of(e)) { Effects = EffectCues.Of(e), Fx = tracker!.Next(e, after, kind, weapon) };
            lines.Add((line, e, before, after));
        }
        return lines;
    }

    private static TablePoint PointOf(MatchState state, ModelId id) => TablePoint.Of(state.FindModel(id)!.Position!.Value);

    private static double BlastOf(MatchSetup setup, ModelId attacker, int weapon)
    {
        var p = setup.ProfileOf(attacker)!;
        return (weapon > 0 && p.Has(AttackKind.Ranged, weapon) ? p.Using(AttackKind.Ranged, weapon) : p).RangedWeaponBlast;
    }

    [Fact]
    public void EveryEffectIsWhereTheServerSaysAndOnlyThere()
    {
        var seen = new Dictionary<string, int>();
        void Saw(string what) => seen[what] = seen.GetValueOrDefault(what) + 1;
        var weapon = 0;
        foreach (var (line, e, before, after) in Lines())
        {
            var fx = line.Fx;
            if (e is RangedAttackDeclared declared)
            {
                weapon = declared.Weapon;
            }
            switch (e)
            {
                case RangedSuccessRolled:
                    Assert.Equal(weapon == ModelProfile.GrenadeWeapon, fx.Thrown);
                    Saw(fx.Thrown ? "thrown" : "fired");
                    break;
                case BlastHit b:
                    var blast = Assert.Single(fx.Blasts);
                    Assert.Equal(new BlastCue(BlastStyle.Weapon, PointOf(after, b.Target), BlastOf(after.Setup!, b.Attacker, weapon)), blast);
                    Saw(weapon == ModelProfile.GrenadeWeapon ? "grenade blast" : "BlastHit");
                    break;
                case AttackScattered a:
                    var scattered = Assert.Single(fx.Blasts);
                    Assert.Equal(TablePoint.Of(a.At), scattered.At);
                    Assert.Equal(a.Target is { } t ? PointOf(after, t) : null, scattered.From);
                    Assert.Equal(BlastOf(after.Setup!, a.Attacker, weapon), scattered.Radius);
                    Saw("AttackScattered");
                    break;
                case MartyrdomTriggered m:
                    Assert.Equal(new BlastCue(BlastStyle.Martyrdom, PointOf(after, m.Model), BattleEffectsTracker.MartyrdomRadius), Assert.Single(fx.Blasts));
                    Saw("MartyrdomTriggered");
                    break;
                case InjuryRolled i:
                    // One burst per extra marker, on the model the roll was for.
                    Assert.Equal(i.ExtraMarkers, fx.Markers.Count);
                    Assert.All(fx.Markers, mk => Assert.Equal(i.Target, mk.Model));
                    foreach (var mk in fx.Markers)
                    {
                        Saw(mk.Style.ToString());
                    }
                    Assert.Empty(fx.Blasts);
                    break;
                default:
                    Assert.Empty(fx.Blasts);
                    Assert.Empty(fx.Markers);
                    Assert.False(fx.Thrown);
                    Assert.Null(fx.Aim);
                    break;
            }
        }
        foreach (var what in new[] { "thrown", "fired", "BlastHit", "grenade blast", "AttackScattered", "MartyrdomTriggered", "Shrapnel", "Gas", "Fire" })
        {
            Assert.True(seen.GetValueOrDefault(what) > 0, $"{what}: {string.Join(", ", seen)}");
        }
    }

    [Fact]
    public void TheBattlefieldPlaysEachEffectInOrder()
    {
        var seen = new Dictionary<BeatKind, int>();
        var all = Lines();
        for (var start = 0; start < all.Count; start += 6)
        {
            var update = all.Skip(start).Take(6).ToList();
            var before = update[0].Before;
            var plan = Choreography.Of([.. update.Select(u => u.Line)], id => before.FindModel(id)?.Position is { } p ? TablePoint.Of(p) : null);
            Assert.True(plan.Length <= Choreography.MaxSeconds + 1e-9);
            foreach (var (line, e, _, _) in update)
            {
                var mine = plan.Beats.Where(b => b.Seq == line.Seq).ToList();
                var at = plan.Lines.Single(c => c.Line.Seq == line.Seq).At;
                foreach (var b in mine)
                {
                    seen[b.Kind] = seen.GetValueOrDefault(b.Kind) + 1;
                }
                if (line.Fx.Thrown && e is RangedSuccessRolled shot)
                {
                    // Lobbed, not fired: no flash or tracer; it lands with its line.
                    var lob = mine.Single(b => b.Kind == BeatKind.Lob);
                    Assert.Equal((shot.Attacker, (ModelId?)shot.Target), (lob.Model, lob.Other));
                    Assert.DoesNotContain(mine, b => b.Kind is BeatKind.MuzzleFlash or BeatKind.Tracer);
                    Assert.Equal(lob.End, at, 9);
                }
                if (line.Fx.Blasts.Count > 0)
                {
                    var blasts = mine.Where(b => b.Kind is BeatKind.Explosion or BeatKind.MineBlast or BeatKind.MartyrBlast).ToList();
                    Assert.Equal(line.Fx.Blasts.Select(x => (x.At, x.Radius)), blasts.Select(x => (x.At!.Value, x.Size)));
                    Assert.Equal(blasts[0].Start, at, 9);
                    foreach (var (cue, b) in line.Fx.Blasts.Zip(blasts))
                    {
                        if (cue.From is { } aimed)
                        {
                            // A scattered attack flies from its target to where it landed, then explodes.
                            var flight = mine.Single(x => x.Kind == BeatKind.Scatter);
                            Assert.Equal((aimed, cue.At), (flight.From!.Value, flight.At!.Value));
                            Assert.Equal(flight.End, b.Start, 9);
                        }
                    }
                    // The Injury Rolls it causes are read after it.
                    foreach (var later in plan.Lines.Where(c => c.Line.Seq > line.Seq))
                    {
                        Assert.True(later.At >= at - 1e-9);
                    }
                }
                foreach (var marker in line.Fx.Markers)
                {
                    var kind = marker.Style switch { MarkerStyle.Gas => BeatKind.Gas, MarkerStyle.Fire => BeatKind.Fire, _ => BeatKind.Shrapnel };
                    var burst = mine.Single(b => b.Kind == kind);
                    Assert.Equal((marker.Model, at), (burst.Model, burst.Start));
                }
            }
        }
        foreach (var kind in new[] { BeatKind.Lob, BeatKind.Explosion, BeatKind.MartyrBlast, BeatKind.Scatter, BeatKind.Shrapnel, BeatKind.Gas, BeatKind.Fire })
        {
            Assert.True(seen.GetValueOrDefault(kind) > 0, $"{kind}: {string.Join(", ", seen)}");
        }
    }

    // A Mine Marker leaves the state when it is revealed; the explosion is still where it lay, with its SHRAPNEL marker.
    [Fact]
    public void AMineExplodesWhereItsMarkerLay()
    {
        // The twelve markers the defender lays (p. 162); mine-3 (the third) lies at (20, 5).
        var markers = Enumerable.Range(0, Mines.Count).Select(i => new MineMarker(Mines.IdOf(i), Previews.PointAt(20, 1 + 2 * i))).ToList();
        var laid = MatchState.None with { Mines = [.. markers] };
        var tracker = new BattleEffectsTracker();
        Assert.Empty(tracker.Next(new MinesLaid(Seat.Two, [.. markers], []), laid, null, 0).Blasts);
        var revealed = MatchState.None;
        tracker.Next(new MineRevealed("mine-3", new ModelId("a1"), live: true), revealed, null, 0);
        var fx = tracker.Next(new MineExploded(new ModelId("a1"), "mine-3", Rules.Core.Dice.InjuryRoll.Roll(new SeededDice(2))), revealed, null, 0);
        Assert.Equal(new BlastCue(BlastStyle.Mine, new TablePoint(20, 5), BattleEffectsTracker.MineBlastRadius), Assert.Single(fx.Blasts));
        Assert.Equal(new MarkerCue(new ModelId("a1"), MarkerStyle.Shrapnel), Assert.Single(fx.Markers));
        // It comes with its line; the burst too.
        var line = new LogLine(5, "", null, Models: [new ModelId("a1")]) { Fx = fx };
        var plan = Choreography.Of([line], _ => null);
        Assert.Equal([BeatKind.MineBlast, BeatKind.Shrapnel], plan.Beats.Select(b => b.Kind));
        Assert.Equal(new TablePoint(20, 5), plan.Beats[0].At);
        Assert.All(plan.Beats, b => Assert.Equal(0, b.Start));
    }

    // An attack at a point: the attacker turns to the point, then fires or throws at it; dust where it fell short; the
    // blast it makes when it hits comes with its own event. Off plays none of it.
    [Fact]
    public void AnAttackAtAPointTurnsToItAndFiresOrThrows()
    {
        var a = new ModelId("a1");
        var point = new TablePoint(12, 9);
        var fired = new LogLine(3, "", null, Models: [a]) { Fx = new BattleFx(false, new AimCue(a, point, true), [], []) };
        var plan = Choreography.Of([fired], _ => null);
        Assert.Equal([BeatKind.Turn, BeatKind.MuzzleFlash, BeatKind.Tracer], plan.Beats.Select(b => b.Kind));
        Assert.All(plan.Beats, b => Assert.Equal(point, b.At));
        Assert.Equal(Choreography.TurnSeconds + Choreography.ImpactAt, plan.Lines.Single().At, 9);
        var thrown = new LogLine(3, "", null, Models: [a]) { Fx = new BattleFx(true, new AimCue(a, point, false), [], []) };
        var lobbed = Choreography.Of([thrown, new LogLine(4, "", null, Models: [a]) { Fx = new BattleFx(false, null, [new BlastCue(BlastStyle.Weapon, new TablePoint(14, 9), 2, point)], []) }], _ => null);
        Assert.Equal([BeatKind.Turn, BeatKind.Lob, BeatKind.Dust, BeatKind.Scatter, BeatKind.Explosion], lobbed.Beats.Select(b => b.Kind));
        var land = lobbed.Beats[1].End;
        Assert.Equal(land, lobbed.Beats[2].Start, 9);
        Assert.Equal(land + Choreography.ThrownSettle, lobbed.Beats[3].Start, 9);
        Assert.Equal(lobbed.Beats[3].End, lobbed.Beats[4].Start, 9);
        Assert.Equal([land, lobbed.Beats[4].Start], lobbed.Lines.Select(c => c.At));
        // Off: only the instant turn to the point (stage A5); nothing flies or shows.
        Assert.Equal([(BeatKind.Turn, 0.0)], Choreography.Of([thrown], _ => null, AnimationSpeed.Off).Beats.Select(b => (b.Kind, b.Seconds)));
    }
}
