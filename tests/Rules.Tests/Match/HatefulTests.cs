using Rules.Core;
using Rules.Core.Commands;
using Rules.Core.Events;
using Rules.Core.State;
using static Rules.Tests.Match.BoardPlay;

namespace Rules.Tests.Match;

// Hateful (Warbands p. 169, U125): when the model activates more than 1" from every enemy, with an enemy in sight within
// 12" that has neither CULT nor DEMONIC, it must take a Charge ACTION at the closest such enemy (one it can charge) before
// anything else; a Down model stands up first, then must charge.
public class HatefulTests
{
    private static readonly ModelProfile Fiend = Rifleman with { Hateful = true };

    private static ModelProfile Profiles(ModelId id, ModelProfile? b2 = null) =>
        id == A(1) ? Fiend : id == B(2) && b2 is not null ? b2 : Rifleman;

    // A1 (4, 2) the Hateful model; B1 6" away, B2 10" away, both in the open on the same line.
    private static BoardPlay Ready(ModelProfile? b2 = null, string b1x = "10", string b2x = "14", ModelStatus a1 = ModelStatus.Standing)
    {
        var play = new BoardPlay(2, id => Profiles(id, b2)).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), b1x, "2").Put(B(2), b2x, "2").Put(A(2), "4", "30");
        if (a1 != ModelStatus.Standing)
        {
            play.SetStatus(A(1), a1);
        }
        return play;
    }

    [Fact]
    [Trait("Rule", "Hateful p. 169: it must take a Charge ACTION at the closest enemy in sight within 12\" before anything else")]
    public void AHatefulModelMustChargeTheClosestEnemyFirst()
    {
        var play = Ready();
        var events = play.Do(new ActivateModel(Seat.One, A(1))).Events;
        Assert.Equal(new ChargeCompelled(A(1), B(1)), events.OfType<ChargeCompelled>().Single());
        Assert.Equal(B(1), play.State.MustCharge);

        // Nothing else first: no move, no shot, no other target, no end to the activation.
        play.Refused(new MoveModel(Seat.One, A(1), Path(("4", "3"))), ReasonCode.MustCharge);
        play.Refused(new Shoot(Seat.One, A(1), B(1)), ReasonCode.MustCharge);
        play.Refused(new ChargeModel(Seat.One, A(1), B(2)), ReasonCode.MustCharge);
        play.Refused(new EndActivation(Seat.One), ReasonCode.MustCharge);
        Assert.False(Activation.Finished(play.State));
        Assert.Equal([new ChargeModel(Seat.One, A(1), B(1))], TurnSequence.LegalCommands(play.State));

        // The charge at B1 is taken; then the activation goes on as usual.
        Assert.Null(TurnSequence.Check(play.State, new ChargeModel(Seat.One, A(1), B(1))));
        play.Do(new ChargeModel(Seat.One, A(1), B(1)), 6);
        Assert.Null(play.State.MustCharge);
        Assert.True(play.State.Actions.HasFlag(ActionsTaken.Charge));
    }

    [Fact]
    [Trait("Rule", "Hateful p. 169: only an enemy without CULT or DEMONIC counts; the closest of those is the target")]
    public void CultAndDemonicEnemiesAreLeftAlone()
    {
        // B1 (the closest) is DEMONIC: B2 is the one it must charge.
        var play = new BoardPlay(2, id => id == A(1) ? Fiend : id == B(1) ? Rifleman with { Traits = ModelTraits.Demonic } : Rifleman).ToActivation();
        // (B2 off B1's line, so B1 does not block the way to it.)
        play.Put(A(1), "4", "2").Put(B(1), "10", "2").Put(B(2), "12", "8").Put(A(2), "4", "30");
        play.Do(new ActivateModel(Seat.One, A(1)));
        Assert.Equal(B(2), play.State.MustCharge);

        // Both CULT or DEMONIC: no charge is forced.
        var neither = new BoardPlay(2, id => id == A(1) ? Fiend : id.Value[0] == 'B' ? Rifleman with { Traits = ModelTraits.Cult } : Rifleman).ToActivation();
        neither.Put(A(1), "4", "2").Put(B(1), "10", "2").Put(B(2), "14", "2").Put(A(2), "4", "30");
        Assert.Empty(neither.Do(new ActivateModel(Seat.One, A(1))).Events.OfType<ChargeCompelled>());
        Assert.Null(neither.State.MustCharge);
        Assert.Null(TurnSequence.Check(neither.State, new EndActivation(Seat.One)));
    }

    [Fact]
    [Trait("Rule", "Hateful p. 169: not within 1\" of an enemy, and only an enemy within 12\"")]
    public void NoChargeIsForcedWhenEngagedOrWhenNoEnemyIsNear()
    {
        // B1 in base contact: it is engaged, so it is free to fight instead.
        var engaged = Ready(b1x: "5");
        engaged.Do(new ActivateModel(Seat.One, A(1)));
        Assert.Null(engaged.State.MustCharge);

        // Every enemy more than 12" away.
        var far = Ready(b1x: "20", b2x: "24");
        far.Do(new ActivateModel(Seat.One, A(1)));
        Assert.Null(far.State.MustCharge);
        Assert.Null(TurnSequence.Check(far.State, new EndActivation(Seat.One)));
    }

    [Fact]
    [Trait("Rule", "Hateful p. 169: a Down Hateful model stands up, then must charge")]
    public void ADownHatefulModelStandsUpThenMustCharge()
    {
        var play = Ready(a1: ModelStatus.Down);
        var events = play.Do(new ActivateModel(Seat.One, A(1))).Events;
        Assert.Single(events.OfType<ModelStoodUp>());
        Assert.Equal(B(1), events.OfType<ChargeCompelled>().Single().Target);
        Assert.Equal(B(1), play.State.MustCharge);
    }

    [Fact]
    [Trait("Rule", "Hateful p. 169: a record that skips the forced charge does not replay")]
    public void TheReducerRefusesAnythingButTheForcedCharge()
    {
        var play = Ready();
        play.Do(new ActivateModel(Seat.One, A(1)));
        Assert.ThrowsAny<Exception>(() => Reducer.Apply(play.State, new ActivationEnded(A(1), ActivationEndReason.Voluntary)));
        Assert.ThrowsAny<Exception>(() => Reducer.Apply(play.State, new ChargeDeclared(A(1), B(2))));
        Assert.Equal(B(1), Reducer.Apply(play.State, new ChargeCompelled(A(1), B(1))).MustCharge);
        Assert.Null(Reducer.Apply(play.State, new ChargeDeclared(A(1), B(1))).MustCharge);
    }

    [Fact]
    [Trait("Rule", "Hateful p. 169: a model without it is never forced")]
    public void AModelWithoutHatefulIsFree()
    {
        var play = new BoardPlay(2).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), "10", "2");
        play.Do(new ActivateModel(Seat.One, A(1)));
        Assert.Null(play.State.MustCharge);
    }
}
