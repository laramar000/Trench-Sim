using Rules.Core;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.State;
using Sim;

namespace Server.Tests;

// Glorious Deeds (U96, 2026-10-26) in whole matches played through the server: a second reading of each match's log,
// written apart from Deeds.Track, works out which deeds were completed, by whom and after which event, and the log must
// announce exactly those, each right after the event that completed it, and count 1 Victory Point for each.
public class GloriousDeedLogTests
{
    private static List<GameEvent> Played(string scenario, int seed)
    {
        var harness = new Harness(seed);
        var (one, two) = harness.Pair(scenario);
        Play.ToEnd(one, two, seed);
        return [.. harness.Lobby.LogOf(one.Welcome!.Code)!.Select(p => p.Event)];
    }

    // The deeds the log should announce, as (index of the event that completed it, the deed). The announcements in the
    // log are applied, as they must be, but otherwise passed over: only the other events are read.
    private static List<(int After, GloriousDeedCompleted Deed)> Expected(List<GameEvent> log)
    {
        var expected = new List<(int, GloriousDeedCompleted)>();
        var state = MatchState.None;
        var done = new HashSet<GloriousDeed>();
        ModelId? active = null;
        var beganDown = false;
        var retreatedFrom = new HashSet<ModelId>();
        var retreating = new HashSet<ModelId>();
        ModelId? chargedInTrench = null;
        var meleeKills = new Dictionary<ModelId, int>();
        var mineRolls = new Dictionary<ModelId, int>();
        (AttackKind Kind, bool Free, bool LongRange, bool Friendly) attack = default;
        for (var i = 0; i < log.Count; i++)
        {
            var e = log[i];
            var before = state;
            state = Reducer.Apply(state, e);
            if (e is GloriousDeedCompleted)
            {
                continue;
            }
            var deeds = Deeds.For(state.Setup);
            void Done(GloriousDeed deed, Seat seat, ModelId? model)
            {
                if (deeds.Contains(deed) && done.Add(deed))
                {
                    expected.Add((i, new GloriousDeedCompleted(seat, deed, model)));
                }
            }
            switch (e)
            {
                case TurnStarted:
                    meleeKills.Clear();
                    break;
                case ActivationStarted a:
                    (active, beganDown, chargedInTrench) = (a.Model, false, null);
                    retreatedFrom.Clear();
                    break;
                case ModelStoodUp:
                    beganDown = true;
                    break;
                case RangedAttackDeclared r:
                    attack = (AttackKind.Ranged, false, r.LongRange, false);
                    break;
                case FriendlyTargetChosen f:
                    attack = (AttackKind.Ranged, false, f.LongRange, true);
                    break;
                case MeleeAttackDeclared m:
                    attack = (AttackKind.Melee, m.FreeFight, false, false);
                    break;
                case RetreatDeclared r:
                    retreating = [.. Engagement.Pairs(before).Where(p => p.One == r.Model || p.Two == r.Model).Select(p => p.One == r.Model ? p.Two : p.One)];
                    break;
                case RetreatResolved r:
                    if (r.Moved)
                    {
                        retreatedFrom.UnionWith(retreating);
                    }
                    retreating.Clear();
                    break;
                case ChargeMoved { Reached: true } c:
                    var field = state.Setup!.Scenario!.Battlefield;
                    var at = state.FindModel(c.Target)!.Position!.Value;
                    var body = new ModelBody(c.Target.Value, at, state.Setup.ProfileOf(c.Target)!.BaseRadius, Towers.ElevationAt(field, at));
                    if (Trenches.Of(field, body) is not null)
                    {
                        chargedInTrench = c.Target;
                    }
                    break;
                case InjuryRolled x when !attack.Friendly:
                    var seat = state.FindModel(x.Attacker)!.Seat;
                    if (before.FindModel(x.Target)!.BloodMarkers < 6 && state.FindModel(x.Target)!.BloodMarkers == 6)
                    {
                        Done(GloriousDeed.Bloodletting, seat, x.Attacker);
                    }
                    if (x.Injury.Outcome == InjuryOutcome.OutOfAction)
                    {
                        var own = x.Attacker == active && !attack.Free;
                        if (attack.Kind == AttackKind.Melee && (meleeKills[x.Attacker] = meleeKills.GetValueOrDefault(x.Attacker) + 1) == 2)
                        {
                            Done(GloriousDeed.LordOfWar, seat, x.Attacker);
                        }
                        if (attack.Kind == AttackKind.Ranged && attack.LongRange)
                        {
                            Done(GloriousDeed.GoodHunting, seat, x.Attacker);
                        }
                        if (own && beganDown)
                        {
                            Done(GloriousDeed.ResistAndBite, seat, x.Attacker);
                        }
                        if (own && retreatedFrom.Contains(x.Target))
                        {
                            Done(GloriousDeed.Headshot, seat, x.Attacker);
                        }
                        if (own && attack.Kind == AttackKind.Melee && chargedInTrench == x.Target)
                        {
                            Done(GloriousDeed.IntoTheTrenches, seat, x.Attacker);
                        }
                    }
                    break;
                case MineExploded x when x.Injury.Outcome != InjuryOutcome.OutOfAction:
                    if ((mineRolls[x.Model] = mineRolls.GetValueOrDefault(x.Model) + 1) == 2)
                    {
                        Done(GloriousDeed.SurviveToTellTheTale, state.FindModel(x.Model)!.Seat, x.Model);
                    }
                    break;
                case MoraleCheckRolled m when m.Check.Outcome != SuccessOutcome.Failure:
                    Done(GloriousDeed.HoldYourGround, m.Seat, null);
                    break;
            }
        }
        return expected;
    }

    private static readonly string[] Names =
        ["ranged", "melee", "ranged-2v2", "melee-3v3", "ranged" + ScenarioSetups.TrenchWarfareSuffix, "melee-2v2" + ScenarioSetups.TrenchWarfareSuffix, "melee-3v3" + ScenarioSetups.TrenchWarfareSuffix];

    public static TheoryData<string> Scenarios() => [.. Names];

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void EveryDeedIsAnnouncedRightAfterTheEventThatCompletedIt(string scenario)
    {
        foreach (var seed in Enumerable.Range(1, 12))
        {
            var log = Played(scenario, seed);
            // Each announcement, with the last event before it that is not one: the event that completed it.
            var announced = new List<(int After, GloriousDeedCompleted Deed)>();
            var last = -1;
            for (var i = 0; i < log.Count; i++)
            {
                if (log[i] is GloriousDeedCompleted deed)
                {
                    announced.Add((last, deed));
                }
                else
                {
                    last = i;
                }
            }
            Assert.Equal(Expected(log), announced);
            var end = log.Aggregate(MatchState.None, Reducer.Apply);
            Assert.Equal(announced.Select(a => a.Deed.Deed), end.Deeds.Completed.Select(c => c.Deed));
        }
    }

    [Fact]
    public void TheMatchesReachSeveralKindsOfDeed()
    {
        var kinds = new HashSet<GloriousDeed>();
        foreach (var scenario in Names)
        {
            foreach (var seed in Enumerable.Range(1, 12))
            {
                kinds.UnionWith(Played(scenario, seed).OfType<GloriousDeedCompleted>().Select(d => d.Deed));
            }
        }
        Assert.True(kinds.Count >= 4, $"only {string.Join(", ", kinds)}");
    }
}
