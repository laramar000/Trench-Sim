using Client.Core;
using Godot;
using Rules.Core.State;

// Audit item T4: what the battlefield's animation shows the automated UI checks (client/scripts/UiCheck.cs) and nothing
// else reads: counters of what was drawn or started, the last effect of each kind, playing a made-up update
// (PlayForCheck), and read-outs of a model's tilt, facing, fade, casualty figure and drawn place. Moved here unchanged
// from BoardView.Animation.cs so the shipped animation code reads without them; the counters are still counted up there.
// Nothing here changes what is drawn or decides anything.
public partial class BoardView
{
    // The animation showcase (dev only, never in play): the status a staged fall leaves a model drawn with, as the server
    // would after the event the scene stages (a model taken Down stays lying instead of standing up again, as the server
    // still has it standing). Empty in play, so a fall ends exactly as before.
    private readonly Dictionary<ModelId, ModelStatus> _showcaseHolds = [];

    public void HoldAfterFall(ModelId id, ModelStatus? status)
    {
        if (status is { } held)
        {
            _showcaseHolds[id] = held;
        }
        else
        {
            _showcaseHolds.Remove(id);
            ShowStatusForPicture(id, null);
        }
    }

    // A fall has ended: drawn as the server has the model (or, in the showcase, as the scene left it).
    private void EndFallHold(ModelId id)
    {
        if (_showcaseHolds.TryGetValue(id, out var held))
        {
            _held[id] = held;
        }
        else
        {
            _held.Remove(id);
        }
    }

    // For the UI check (final animation review): how often a log behind what was already shown was waited out.
    public int LogsBehind { get; private set; }

    // For the UI check (audit item R1): how often a new match was started, the models whose last drawn place or shape is
    // remembered, one remembered shape, and how many facings are kept.
    public int NewMatches { get; private set; }

    public IReadOnlyCollection<ModelId> RememberedModels => _lastDrawn.Models;

    public ModelShape? RememberedShape(ModelId id) => _lastDrawn.TryGet(id, out var shape) ? shape : null;

    public int FacingCount => _facing.Count;

    // For the dev pictures only (never in play): draws a model as if it had this status (null: as the server has it), or
    // hides its nodes, so an animation can be compared with the drawing it ends on.
    public void ShowStatusForPicture(ModelId id, ModelStatus? status)
    {
        if (status is { } shown)
        {
            _held[id] = shown;
        }
        else
        {
            _held.Remove(id);
        }
        Rebuild();
    }

    public void HideForPicture(ModelId id, bool hidden)
    {
        if (_modelNodes.TryGetValue(id, out var nodes))
        {
            foreach (var (node, _) in nodes)
            {
                if (IsInstanceValid(node))
                {
                    node.Visible = !hidden;
                }
            }
        }
    }

    // For the UI check only (never in play): plays a made-up update; its beats (event number 0) are not counted as the
    // match's.
    public void PlayForCheck(Choreography plan) => Play(plan);

    // The instance ids of the nodes a model is drawn with now (for the UI check: a redraw gives new ones).
    public IReadOnlyList<ulong> NodeIdsOf(ModelId id) => _modelNodes.TryGetValue(id, out var nodes) ? [.. nodes.Select(n => n.Node.GetInstanceId())] : [];

    // Who finished playback early while something was still playing (for the UI check; at most 50 kept).
    public List<string> FinishedEarly { get; } = [];

    // Stage B1 (UI check hotseat-switch-keeps-playback): each change of the seat shown in a match, whether Main switched
    // because the seat follows the turn, whether something was playing just before and just after it, and what then
    // happened to that playback until the battlefield was still again: beats drawn on their time or shown at once, the
    // kinds of beats (queued and posing), and who cut it short, if anyone (FinishAnimations' caller). Display only.
    public sealed class SeatSwitch(Seat? from, Seat? to, bool followed, bool playingBefore)
    {
        public Seat? From { get; } = from;
        public Seat? To { get; } = to;
        public bool Followed { get; } = followed;
        public bool PlayingBefore { get; } = playingBefore;
        public bool PlayingAfter { get; set; }
        public int Seq { get; set; }
        public int Drawn { get; set; }
        public int AtOnce { get; set; }
        public HashSet<BeatKind> Kinds { get; } = [];
        public string? CutBy { get; set; }
        public bool Done { get; set; }
    }

    public List<SeatSwitch> SeatSwitches { get; } = [];

    // Every event with effects this window took in (its number, how many effects, caught up or not), and the effects it
    // played (for the UI check: all of the live ones, none of the caught-up ones).
    public List<(int Seq, int Count, bool Replayed)> EffectLines { get; } = [];

    public List<(int Seq, EffectCue Cue)> EffectsPlayed { get; } = [];

    // Animation stage A3: every event with status changes this window took in (its number, how many, caught up or not).
    public List<(int Seq, int Count, bool Replayed)> StatusLines { get; } = [];

    // Animation stage A4: every event with explosions or marker bursts this window took in.
    public List<(int Seq, int Blasts, int Markers, bool Replayed)> FxLines { get; } = [];

    public int EffectsShowing => _effectsShowing;

    // For the dev pictures only: show an effect without an event (never used in play).
    public void ShowEffectForPicture(EffectCue cue) => PlayEffect(cue);

    // For the UI check (audit item T1): how long each flash, puff, strike or impact dust was drawn for (seconds), in the
    // order drawn; at most 200 kept.
    public List<float> EffectLengths { get; } = [];

    // How many tracer beats came due (drawn, or shown at once by FinishAnimations), how many tracers were drawn, and the
    // last one's start and end (for the UI check).
    public int TracerBeats { get; private set; }

