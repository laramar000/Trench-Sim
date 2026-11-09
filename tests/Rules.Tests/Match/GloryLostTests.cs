using Rules.Core;
using Rules.Core.State;
using static Rules.Tests.Match.BoardPlay;

namespace Rules.Tests.Match;

// October 2026 Review (U157): Glory Lost, +1 DICE to its Melee Attacks while friendly Chain Fiends carry 3 or more SIN MARKERS between them.
public class GloryLostTests
{
    [Fact]
    [Trait("Rule", "Glory Lost: +1 DICE to Melee Attacks while there are 3 or more SIN MARKERS next to friendly Chain Fiends")]
    public void TheDieNeedsThreeSinMarkersOnFriendlyChainFiends()
    {
        var fiend = Rifleman with { GloryLost = true, Commandable = true };
        var play = new BoardPlay(2, id => id == A(1) ? fiend : id == A(2) ? Rifleman with { Commandable = true } : Rifleman).ToActivation();
        play.Put(A(1), "4", "2").Put(B(1), "5.1", "2").Put(A(2), "2", "30").Put(B(2), "30", "30");
        play.Sin(A(1), 1).Sin(A(2), 1);
        Assert.Equal(0, TurnSequence.GloryDice(play.State, A(1)));
        play.Sin(A(2), 2);   // 1 + 2 = 3 in all
        Assert.Equal(1, TurnSequence.GloryDice(play.State, A(1)));
        play.Sin(A(2), 0).Sin(A(1), 0);
        play.Sin(B(2), 6);   // the enemy's markers do not count
        Assert.Equal(0, TurnSequence.GloryDice(play.State, A(1)));
        // Without the ability, never.
        var plain = new BoardPlay(2, id => Rifleman with { Commandable = true }).ToActivation();
        plain.Sin(A(1), 6);
        Assert.Equal(0, TurnSequence.GloryDice(plain.State, A(1)));
    }
}
