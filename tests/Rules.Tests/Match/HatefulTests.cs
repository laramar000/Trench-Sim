using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.State;
using static Rules.Tests.Match.BoardPlay;

namespace Rules.Tests.Match;

// Roster stage R7a (U128): Hateful (Warbands p. 169): a model that activates over 1" from every enemy with a model that
// is neither CULT nor DEMONIC in sight within 12" must charge the closest one.
public class HatefulTests
{
    private static readonly ModelProfile Hater = Rifleman with { Hateful = true };

    // A1 (4, 2) is Hateful; B1 is 6" away, B2 10" away, B3 far.
    private static BoardPlay Ready(Func<ModelId, ModelProfile>? others = null)
    {
        var play = new BoardPlay(3, id => id == A(1) ? Hater : others?.Invoke(id) ?? Rifleman).ToActivation();
        play.Put(A(1), "4", "2").Put(A(2), "30", "2").Put(A(3), "30", "6").Put(B(1), "4", "8.5").Put(B(2), "10", "9").Put(B(3), "30", "30");
        return play;
    }

    [Fact]
    [Trait("Rule", "Hateful p. 169: a bound model charges the closest enemy it sees; nothing else, and it cannot end the activation")]
    public void ABoundModelMustChargeTheClosest()
    {
        var play = Ready();
        play.Do(new ActivateModel(Seat.One, A(1)));
        Assert.Equal([B(1)], Board.HatefulTargets(play.State, play.M(A(1))).Select(m => m.Id).ToArray());
        play.Refused(new MoveModel(Seat.One, A(1), Path(("4", "3"))), ReasonCode.ActionExcluded);
        play.Refused(new Shoot(Seat.One, A(1), B(1)), ReasonCode.ActionExcluded);
        play.Refused(new EndActivation(Seat.One), ReasonCode.ActionExcluded);
        play.Refused(new ChargeModel(Seat.One, A(1), B(2)), ReasonCode.ActionExcluded);
        Assert.Contains(play.Do(new ChargeModel(Seat.One, A(1), B(1)), 6).Events, e => e is ChargeMoved);
        // Once it has charged, it is free again.
        Assert.Empty(Board.HatefulTargets(play.State, play.M(A(1))));
    }

    [Fact]
    [Trait("Rule", "Hateful p. 169: CULT and DEMONIC enemies, and enemies out of sight or beyond 12\", do not count")]
    public void OnlyTargetsItHatesCount()
    {
        var cult = Ready(id => id == B(1) ? Rifleman with { Traits = ModelTraits.Cult } : Rifleman);
        cult.Do(new ActivateModel(Seat.One, A(1)));
        Assert.Equal([B(2)], Board.HatefulTargets(cult.State, cult.M(A(1))).Select(m => m.Id).ToArray());

        var none = Ready(id => id is { } i && (i == B(1) || i == B(2)) ? Rifleman with { Traits = ModelTraits.Demonic } : Rifleman);
        none.Do(new ActivateModel(Seat.One, A(1)));
        Assert.Empty(Board.HatefulTargets(none.State, none.M(A(1))));
        none.Do(new EndActivation(Seat.One));

        // Within 1" of an enemy it is already engaged: free to Fight.
        var engaged = Ready().Put(B(1), "4", "2.9");
        engaged.Do(new ActivateModel(Seat.One, A(1)));
        Assert.Empty(Board.HatefulTargets(engaged.State, engaged.M(A(1))));
    }

    [Fact]
    [Trait("Rule", "Hateful p. 169: a Down model stands up first, then must charge (halved Movement)")]
    public void ADownModelStandsUpThenCharges()
    {
        var play = Ready().SetStatus(A(1), ModelStatus.Down);
        var events = play.Do(new ActivateModel(Seat.One, A(1))).Events;
        Assert.Contains(events, e => e is ModelStoodUp);
        Assert.Single(Board.HatefulTargets(play.State, play.M(A(1))));
    }
}