    public int TracersDrawn { get; private set; }

    public (Vector3 From, Vector3 To, bool Hit)? LastTracer { get; private set; }

    // How many effects a skip hurried, how many grenades, explosions and marker bursts were drawn, and the last
    // grenade's and explosion's points (for the UI check); how many explosion and marker beats of the match came due.
    public int EffectsStopped { get; private set; }

    public int LobsDrawn { get; private set; }

    public (Vector3 From, Vector3 To)? LastLob { get; private set; }

    public int ExplosionsDrawn { get; private set; }

    public (Vector3 At, float Radius, BeatKind Kind)? LastExplosion { get; private set; }

    public int MarkersDrawn { get; private set; }

    public int BlastBeats { get; private set; }

    public int MarkerBeats { get; private set; }

    public int RingsDrawn { get; private set; }

    public (Vector3 At, float Radius)? LastRing { get; private set; }

    // How many moves have been shown travelling, and the last one (for the UI check).
    public int GlidesStarted { get; private set; }

    public (ModelId Model, Vector2 From, Vector2 To, double Seconds)? LastGlide { get; private set; }

    // How many flinches were shown (for the UI check).
    public int FlinchesStarted { get; private set; }

    // The casualty figures in the battlefield now (for the UI check: none once they have gone).
    public int CasualtyNodes => _effects.GetChildren().Count(c => c.Name.ToString().StartsWith("Casualty", StringComparison.Ordinal) && !c.IsQueuedForDeletion());

    // Animation stage A3: how many falls, rises and casualties of the match were started (made-up ones for the UI check
    // are not counted), how many Out of Action figures are still
    // showing as they go, how many models are still drawn standing until their fall, and how many casualty figures were
    // freed (for the UI check).
    public int FallsStarted { get; private set; }

    public int RisesStarted { get; private set; }

    public int CasualtiesStarted { get; private set; }

    public int GhostsShowing => _ghosts.Count;

    public int GhostsFreed { get; private set; }

    // Animation stage A5: how far the first casualty figure has faded (its PS1 shader's fade and its paints' alpha, 1
    // whole), and the fade a model's own figure is drawn with (1 whole; it must never change); for the UI check.
    public (float Fade, float Alpha)? CasualtyLook => _ghosts.Count == 0 ? null
        : (_ghosts[0].Shaders.Count == 0 ? 1f : _ghosts[0].Shaders.Min(m => m.GetShaderParameter("fade").VariantType == Variant.Type.Nil ? 1f : (float)m.GetShaderParameter("fade")),
           _ghosts[0].Paints.Count == 0 ? 1f : _ghosts[0].Paints.Min(m => m.AlbedoColor.A));

    public float? FigureFade(ModelId id)
    {
        if (!_figures.TryGetValue(id, out var figure) || !IsInstanceValid(figure))
        {
            return null;
        }
        var fade = 1f;
        foreach (var mesh in figure.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
        {
            for (var i = 0; mesh.Mesh is { } m && i < m.GetSurfaceCount(); i++)
            {
                if ((mesh.GetSurfaceOverrideMaterial(i) ?? m.SurfaceGetMaterial(i)) is ShaderMaterial psx && psx.GetShaderParameter("fade").VariantType != Variant.Type.Nil)
                {
                    fade = Math.Min(fade, (float)psx.GetShaderParameter("fade"));
                }
            }
        }
        return fade;
    }

    public int HeldStanding => _held.Count;

    // Audit item B2 (UI check): where a model's casualty figure stands now (its root, on the table) and how far its figure
    // leans from upright (radians); null when it has none.
    public (Vector3 At, float Tilt)? CasualtyOf(ModelId id) =>
        _ghosts.LastOrDefault(g => g.Model == id && IsInstanceValid(g.Root)) is { } ghost
            ? (ghost.Root.Position, ghost.Holder.Basis.Y.Normalized().AngleTo(Vector3.Up))
            : null;

    // How far a model's figure leans from upright now (radians; 0 standing, about pi/2 lying in a fall), and its scale;
    // null when the model has no figure (for the UI check).
    public (float Tilt, float Scale)? TiltOf(ModelId id) =>
        _figures.TryGetValue(id, out var figure) && IsInstanceValid(figure)
            ? (figure.Basis.Y.Normalized().AngleTo(Vector3.Up), figure.Basis.Y.Length())
            : null;

    // How many turns, lunges and leans were started, and how many strikes and charge impacts of the match came due (for
    // the UI check).
    public int TurnsStarted { get; private set; }

    public int LungesStarted { get; private set; }

    public int LeansStarted { get; private set; }

    public int StrikeBeats { get; private set; }

    public int ImpactBeats { get; private set; }

    // The facing a model's figure is drawn with now (its yaw, radians) and the facing the last turn left it, and its lean
    // now (for the UI check); null when the model has no figure.
    public (float Yaw, float Wanted, float Lean)? FacingOf(ModelId id) =>
        _figures.TryGetValue(id, out var figure) && IsInstanceValid(figure) ? (figure.Rotation.Y, FacingNow(id), figure.Rotation.X) : null;

    // Where a model is drawn now (its base centre, inches); the server's position once it has stopped.
    public Vector2? DrawnAt(ModelId id) => _modelNodes.TryGetValue(id, out var nodes) && nodes.Count > 0
        ? new Vector2(nodes[0].Node.Position.X, nodes[0].Node.Position.Z)
        : null;

    // For the UI check (audit item B2): casualty figures that travelled their model's move first.
    public int CasualtiesRiding { get; private set; }
}
