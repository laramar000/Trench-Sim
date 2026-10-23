using Rules.Core.Events;
using Rules.Core.State;

namespace Client.Core;

// QoL I4: what the match summary shows once a match is over, all counted from the server's events and its final
// state: VP scored at each turn's end, models Down and Out of Action at the end, and rolls the rules counted as passed
// or failed for each side. Counts only: no odds and nothing the client worked out itself.
public sealed record MatchSummary(
    IReadOnlyList<(int Turn, int One, int Two)> VpByTurn,
    (int One, int Two) Vp,
    (int One, int Two) Down,
    (int One, int Two) OutOfAction,
    (int Passed, int Failed) RollsOne,
    (int Passed, int Failed) RollsTwo)
{
    public IEnumerable<string> Lines()
    {
        yield return VpByTurn.Count == 0
            ? $"VP: A {Vp.One}, B {Vp.Two} (no turn was scored)"
            : $"VP by turn: {string.Join(" · ", VpByTurn.Select(t => $"turn {t.Turn}: A +{t.One}, B +{t.Two}"))}; total A {Vp.One}, B {Vp.Two}";
        yield return $"Models at the end: Down A {Down.One}, B {Down.Two} · Out of Action A {OutOfAction.One}, B {OutOfAction.Two}";
        yield return $"Rolls passed / failed (the server's dice): A {RollsOne.Passed} / {RollsOne.Failed}, B {RollsTwo.Passed} / {RollsTwo.Failed}";
    }
}

// Collects the summary's numbers event by event, keyed by event number, so events sent again (a rejoin, a resync)
// are counted once.
public sealed class MatchTally
{
    private readonly Dictionary<int, (int Turn, int One, int Two)> _scored = [];
    private readonly Dictionary<int, (Seat Seat, bool Passed)> _rolls = [];

    public void Clear()
    {
        _scored.Clear();
        _rolls.Clear();
    }

    // state: the match as it is around this event (only used to find a rolling model's side, which never changes).
    public void Add(int seq, GameEvent e, MatchState state)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(state);
        if (e is ObjectivesScored scored)
        {
            _scored[seq] = (scored.Turn, scored.Results.Where(r => r.Controller == Seat.One).Sum(r => r.Points),
                scored.Results.Where(r => r.Controller == Seat.Two).Sum(r => r.Points));
        }
        if (DiceRolls.Of(e) is { Verdict: RollVerdict.Pass or RollVerdict.Critical or RollVerdict.Fail } roll)
        {
            Seat? seat = e is MoraleCheckRolled morale ? morale.Seat : roll.Model is { } model ? state.FindModel(model)?.Seat : null;
            if (seat is { } s)
            {
                _rolls[seq] = (s, roll.Verdict != RollVerdict.Fail);
            }
        }
    }

    public MatchSummary Summary(MatchState final)
    {
        ArgumentNullException.ThrowIfNull(final);
        (int, int) Count(Func<ModelState, bool> which) => (final.Models.Count(m => m.Seat == Seat.One && which(m)), final.Models.Count(m => m.Seat == Seat.Two && which(m)));
        (int, int) Rolls(Seat seat) => (_rolls.Values.Count(r => r.Seat == seat && r.Passed), _rolls.Values.Count(r => r.Seat == seat && !r.Passed));
        return new MatchSummary(
            [.. _scored.OrderBy(kv => kv.Key).Select(kv => kv.Value)],
            (final.One.VictoryPoints, final.Two.VictoryPoints),
            Count(m => m.Status == ModelStatus.Down),
            Count(m => m.Status == ModelStatus.OutOfAction),
            Rolls(Seat.One),
            Rolls(Seat.Two));
    }
}
