using Rules.Core.Events;

namespace Client.Core;

// QoL I3: the short sounds the client can play. Each comes from a server event (or, for YourMove, from the server's
// state turning to this seat's move); none says more than the screen already does.
public enum SoundCue
{
    YourMove,
    Activation,
    Pass,
    Fail,
    Scoring,
}

public static class SoundCues
{
    // The cue for one event: an activation starting, a roll the rules count as passed (or critical) or failed, and the
    // objectives being scored. Rolls the rules give no pass or fail (roll-offs, a charge's distance) have no sound.
    public static SoundCue? Of(GameEvent e) => e switch
    {
        ActivationStarted => SoundCue.Activation,
        ObjectivesScored or TrenchesScored => SoundCue.Scoring,
        _ => DiceRolls.Of(e)?.Verdict switch
        {
            RollVerdict.Pass or RollVerdict.Critical => SoundCue.Pass,
            RollVerdict.Fail => SoundCue.Fail,
            _ => null,
        },
    };
}

// QoL I3: when it becomes this window's move while the window is not in front, the title says so and the window asks
// for attention once; both end as soon as the window is in front again or the move is no longer this window's.
public sealed class TurnAlert
{
    public const string Prefix = "● YOUR MOVE · ";

    private bool _wasYourMove;

    public bool Alerted { get; private set; }

    // Returns true when the window should ask for attention now (once per turn to move).
    public bool Update(bool yourMove, bool focused)
    {
        var ask = yourMove && !_wasYourMove && !focused;
        _wasYourMove = yourMove;
        if (ask)
        {
            Alerted = true;
        }
        else if (!yourMove || focused)
        {
            Alerted = false;
        }
        return ask;
    }

    public string Title(string title) => Alerted ? Prefix + title : title;
}
