using Match.Protocol;
using Rules.Core;
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

    // Battlefield UI, stage K6: one Combat action for Shoot, Charge and Fight. Choosing an enemy opens a small menu of the
    // attacks that apply to it now; each stays its own command and its own preview.
    Combat,
}

// Battlefield UI 3, stage U1 (approved by Jason on 2026-10-22): one entry of the Combat menu, which lists the attacks of
// the acting model before any target is chosen: the action, whether it is open (the rules open the action and accept at
// least one enemy as its target), how many enemies they accept, and if it is not open, the rules' reason (greyed out).
public sealed record CombatEntry(InteractionMode Mode, string Label, string Key, bool Enabled, string? Why, int Targets);

// Stage K6: what picking an entry of the Combat menu does next.
public enum CombatPick
{
    // Nothing (greyed out, or not choosing in Combat).
    None,

    // Stage U1: the action is chosen; the player now chooses its target (the enemies the rules accept are ringed). Nothing
    // is sent: Confirm beside the chosen target (or Space) sends.
    Target,
}

// What a button of the battlefield's action bar does.
// What a left click on the table did (Interaction.ClickAt).
public enum ClickResult
{
    None,
    Waypoint,
    Send,
    Refused,
}

public enum BarKind
{
    // Starts choosing an action (Mode): Move, Shoot, Place ...
    Start,

    // Sends ActivateModel for the selected model.
    Activate,

    // A line of text, not a button (shown disabled): "Not activated yet".
    Note,

    // Sends an answer to a decision of yours (Command): Blood Markers, free attacks, the own model a shot hits.
    Decide,

    // Sends EndActivation.
    EndActivation,

    // Sends the previewed command (the same as Space).
    Confirm,

    // Removes the last waypoint.
    Undo,

    // Stops choosing (Esc, right-click).
    Cancel,

    // A command is on its way; nothing to press.
    Waiting,

    // Stage K4: chooses the model to place next (Model); nothing is sent.
    Pick,
}

// One button of the action bar: a short label, a tooltip that says what it does (and its optional key), and the command
// it sends for Activate and End activation (the rules' own legal command, which the server still decides).
public sealed record BarItem(BarKind Kind, string Label, string Tip, bool Enabled = true, InteractionMode Mode = InteractionMode.None, Command? Command = null, ModelId? Model = null)
{
    // QoL H5: the key that does the same, shown on the button ("Move · M"); null when there is none. No key is new.
    public string? Key => Kind switch
    {
        BarKind.Start => Interaction.Key(Mode) is { Length: > 0 } k ? k : null,
        BarKind.Activate => "A",
        BarKind.Confirm => "Space",
        BarKind.Undo => "Backspace",
        BarKind.Cancel => "Esc",
        _ => null,
    };

    public string ButtonText => Key is { } key ? $"{Label} · {key}" : Label;
}

// The bar next to a model (Anchor; none while placing): only the choices that make sense now.
public sealed record ActionBar(ModelId? Anchor, IReadOnlyList<BarItem> Items)
{
    public static readonly ActionBar Empty = new(null, []);
}

public sealed class Interaction
{
    // Keyboard cursor steps, in inches: half an inch, two inches with Shift.
    public const double Step = 0.5;
    public const double BigStep = 2.0;

    private static readonly InteractionMode[] ActionModes =
        [InteractionMode.Move, InteractionMode.Dash, InteractionMode.Retreat, InteractionMode.Shoot, InteractionMode.Charge, InteractionMode.Fight];

    private readonly List<TablePoint> _waypoints = [];

    // The server's state, as the mirror holds it.
    private MatchState _server = MatchState.None;

    // What the previews and the bar work from: the server's state, or while a unit is only chosen, the state as the
    // server would hold it after that unit's activation (Previews.AsIfActivated). Never sent, never shown as the match.
    private MatchState _state = MatchState.None;

    // The state the previews are made on (the server's, or the preview-only state after choosing a unit), for the UI check.
    public MatchState PreviewState => _state;
    private Seat? _seat;
    private Preview? _preview;
    private bool _stale = true;

    public InteractionMode Mode { get; private set; }

    // A unit of yours chosen to activate but not activated on the server yet: nothing has been sent for it, so another
    // eligible unit can be chosen instead. Its first action (or "Take no action") sends ActivateModel first.
    public ModelId? Chosen { get; private set; }

    // Whether this unit of yours could be chosen now (the rules offer its activation: TurnSequence.LegalCommands).
    public bool CanChoose(ModelId id) => _seat is { } seat && Previews.AsIfActivated(_server, seat, id) is not null;

    // Chooses a unit to activate, or switches to another one, before anything is sent; null drops the choice. Refused
    // (false, with a notice) while a command is on its way, for a unit the rules do not offer, and once your activation
    // has started on the server: from then on that activation is fixed until it ends.
    public bool Choose(ModelId? id)
    {
        if (Pending is not null)
        {
            return false;
        }
        if (id is not { } wanted)
        {
            if (Chosen is not null)
            {
                Chosen = null;
                Clear();
                _state = _server;
                Changed();
            }
            return true;
        }
        if (Chosen == wanted || _seat is not { } seat)
        {
            return Chosen == wanted;
        }
        if (_server.ActiveModel is { } active && active != wanted && _server.FindModel(active)?.Seat == seat && _server.FindModel(wanted)?.Seat == seat)
        {
            Notice = $"{NameOf(active)} has already started its activation on the server, so you cannot switch to {NameOf(wanted)}: " +
                $"an activation is fixed once the server has it. End {NameOf(active)}'s activation first.";
            Changed();
            return false;
        }
        if (Previews.AsIfActivated(_server, seat, wanted) is not { } view)
        {
            return false;
        }
        Clear();
        Chosen = wanted;
        _state = view;
        Notice = null;
        Changed();
        return true;
    }

