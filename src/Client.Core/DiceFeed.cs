using Rules.Core.State;

namespace Client.Core;

// One roll in a box on screen, with the event number it came from; FollowUp is the end of a charge ("✓ REACHED").
public sealed record DiceRow(int Seq, DiceRoll Roll, string? FollowUp = null, RollVerdict FollowUpVerdict = RollVerdict.Neutral);

// The rolls of one action, shown together: a shot's target die, hit roll and injury roll, in event order. Id is the
// event number of its first roll. Updated is when a roll last arrived (seconds, the caller's clock).
public sealed class DiceBox
{
    internal DiceBox(int id, ModelId? model, double now)
    {
        Id = id;
        Model = model;
        Updated = now;
    }

    public int Id { get; }
    public ModelId? Model { get; }
    public List<DiceRow> Rows { get; } = [];
    public double Updated { get; internal set; }
    internal bool Open { get; set; } = true;
}

// What the dice display holds (visible dice rolls, QoL stage F2): the server's rolls as they arrive, grouped per action,
// each box kept for Seconds after its last roll and then dropped (or closed at once by a click). Seconds 0 is "Off":
// nothing is shown, the rolls stay in the combat log. Only lines handed to Add are shown; the battlefield hands over
// only events newer than the last it showed, so a roll is never shown twice and nothing is shown for a catch-up.
public sealed class DiceFeed
{
    public const double FadeSeconds = 0.6;
    public const int MaxBoxes = 3;
    public const int MaxRows = 4;

    private readonly List<DiceBox> _boxes = [];
    private readonly List<DiceRow> _shown = [];
    private DiceBox? _current;

    public double Seconds { get; set; } = 4;

    // The boxes on screen now, oldest first.
    public IReadOnlyList<DiceBox> Boxes => _boxes;

    // Every row ever put on screen, in order (for the UI check: each event number at most once, each equal to its event).
    public IReadOnlyList<DiceRow> Shown => _shown;

    // True when the boxes changed since the last call (the screen redraws only then).
    public bool Changed { get; private set; }

    public bool TakeChanged()
    {
        var changed = Changed;
        Changed = false;
        return changed;
    }

    public void Add(LogLine line, double now)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.Step is not { } step)
        {
            return;
        }
        if (step.Cue is DiceCue.NewAction or DiceCue.EndAction or DiceCue.OwnBox)
        {
            Close();
        }
        if (step.FollowUp is { } follow)
        {
            if (_current is { Open: true } box && box.Rows.Count > 0 && box.Rows[^1].FollowUp is null && box.Rows[^1].Roll.Charge)
            {
                box.Rows[^1] = box.Rows[^1] with { FollowUp = follow, FollowUpVerdict = step.FollowUpVerdict };
                _shown[^1] = box.Rows[^1];
                box.Updated = now;
                Changed = true;
            }
            return;
        }
        if (step.Roll is not { } roll || Seconds <= 0)
        {
            return;
        }
        if (_current is not { Open: true } || !_boxes.Contains(_current) || _current.Rows.Count >= MaxRows)
        {
            _current = new DiceBox(line.Seq, roll.Model, now);
            _boxes.Add(_current);
            while (_boxes.Count > MaxBoxes)
            {
                _boxes.RemoveAt(0);
            }
        }
        var row = new DiceRow(line.Seq, roll);
        _current.Rows.Add(row);
        _current.Updated = now;
        _shown.Add(row);
        Changed = true;
        if (step.Cue == DiceCue.OwnBox)
        {
            Close();
        }
    }

    // How visible a box is now: 1 while it is shown, falling to 0 over the last FadeSeconds.
    public double Alpha(DiceBox box, double now)
    {
        ArgumentNullException.ThrowIfNull(box);
        var left = box.Updated + Seconds - now;
        return Math.Clamp(left / FadeSeconds, 0, 1);
    }

    // Drops the boxes whose time is up.
    public void Expire(double now)
    {
        if (_boxes.RemoveAll(b => Alpha(b, now) <= 0) > 0)
        {
            Changed = true;
        }
    }

    // A click on a box closes it at once.
    public void Dismiss(int id)
    {
        if (_boxes.RemoveAll(b => b.Id == id) > 0)
        {
            Changed = true;
        }
    }

    // Takes everything off the screen (for "Off" and a new match); what was shown stays in Shown.
    public void Clear()
    {
        _boxes.Clear();
        _current = null;
        Changed = true;
    }

    private void Close()
    {
        if (_current is not null)
        {
            _current.Open = false;
        }
    }
}
