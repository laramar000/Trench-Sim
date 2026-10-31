using System.Collections.Immutable;
using Rules.Core.Events;
using Rules.Core.Geometry;
using Rules.Core.Scenarios;
using Rules.Core.State;

namespace Rules.Core;

// Glorious Deeds (pp. 149-150; Scenario I p. 153, Scenario IV p. 163; U96). The reducer keeps what the deeds need to
// know (DeedState) as each event is applied, and marks a deed owed the moment an event completes it; the turn sequence
// then announces it with GloriousDeedCompleted, which adds its Victory Point. Each deed is completed once per game, by
// whoever completes it first. Every deed here is completed by one model's attack, roll or Morale Check, so two seats
// can never complete one at the same moment and the book's roll-off for that case never arises.
// Not built: Cast Them Down (Falling, U69) and Suicidal Bravery (a charge has one target) cannot happen in this
// version; Sniper needs the ELITE keyword, which no model has; Victory or Death is for campaigns only.
public static class Deeds
{
    private static readonly ImmutableArray<GloriousDeed> ClaimDeeds =
        [GloriousDeed.Bloodletting, GloriousDeed.HoldYourGround, GloriousDeed.LordOfWar, GloriousDeed.ResistAndBite];

    private static readonly ImmutableArray<GloriousDeed> TrenchDeeds =
        [GloriousDeed.GoodHunting, GloriousDeed.Headshot, GloriousDeed.HoldYourGround, GloriousDeed.IntoTheTrenches, GloriousDeed.SurviveToTellTheTale];

    public const int VictoryPoints = 1;

    // The deed's title as the book prints it.
    public static string Name(GloriousDeed deed) => deed switch
    {
        GloriousDeed.Bloodletting => "Bloodletting",
        GloriousDeed.HoldYourGround => "Hold Your Ground",
        GloriousDeed.LordOfWar => "Lord of War",
        GloriousDeed.ResistAndBite => "Resist and Bite",
        GloriousDeed.GoodHunting => "Good Hunting",
        GloriousDeed.Headshot => "Headshot",
        GloriousDeed.IntoTheTrenches => "Into the Trenches!",
        GloriousDeed.SurviveToTellTheTale => "Survive to Tell the Tale",
        _ => throw new ArgumentOutOfRangeException(nameof(deed)),
    };

    // What completes the deed, in short (pp. 150, 153, 163), for the scenario card.
    public static string Condition(GloriousDeed deed) => deed switch
    {
        GloriousDeed.Bloodletting => "an attack puts a sixth Blood Marker on an enemy model",
        GloriousDeed.HoldYourGround => "the first warband to pass a Morale Check",
        GloriousDeed.LordOfWar => "one model takes two enemy models Out of Action in melee in one Turn",
        GloriousDeed.ResistAndBite => "a model that began its activation Down takes an enemy model Out of Action in that activation",
        GloriousDeed.GoodHunting => "a model takes an enemy model Out of Action with a Long Range ranged attack",
        GloriousDeed.Headshot => "a model that retreated earlier in its activation takes Out of Action an enemy it retreated from",
        GloriousDeed.IntoTheTrenches => "a model charges an enemy model in a trench and takes it Out of Action in melee in that activation",
        GloriousDeed.SurviveToTellTheTale => "a model makes two Injury Rolls from mines and neither takes it Out of Action",
        _ => throw new ArgumentOutOfRangeException(nameof(deed)),
    };

    // The deeds of the scenario being played; none without a battlefield (the M2 setups) or in any other scenario.
    public static ImmutableArray<GloriousDeed> For(MatchSetup? setup) => setup?.Scenario is null ? [] : setup.ScenarioId switch
    {
        ClaimNoMansLand.ScenarioId => ClaimDeeds,
        TrenchWarfare.ScenarioId => TrenchDeeds,
        _ => [],
    };

