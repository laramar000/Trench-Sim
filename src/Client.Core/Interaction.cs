using Rules.Core.Commands;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Client.Core;

// M7.4: what the player is pointing at on the battlefield, as plain C# the Godot client forwards input to (stage 4b).
// Choose an action, then a placement point, a path of waypoints or a target; the preview (Previews) says what the
// server would probably answer. Confirming sends the command through Send (stage 4c: the screen's MatchController), once
// per confirmation, and the server's answer is what counts: DONE only when it accepted the command, never because a
// preview looked legal. A preview that says the server would refuse needs a second confirmation to be sent anyway.
public enum InteractionMode
{
    None,
    Place,
    Move,
    Dash,
    Retreat,
    Shoot,
    Charge,
    Fight,
}

public sealed class Interaction
{
    // Keyboard cursor steps, in inches: half an inch, two inches with Shift.
    public const double Step = 0.5;
    public const double BigStep = 2.0;

    private static readonly InteractionMode[] ActionModes =
        [InteractionMode.Move, InteractionMode.Dash, InteractionMode.Retreat, InteractionMode.Shoot, InteractionMode.Charge, InteractionMode.Fight];

    private readonly List<TablePoint> _waypoints = [];
    private MatchState _state = MatchState.None;
    private Seat? _seat;
    private Preview? _preview;
    private bool _stale = true;

    public InteractionMode Mode { get; private set; }

    // The model placed or acting.
    public ModelId? Actor { get; private set; }

    public TablePoint Cursor { get; private set; }

    public IReadOnlyList<TablePoint> Waypoints => _waypoints;

    public ModelId? Target { get; private set; }

    // How a confirmed command reaches the server: the screen's MatchController.SendAsync, which sends one command at a
    // time, refuses while the match is paused, over or not connected, and reports the server's answer. Null: no match.
    public Func<Command, string, Task<Feedback>>? Send { get; set; }

    // The command sent and not yet answered; null when nothing is waiting.
    public Command? Pending { get; private set; }

    // The server's answer to the last command sent from the battlefield.
    public Feedback? Answer { get; private set; }

    // The preview refuses and the player confirmed once: the next confirmation sends it anyway.
    private bool _armed;

    // The last thing worth telling the player (a refused confirm, a cleared preview); null when there is none.
    public string? Notice { get; private set; }

    // Goes up on every change, so a view redraws only when something changed.
    public long Version { get; private set; }

    public bool IsPath => Mode is InteractionMode.Move or InteractionMode.Dash or InteractionMode.Retreat;

    public bool IsTarget => Mode is InteractionMode.Shoot or InteractionMode.Charge or InteractionMode.Fight;

    public bool UsesCursor => IsPath || Mode == InteractionMode.Place;