    // The preview state: while a unit is chosen and still eligible, as if it were activating; otherwise the server's.
    private MatchState Effective()
    {
        if (Chosen is { } chosen && _seat is { } seat && Previews.AsIfActivated(_server, seat, chosen) is { } view)
        {
            return view;
        }
        Chosen = null;
        return _server;
    }

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

    // Stage U1: Combat itself chooses no target (it lists the attacks); Shoot, Charge and Fight do.
    public bool IsTarget => Mode is InteractionMode.Shoot or InteractionMode.Charge or InteractionMode.Fight;

    // Stage U1: the attack being chosen was picked from the Combat menu, so Esc goes back to the menu first.
    public bool FromCombat { get; private set; }

    // Stage K6: Shoot, Charge and Fight, which the bar offers as one Combat action.
    public static bool IsAttack(InteractionMode mode) => mode is InteractionMode.Shoot or InteractionMode.Charge or InteractionMode.Fight;

    public bool UsesCursor => IsPath || Mode == InteractionMode.Place || Mines is not null;

    // Trench Warfare (p. 162): while this seat must set up its Mine Markers, the markers put down so far on this screen.
    // A click on the battlefield goes to it, and the decision card sends the whole layout.
    public MineLaying? Mines { get; private set; }

    // A new state from the server. The choice in progress is kept while it still makes sense, and cleared (with a
    // notice) when the match has moved on: another activation, the end of your deployment turn, a decision to make.
    public void Update(MatchState state, Seat? seat)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (ReferenceEquals(state, _server) && seat == _seat)
        {
            return;
        }
        // Another seat (hot-seat switching): a choice made for the previous seat never carries over.
        if (seat != _seat && Pending is null && Mode != InteractionMode.None)
        {
            Clear();
            Notice = SeatShownNotice;
        }
        if (seat != _seat)
        {
            Chosen = null;
        }
        // Stage K4: a new state (or seat) brings placing back after Esc dropped it.
        _placeDismissed = false;
        _server = state;
        _seat = seat;
        _state = Effective();
        _armed = false;
        // While a command is on its way, the server's answer decides what happens to the choice.
        if (Pending is null && Mode != InteractionMode.None && !StillPossible())
        {
            if (Mode == InteractionMode.Place && PlacingNow)
            {
                // Stage K4: the model was placed and you place another: the next one, keeping the cursor and the answer.
                Actor = NextToPlace();
                _armed = false;
            }
            else
            {
                Clear();
                Notice = "The match moved on, so the preview was cleared.";
            }
        }
        // A new laying for a new decision (or seat); the markers already put down are kept while it is the same one.
        if (seat is { } layer && state.Pending is { Kind: DecisionKind.LayMines } lay && lay.Seat == layer)
        {
            if (Mines is not { } laying || laying.Seat != layer || !ReferenceEquals(laying.State.Setup, state.Setup))
            {
                Mines = new MineLaying(state, layer);
            }
        }
        else
        {
            Mines = null;
        }
        FightAfterCharge();
        if (AutoPlace() && Notice == SeatShownNotice)
        {
            // The seat now shown places its models: nothing of the other seat's was lost worth a note.
            Notice = null;
        }
        Changed();
    }

    // Stage K7: the charger and the enemy of the last accepted charge, until the Combat menu opens on it with Fight ready
    // (when the rules allow that Fight) or the activation ends.
    private (ModelId Charger, ModelId Target)? _afterCharge;

    // For the UI check: how many times Combat opened by itself after a charge.
    public int CombatAfterCharge { get; private set; }

    private void FightAfterCharge()
    {
        if (_afterCharge is not { } after)
        {
            return;
        }
        if (_server.ActiveModel != after.Charger || _seat is not { } seat)
        {
            _afterCharge = null;
            return;
        }
        if (Pending is not null || Mode != InteractionMode.None || _state.Pending is not null || !Available.Contains(InteractionMode.Combat))
        {
            return;
        }
        // The charge's events may still be on their way: wait until the rules accept a Fight on that enemy.
        if (Previews.ActionOpen(_state, seat, after.Charger, PreviewKind.Fight) is not null || !Previews.Target(_state, seat, PreviewKind.Fight, after.Charger, after.Target).Legal)
        {
            return;
        }
        _afterCharge = null;
        // Stage U1: Fight is chosen on the charged enemy, waiting for Confirm fight (Esc goes back to the Combat menu).
        Mode = InteractionMode.Fight;
        FromCombat = true;
        Actor = after.Charger;
        Target = after.Target;
        _armed = false;
        CombatAfterCharge++;
    }

    // Stage K4: on your deployment turn the battlefield is in Place mode by itself (no button, no key): a click on the
    // table places the model shown by the ghost. Esc drops it until the next state from the server (or L brings it back).
    private bool _placeDismissed;

    // The model the player chose to place next (a chip on the bar, or N and P); the first undeployed one otherwise.
    private ModelId? _placeNext;

    private bool PlacingNow => Available is [InteractionMode.Place];

    private ModelId NextToPlace()
    {
        var undeployed = Undeployed();
        return _placeNext is { } wanted && undeployed.Any(m => m.Id == wanted) ? wanted : undeployed[0].Id;
    }

    private bool AutoPlace()
    {
        if (Pending is not null || Mode != InteractionMode.None || _placeDismissed || !PlacingNow)
        {
            return false;
        }
        Mode = InteractionMode.Place;
        Actor = NextToPlace();
        Cursor = ZoneCentre();
        _armed = false;
        return true;
    }

    private const string SeatShownNotice = "Another seat is shown now, so the preview was cleared.";

    // Stage K4: what to do now while placing, for the HUD: "Place Sharpshooter (A1) in ZONE A: click a spot".
    public string? Prompt => Mode == InteractionMode.Place && Pending is null && Actor is { } actor && _seat is { } seat
        ? $"Place {(NameOf(actor) is var name && name != actor.Value ? $"{name} ({actor.Value})" : actor.Value)} in ZONE {Client.Core.Words.SeatLetter(seat)}: click a spot"
        : null;

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
                return [.. ActionModes, InteractionMode.Combat];
            }
            return [];
        }
    }

    // Stage K6: what the bar offers: Offered with Shoot, Charge and Fight folded into one Combat action where the first
    // of them was (Combat appears exactly when at least one of them is open).
    public IReadOnlyList<InteractionMode> BarOffered
    {
        get
        {
            var offered = Offered;
            var list = new List<InteractionMode>();
            foreach (var mode in offered)
            {
                if (IsAttack(mode))
                {
                    if (!list.Contains(InteractionMode.Combat))
                    {
                        list.Add(InteractionMode.Combat);
                    }
                }
                else if (mode != InteractionMode.Combat)
                {
                    list.Add(mode);
                }
            }
            return list;
        }
    }

    // The actions worth offering as buttons now: those of Available that the preview checks say the acting model can
    // take at all (Previews.ActionOpen: not already taken, a target in reach, engaged for a Retreat). Advisory, like every
    // preview: the keys still start any Available action, and the server decides every command.
    public IReadOnlyList<InteractionMode> Offered
    {
        get
        {
            var available = Available;
            if (_seat is not { } seat || _state.ActiveModel is not { } actor || available.Count == 0 || available[0] == InteractionMode.Place)
            {
                return available;
            }
            return [.. available.Where(mode => mode != InteractionMode.Combat && ActionOpen(seat, actor, Kind(mode)) is null)];
        }
    }

    // Why an action is not offered now, in the rules' words; null when it is offered (or not an action of this moment).
    public string? WhyNot(InteractionMode mode) =>
        _seat is { } seat && _state.ActiveModel is { } actor && Available.Contains(mode) && mode != InteractionMode.Place && mode != InteractionMode.Combat
            ? ActionOpen(seat, actor, Kind(mode))?.Text
            : null;

    // How far the action being chosen can still take the acting model, in inches (drawn as a ring); null when none.
    // Stage U4: the compact numbers for the action being chosen (ActionStats), or null.
    public string? Stats => _seat is { } s && Actor is { } a ? ActionStats.For(_state, s, a, Mode) : null;

    public double? Reach() => _seat is { } s && Actor is { } a ? Previews.Reach(_state, s, a) : null;

    // The action bar for the model the player selected (or the activating one): only what can be done now. While an
    // action is being chosen it holds that action's send, undo and cancel buttons; while a command is on its way, only
    // a note that it is waiting.
    public ActionBar Bar(ModelId? selected)
    {
        if (_seat is not { } seat || _state.Phase is Phase.None or Phase.Ended || _state.Setup?.Scenario is null)
        {
            return ActionBar.Empty;
        }
        if (Pending is not null)
        {
            return new(Actor ?? _state.ActiveModel, [new BarItem(BarKind.Waiting, "Waiting for the server…", "The command was sent; the server's answer comes next.", Enabled: false)]);
        }
        if (Mode != InteractionMode.None)
        {
            var items = new List<BarItem>
            {
                new(BarKind.Confirm, ConfirmLabel(Mode), Preview is { Legal: false }
                    ? "The preview says the server would refuse this (see the battlefield). Click twice to send it anyway; the server decides."
                    : $"Send it. The server decides. ({(IsTarget ? "Or the Confirm button beside the target" : "Or just click the destination")}; key Space.)",
                    Enabled: Preview is not null, Mode: Mode),
            };
            if (IsPath && _waypoints.Count > 0)
            {
                items.Add(new(BarKind.Undo, "Undo point", "Remove the last waypoint (key Backspace). Shift+click adds one.", Mode: Mode));
            }
            items.Add(new(BarKind.Cancel, "Cancel", "Stop choosing this action; nothing is sent (Esc or right-click).", Mode: Mode));
            if (Mode == InteractionMode.Place)
            {
                // Stage K4: a chip per model still to place; the one placed next is marked. N and P do the same.
                items.AddRange(Undeployed().Select(m => new BarItem(BarKind.Pick, $"{(m.Id == Actor ? "▶ " : "")}{m.Id.Value}{(NameOf(m.Id) is var n && n != m.Id.Value ? $" {n}" : "")}",
                    m.Id == Actor ? "The model a click places next." : "Place this model next (N and P go through them). Nothing is sent.", Model: m.Id)));
            }
            return new(Mode == InteractionMode.Place ? null : Actor, items);
        }
        if (DecisionBar(seat) is { } decision)
        {
            return decision;
        }
        var offered = Offered;
        if (offered.Count == 1 && offered[0] == InteractionMode.Place)
        {
            return new(null, [new(BarKind.Start, "Place a model", Tip(InteractionMode.Place), Mode: InteractionMode.Place)]);
        }
        var legal = _state.Pending is null ? LegalCommands().Where(c => CommandSeats.Of(c) == seat).ToList() : [];
        if (_state.Phase == Phase.Activation && _state.ActiveModel is { } active && _state.FindModel(active)?.Seat == seat)
        {
            var items = BarOffered.Select(m => new BarItem(BarKind.Start, Words(m), Tip(m), Mode: m)).ToList();
            if (legal.OfType<EndActivation>().FirstOrDefault() is { } end)
            {
                items.Add(Chosen is null
                    ? new(BarKind.EndActivation, "End activation", "Finish this model's activation; then the next activation begins.", Command: end)
                    : new(BarKind.EndActivation, "Take no action", "Activate this unit and end its activation at once, without acting.", Command: end));
            }
            if (Chosen is not null)
            {
                items.Add(new(BarKind.Note, "Not activated yet", "Nothing is sent yet: click another of your units to switch. Its first action activates this unit.", Enabled: false));
            }
            return items.Count == 0 ? ActionBar.Empty : new(active, items);
        }
        if (selected is { } chosen && legal.OfType<ActivateModel>().FirstOrDefault(a => a.Model == chosen) is { } activate)
        {
            return new(chosen, [new(BarKind.Activate, "Activate", "Start this model's activation, then choose its actions (key A).", Command: activate)]);
        }
        return ActionBar.Empty;
    }

    // A decision of yours about a model on the battlefield (spending Blood Markers, free attacks on a retreating model,
    // which own model a shot into melee hits): its answers, as the rules list them (TurnSequence.LegalCommands), on a bar
    // next to the model concerned. The server decides each answer, exactly as with the side panel's buttons.
    private ActionBar? DecisionBar(Seat seat)
    {
        if (_state.Pending is not { } pending || pending.Seat != seat)
        {
            return null;
        }
        ModelId? anchor = pending.Kind switch
        {
            DecisionKind.SpendMarkersOnSuccessRoll => _state.InProgress?.Actor,
            DecisionKind.SpendMarkersOnInjuryRoll => _state.InProgress?.Target,
            DecisionKind.ChooseFriendlyTarget => _state.InProgress?.Actor,
            DecisionKind.FreeFight => _state.Retreat?.Model,
            _ => null,
        };
        if (anchor is not { } holder)
        {
            return null;
        }
        var items = LegalCommands().Where(c => CommandSeats.Of(c) == seat).Select(c => c switch
        {
            SpendMarkers { Count: 0 } m => new BarItem(BarKind.Decide, "No Blood", "Spend no Blood Markers on this roll.", Command: m),
            SpendMarkers m => new BarItem(BarKind.Decide, $"Spend {m.Count} Blood", pending.Kind == DecisionKind.SpendMarkersOnSuccessRoll
                ? $"Spend {m.Count} of {NameOf(holder)}'s Blood Markers: -1 DICE each on its Success Roll."
                : $"Spend {m.Count} of {NameOf(holder)}'s Blood Markers: +1 INJURY DICE each on the Injury Roll against it.", Command: m),
            FreeFight f => new BarItem(BarKind.Decide, $"Free attack: {NameOf(f.Attacker)}", $"{NameOf(f.Attacker)} attacks the retreating model first.", Command: f),
            DeclineFreeFights d => new BarItem(BarKind.Decide, "No more free attacks", "Let the model retreat without further free attacks.", Command: d),
            ChooseFriendlyTarget t => new BarItem(BarKind.Decide, $"Hit {NameOf(t.Victim)}", $"The shot into melee hits your own {NameOf(t.Victim)}.", Command: t),
            _ => null,
        }).OfType<BarItem>().ToList();
        return items.Count == 0 ? null : new(holder, items);
    }

    // Target modes: every enemy that could be chosen, and whether the preview would accept it (null) or why not.
    public IReadOnlyList<(ModelId Model, Rejection? Refusal)> TargetVerdicts()
    {
        if (!IsTarget || _seat is not { } seat || Actor is not { } actor)
        {
            return [];
        }
        if (_verdicts is null || _verdictsVersion != Version)
        {
            _verdicts = [.. Targets().Select(t => (t, TargetPreview(seat, Kind(Mode), actor, t).Refusal))];
            _verdictsVersion = Version;
        }
        return _verdicts;
    }

    private List<(ModelId, Rejection?)>? _verdicts;
    private long _verdictsVersion = -1;

    // Line-of-sight stage L4: in the target modes, how the acting model sees every enemy (the icon above it), from the
    // rules (TargetSights, Cover.Of). Empty outside the target modes.
    public IReadOnlyList<TargetSight> TargetSights()
    {
        if (!IsTarget || Actor is not { } actor)
        {
            return [];
        }
        // Performance: worked out once per state and acting model (the state is immutable; a new one is a new object).
        if (_sights is null || !ReferenceEquals(_sightsState, _state) || _sightsActor != actor)
        {
            _sights = Client.Core.TargetSights.Of(_state, actor);
            _sightsState = _state;
            _sightsActor = actor;
        }
        return _sights;
    }

    private IReadOnlyList<TargetSight>? _sights;
    private MatchState? _sightsState;
    private ModelId? _sightsActor;

    // Performance: Previews.Target for one target, worked out once per state. Opening Combat, choosing Shoot, Charge or
    // Fight, pointing at an enemy and clicking it all ask for the same previews; before, each asked again (a Charge
    // preview can take tens of milliseconds). The answer is a function of the state, the seat, the action, the acting
    // model and the target only, and MatchState is immutable, so it is kept exactly as long as the state object is the
    // same one (Update, choosing a unit or dropping it replace the state and so empty the memo). Nothing is decided from
    // an older state, and the server still checks every command itself.
    private Preview TargetPreview(Seat seat, PreviewKind kind, ModelId actor, ModelId target)
    {
        Remember(seat);
        if (!_targetMemo.TryGetValue((kind, actor, target), out var preview))
        {
            preview = Previews.Target(_state, seat, kind, actor, target);
            _targetMemo[(kind, actor, target)] = preview;
            TargetPreviewsWorkedOut++;
        }
        return preview;
    }

    // The memos below belong to one state object and seat; a new one empties them.
    private void Remember(Seat seat)
    {
        if (!ReferenceEquals(_memoState, _state) || _memoSeat != seat)
        {
            _targetMemo.Clear();
            _openMemo.Clear();
            _memoState = _state;
            _memoSeat = seat;
        }
    }

    // Performance: the action bar is worked out every frame, and asked Previews.ActionOpen for each action twice and the
    // rules' legal commands once each time (tens of milliseconds a frame in some states). Both depend only on the state
    // (and seat, model and action), so they are kept for the state object exactly like the target previews.
    private Rejection? ActionOpen(Seat seat, ModelId actor, PreviewKind kind)
    {
        Remember(seat);
        if (!_openMemo.TryGetValue((kind, actor), out var closed))
        {
            closed = Previews.ActionOpen(_state, seat, actor, kind);
            _openMemo[(kind, actor)] = closed;
        }
        return closed;
    }

    private IReadOnlyList<Command> LegalCommands()
    {
        if (!ReferenceEquals(_legalState, _state))
        {
            _legal = [.. TurnSequence.LegalCommands(_state)];
            _legalState = _state;
        }
        return _legal!;
    }

    private readonly Dictionary<(PreviewKind, ModelId), Rejection?> _openMemo = [];
    private IReadOnlyList<Command>? _legal;
    private MatchState? _legalState;
    private readonly Dictionary<(PreviewKind, ModelId, ModelId), Preview> _targetMemo = [];
    private MatchState? _memoState;
    private Seat? _memoSeat;

    // How many target previews have been worked out (not taken from the memo), for the tests.
    public int TargetPreviewsWorkedOut { get; private set; }

    // Line-of-sight stage L6: while choosing where to move (Move, Dash, Retreat), how the model would see every enemy from
    // the end of a path the preview accepts, at the height the battlefield gives that point. The same rules function as
    // the target icons; it changes nothing about movement. Empty otherwise.
    public IReadOnlyList<TargetSight> DestinationSights()
    {
        if (!IsPath || Actor is not { } actor || Preview is not { Legal: true, Path.Count: > 1 } preview)
        {
            return [];
        }
        if (_destination is null || _destinationVersion != Version)
        {
            var end = preview.Path[^1].At;
            var at = new Rules.Core.Geometry.Point2(
                Rules.Core.Geometry.Length.FromThousandths((long)Math.Round(end.X * 1000)),
                Rules.Core.Geometry.Length.FromThousandths((long)Math.Round(end.Y * 1000)));
            _destination = Client.Core.TargetSights.Of(_state, actor, at);
            _destinationVersion = Version;
        }
        return _destination;
    }

    private IReadOnlyList<TargetSight>? _destination;
    private long _destinationVersion = -1;

    // Pointing at a model in Shoot, Charge or Fight (QoL H1): the preview of choosing it as the target, from the same rules
    // check as a click (Previews.Target on the same state), before anything is chosen. Null outside target modes or for the
    // model already chosen. Nothing is chosen or sent.
    public Preview? PreviewOf(ModelId target)
    {
        if (!IsTarget || _seat is not { } seat || Actor is not { } actor || target == Target || target == actor)
        {
            return null;
        }
        if (_pointedVersion != Version || _pointedAt != target)
        {
            _pointed = TargetPreview(seat, Kind(Mode), actor, target);
            _pointedVersion = Version;
            _pointedAt = target;
        }
        return _pointed;
    }

    private Preview? _pointed;
    private ModelId? _pointedAt;
    private long _pointedVersion = -1;

    // A left click on the table while placing or choosing a path. A plain click is the destination: when the preview
    // says the server would accept it, the caller sends it at once (Send); when the preview refuses, nothing is sent
    // (Refused, with RefusedNote saying why). Shift+click adds a waypoint instead, for a path around something.
    public ClickResult ClickAt(double x, double y, bool waypoint = false)
    {
        if (Pending is not null || !UsesCursor)
        {
            return ClickResult.None;
        }
        SetCursor(x, y);
        if (waypoint && IsPath)
        {
            return AddPoint() ? ClickResult.Waypoint : ClickResult.None;
        }
        if (Preview is not { } preview)
        {
            return ClickResult.None;
        }
        if (preview.Legal)
        {
            return ClickResult.Send;
        }
        Notice = $"Not sent: {preview.Refusal!.Text} ({preview.Refusal.RuleRef}) Choose another point; the keys can still send it (Space twice) for the server to decide.";
        Changed();
        return ClickResult.Refused;
    }

    // What to show above the model when a click was not sent: too far for its movement, or not a legal end point.
    public UnitNote? RefusedNote()
    {
        if (Preview is not { Refusal: { } refusal } preview)
        {
            return null;
        }
        var text = preview.Kind == PreviewKind.Place ? "INVALID PLACEMENT"
            : preview.Path.Count > 0 && Reach() is { } reach && preview.Path[^1].Inches > reach + 1e-9 ? "TOO FAR"
            : refusal.Code == Rules.Core.ReasonCode.RetreatEndsEngaged ? "TOO CLOSE"
            : "INVALID MOVE";
        return new UnitNote(preview.Model, text, NoteTone.Failure);
    }

    // Where the acting model could end its Move, Dash or Retreat with one more straight leg (Previews.Reachable), worked
    // out again only when the state, the action or the waypoints change (not when the cursor moves).
    public IReadOnlyList<TablePoint> ReachArea()
    {
        if (!IsPath || _seat is not { } seat || Actor is not { } actor)
        {
            return [];
        }
        var key = (_state, Mode, actor, string.Join(";", _waypoints), Sides);
        if (_area is null || !_areaKey.Equals(key))
        {
            _area = Previews.Reachable(_state, seat, Kind(Mode), actor, [.. _waypoints.Select(p => Previews.PointAt(p.X, p.Y))], sides: Sides);
            _areaKey = key;
        }
        return _area;
    }

    private IReadOnlyList<TablePoint>? _area;
    private (MatchState, InteractionMode, ModelId, string, bool) _areaKey;

    // Towers proposal, stage T6: whether a Move or Dash to another level climbs a tower's side (a Risky Success Roll,
    // p. 39) instead of using a ladder. K switches it; a new action starts with ladders.
    public bool ClimbSides { get; private set; }

    private bool Sides => ClimbSides && Mode is InteractionMode.Move or InteractionMode.Dash;

    // Can K switch it now: a Move or Dash on a battlefield with towers.
    public bool CanClimbSides => Mode is InteractionMode.Move or InteractionMode.Dash && _state.Setup?.Scenario?.Battlefield.Terrain.Any(t => t.IsTower) == true;

    public bool ToggleClimbSides()
    {
        if (!CanClimbSides)
        {
            return false;
        }
        ClimbSides = !ClimbSides;
        _armed = false;
        Changed();
        return true;
    }

    // How the bar names a model ("Runner (b2)"); the battlefield sets it from its scene.
    public Func<ModelId, string> NameOf { get; set; } = id => id.Value;

    private static string ConfirmLabel(InteractionMode mode) => mode switch
    {
        InteractionMode.Place => "Place here",
        InteractionMode.Move => "Move here",
        InteractionMode.Dash => "Dash here",
        InteractionMode.Retreat => "Retreat here",
        InteractionMode.Shoot => "Shoot target",
        InteractionMode.Charge => "Charge target",
        _ => "Fight target",
    };

    // What each action does, in a line, with its optional key.
    public static string Tip(InteractionMode mode) => mode switch
    {
        InteractionMode.Place => "Place your next model in your deployment zone: click a spot (key L).",
        InteractionMode.Move => "Move up to the model's Movement: click a spot in the shaded area to move there; Shift+click adds a waypoint (key M).",
        InteractionMode.Dash => "Move again up to the model's Movement, with a Risky roll that decides whether it works: click a spot in the shaded area (key D).",
        InteractionMode.Retreat => "Leave melee: click a spot in the shaded area, more than 1\" from every enemy; enemies within 1\" may attack first (key R).",
        InteractionMode.Shoot => "Shoot an enemy in range and in sight: click a target, then Confirm shoot beside it (key S).",
        InteractionMode.Charge => "Charge an enemy within 12\" into melee; a D6 adds reach: in Combat (C), choose Charge, click a target, then Confirm charge beside it (key 2).",
        InteractionMode.Fight => "Attack an enemy within 1\": click a target, then Confirm fight beside it (key X).",
        InteractionMode.Combat => "Shoot, Charge or Fight: choose the attack (1, 2, 3), click an enemy the rules accept (ringed), then Confirm beside it (or Space); an attack the rules refuse is greyed out with the reason (key C).",
        _ => "",
    };

    // Battlefield UI 2, stage N2 (decision D3, approved by Jason on 2026-10-21): when the rules leave the activating model
    // of the seat shown nothing to do (Rules.Core.Activation.Finished, on the server's state), the battlefield says so
    // and, AutoEndDelay seconds later, sends EndActivation once, as the End activation button would. Esc keeps the
    // activation going (the button still ends it). Never while a command is on its way, while a unit is only chosen, or
    // for the seat not shown (hot-seat). The server decides; once sent for an activation it is never sent again for it,
    // so a refusal is shown, not repeated.
    public const double AutoEndDelay = 1.5;

    // Off only for the UI check's driver, which plays its own fixed sequence of commands (on in the game).
    public bool AutoEnd { get; set; } = true;

    // The note shown while the activation is about to end by itself; null otherwise.
    public string? AutoEndNote { get; private set; }

    // How many EndActivation commands were sent by this (for the tests and the UI check).
    public int AutoEndsSent { get; private set; }

    private MatchState? _finishedFor;
    private bool _finished;
    private string? _autoEndKey;
    private double _autoEndSince;
    private string? _autoEndKept;
    private string? _autoEndSent;

    // The activation the rules say is finished and that this seat may end now, as "turn|model"; null when there is none.
    private string? FinishedActivation()
    {
        if (!AutoEnd || Pending is not null || Chosen is not null || _seat is not { } seat || _server.ActiveModel is not { } active
            || _server.FindModel(active)?.Seat != seat)
        {
            return null;
        }
        if (!ReferenceEquals(_finishedFor, _server))
        {
            _finishedFor = _server;
            _finished = Activation.Finished(_server);
        }
        return _finished ? $"{_server.Turn}|{active.Value}" : null;
    }

    // Called every frame with the time in seconds (passed in, so it is tested without a clock). Returns the server's
    // answer when it sent EndActivation, else null.
    public async Task<Feedback?> TickAutoEndAsync(double now)
    {
        var key = FinishedActivation();
        if (key is null || key == _autoEndKept || key == _autoEndSent)
        {
            if (AutoEndNote is not null)
            {
                AutoEndNote = null;
                _autoEndKey = null;
                Changed();
            }
            return null;
        }
        var active = _server.ActiveModel!.Value;
        if (_autoEndKey != key)
        {
            _autoEndKey = key;
            _autoEndSince = now;
            AutoEndNote = $"Nothing left for {NameOf(active)} to do: its activation ends by itself. Esc keeps it going.";
            Changed();
            return null;
        }
        if (now - _autoEndSince < AutoEndDelay)
        {
            return null;
        }
        _autoEndSent = key;
        _autoEndKey = null;
        AutoEndNote = null;
        AutoEndsSent++;
        return await SendCommandAsync(new EndActivation(_server.FindModel(active)!.Seat), $"End {NameOf(active)}'s activation");
    }

    // Esc while the note is shown: the activation goes on (the End activation button ends it). False when no note was shown.
    public bool KeepActivation()
    {
        if (AutoEndNote is null)
        {
            return false;
        }
        _autoEndKept = _autoEndKey;
        _autoEndKey = null;
        AutoEndNote = null;
        Notice = "The activation goes on: nothing is left the rules allow but End activation.";
        Changed();
        return true;
    }

    // Sends a command the rules offer outright (Activate, End activation), as its hint button does: once, answered by the
    // server.
    public async Task<Feedback?> SendCommandAsync(Command command, string label)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (Pending is not null)
        {
            Notice = WaitingNotice;
            Changed();
            return null;
        }
        Clear();
        return await SendNow(command, label);
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
            _placeDismissed = false;
            Actor = NextToPlace();
            Cursor = ZoneCentre();
        }
        else
        {
            Actor = _state.ActiveModel;
            Cursor = TablePoint.Of(_state.FindModel(Actor!.Value)!.Position!.Value);
            // Stage N1: no target is chosen for the player; a click on an enemy (or N and P) chooses one.
            Target = null;
        }
        Changed();
        return true;
    }

    // Stage U1: Esc and a right-click take an attack picked from the Combat menu back to the menu; all (the card's Cancel
    // button) leaves the choice altogether.
    public void Cancel(bool all = false)
    {
        if (Pending is not null || (Mode == InteractionMode.None && Notice is null))
        {
            return;
        }
        if (Mode == InteractionMode.Place)
        {
            _placeDismissed = true;
        }
        // Stage U1: an attack picked from the Combat menu goes back to the menu; Esc again leaves Combat.
        if (!all && FromCombat && IsAttack(Mode) && Available.Contains(InteractionMode.Combat))
        {
            var actor = Actor;
            Clear();
            Mode = InteractionMode.Combat;
            Actor = actor;
            Changed();
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

    // Stage U1: the Combat menu, before any target: Shoot, Charge and Fight for the acting model. An entry is open when the
    // rules open that action (Previews.ActionOpen) and accept at least one enemy as its target (Previews.Target, the same
    // check as choosing it); else it is greyed out with the rules' reason (for the nearest enemy when none is accepted).
    public IReadOnlyList<CombatEntry> CombatEntries()
    {
        if (Mode != InteractionMode.Combat || Pending is not null || _seat is not { } seat || Actor is not { } actor)
        {
            return [];
        }
        if (_combatEntries is not null && _combatVersion == Version)
        {
            return _combatEntries;
        }
        var entries = new List<CombatEntry>();
        var number = 0;
        var enemies = Targets();
        foreach (var mode in new[] { InteractionMode.Shoot, InteractionMode.Charge, InteractionMode.Fight })
        {
            number++;
            if (!Available.Contains(mode))
            {
                continue;
            }
            var key = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (ActionOpen(seat, actor, Kind(mode)) is { } closed)
            {
                entries.Add(new CombatEntry(mode, Words(mode), key, false, $"{closed.Text} ({closed.RuleRef})", 0));
                continue;
            }
            var verdicts = enemies.Select(t => TargetPreview(seat, Kind(mode), actor, t)).ToList();
            var legal = verdicts.Count(v => v.Legal);
            var why = legal > 0 ? null
                : verdicts.Select(v => v.Refusal).FirstOrDefault(r => r is not null) is { } first ? $"No enemy can be the target: {first.Text} ({first.RuleRef})"
                : "No enemy can be the target.";
            entries.Add(new CombatEntry(mode, Words(mode), key, legal > 0, why, legal));
        }
        _combatEntries = entries;
        _combatVersion = Version;
        return entries;
    }

    private IReadOnlyList<CombatEntry>? _combatEntries;
    private long _combatVersion = -1;

    // Stage U1: picks an attack from the Combat menu (a click, or keys 1, 2 and 3). The attack becomes the one being chosen,
    // with no target yet: the player clicks an enemy the rules accept (ringed), then Confirm beside it. A greyed-out entry
    // picked by a click does nothing; by a key it is chosen anyway (so the keyboard keeps "confirm twice to send anyway").
    // Nothing is ever sent from here.
    public CombatPick ChooseCombat(InteractionMode mode, bool byKey = false)
    {
        if (Pending is not null || Mode != InteractionMode.Combat || !IsAttack(mode) || CombatEntries().FirstOrDefault(e => e.Mode == mode) is not { } entry)
        {
            return CombatPick.None;
        }
        if (!entry.Enabled && !byKey)
        {
            return CombatPick.None;
        }
        Mode = mode;
        FromCombat = true;
        Target = null;
        _armed = false;
        Notice = null;
        Changed();
        return CombatPick.Target;
    }

    // Stage K4: the models of the seat shown still to place, in order (for the bar's chips and the UI check).
    public IReadOnlyList<ModelId> ToPlace => [.. Undeployed().Select(m => m.Id)];

    // Stage K4: a chip on the bar: the model to place next. Nothing is sent.
    public void PickToPlace(ModelId id)
    {
        if (Pending is not null || Mode != InteractionMode.Place || Actor == id || !Undeployed().Any(m => m.Id == id))
        {
            return;
        }
        Actor = id;
        _placeNext = id;
        _armed = false;
        Changed();
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
            _placeNext = next;
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
        Chosen = null;
        _state = _server;
        return await SendNow(new ActivateModel(seat, model), $"Activate {model.Value}");
    }

    // A command for a unit that is only chosen: ActivateModel first, and the command only once the server has accepted
    // that activation. A refused (or lost) activation sends nothing more.
    private async Task<Feedback?> SendNow(Command command, string label)
    {
        if (Chosen is { } chosen && _seat is { } seat && command is not ActivateModel)
        {
            var activated = await SendOne(new ActivateModel(seat, chosen), $"Activate {NameOf(chosen)}", keepChoice: true);
            if (activated is not { Kind: FeedbackKind.Accepted })
            {
                return activated;
            }
        }
        return await SendOne(command, label, keepChoice: false);
    }

    // How many commands this battlefield has handed to the server (for the UI check: a second confirmation while one is
    // waiting adds none).
    public int SentCount { get; private set; }

    private async Task<Feedback?> SendOne(Command command, string label, bool keepChoice)
    {
        if (Send is not { } send)
        {
            Notice = "Not connected to a match: nothing was sent.";
            Changed();
            return null;
        }
        SentCount++;
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
            case FeedbackKind.Accepted when keepChoice:
                // The activation is on the server now: the unit is no longer only chosen, and its action follows.
                Chosen = null;
                Notice = $"DONE: {label}.";
                break;
            case FeedbackKind.Accepted:
                Clear();
                Answer = answer;
                Notice = $"DONE: {label}. The server accepted it; the battlefield shows its result.";
                // Stage K4: when the server's events already placed it, the next placement starts now (else with them).
                if (command is PlaceModel placed && _state.FindModel(placed.Model) is { Deployed: true })
                {
                    AutoPlace();
                }
                // Stage K7: after an accepted charge, Combat opens on the charged enemy when the rules let it Fight.
                if (command is ChargeModel charged)
                {
                    _afterCharge = (charged.Model, charged.Target);
                    FightAfterCharge();
                }
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
                if (answer.Kind == FeedbackKind.RulesRefused && ActorOf(command) is { } refusedFor)
                {
                    // Proposal G: a short word over the battlefield; the reason and the page are in the panel (Notice).
                    ServerNote = new UnitNote(refusedFor, "REFUSED", NoteTone.Failure);
                    ServerNotes++;
                }
                break;
        }
        Changed();
        return answer;
    }

    // The note to show above the model when the server refused its command (the full reason is in Notice), and how many
    // such notes there have been, so the battlefield shows each once.
    public UnitNote? ServerNote { get; private set; }

    public int ServerNotes { get; private set; }

    private static ModelId? ActorOf(Command command) => command switch
    {
        PlaceModel p => p.Model,
        MoveModel m => m.Model,
        DashModel d => d.Model,
        RetreatModel r => r.Model,
        Shoot s => s.Attacker,
        ChargeModel c => c.Model,
        FightModel f => f.Model,
        ActivateModel a => a.Model,
        _ => null,
    };

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
        if (Chosen is { } chosen)
        {
            lines.Add($"{NameOf(chosen)} is chosen but not activated yet: nothing has been sent. Click another of your units to switch; its first action activates it.");
        }
        if (Mode == InteractionMode.None)
        {
            var available = Available;
            var offered = Offered;
            lines.Add(available.Count == 0
                ? "No action to preview right now."
                : offered.Count == 0
                    ? "No action is open to this model now; end its activation."
                    : $"Choose an action on the battlefield's bar: {string.Join(", ", offered.Select(Words))} (keys {string.Join(", ", offered.Select(Key))} also work).");
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
        InteractionMode.Combat => "Combat",
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
        // Stage K6: C opens Combat; in Combat, 2 is Charge.
        InteractionMode.Charge => "2",
        InteractionMode.Fight => "X",
        InteractionMode.Combat => "C",
        _ => "",
    };

    private string Help() => Mode switch
    {
        InteractionMode.Place => "Click a spot in your zone to place the model. Right-click or Esc cancels. Keys: arrows move the cursor (2\" with Shift), N and P choose the model, Space sends.",
        _ when IsPath => "Click a spot in the shaded area to go there (Shift+click adds a waypoint for a path around something). Right-click or Esc cancels. Keys: arrows move the cursor (2\" with Shift), Enter fixes a point, Backspace removes one, Space sends."
            + (CanClimbSides ? (ClimbSides ? " K: back to ladders (now climbing a tower's side, a Risky roll)." : " K: climb a tower's side instead of a ladder (a Risky roll).") : ""),
        InteractionMode.Combat => "Choose Shoot, Charge or Fight; then click an enemy the rules accept (ringed) and Confirm beside it (or Space). An attack the rules refuse is greyed out with the reason. Right-click or Esc cancels. Keys: 1 Shoot, 2 Charge, 3 Fight.",
        InteractionMode.Charge => "Click Confirm charge beside the target. Right-click or Esc cancels. Keys: N and P choose, Enter or Space sends.",
        _ => "Click a target (valid ones are marked), then Confirm beside it. Right-click or Esc cancels. Keys: N and P choose, Enter or Space sends.",
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
                // Towers proposal, stage T4: a point on another level is reached by a ladder (Previews.Route).
                return points.Count == 0 ? null : Previews.Path(_state, seat, Kind(Mode), actor, Previews.Route(_state, actor, [.. points.Select(p => Previews.PointAt(p.X, p.Y))], Sides));
            case InteractionMode.Shoot or InteractionMode.Charge or InteractionMode.Fight:
                return Target is { } target ? TargetPreview(seat, Kind(Mode), actor, target) : null;
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
        FromCombat = false;
        Actor = null;
        Target = null;
        _armed = false;
        Notice = null;
        Answer = null;
        ClimbSides = false;
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