    // The deeds state after an event (applied to before, giving after), with any deed it completed owed.
    internal static MatchState Track(MatchState before, GameEvent e, MatchState after)
    {
        var deeds = For(after.Setup);
        if (deeds.IsEmpty)
        {
            return after;
        }
        var d = after.Deeds;
        var owed = new List<CompletedDeed>();
        void Complete(GloriousDeed deed, Seat seat, ModelId? model)
        {
            if (deeds.Contains(deed) && !d.Done(deed) && !owed.Any(o => o.Deed == deed))
            {
                owed.Add(new CompletedDeed(deed, seat, model));
            }
        }
        switch (e)
        {
            case TurnStarted:
                d = d with { MeleeTakedowns = [] };
                break;
            case ActivationStarted:
                d = d with { ActiveBeganDown = false, RetreatingFrom = [], RetreatedFrom = [], ChargedInTrench = null };
                break;
            case ModelStoodUp:
                d = d with { ActiveBeganDown = true };
                break;
            case RangedAttackDeclared x:
                d = d with { ShotAtLongRange = x.LongRange };
                break;
            case FriendlyTargetChosen x:
                d = d with { ShotAtLongRange = x.LongRange };
                break;
            case RetreatDeclared x:
                var engaged = Engagement.Pairs(before).Where(p => p.One == x.Model || p.Two == x.Model).Select(p => p.One == x.Model ? p.Two : p.One);
                d = d with { RetreatingFrom = [.. engaged.Order(ModelOrder)] };
                break;
            case RetreatResolved x:
                d = d with
                {
                    RetreatedFrom = x.Moved ? [.. d.RetreatedFrom.Union(d.RetreatingFrom).Order(ModelOrder)] : d.RetreatedFrom,
                    RetreatingFrom = [],
                };
                break;
            case ChargeMoved { Reached: true } x:
                var target = after.FindModel(x.Target)!;
                if (Board.ScenarioOf(after) is { } scenario && Board.BodyOf(after, target) is { } body && Trenches.Of(scenario.Battlefield, body) is not null)
                {
                    d = d with { ChargedInTrench = x.Target };
                }
                break;
            case InjuryRolled x when before.InProgress is { FriendlyFire: false } attack:
                var attacker = before.FindModel(x.Attacker)!;
                var wasMarked = before.FindModel(x.Target)!.BloodMarkers;
                var hurt = after.FindModel(x.Target)!;
                if (wasMarked < BloodMarkers.Max && hurt.BloodMarkers == BloodMarkers.Max)
                {
                    Complete(GloriousDeed.Bloodletting, attacker.Seat, attacker.Id);
                }
                if (hurt.Status == ModelStatus.OutOfAction)
                {
                    var active = before.ActiveModel == attacker.Id && !attack.FreeFight;
                    if (attack.Kind == AttackKind.Melee)
                    {
                        d = d with { MeleeTakedowns = d.MeleeTakedowns.Add(attacker.Id) };
                        if (d.MeleeTakedowns.Count(m => m == attacker.Id) >= 2)
                        {
                            Complete(GloriousDeed.LordOfWar, attacker.Seat, attacker.Id);
                        }
                    }
                    if (attack.Kind == AttackKind.Ranged && d.ShotAtLongRange)
                    {
                        Complete(GloriousDeed.GoodHunting, attacker.Seat, attacker.Id);
                    }
                    if (active && d.ActiveBeganDown)
                    {
                        Complete(GloriousDeed.ResistAndBite, attacker.Seat, attacker.Id);
                    }
                    if (active && d.RetreatedFrom.Contains(x.Target))
                    {
                        Complete(GloriousDeed.Headshot, attacker.Seat, attacker.Id);
                    }
                    if (active && attack.Kind == AttackKind.Melee && d.ChargedInTrench == x.Target)
                    {
                        Complete(GloriousDeed.IntoTheTrenches, attacker.Seat, attacker.Id);
                    }
                }
                break;
            case MineExploded x:
                var survivor = after.FindModel(x.Model)!;
                if (survivor.Status != ModelStatus.OutOfAction)
                {
                    d = d with { MineSurvivals = d.MineSurvivals.Add(x.Model) };
                    if (d.MineSurvivals.Count(m => m == x.Model) >= 2)
                    {
                        Complete(GloriousDeed.SurviveToTellTheTale, survivor.Seat, survivor.Id);
                    }
                }
                break;
            case MoraleCheckRolled x when x.Check.Outcome != Dice.SuccessOutcome.Failure:
                Complete(GloriousDeed.HoldYourGround, x.Seat, null);
                break;
        }
        if (owed.Count > 0)
        {
            d = d with { Owed = d.Owed.AddRange(owed) };
        }
        return ReferenceEquals(d, after.Deeds) ? after : after with { Deeds = d };
    }

    private static readonly IComparer<ModelId> ModelOrder = Comparer<ModelId>.Create((a, b) => string.CompareOrdinal(a.Value, b.Value));
}