    // A new state from the server. The choice in progress is kept while it still makes sense, and cleared (with a
    // notice) when the match has moved on: another activation, the end of your deployment turn, a decision to make.
    public void Update(MatchState state, Seat? seat)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (ReferenceEquals(state, _state) && seat == _seat)
        {
            return;
        }
        // Another seat (hot-seat switching): a choice made for the previous seat never carries over.
        if (seat != _seat && Pending is null && Mode != InteractionMode.None)
        {
            Clear();
            Notice = "Another seat is shown now, so the preview was cleared.";
        }
        _state = state;
        _seat = seat;
        _armed = false;
        // While a command is on its way, the server's answer decides what happens to the choice.
        if (Pending is null && Mode != InteractionMode.None && !StillPossible())
        {
            Clear();
            Notice = "The match moved on, so the preview was cleared.";
        }
        Changed();
    }

    // The actions the player can preview now: placing a model on your deployment turn, or the actions of your
    // activating model. The preview then says whether the server would probably accept each one.
    public IReadOnlyList<InteractionMode> Available
    {
        get
        {
            if (_seat is not { } seat || _state.Pending is not null || _state.Setup?.Scenario is null)
            {
                return [];
            }
            if (_state.Phase == Phase.Deployment && MatchView.NextDeployer(_state) == seat && Undeployed().Count > 0)
            {
                return [InteractionMode.Place];
            }
            if (_state.Phase == Phase.Activation && _state.ActiveModel is { } active && _state.FindModel(active) is { Seat: var owner, Position: not null } && owner == seat)
            {
                return ActionModes;
            }
            return [];
        }
    }

    public bool Start(InteractionMode mode)
    {
        if (Pending is not null)
        {
            Notice = WaitingNotice;
            Changed();
            return false;
        }
        if (mode == InteractionMode.None)
        {
            Cancel();
            return true;
        }
        if (!Available.Contains(mode))
        {
            Notice = Available.Count == 0
                ? "Nothing to preview now: previews are for placing your models on your deployment turn and for the actions of your activating model."
                : $"{Words(mode)} is not one of the choices now.";
            Changed();
            return false;
        }
        Clear();
        Mode = mode;
        if (mode == InteractionMode.Place)
        {
            Actor = Undeployed()[0].Id;
            Cursor = ZoneCentre();
        }
        else
        {
            Actor = _state.ActiveModel;
            Cursor = TablePoint.Of(_state.FindModel(Actor!.Value)!.Position!.Value);
            Target = Targets().FirstOrDefault();
        }
        Changed();
        return true;
    }

    public void Cancel()
    {
        if (Pending is not null || (Mode == InteractionMode.None && Notice is null))
        {
            return;
        }
        Clear();
        Changed();
    }

    // Moves the cursor by inches (east and south are positive), kept on the table.
    public void MoveCursor(double dx, double dy) => SetCursor(Cursor.X + dx, Cursor.Y + dy);

    public void SetCursor(double x, double y)
    {
        // While a command is on its way the choice stays exactly what was sent (Pending), until the server answers.
        if (Pending is not null || !UsesCursor || _state.Setup?.Scenario is not { } scenario)
        {
            return;
        }
        var width = TableRect.Inches(scenario.Battlefield.Width);
        var depth = TableRect.Inches(scenario.Battlefield.Height);
        var next = new TablePoint(Math.Round(Math.Clamp(x, 0, width), 3), Math.Round(Math.Clamp(y, 0, depth), 3));
        if (next == Cursor)
        {
            return;
        }
        Cursor = next;
        _armed = false;
        Changed();
    }

    // Path modes: the cursor becomes a fixed waypoint, and the next leg starts there.
    public bool AddPoint()
    {
        if (Pending is not null || !IsPath || (_waypoints.Count > 0 && _waypoints[^1] == Cursor) || _waypoints.Count >= Rules.Core.Events.ModelMoved.MaxWaypoints)
        {
            return false;
        }
        _waypoints.Add(Cursor);
        _armed = false;
        Changed();
        return true;
    }

    public bool RemovePoint()
    {
        if (Pending is not null || !IsPath || _waypoints.Count == 0)
        {
            return false;
        }
        _waypoints.RemoveAt(_waypoints.Count - 1);
        _armed = false;
        Changed();
        return true;
    }

    // The next (or previous) choice: the target for Shoot, Charge and Fight; the model to place during Deployment.
    public void NextChoice(bool forward = true)
    {
        var choices = Mode == InteractionMode.Place ? Undeployed().Select(m => m.Id).ToList() : IsTarget ? Targets() : [];
        var current = Mode == InteractionMode.Place ? Actor : Target;
        if (Pending is not null || choices.Count == 0)
        {
            return;
        }
        var at = current is { } c ? choices.IndexOf(c) : -1;
        var next = at < 0 ? (forward ? choices[0] : choices[^1]) : choices[((at + (forward ? 1 : -1)) % choices.Count + choices.Count) % choices.Count];
        if (Mode == InteractionMode.Place)
        {
            Actor = next;
        }
        else
        {
            Target = next;
        }
        _armed = false;
        Changed();
    }

    // Target modes: pick a model directly (a click). Any model may be picked; the preview says if the server would refuse.
    public void SetTarget(ModelId id)
    {
        if (Pending is not null || !IsTarget || Target == id)
        {
            return;
        }
        Target = id;
        _armed = false;
        Changed();
    }

    // The models a target can be cycled through: enemies on the table, nearest first.
    public List<ModelId> Targets()
    {
        if (Actor is not { } actor || _state.FindModel(actor) is not { Position: { } from } me)
        {
            return [];
        }
        return _state.Models
            .Where(m => m.Seat != me.Seat && m.Position is not null && m.Status != ModelStatus.OutOfAction)
            .OrderBy(m => Distance(from, m.Position!.Value))
            .ThenBy(m => m.Id.Value, StringComparer.Ordinal)
            .Select(m => m.Id)
            .ToList();
    }

    // The preview for the current choice, worked out once per change; null while there is nothing to preview yet.
    public Preview? Preview
    {
        get
        {
            if (_stale)
            {
                _preview = Compute();
                _stale = false;
            }
            return _preview;
        }
    }

    // The path the command would carry: the fixed waypoints, then the cursor if it is somewhere new.
    public IReadOnlyList<TablePoint> PathPoints()
    {
        if (!IsPath || Actor is not { } actor || _state.FindModel(actor)?.Position is not { } start)
        {
            return [];
        }
        var points = new List<TablePoint>(_waypoints);
        var last = points.Count > 0 ? points[^1] : TablePoint.Of(start);
        if (Cursor != last)
        {
            points.Add(Cursor);
        }
        return points;
    }

    private const string WaitingNotice = "Waiting for the server's answer to the command already sent; nothing else was sent.";

    // Sends the previewed command to the server, once, and reports the server's answer. Returns that answer, or null when
    // nothing was sent (no preview, already waiting, a refusing preview confirmed only once, no match).
    public async Task<Feedback?> ConfirmAsync()
    {
        if (Pending is not null)
        {
            Notice = WaitingNotice;
            Changed();
            return null;
        }
        if (Preview is not { } preview)
        {
            Notice = Mode == InteractionMode.None ? "Choose an action first." : "Nothing to send yet: choose a point or a target.";
            Changed();
            return null;
        }
        if (preview.Refusal is { } refusal && !_armed)
        {
            _armed = true;
            Notice = $"Not sent: the preview says the server would refuse. {refusal.Text} ({refusal.RuleRef}) Confirm again to send it anyway; the server decides.";
            Changed();
            return null;
        }
        return await SendNow(preview.Command, Describe(preview));
    }

    // Activates one of your models: the same command as its "Activate" hint button, sent the same way.
    public async Task<Feedback?> ActivateAsync(ModelId model)
    {
        if (Pending is not null)
        {
            Notice = WaitingNotice;
            Changed();
            return null;
        }
        if (_seat is not { } seat)
        {
            Notice = "Not in a match: nothing was sent.";
            Changed();
            return null;
        }
        Clear();
        return await SendNow(new ActivateModel(seat, model), $"Activate {model.Value}");
    }

    private async Task<Feedback?> SendNow(Command command, string label)
    {
        if (Send is not { } send)
        {
            Notice = "Not connected to a match: nothing was sent.";
            Changed();
            return null;
        }
        Pending = command;
        _armed = false;
        Answer = null;
        Notice = $"SENT: {label}. Waiting for the server's answer.";
        Changed();
        Feedback answer;
        try
        {
            answer = await send(command, label);
        }
        catch (Exception e)
        {
            // Whatever went wrong (a cancelled or failed send), the command may or may not have reached the server: report
            // it as unknown and never leave the battlefield stuck waiting.
            answer = new Feedback(FeedbackKind.Unknown, $"Sending failed ({e.GetType().Name}); the server reports what happened after reconnecting.");
        }
        Pending = null;
        Answer = answer;
        switch (answer.Kind)
        {
            case FeedbackKind.Accepted:
                Clear();
                Answer = answer;
                Notice = $"DONE: {label}. The server accepted it; the battlefield shows its result.";
                break;
            case FeedbackKind.Unknown:
                // The answer was lost: drop the choice so a second key press cannot send it twice.
                Clear();
                Answer = answer;
                Notice = $"UNKNOWN: {answer.Text} The choice was cleared so it is not sent twice.";
                break;
            default:
                var word = answer.Kind switch
                {
                    FeedbackKind.RulesRefused => "REFUSED BY THE RULES",
                    FeedbackKind.Stale => "OUT OF DATE",
                    _ => "NOT TAKEN",
                };
                if (Mode != InteractionMode.None && !StillPossible())
                {
                    Clear();
                    Answer = answer;
                }
                Notice = $"{word}: {answer.Text}{(answer.RuleRef is { } rule ? $" ({rule})" : "")}";
                break;
        }
        Changed();
        return answer;
    }

    // The command in words, for the player and for the screen's "last command".
    public static string Describe(Preview preview) => preview.Command switch
    {
        PlaceModel p => $"Place {p.Model.Value} at {TablePoint.Of(p.At)}",
        MoveModel m => $"Move {m.Model.Value} to {TablePoint.Of(m.Path[^1])}",
        DashModel d => $"Dash {d.Model.Value} to {TablePoint.Of(d.Path[^1])}",
        RetreatModel r => $"Retreat {r.Model.Value} to {TablePoint.Of(r.Path[^1])}",
        Shoot s => $"Shoot {s.Target.Value} with {s.Attacker.Value}",
        ChargeModel c => $"Charge {c.Target.Value} with {c.Model.Value}",
        FightModel f => $"Fight {f.Target.Value} with {f.Model.Value}",
        _ => preview.Verb,
    };

    // What the player needs to read: the action, the keys, and the preview's lines.
    public IReadOnlyList<string> Lines()
    {
        var lines = new List<string>();
        if (Mode == InteractionMode.None)
        {
            var available = Available;
            lines.Add(available.Count == 0
                ? "No action to preview right now."
                : $"Preview an action: {string.Join(", ", available.Select(m => $"{Key(m)} {Words(m)}"))}.");
        }
        else
        {
            lines.Add($"{Words(Mode)} with {Actor?.Value}. {Help()}");
            if (UsesCursor)
            {
                lines.Add($"Cursor at {Cursor}.{(IsPath ? $" Waypoints fixed: {_waypoints.Count}." : "")}");
            }
            if (Preview is { } preview)
            {
                lines.AddRange(preview.Lines());
            }
            else
            {
                lines.Add(IsPath ? "Move the cursor to where the model should go." : "No target to preview.");
            }
        }
        if (Notice is { } notice)
        {
            lines.Add(notice);
        }
        return lines;
    }

    public static string Words(InteractionMode mode) => mode switch
    {
        InteractionMode.Place => "Place a model",
        InteractionMode.Move => "Move",
        InteractionMode.Dash => "Dash",
        InteractionMode.Retreat => "Retreat",
        InteractionMode.Shoot => "Shoot",
        InteractionMode.Charge => "Charge",
        InteractionMode.Fight => "Fight",
        _ => "Nothing",
    };

    // The key that starts each action on the battlefield (F is Fit and P is Previous, so Fight is X and Place is L).
    public static string Key(InteractionMode mode) => mode switch
    {
        InteractionMode.Place => "L",
        InteractionMode.Move => "M",
        InteractionMode.Dash => "D",
        InteractionMode.Retreat => "R",
        InteractionMode.Shoot => "S",
        InteractionMode.Charge => "C",
        InteractionMode.Fight => "X",
        _ => "",
    };

    private string Help() => Mode switch
    {
        InteractionMode.Place => "Arrows move the cursor (half an inch, 2\" with Shift), N and P choose the model, Enter or Space sends it, Esc cancels.",
        _ when IsPath => "Arrows move the cursor (half an inch, 2\" with Shift), Enter fixes a waypoint, Backspace removes one, Space sends it, Esc cancels.",
        _ => "N and P choose the target (or click it), Enter or Space sends it, Esc cancels.",
    };

    private Preview? Compute()
    {
        if (_seat is not { } seat || Actor is not { } actor)
        {
            return null;
        }
        switch (Mode)
        {
            case InteractionMode.Place:
                return Previews.Place(_state, seat, actor, Previews.PointAt(Cursor.X, Cursor.Y));
            case InteractionMode.Move or InteractionMode.Dash or InteractionMode.Retreat:
                var points = PathPoints();
                return points.Count == 0 ? null : Previews.Path(_state, seat, Kind(Mode), actor, points.Select(p => Previews.PointAt(p.X, p.Y)).ToList());
            case InteractionMode.Shoot or InteractionMode.Charge or InteractionMode.Fight:
                return Target is { } target ? Previews.Target(_state, seat, Kind(Mode), actor, target) : null;
            default:
                return null;
        }
    }

    private static PreviewKind Kind(InteractionMode mode) => mode switch
    {
        InteractionMode.Place => PreviewKind.Place,
        InteractionMode.Move => PreviewKind.Move,
        InteractionMode.Dash => PreviewKind.Dash,
        InteractionMode.Retreat => PreviewKind.Retreat,
        InteractionMode.Shoot => PreviewKind.Shoot,
        InteractionMode.Charge => PreviewKind.Charge,
        _ => PreviewKind.Fight,
    };

    private bool StillPossible()
    {
        if (!Available.Contains(Mode))
        {
            return false;
        }
        if (Mode == InteractionMode.Place)
        {
            return Actor is { } a && Undeployed().Any(m => m.Id == a);
        }
        return Actor == _state.ActiveModel;
    }

    private List<ModelState> Undeployed() =>
        _seat is { } seat ? _state.Models.Where(m => m.Seat == seat && !m.Deployed).OrderBy(m => m.Id.Value, StringComparer.Ordinal).ToList() : [];

    private TablePoint ZoneCentre()
    {
        var field = _state.Setup!.Scenario!.Battlefield;
        if (_seat is { } seat && _state.SeatFor(seat).ZoneId is { } id && field.Zone(id) is { } zone)
        {
            var area = TableRect.Of(zone.Area.Bounds);
            return new TablePoint(Math.Round(area.CentreX * 2) / 2, Math.Round(area.CentreY * 2) / 2);
        }
        return new TablePoint(TableRect.Inches(field.Width) / 2, TableRect.Inches(field.Height) / 2);
    }

    private static double Distance(Point2 a, Point2 b)
    {
        var dx = TableRect.Inches(a.X) - TableRect.Inches(b.X);
        var dy = TableRect.Inches(a.Y) - TableRect.Inches(b.Y);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private void Clear()
    {
        Mode = InteractionMode.None;
        Actor = null;
        Target = null;
        _armed = false;
        Notice = null;
        Answer = null;
        _waypoints.Clear();
    }

    private void Changed()
    {
        _stale = true;
        Version++;
    }
}

// Mouse hover asks for a preview at most once per interval: the newest point waits until the interval has passed, so a
// fast-moving mouse never queues up stale work. Time is passed in (seconds), so this is tested without a clock.
public sealed class HoverThrottle(double intervalSeconds)
{
    private TablePoint? _pending;
    private double _last = double.NegativeInfinity;

    public void Offer(TablePoint point) => _pending = point;

    public TablePoint? Take(double nowSeconds)
    {
        if (_pending is not { } point || nowSeconds - _last < intervalSeconds)
        {
            return null;
        }
        _pending = null;
        _last = nowSeconds;
        return point;
    }
}
