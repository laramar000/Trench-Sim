using Rules.Core;
using Rules.Core.Dice;
using Rules.Core.Events;
using Rules.Core.State;

namespace Client.Core;

// Animation stage A4: what the battlefield shows for an explosion, a thrown grenade, a scattered attack and the extra
// markers SHRAPNEL, GAS and FIRE put on a model. Every point comes from the server's events and state (a blast centred on
// the model it hit, an attack at a point, where a missed attack scattered to, a Martyrdom Device's bearer, the Mine Marker
// a model set off); nothing here works out a result or changes the match. Display only.
public enum BlastStyle
{
    Weapon,
    Mine,
    Martyrdom,
}

public enum MarkerStyle
{
    Shrapnel,
    Gas,
    Fire,
}

// An explosion: its centre and radius (inches); From is where a scattered attack was aimed (its target), when it had one.
public sealed record BlastCue(BlastStyle Style, TablePoint At, double Radius, TablePoint? From = null);

public sealed record MarkerCue(ModelId Model, MarkerStyle Style);

// An attack at a point: the attacker, the point, and whether the Success Roll passed.
public sealed record AimCue(ModelId Attacker, TablePoint At, bool Hit);

// Animation suite (2026-11-25): an ACTION a model takes on itself or a friend, as its event declares it: taking aim,
// reloading (a RELOAD weapon's activation end), digging in (Fortify), rallying its friends (Enforced Orthodoxy), or
// tending a friend (Treat, Laying on of Hands; Target the friend).
public enum ActionStyle
{
    Aim,
    Reload,
    Fortify,
    Rally,
    Treat,
}

public sealed record ActionCue(ModelId Model, ActionStyle Style, ModelId? Target = null);

// Thrown: the attack's weapon is a grenade (it is lobbed, not fired).
public sealed record BattleFx(bool Thrown, AimCue? Aim, IReadOnlyList<BlastCue> Blasts, IReadOnlyList<MarkerCue> Markers)
{
    public static BattleFx None { get; } = new(false, null, [], []);

    public ActionCue? Action { get; init; }

    public bool Any => Thrown || Aim is not null || Blasts.Count > 0 || Markers.Count > 0 || Action is not null;
}

// Follows the match event by event (with the state just after each one, and the attack the event belongs to) to name each
// event's effects; it remembers where each Mine Marker lay, as a marker leaves the state when it is revealed.
public sealed class BattleEffectsTracker
{
    // A blast whose weapon names no radius (it never happens in a legal match) is drawn this wide.
    public const double DefaultBlastRadius = 2;
    public const double MineBlastRadius = 1;
    public const double MartyrdomRadius = 3;

    private readonly Dictionary<string, TablePoint> _mines = new(StringComparer.Ordinal);

    public void Clear() => _mines.Clear();

    public BattleFx Next(GameEvent e, MatchState after, AttackKind? kind, int weapon)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(after);
        foreach (var mine in after.Mines)
        {
            _mines[mine.Id] = TablePoint.Of(mine.At);
        }
        var setup = after.Setup;
        var thrown = e is RangedSuccessRolled or PointSuccessRolled && kind == AttackKind.Ranged && weapon == ModelProfile.GrenadeWeapon;
        AimCue? aim = e is PointSuccessRolled p ? new(p.Attacker, TablePoint.Of(p.At), p.Check.Outcome != SuccessOutcome.Failure) : null;
        var blasts = new List<BlastCue>();
        var markers = new List<MarkerCue>();
        switch (e)
        {
            case BlastHit b when At(after, b.Target) is { } centre:
                blasts.Add(new(BlastStyle.Weapon, centre, Radius(setup, b.Attacker, weapon)));
                break;
            case PointBlastHit pb:
                blasts.Add(new(BlastStyle.Weapon, TablePoint.Of(pb.At), Radius(setup, pb.Attacker, weapon)));
                break;
            case AttackScattered a:
                blasts.Add(new(BlastStyle.Weapon, TablePoint.Of(a.At), Radius(setup, a.Attacker, weapon), a.Target is { } aimed ? At(after, aimed) : null));
                break;
            case MartyrdomTriggered m when At(after, m.Model) is { } bearer:
                blasts.Add(new(BlastStyle.Martyrdom, bearer, MartyrdomRadius));
                break;
            case MineExploded x:
                if ((_mines.TryGetValue(x.Marker, out var marker) ? marker : At(after, x.Model)) is { } where)
                {
                    blasts.Add(new(BlastStyle.Mine, where, MineBlastRadius));
                }
                // A live mine adds 1 SHRAPNEL marker even on No Effect (U93), unless the model negates it.
                if (setup?.ProfileOf(x.Model) is not { } hurt || !hurt.Negates.HasFlag(WeaponKeywords.Shrapnel))
                {
                    markers.Add(new(x.Model, MarkerStyle.Shrapnel));
                }
                break;
            case InjuryRolled i when i.ExtraMarkers > 0:
                var attackKind = kind ?? AttackKind.Ranged;
                var keywords = (Armed(setup, i.Attacker, attackKind, weapon)?.AttackKeywords(attackKind) ?? WeaponKeywords.None)
                    & ModelProfile.MarkerKeywords & ~(setup?.ProfileOf(i.Target)?.Negates ?? WeaponKeywords.None);
                foreach (var (flag, style) in new[] { (WeaponKeywords.Shrapnel, MarkerStyle.Shrapnel), (WeaponKeywords.Gas, MarkerStyle.Gas), (WeaponKeywords.Fire, MarkerStyle.Fire) })
                {
                    if (keywords.HasFlag(flag))
                    {
                        markers.Add(new(i.Target, style));
                    }
                }
                break;
        }
        ActionCue? action = e switch
        {
            SelfActionDeclared { Action: SelfActionKind.Aim } d => new(d.Model, ActionStyle.Aim),
            SelfActionDeclared { Action: SelfActionKind.Fortify } d => new(d.Model, ActionStyle.Fortify),
            // Hide (U126): it crouches like a model taking aim.
            SelfActionDeclared { Action: SelfActionKind.Hide } d => new(d.Model, ActionStyle.Aim),
            SelfActionDeclared { Action: SelfActionKind.EnforcedOrthodoxy } d => new(d.Model, ActionStyle.Rally),
            SelfActionDeclared { Action: SelfActionKind.LayingOnHands } d => new(d.Model, ActionStyle.Treat, d.Target),
            TreatDeclared t => new(t.Model, ActionStyle.Treat, t.Target),
            ActivationEnded { Reason: ActivationEndReason.Reload } r => new(r.Model, ActionStyle.Reload),
            _ => null,
        };
        return !thrown && aim is null && blasts.Count == 0 && markers.Count == 0 && action is null ? BattleFx.None : new(thrown, aim, blasts, markers) { Action = action };
    }

    private static TablePoint? At(MatchState state, ModelId id) => state.FindModel(id)?.Position is { } at ? TablePoint.Of(at) : null;

    // The attacker's profile as the attack's weapon sees it (as ResolutionLog reads it).
    private static ModelProfile? Armed(MatchSetup? setup, ModelId attacker, AttackKind kind, int weapon) =>
        setup?.ProfileOf(attacker) is { } p ? weapon > 0 && p.Has(kind, weapon) ? p.Using(kind, weapon) : p : null;

    private static double Radius(MatchSetup? setup, ModelId attacker, int weapon) =>
        Armed(setup, attacker, AttackKind.Ranged, weapon)?.RangedWeaponBlast is > 0 and var blast ? blast : DefaultBlastRadius;
}
