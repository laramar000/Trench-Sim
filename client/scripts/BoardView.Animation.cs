using Client.Core;
using Godot;
using Rules.Core.Geometry;
using Rules.Core.State;

// Audit item T2: the battlefield's animation, moved out of BoardView.cs unchanged so each file reads on its own. Here: the
// playback of each update (stage A1: queue, lines and beats on their time, finishing at once), the event effects (graphics
// stage G5: flashes, puffs, tracers) and battlefield effects (stage A4: grenades, explosions, scatter, markers, blast
// rings), and the watchable moves with every pose track (glides, knocks, turns, leans, falls, rises, casualty figures,
// ApplyPoses and SetPoses). BoardView.cs keeps drawing the battlefield, input, notes and see-through walls.
public partial class BoardView
{
    // ---- animation (stage A1) ----

    // How fast the battlefield plays what the server resolved (the Menu's Animations choice).
    public AnimationSpeed Animation { get; set; } = AnimationSpeed.Full;

    // The playback clock: wall time, unless the showcase runs in slow motion (dev only, SlowMotion; 1 in play).
    private static double Now => _slowBase + (Time.GetTicksMsec() / 1000.0 - _slowFrom) * _slow;

    // What is still to be shown (PlaybackQueue: lines and one-off beats in time order); glides and flinches move models
    // instead (_glides, _knocks, _turns, _leans). The lists are kept and used again, so a frame allocates nothing.
    private readonly PlaybackQueue _queue = new();
    private readonly List<PlaybackQueue.Item> _taken = [];
    private readonly List<Beat> _moves = [];
    // Animation stage A3: each model as it was last drawn (an Out of Action model is drawn again from this as it goes).
    private readonly LastDrawn _lastDrawn = new();

    // Audit item R1: a new match (Main.Attach: hot-seat, host, join, rejoin, rematch) starts from nothing the last one
    // left: whatever was still playing is shown at once, then the last drawn places and shapes, the facings, the table
    // drawn (so the first update is drawn as a new match: its facings start afresh and the camera fits its table), the
    // events already seen (so the match's events so far are caught up at once, as for the first match), the selection and
    // the seat shown are forgotten. Nothing here is the match's state; the next Show draws the new match from the server's.
    public void NewMatch()
    {
        FinishAnimations();
        ClearBodies();
        _bodyPose.Clear();
        _fallPush.Clear();
        _downFalls.Clear();
        _fallLand.Clear();
        _rigs.Clear();
        _fallObstacles.Clear();
        _recentImpact.Clear();
        _lastDrawn.Clear();
        _facing.Clear();
        _scene = null;
        _lastSeq = -1;
        _selected = null;
        _lastActive = null;
        _seatSeen = null;
        NewMatches++;
    }

    // Plays one update after what is still playing; an update arriving with more than Choreography.MaxSeconds still to
    // play first shows all of that at once, so the battlefield is never long behind the server.
    private void Play(Choreography plan)
    {
        var now = Now;
        if (_queue.Behind(now) > Choreography.MaxSeconds)
        {
            FinishAnimations();
        }
        _moves.Clear();
        var begins = _queue.Add(plan, now, _moves);
        // Ragdoll (2026-11-24): each model's last hit in this update, when it lands in this playback and how hard, so a
        // figure put Out of Action goes limp on the hit itself, thrown as hard as it was hit.
        foreach (var (id, hit) in RagdollImpacts.Of(plan, id => _lastDrawn.Find(_scene, id) is { } s ? new TablePoint(s.X, s.Y) : null))
        {
            _recentImpact[id] = (begins + hit.At, hit);
        }
        foreach (var move in _moves)
        {
            _openSwitch?.Kinds.Add(move.Kind);
            switch (move.Kind)
            {
                case BeatKind.Glide:
                    StartGlide(move, move.Start);
                    break;
                case BeatKind.Turn:
                    StartTurn(move, move.Start);
                    break;
                case BeatKind.Lean:
                    StartLean(move, move.Start);
                    break;
                case BeatKind.Fall or BeatKind.Rise:
                    StartFall(move, move.Start);
                    break;
                case BeatKind.Casualty:
                    StartCasualty(move, move.Start);
                    break;
                case BeatKind.Brace or BeatKind.DigIn or BeatKind.Rally:
                    StartGesture(move, move.Start);
                    break;
                case BeatKind.Wound:
                    StartWound(move, move.Start);
                    break;
                default:
                    StartKnock(move, move.Start);
                    break;
            }
        }
        RunDue(now, draw: true);
    }

    // Shows everything due by now, in order. RunTaken with draw false (finishing at once) shows the lines and counts the
    // beats without drawing them.
    private void RunDue(double now, bool draw)
    {
        if (_queue.Count == 0)
        {
            return;
        }
        _taken.Clear();
        _queue.TakeDue(now, _taken);
        RunTaken(draw);
    }

    private void RunTaken(bool draw)
    {
        foreach (var item in _taken)
        {
            if (item.Line is { } line)
            {
                Dice.Feed.Add(line, DiceTray.Now);
                LineShown?.Invoke(line);
                foreach (var note in line.Notes ?? [])
                {
                    AddNote(note);
                }
            }
            else if (item.Beat is { } beat)
            {
                RunBeat(beat, draw);
            }
        }
        _taken.Clear();
    }

    private void RunBeat(Beat beat, bool draw)
    {
        if (_openSwitch is { } open)
        {
            open.Drawn += draw ? 1 : 0;
            open.AtOnce += draw ? 0 : 1;
            open.Kinds.Add(beat.Kind);
        }
        switch (beat.Kind)
        {
            case BeatKind.Strike or BeatKind.Impact:
                if (beat.Seq > 0)
                {
                    StrikeBeats += beat.Kind == BeatKind.Strike ? 1 : 0;
                    ImpactBeats += beat.Kind == BeatKind.Impact ? 1 : 0;
                }
                if (draw)
                {
                    PlayStrike(beat, (float)Choreography.EffectScale(beat, Animation));
                }
                break;
            case BeatKind.Tracer:
                // (A tracer at a point, stage A4, is not a shot at a model.)
                TracerBeats += beat.Seq > 0 && beat.Other is not null ? 1 : 0;
                if (draw)
                {
                    PlayTracer(beat);
                }
                break;
            case BeatKind.Flame:
                FlameBeats += beat.Seq > 0 ? 1 : 0;
                if (draw)
                {
                    PlayFlame(beat);
                }
                break;
            case BeatKind.Lob:
                // Animation stage A4: a grenade thrown at a model plays its throw in place of the shot's flash and tracer.
                if (beat.Seq > 0 && beat.Other is not null)
                {
                    TracerBeats++;
                    EffectsPlayed.Add((beat.Seq, new EffectCue(EffectKind.MuzzleFlash, beat.Model)));
                }
                if (draw)
                {
                    PlayLob(beat);
                }
                break;
            case BeatKind.Explosion or BeatKind.MineBlast or BeatKind.MartyrBlast:
                BlastBeats += beat.Seq > 0 ? 1 : 0;
                if (draw)
                {
                    PlayExplosion(beat);
                }
                break;
            case BeatKind.Scatter or BeatKind.Dust:
                if (draw)
                {
                    PlayScatterOrDust(beat);
                }
                break;
            case BeatKind.Shrapnel or BeatKind.Gas or BeatKind.Fire:
                MarkerBeats += beat.Seq > 0 ? 1 : 0;
                if (draw)
                {
                    PlayMarker(beat);
                }
                break;
            case BeatKind.MuzzleFlash or BeatKind.HitPuff or BeatKind.DirtPuff:
                var cue = new EffectCue(beat.Kind switch { BeatKind.MuzzleFlash => EffectKind.MuzzleFlash, BeatKind.HitPuff => EffectKind.HitPuff, _ => EffectKind.DirtPuff }, beat.Model);
                if (draw)
                {
                    PlayEffect(cue, (float)Choreography.EffectScale(beat, Animation));
                }
                if (beat.Seq > 0)
                {
                    EffectsPlayed.Add((beat.Seq, cue));
                }
                break;
        }
    }

    // Shows at once everything still playing: lines still due are shown, beats are not drawn, every model is put back on
    // the server's position. Sending a command, Esc, a catch-up and a new match all do this, so the player never waits
    // for an animation and never acts on a picture that is behind the server. A change of the seat shown does not.
    public void FinishAnimations([System.Runtime.CompilerServices.CallerMemberName] string caller = "")
    {
        if (Gliding && FinishedEarly.Count < 50)
        {
            FinishedEarly.Add(caller);
        }
        if (Gliding && _openSwitch is { } open)
        {
            open.CutBy ??= caller;
        }
        // Animation stage A4: every effect still showing (puffs, tracers, grenades, explosions) goes at once too.
        StopEffects();
        _taken.Clear();
        _queue.TakeAll(Now, _taken);
        RunTaken(draw: false);
        // Animation stage A3: falls end lying (drawn from the server's status), rises upright, casualties are gone.
        if (_falls.Count > 0 || _ghosts.Count > 0)
        {
            foreach (var ghost in _ghosts)
            {
                EndGhost(ghost);
            }
            _ghosts.Clear();
            foreach (var fall in _falls)
            {
                if (!fall.Rising)
                {
                    EndFallHold(fall.Model);
                }
            }
            var upright = _falls.Where(f => f.Rising || _scene?.Find(f.Model) is { Status: ModelStatus.Standing } && !_showcaseHolds.ContainsKey(f.Model)).Select(f => f.Model).ToList();
            _falls.Clear();
            Rebuild();
            foreach (var id in upright)
            {
                Upright(id);
            }
        }
        if (_glides.Count > 0 || _knocks.Count > 0 || _turns.Count > 0 || _leans.Count > 0 || _gestures.Count > 0 || _wounds.Count > 0)
        {
            _pose.Clear();
            _bob.Clear();
            _turned.Clear();
            foreach (var glide in _glides)
            {
                _pose[glide.Model] = Vector3.Zero;
            }
            foreach (var knock in _knocks)
            {
                _pose[knock.Model] = Vector3.Zero;
            }
            // Turned to their final facing, upright.
            foreach (var turn in _turns)
            {
                _turned[turn.Model] = (0, turn.To);
            }
            foreach (var lean in _leans)
            {
                _turned[lean.Model] = (0, _turned.TryGetValue(lean.Model, out var t) ? t.Yaw : FacingNow(lean.Model));
            }
            // Animation suite: gestures end standing as before.
            foreach (var gesture in _gestures)
            {
                _turned[gesture.Model] = (0, _turned.TryGetValue(gesture.Model, out var t) ? t.Yaw : FacingNow(gesture.Model));
            }
            _gestures.Clear();
            _glides.Clear();
            _stride.Clear();
            ApplyWalk();
            EndWounds();
            _knocks.Clear();
            _turns.Clear();
            _leans.Clear();
            SetPoses();
        }
    }

    // Set by Main just before the redraw that follows a hot-seat switch to the seat that must act (HotSeat.FollowIfNeeded).
    public bool FollowedTurn { get; set; }

    private Seat? _seatSeen;
    private SeatSwitch? _openSwitch;

    // Where a model is drawn from for an effect: the table, or (taken off it) where it was last drawn.
    private bool Where(ModelId id, out Vector3 at, out float radius)
    {
        // Audit item T3: on the table, or (just taken off it) as last drawn.
        if (_lastDrawn.Find(_scene, id) is { } model)
        {
            at = new Vector3((float)model.X, (float)model.Standing, (float)model.Y);
            radius = (float)model.Radius;
            return true;
        }
        at = default;
        radius = 0;
        return false;
    }

    // ---- event effects (graphics stage G5) ----

    private int _effectsShowing;

    private void NoteEffectLength(float seconds)
    {
        if (EffectLengths.Count < 200)
        {
            EffectLengths.Add(seconds);
        }
    }

    // PS1-style: a few flat, unshaded squares that face the camera, grow and fade, then go. No lights, no particles.
    // Audit item T1: scale is Choreography.EffectScale (1 in Full; the beat's shortening in Fast).
    private void PlayEffect(EffectCue cue, float scale = 1)
    {
        NoteEffectLength((cue.Kind switch
        {
            EffectKind.MuzzleFlash => (float)Choreography.FlashSeconds * 2,
            EffectKind.HitPuff => (float)Choreography.HitPuffSeconds,
            _ => (float)Choreography.DirtPuffSeconds,
        }) * scale);
        if (!Where(cue.At, out var at, out var radius))
        {
            return;
        }
        switch (cue.Kind)
        {
            case EffectKind.MuzzleFlash:
                Puff(at + new Vector3(0, 0.85f, 0), Vector3.Zero, 0.5f, 0.4f, new Color(1f, 0.9f, 0.45f), (float)Choreography.FlashSeconds * 2 * scale);
                break;
            case EffectKind.HitPuff:
                // Showcase review (2026-11-23): four see-through spatters flying apart from the hit, so the target stays in
                // sight (four opaque squares grown to 0.7" from one point hid it for most of their time).
                for (var i = 0; i < 4; i++)
                {
                    var away = new Vector3(Mathf.Cos(i * 1.7f), 0.6f, Mathf.Sin(i * 1.7f)) * 0.6f;
                    Puff(at + new Vector3(0, 0.7f, 0) + away * 0.25f, away, 0.18f, 0.2f, new Color(0.72f, 0.2f, 0.15f, HitAlpha), (float)Choreography.HitPuffSeconds * scale);
                }
                break;
            default:
                for (var i = 0; i < 5; i++)
                {
                    var side = new Vector3(Mathf.Cos(i * 1.3f), 0, Mathf.Sin(i * 1.3f)) * (radius + 0.35f);
                    Puff(at + side + new Vector3(0, 0.15f, 0), new Vector3(0, 0.6f, 0), 0.4f, 0.5f, Dust, (float)Choreography.DirtPuffSeconds * scale);
                }
                break;
        }
    }

    // Animation stage A1: effect squares are kept and used again (one unit square shared by all, a paint each), at most
    // MaxEffects showing at once (96 since stage A4, for explosions); one beyond that is not drawn.
    public const int MaxEffects = 96;

    private static readonly QuadMesh UnitQuad = new() { Size = Vector2.One };
    private readonly List<(MeshInstance3D Node, StandardMaterial3D Paint)> _freePuffs = [];

    private void Puff(Vector3 from, Vector3 drift, float size, float grow, Color colour, float seconds)
    {
        if (_effectsShowing >= MaxEffects)
        {
            return;
        }
        (MeshInstance3D Node, StandardMaterial3D Paint) puff;
        if (_freePuffs.Count > 0)
        {
            puff = _freePuffs[^1];
            _freePuffs.RemoveAt(_freePuffs.Count - 1);
        }
        else
        {
            var paint = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
                // Explosion review (2026-11-23): without this a billboard drops the node's scale, so every square was drawn
                // 1" across whatever size it was given (explosions, smoke and hits read as large blobs).
                BillboardKeepScale = true,
            };
            puff = (new MeshInstance3D { Mesh = UnitQuad, MaterialOverride = paint }, paint);
            _effects.AddChild(puff.Node);
        }
        _effectsShowing++;
        puff.Paint.AlbedoColor = colour;
        puff.Node.Position = from;
        puff.Node.Scale = Vector3.One * size;
        puff.Node.Visible = true;
        var tween = puff.Node.CreateTween().SetParallel();
        tween.TweenProperty(puff.Node, "position", from + drift, seconds);
        tween.TweenProperty(puff.Node, "scale", Vector3.One * (size + grow), seconds);
        tween.TweenProperty(puff.Paint, "albedo_color:a", 0f, seconds);
        Run(tween, () =>
        {
            puff.Node.Visible = false;
            _effectsShowing--;
            _freePuffs.Add(puff);
        }, seconds);
    }

    // Animation stage A1: a tracer, a short bright streak from the shooter's muzzle to the target's body (a hit), or past
    // the target into the ground beside it (a miss; the side alternates by event number, nothing random). Display only:
    // whether it hit is the rules' verdict on the roll.
    public const float TracerLength = 1.2f;

    private static readonly BoxMesh TracerMesh = new() { Size = new Vector3(0.05f, 0.05f, TracerLength) };
    private static readonly StandardMaterial3D TracerPaint = new() { AlbedoColor = new Color(1f, 0.86f, 0.5f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
    private readonly List<MeshInstance3D> _freeTracers = [];

    private void PlayTracer(Beat beat)
    {
        if (!Where(beat.Model, out var shooter, out _))
        {
            return;
        }
        var from = shooter + new Vector3(0, 0.85f, 0);
        if (beat.Other is { } target && Where(target, out var aim, out var radius))
        {
            var to = aim + new Vector3(0, 0.7f, 0);
            var flat = new Vector3(to.X - from.X, 0, to.Z - from.Z);
            if (flat.Length() < 0.1f)
            {
                return;
            }
            var along = flat.Normalized();
            if (!beat.Hit)
            {
                var side = new Vector3(-along.Z, 0, along.X) * (beat.Seq % 2 == 0 ? 1 : -1);
                to = aim + side * (radius + 0.6f) + along * 1.5f + new Vector3(0, 0.05f, 0);
            }
            Streak(from, to, beat.Seconds, beat.Hit);
        }
        else if (beat.At is { } point)
        {
            // Stage A4: an attack at a point: the tracer goes to the ground there.
            Streak(from, Ground(point) + new Vector3(0, 0.05f, 0), beat.Seconds, beat.Hit);
        }
    }

    // Flamethrowers (Jason, 2026-11-26): a jet of flame rolls from the weapon to the target (past it into the ground beside
    // it on a miss, as a tracer does): see-through tongues of orange and yellow flame thrown out along the line, each a
    // little further and wider than the last, the far ones reaching the target as the jet lands, fading as they go, with
    // a thin smoke behind. Pooled squares like every effect; display only.
    private void PlayFlame(Beat beat)
    {
        if (!Where(beat.Model, out var shooter, out _) || beat.Other is not { } target || !Where(target, out var aim, out var radius))
        {
            return;
        }
        var from = shooter + new Vector3(0, 0.8f, 0);
        var to = aim + new Vector3(0, 0.6f, 0);
        var flat = new Vector3(to.X - from.X, 0, to.Z - from.Z);
        if (flat.Length() < 0.1f)
        {
            return;
        }
        var along = flat.Normalized();
        if (!beat.Hit)
        {
            var side = new Vector3(-along.Z, 0, along.X) * (beat.Seq % 2 == 0 ? 1 : -1);
            to = aim + side * (radius + 0.4f) + along * 0.6f + new Vector3(0, 0.15f, 0);
        }
        var seconds = (float)beat.Seconds;
        // A steady stream: bursts of flame leave the muzzle one after another through the jet's time, each rolling out the
        // whole way and widening as it goes; the first reaches the target as the jet lands, the last trails off past it.
        const int bursts = 4;
        for (var burst = 0; burst < bursts; burst++)
        {
            var delay = seconds * 0.22f * burst;
            var index = burst;
            void Burst()
            {
                for (var i = 0; i < 4; i++)
                {
                    var share = 0.55f + 0.17f * i;
                    var colour = (i + index) % 3 == 0 ? new Color(1f, 0.9f, 0.45f, FireAlpha) : new Color(1f, 0.5f + 0.1f * (i % 2), 0.12f, FireAlpha);
                    Puff(from, (to - from) * share + new Vector3(0, 0.1f * share, 0), 0.1f, 0.2f + 0.3f * share, colour, seconds * (0.8f + 0.15f * i));
                }
            }
            if (burst == 0)
            {
                Burst();
            }
            else
            {
                Later(delay, Burst);
            }
        }
        // A thin smoke trailing behind the jet.
        Puff(from + (to - from) * 0.4f, new Vector3(0, 0.45f, 0), 0.2f, 0.35f, new Color(0.25f, 0.24f, 0.23f, BlastSmokeAlpha), seconds * 2.5f);
    }

    // The ground (a tower's deck, a trench's floor) at a table point.
    private Vector3 Ground(TablePoint at) => new((float)at.X, (float)LevelAt(at.X, at.Y), (float)at.Y);

    // A short bright streak flying from one point to another (a tracer, a scattered attack skimming the ground).
    private void Streak(Vector3 from, Vector3 to, double seconds, bool hit)
    {
        if (!_effects.IsInsideTree() || _effectsShowing >= MaxEffects || from.DistanceTo(to) < 0.1f)
        {
            return;
        }
        var tracer = _freeTracers.Count > 0 ? _freeTracers[^1] : null;
        if (tracer is null)
        {
            tracer = new MeshInstance3D { Mesh = TracerMesh, MaterialOverride = TracerPaint };
            _effects.AddChild(tracer);
        }
        else
        {
            _freeTracers.RemoveAt(_freeTracers.Count - 1);
        }
        _effectsShowing++;
        TracersDrawn++;
        LastTracer = (from, to, hit);
        tracer.Visible = true;
        tracer.LookAtFromPosition(from, to, Math.Abs((to - from).Normalized().Dot(Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up);
        var tween = tracer.CreateTween();
        tween.TweenProperty(tracer, "position", to, seconds);
        Run(tween, () =>
        {
            tracer.Visible = false;
            _effectsShowing--;
            _freeTracers.Add(tracer);
        }, seconds);
    }

    // ---- animation stage A4: grenades, explosions, scatter, markers ----

    // Every effect still showing, with what puts its node back; StopEffects ends them all at once (a skip).
    private sealed class Running(Tween tween, Action release, double end)
    {
        public Tween Tween { get; } = tween;
        public Action Release { get; } = release;
        public double End { get; } = end;
    }

    // Animation stage A5: a skip hurries every effect still showing to finish within this long (rather than cutting it
    // off mid-frame), so the battlefield settles smoothly and still at once.
    public const double HurrySeconds = 0.2;

    private readonly List<Running> _running = [];

    private void Run(Tween tween, Action release, double seconds)
    {
        var running = new Running(tween, release, Now + seconds);
        _running.Add(running);
        tween.Chain().TweenCallback(Callable.From(() =>
        {
            if (_running.Remove(running))
            {
                release();
            }
        }));
    }

    // Flamethrowers (2026-11-26): an effect drawn a moment later (a burn's later waves of flame); a skip in between drops it
    // rather than drawing it after the battlefield settled.
    private int _stops;

    private void Later(double delay, Action draw)
    {
        if (!_effects.IsInsideTree())
        {
            return;
        }
        var stops = _stops;
        var wait = _effects.CreateTween();
        wait.TweenInterval(delay);
        Run(wait, () =>
        {
            if (stops == _stops)
            {
                draw();
            }
        }, delay);
    }

    private void StopEffects()
    {
        _stops++;
        if (_running.Count == 0)
        {
            return;
        }
        var now = Now;
        foreach (var running in _running)
        {
            if (GodotObject.IsInstanceValid(running.Tween) && running.Tween.IsValid())
            {
                running.Tween.SetSpeedScale((float)Math.Max(1, (running.End - now) / HurrySeconds));
            }
        }
        EffectsStopped += _running.Count;
    }

    private static readonly BoxMesh GrenadeMesh = new() { Size = new Vector3(0.14f, 0.14f, 0.14f) };
    private static readonly StandardMaterial3D GrenadePaint = new() { AlbedoColor = new Color(0.16f, 0.16f, 0.14f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
    private readonly List<MeshInstance3D> _freeGrenades = [];

    // A grenade lobbed in an arc from the thrower's hand to the target's body (beside it on a miss) or to the ground at a
    // point; it lands at the end of the beat, where the server's blast (its next event) goes off.
    private void PlayLob(Beat beat)
    {
        if (!_effects.IsInsideTree() || _effectsShowing >= MaxEffects || !Where(beat.Model, out var thrower, out _))
        {
            return;
        }
        var from = thrower + new Vector3(0, 0.95f, 0);
        Vector3 to;
        if (beat.Other is { } target && Where(target, out var aim, out var radius))
        {
            to = aim + new Vector3(0, 0.5f, 0);
            var flat = new Vector3(to.X - from.X, 0, to.Z - from.Z);
            if (!beat.Hit && flat.Length() > 0.1f)
            {
                var along = flat.Normalized();
                var side = new Vector3(-along.Z, 0, along.X) * (beat.Seq % 2 == 0 ? 1 : -1);
                to = aim + side * (radius + 0.8f) + new Vector3(0, 0.07f, 0);
            }
        }
        else if (beat.At is { } point)
        {
            to = Ground(point) + new Vector3(0, 0.07f, 0);
        }
        else
        {
            return;
        }
        var grenade = _freeGrenades.Count > 0 ? _freeGrenades[^1] : null;
        if (grenade is null)
        {
            grenade = new MeshInstance3D { Mesh = GrenadeMesh, MaterialOverride = GrenadePaint };
            _effects.AddChild(grenade);
        }
        else
        {
            _freeGrenades.RemoveAt(_freeGrenades.Count - 1);
        }
        _effectsShowing++;
        LobsDrawn++;
        LastLob = (from, to);
        var apex = 1f + 0.12f * new Vector2(to.X - from.X, to.Z - from.Z).Length();
        grenade.Position = from;
        grenade.Visible = true;
        var tween = grenade.CreateTween();
        tween.TweenMethod(Callable.From<float>(p =>
        {
            grenade.Position = from.Lerp(to, p) + new Vector3(0, 4 * apex * p * (1 - p), 0);
            grenade.Rotation = new Vector3(p * 9f, p * 5f, 0);
        }), 0f, 1f, beat.Seconds);
        Run(tween, () =>
        {
            grenade.Visible = false;
            _effectsShowing--;
            _freeGrenades.Add(grenade);
        }, beat.Seconds);
    }

    // Animation stage A5: dust, smoke and gas are drawn see-through (at most SmokeAlpha), so they never hide a model.
    private const float SmokeAlpha = 0.55f;

    // Showcase review (2026-11-23): a hit's spatter and a blow's or charge's flash are see-through too.
    public const float HitAlpha = 0.5f;

    // A burning model's flames are drawn see-through too (seen on screen: opaque, they hid the model; 0.6 since the
    // explosion review, 2026-11-23, when squares were first drawn at their true size).
    private const float FireAlpha = 0.6f;

    // Explosion review (2026-11-23): an explosion's squares are thinner than other effects, since several overlap in one
    // blast: the flash, the tongues of fire and the smoke (gas round a model too).
    public const float BlastFlashAlpha = 0.6f;
    public const float BlastFireAlpha = 0.5f;
    public const float BlastSmokeAlpha = 0.4f;
    private static readonly Color Dust = new(0.58f, 0.49f, 0.37f, SmokeAlpha);

    // A blast ring: a thin ring on the ground spreading out to the blast's radius (the server's, in inches) and fading, so
    // the area an explosion covered can be read at a glance; drawn through walls like the selection ring.
    private static readonly TorusMesh UnitRing = new() { InnerRadius = 0.95f, OuterRadius = 1f, Rings = 32, RingSegments = 4 };
    private readonly List<(MeshInstance3D Node, StandardMaterial3D Paint)> _freeRings = [];

    private void BlastRing(Vector3 at, float radius, Color colour, float seconds)
    {
        if (!_effects.IsInsideTree() || _effectsShowing >= MaxEffects || radius <= 0)
        {
            return;
        }
        (MeshInstance3D Node, StandardMaterial3D Paint) ring;
        if (_freeRings.Count > 0)
        {
            ring = _freeRings[^1];
            _freeRings.RemoveAt(_freeRings.Count - 1);
        }
        else
        {
            var paint = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                NoDepthTest = true,
            };
            ring = (new MeshInstance3D { Mesh = UnitRing, MaterialOverride = paint }, paint);
            _effects.AddChild(ring.Node);
        }
        _effectsShowing++;
        RingsDrawn++;
        LastRing = (at, radius);
        ring.Paint.AlbedoColor = colour with { A = 0.85f };
        ring.Node.Position = at + new Vector3(0, 0.06f, 0);
        ring.Node.Scale = new Vector3(radius * 0.3f, 1, radius * 0.3f);
        ring.Node.Visible = true;
        var spread = seconds * 0.3f;
        var tween = ring.Node.CreateTween();
        tween.TweenProperty(ring.Node, "scale", new Vector3(radius, 1, radius), spread).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(ring.Paint, "albedo_color:a", 0f, seconds - spread);
        Run(tween, () =>
        {
            ring.Node.Visible = false;
            _effectsShowing--;
            _freeRings.Add(ring);
        }, seconds);
    }

    // An explosion at a point (a weapon's BLAST, a Mine, a Martyrdom Device): a pale flash, a fireball (for a mine, a
    // column of earth), smoke rolling out to about the blast's radius and a few clods of debris. Flat PS1-style squares
    // like every effect, about 16 of them; display only (the models it hit are the server's).
    private void PlayExplosion(Beat beat)
    {
        if (beat.At is not { } point)
        {
            return;
        }
        var at = Ground(point);
        var r = (float)Math.Clamp(beat.Size, 0.75, 6);
        ExplosionsDrawn++;
        LastExplosion = (at, (float)beat.Size, beat.Kind);
        var seconds = (float)beat.Seconds;
        var mine = beat.Kind == BeatKind.MineBlast;
        var martyr = beat.Kind == BeatKind.MartyrBlast;
        var smoke = new Color(0.4f, 0.37f, 0.33f, BlastSmokeAlpha);
        var earth = new Color(0.42f, 0.33f, 0.22f, BlastSmokeAlpha);
        // Explosion review (2026-11-23, seen in the showcase: the flash, fireball and smoke were large, near-solid squares
        // that hid every model in the blast for about 0.3 s): a small pale flash, then many small see-through tongues of
        // fire and bright sparks thrown up and outwards, so the impact reads as a burst rather than a blob; the smoke rolls
        // outward to the ring in smaller, thinner puffs so the centre clears. Every length is as before.
        Puff(at + new Vector3(0, 0.5f, 0), Vector3.Zero, Mathf.Min(r * (martyr ? 0.45f : 0.4f), 1f), 0.35f, new Color(1f, 0.93f, 0.72f, BlastFlashAlpha), seconds * 0.12f);
        var fire = mine ? 0 : martyr ? 8 : 6;
        for (var i = 0; i < fire; i++)
        {
            var angle = i * Mathf.Tau / fire + 0.2f;
            var dir = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            var hot = i % 2 == 0;
            var colour = martyr ? (hot ? new Color(0.95f, 0.35f, 0.1f, BlastFireAlpha) : new Color(1f, 0.55f, 0.2f, BlastFireAlpha))
                : hot ? new Color(1f, 0.55f, 0.15f, BlastFireAlpha) : new Color(1f, 0.78f, 0.3f, BlastFireAlpha);
            Puff(at + new Vector3(0, 0.3f, 0) + dir * r * 0.2f, dir * r * 0.35f + new Vector3(0, hot ? 1.4f : 1f, 0), 0.26f * Mathf.Sqrt(r), 0.2f, colour, seconds * 0.45f);
        }
        if (mine)
        {
            for (var i = 0; i < 6; i++)
            {
                var angle = i * Mathf.Tau / 6;
                Puff(at + new Vector3(Mathf.Cos(angle) * 0.2f, 0.2f, Mathf.Sin(angle) * 0.2f), new Vector3(Mathf.Cos(angle) * 0.35f, 1.4f, Mathf.Sin(angle) * 0.35f), 0.45f, 0.35f, earth, seconds * 0.6f);
            }
        }
        // Stage A5: with many effects already showing, an explosion draws less (no debris, half the smoke), so several
        // together stay readable and within MaxEffects; its blast ring always shows.
        var busy = _effectsShowing > MaxEffects / 2;
        BlastRing(at, (float)beat.Size, martyr ? new Color(1f, 0.55f, 0.3f) : mine ? new Color(0.85f, 0.7f, 0.5f) : new Color(1f, 0.9f, 0.7f), seconds);
        // Sparks: tiny and bright, thrown fast and wide, gone in a third of the blast.
        for (var i = 0; i < (mine ? 0 : busy ? 3 : 6); i++)
        {
            var angle = i * 2.4f + 0.5f;
            var dir = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            Puff(at + new Vector3(0, 0.4f, 0), dir * r * 0.9f + new Vector3(0, 1.1f + 0.15f * (i % 3), 0), 0.1f, 0f, new Color(1f, 0.9f, 0.55f, 0.9f), seconds * 0.3f);
        }
        var ring = (martyr ? 8 : 6) / (busy ? 2 : 1);
        for (var i = 0; i < ring; i++)
        {
            var angle = i * Mathf.Tau / ring + 0.3f;
            var dir = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            Puff(at + dir * r * 0.45f + new Vector3(0, 0.2f, 0), dir * r * 0.45f + new Vector3(0, 0.6f, 0), 0.6f, 0.6f, smoke, seconds * 1.1f);
        }
        for (var i = 0; i < (busy ? 0 : 5); i++)
        {
            var angle = i * 1.9f;
            var dir = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            Puff(at + new Vector3(0, 0.3f, 0), dir * r * 0.8f + new Vector3(0, 0.9f, 0), 0.13f, 0f, mine ? earth with { A = 0.85f } : new Color(0.12f, 0.11f, 0.1f, 0.85f), seconds * 0.55f);
        }
    }

    // A scattered attack skims the ground from where it was aimed to where it landed (the explosion follows), or dust
    // where an attack at a point fell short.
    private void PlayScatterOrDust(Beat beat)
    {
        if (beat.At is not { } point)
        {
            return;
        }
        var at = Ground(point);
        if (beat.Kind == BeatKind.Scatter && beat.From is { } aimed)
        {
            var from = Ground(aimed) + new Vector3(0, 0.15f, 0);
            Streak(from, at + new Vector3(0, 0.15f, 0), beat.Seconds, false);
            Puff(from, new Vector3(0, 0.4f, 0), 0.4f, 0.4f, Dust, (float)beat.Seconds * 2);
            return;
        }
        for (var i = 0; i < 5; i++)
        {
            var side = new Vector3(Mathf.Cos(i * 1.3f), 0, Mathf.Sin(i * 1.3f)) * 0.35f;
            Puff(at + side + new Vector3(0, 0.12f, 0), new Vector3(0, 0.5f, 0), 0.35f, 0.4f, Dust, (float)beat.Seconds);
        }
    }

    // The burst of a marker put on a model: SHRAPNEL dark shards flying off it, GAS a slow sickly cloud round it, FIRE
    // flames licking up it.
    private void PlayMarker(Beat beat)
    {
        if (!Where(beat.Model, out var at, out var radius))
        {
            return;
        }
        MarkersDrawn++;
        var seconds = (float)beat.Seconds;
        switch (beat.Kind)
        {
            case BeatKind.Shrapnel:
                for (var i = 0; i < 6; i++)
                {
                    var dir = new Vector3(Mathf.Cos(i * 1.05f), 0.3f, Mathf.Sin(i * 1.05f));
                    Puff(at + new Vector3(0, 0.6f, 0), dir * 0.7f, 0.12f, 0f, new Color(0.3f, 0.3f, 0.32f), seconds);
                }
                Puff(at + new Vector3(0, 0.6f, 0), Vector3.Zero, 0.3f, 0.15f, new Color(1f, 0.9f, 0.6f, HitAlpha), seconds * 0.4f);
                break;
            case BeatKind.Gas:
                // Explosion review (2026-11-23): smaller, thinner wisps round the base that drift outward, so the model
                // stands clear in the middle of its gas.
                for (var i = 0; i < 4; i++)
                {
                    var side = new Vector3(Mathf.Cos(i * 1.57f + 0.4f), 0, Mathf.Sin(i * 1.57f + 0.4f)) * (radius + 0.2f);
                    Puff(at + side + new Vector3(0, 0.3f, 0), new Vector3(0, 0.35f, 0) + side * 0.5f, 0.5f, 0.6f, new Color(0.55f, 0.72f, 0.28f, BlastSmokeAlpha), seconds);
                }
                break;
            default:
                // Stage A5 (seen on screen: opaque flames over the body hid the model): small see-through flames round
                // the base's edge, licking up past the figure. Flamethrowers (2026-11-26): it burns: flames catch round
                // its feet and climb its body in waves over the burn's length, then a thin smoke rises off it.
                var tall = _lastDrawn.Find(_scene, beat.Model) is { } burning ? StandingHeight(burning) : StandingTall;
                for (var wave = 0; wave < 3; wave++)
                {
                    for (var i = 0; i < 5; i++)
                    {
                        var turn = i * 1.26f + wave * 0.6f;
                        var side = new Vector3(Mathf.Cos(turn), 0, Mathf.Sin(turn)) * radius * (0.85f - 0.2f * wave);
                        var life = seconds * (0.35f + 0.05f * i);
                        var colour = (i + wave) % 2 == 0 ? new Color(1f, 0.6f, 0.15f, FireAlpha) : new Color(1f, 0.85f, 0.3f, FireAlpha);
                        // Each wave a little later and higher up the body (a wave's flames start where the last one's rose).
                        var start = at + side + new Vector3(0, 0.1f + tall * 0.25f * wave + 0.06f * i, 0);
                        if (wave == 0)
                        {
                            Puff(start, new Vector3(0, tall * 0.55f, 0), 0.26f, 0.12f, colour, life);
                        }
                        else
                        {
                            var delay = seconds * 0.22f * wave;
                            Later(delay, () => Puff(start, new Vector3(0, tall * 0.5f, 0), 0.24f, 0.1f, colour, life));
                        }
                    }
                }
                Later(seconds * 0.55f, () => Puff(at + new Vector3(0, tall * 0.9f, 0), new Vector3(0.1f, tall * 0.7f, 0), 0.25f, 0.45f, new Color(0.22f, 0.21f, 0.2f, BlastSmokeAlpha), seconds * 0.8f));
                break;
        }
    }

    // Animation stage A2: where a blow lands (Strike: a brief pale flash with two sparks, between the bodies) or a charge
    // strikes home (Impact: dust kicked up round the point of contact, and a smaller flash). The point is on the target's
    // base edge towards the attacker. Pooled squares like every effect; display only.
    private void PlayStrike(Beat beat, float scale = 1)
    {
        if (beat.Other is not { } attacker || !Where(beat.Model, out var at, out var radius) || !Where(attacker, out var from, out _))
        {
            return;
        }
        var toward = new Vector3(from.X - at.X, 0, from.Z - at.Z);
        toward = toward.Length() < 0.01f ? Vector3.Forward : toward.Normalized();
        var contact = at + toward * radius;
        var side = new Vector3(-toward.Z, 0, toward.X);
        if (beat.Kind == BeatKind.Strike)
        {
            var seconds = (float)Choreography.StrikeSeconds * scale;
            NoteEffectLength(seconds);
            // Showcase review: the flash small and see-through (an opaque square grown to 0.65" hid both models).
            Puff(contact + new Vector3(0, 0.8f, 0), Vector3.Zero, 0.2f, 0.15f, new Color(1f, 0.95f, 0.82f, HitAlpha), seconds);
            Puff(contact + new Vector3(0, 0.85f, 0), side * 0.5f + new Vector3(0, 0.25f, 0), 0.12f, 0f, new Color(1f, 0.85f, 0.5f), seconds * 2);
            Puff(contact + new Vector3(0, 0.75f, 0), -side * 0.5f + new Vector3(0, 0.3f, 0), 0.12f, 0f, new Color(1f, 0.85f, 0.5f), seconds * 2);
            return;
        }
        var dust = (float)Choreography.ImpactSeconds * scale;
        NoteEffectLength(dust);
        for (var i = 0; i < 6; i++)
        {
            var angle = i * Mathf.Tau / 6;
            var out_ = (side * Mathf.Cos(angle) + toward * Mathf.Sin(angle) * 0.5f) * 0.7f;
            Puff(contact + new Vector3(0, 0.12f, 0), out_ + new Vector3(0, 0.35f, 0), 0.35f, 0.45f, Dust, dust);
        }
        Puff(contact + new Vector3(0, 0.7f, 0), Vector3.Zero, 0.2f, 0.1f, new Color(1f, 0.95f, 0.82f, HitAlpha), (float)Choreography.StrikeSeconds * scale);
    }

    // ---- watchable moves (XCOM-style UX, improvement 3) ----

    // A model the server moved travels its path from where it was drawn, briefly, instead of jumping. Only the drawing
    // moves: the scene, the selection, the previews and every check use the server's position throughout. When the
    // path ends off screen the camera turns to it (keeping the zoom).
    // Animation stage A1: a glide starts at an absolute time (it waits its turn in the update), and its points are measured
    // from where the server's state has the model (Rest), so two glides of one model in one update (a charge that carries
    // on) follow each other.
    private sealed record Glide(ModelId Model, Vector2[] Points, float[] Lengths, float Total, double Start, double Seconds, float[] Heights, Vector2 Rest, float RestHeight);

    // A knock: a short push of a model along Dir and back to rest, never off the server's position: a hit model's flinch
    // away from its attacker (peaking half way), or since stage A2 an attacker's lunge at its target (peaking at the blow).
    private sealed record Knock(ModelId Model, double Start, double Seconds, Vector3 Dir, float Peak);

    // Animation stage A2: a turn of a model's figure from one facing to another, and a charger's lean (a tilt forward of
    // its figure). Facing is the figure's yaw about the vertical; only the figure turns, never its base or label.
    private sealed record FigureTurn(ModelId Model, double Start, double Seconds, float From, float To);

    private sealed record Lean(ModelId Model, double Start, double Seconds);

    public const float FlinchInches = 0.35f;
    public const float LungeInches = 0.4f;
    public const float RecoilInches = 0.12f;
    public const float ReachInches = 0.25f;

    // Animation suite (2026-11-25): an ACTION played on a model's figure (not its base or label): bracing (taking aim or
    // working a RELOAD weapon) crouches a little and leans in; digging in (Fortify) crouches lower with spade strokes; a
    // rally straightens it up and lifts it. Each eases in and back out to exactly how it stood.
    private sealed record Gesture(ModelId Model, double Start, double Seconds, BeatKind Kind);

    private readonly List<Gesture> _gestures = [];

    private void StartGesture(Beat beat, double start)
    {
        _gestures.Add(new Gesture(beat.Model, start, beat.Seconds, beat.Kind));
        GesturesStarted++;
    }

    // Each gesture's crouch (a share of the figure's height, down) and lean (degrees, forward) at t (0 to 1 of its time).
    public static (float Crouch, float Lean) GestureAt(BeatKind kind, float t)
    {
        if (t is <= 0 or >= 1)
        {
            return (0, 0);
        }
        var k = Mathf.Sin(Mathf.Pi * t);
        k *= k;
        return kind switch
        {
            BeatKind.DigIn => (0.12f * k, (16 + 10 * Mathf.Sin(Mathf.Tau * 2 * t)) * k),
            BeatKind.Rally => (-0.05f * k, -8 * k),
            _ => (0.07f * k, 9 * k),
        };
    }

    private void ApplyGestures(double now)
    {
        for (var i = 0; i < _gestures.Count; i++)
        {
            var gesture = _gestures[i];
            var t = (float)((now - gesture.Start) / gesture.Seconds);
            if (t >= 1)
            {
                _gestures.RemoveAt(i--);
                _turned.TryAdd(gesture.Model, (0, FacingNow(gesture.Model)));
                _pose.TryAdd(gesture.Model, Vector3.Zero);
                continue;
            }
            // Drawn from its rest each frame (the crouch is added to that).
            _pose.TryAdd(gesture.Model, Vector3.Zero);
            var (crouch, lean) = GestureAt(gesture.Kind, t);
            var tall = _lastDrawn.Find(_scene, gesture.Model) is { } shape ? StandingHeight(shape) : StandingTall;
            _bob[gesture.Model] = _bob.GetValueOrDefault(gesture.Model) - crouch * tall;
            var yaw = _turned.TryGetValue(gesture.Model, out var turned) ? turned.Yaw : FacingNow(gesture.Model);
            _turned[gesture.Model] = ((turned.Pitch) + Mathf.DegToRad(lean), yaw);
        }
    }
    public const float LeanDegrees = 14f;

    // Each model's facing as the last turn left it (display only, never the rules'): a figure drawn again (selected,
    // hovered, a new state) is drawn facing this way. Without one, a figure faces the middle of the battlefield.
    private readonly Dictionary<ModelId, float> _facing = [];

    // Each model's figure holder (the node that turns and leans), when it is drawn with art.
    private readonly Dictionary<ModelId, Node3D> _figures = [];
    private readonly List<FigureTurn> _turns = [];
    private readonly List<Lean> _leans = [];
    // Each turning or leaning model's lean and facing this frame (used again every frame).
    private readonly Dictionary<ModelId, (float Pitch, float Yaw)> _turned = [];

    private readonly List<Glide> _glides = [];
    private readonly List<Knock> _knocks = [];
    private readonly Dictionary<ModelId, List<(Node3D Node, Vector3 At)>> _modelNodes = [];
    // Each model's offset from its rest this frame (used again every frame: no allocation while models move).
    private readonly Dictionary<ModelId, Vector3> _pose = [];

    // Movement review (2026-11-23): a gliding figure (not its base, rings or label) rises a little with each step, so it
    // walks rather than slides; StepBob high at most, a step every StepStride along its way, fading in and out with the
    // glide so it starts and ends exactly at rest. Display only.
    private readonly Dictionary<ModelId, float> _bob = [];
    private const float StepBob = 0.06f;
    private const float StepStride = 0.75f;

    // Walk cycle (2026-11-25): one step is this share of the figure's height (a stride of two steps a cycle).
    public const float StepLength = 0.55f;

    // At most this many steps a second (2026-11-26: moves glide at up to 24" a second; a step per StepLength would be a
    // blur of legs there).
    public const float MaxStepsPerSecond = 7f;

    // Movement review (2026-11-23): how far a fall has tipped at t (0 to 1 of its time): accelerating down, lying by 80%,
    // then a small rebound and settle, so it lands with weight instead of stopping dead. A rise eases in and out (it used to
    // leave the ground at full speed). Both take the same time as before.
    private static float FallCurve(float t) =>
        t < 0.8f ? t / 0.8f * (t / 0.8f) : 1 - 0.07f * Mathf.Sin(Mathf.Pi * (t - 0.8f) / 0.2f);

    private static float RiseCurve(float t) => 1 - t * t * (3 - 2 * t);
    private readonly HashSet<ModelId> _posed = [];

    public const float GlideInchesPerSecond = (float)Choreography.GlideInchesPerSecond;
    public const double GlideMinSeconds = Choreography.GlideMinSeconds;
    public const double GlideMaxSeconds = Choreography.GlideMaxSeconds;

    // Whether the battlefield is still playing what the server resolved: a model travelling or flinching, or a line or
    // effect still to show (the UI check waits for this before it measures the drawing; the auto-end waits for it).
    public bool Gliding => _glides.Count > 0 || _knocks.Count > 0 || _turns.Count > 0 || _leans.Count > 0 || _falls.Count > 0 || _ghosts.Count > 0 || _gestures.Count > 0 || _wounds.Count > 0 || _queue.Count > 0;

    // The point of a model the see-through walls keep visible (Godot units: x, height, table y; for the UI check).
    public Vector3? BodyAt(ModelId id) => _modelNodes.TryGetValue(id, out var nodes) && nodes.Count > 0 ? nodes[0].Node.Position : null;

    private void StartGlide(Beat beat, double start)
    {
        if (beat.Motion is not { } motion || beat.From is not { } origin)
        {
            return;
        }
        var points = new[] { new Vector2((float)origin.X, (float)origin.Y) }.Concat(motion.Path.Select(p => new Vector2((float)p.X, (float)p.Y))).ToArray();
        var lengths = new float[points.Length];
        for (var i = 1; i < points.Length; i++)
        {
            lengths[i] = lengths[i - 1] + points[i - 1].DistanceTo(points[i]);
        }
        // Towers proposal, stage T4: each point's height (a tower's platform or the ground), so a climb is seen going up.
        var heights = points.Select(p => (float)LevelAt(p.X, p.Y)).ToArray();
        var rest = _scene?.Find(motion.Model) is { } now ? new Vector2((float)now.X, (float)now.Y) : points[^1];
        _glides.Add(new Glide(motion.Model, points, lengths, lengths[^1], start, beat.Seconds, heights, rest, (float)LevelAt(rest.X, rest.Y)));
        GlidesStarted++;
        LastGlide = (motion.Model, points[0], points[^1], beat.Seconds);
        var end = points[^1];
        var standing = _scene?.Find(motion.Model)?.Standing ?? 0;
        if (!OnScreen(ScreenOf(end.X, end.Y, standing)))
        {
            CentreOnPoint(end);
        }
    }

    private void StartKnock(Beat beat, double start)
    {
        // Animation suite: a shooter's recoil, a short kick back from what it fires at (a model, or a point).
        if (beat.Kind == BeatKind.Recoil)
        {
            if (Where(beat.Model, out var gun, out _)
                && (beat.Other is { } aimed && Where(aimed, out var target, out _) ? target : beat.At is { } point ? Ground(point) : (Vector3?)null) is { } towards
                && new Vector3(gun.X - towards.X, 0, gun.Z - towards.Z) is { } back && back.Length() > 0.01f)
            {
                _knocks.Add(new Knock(beat.Model, start, beat.Seconds, back.Normalized() * RecoilInches, 0.15f));
                RecoilsStarted++;
            }
            return;
        }
        if (beat.Other is not { } other || !Where(beat.Model, out var at, out _) || !Where(other, out var from, out _))
        {
            return;
        }
        var away = new Vector3(at.X - from.X, 0, at.Z - from.Z);
        if (away.Length() < 0.01f)
        {
            return;
        }
        if (beat.Kind == BeatKind.Reach)
        {
            // Animation suite: a lean to the friend it tends and back, held at its peak a moment.
            _knocks.Add(new Knock(beat.Model, start, beat.Seconds, -away.Normalized() * ReachInches, 0.5f));
            return;
        }
        if (beat.Kind == BeatKind.Lunge)
        {
            _knocks.Add(new Knock(beat.Model, start, beat.Seconds, -away.Normalized() * LungeInches, (float)(Choreography.LungePeak / Choreography.LungeSeconds)));
            LungesStarted++;
            return;
        }
        FlinchesStarted++;
        // Hit recoil (2026-11-26, "when the model is hit I dont want the entire model to move just have them recoil"): a
        // figure with a skeleton recoils in its body only (the trunk and head snap back from the hit, as hard as the hit),
        // its base and place untouched.
        if (WalkingRig(beat.Model) is not null)
        {
            _wounds.Add(new Wound(beat.Model, start, beat.Seconds, away.Normalized(), Choreography.PowerOf(beat), FlinchRecoil));
            RecoilPosesStarted++;
            return;
        }
        // No skeleton: a small knock back and in again (a pistol less than a rifle, a shotgun or a heavy weapon further).
        var knock = FlinchInches * (beat.Size >= 1 ? (float)HitPowers.Scale(Choreography.PowerOf(beat)) : 1f);
        _knocks.Add(new Knock(beat.Model, start, beat.Seconds, away.Normalized() * knock, 0.5f));
    }

    // The facing from one table point to another (the yaw the figure holder is drawn with).
    private static float FacingTo(float fromX, float fromY, float toX, float toY) => Mathf.Atan2(toX - fromX, toY - fromY);

    // The facing a model's figure has after the turns started so far: the last turn's, or the middle of the battlefield.
    private float FacingNow(ModelId id) =>
        _facing.TryGetValue(id, out var yaw) ? yaw
        : _scene?.Find(id) is { } model ? FacingTo((float)model.X, (float)model.Y, (float)(_scene.Width / 2), (float)(_scene.Depth / 2))
        : 0;

    private void StartTurn(Beat beat, double start)
    {
        Vector3 aim;
        if (beat.Other is { } target && Where(target, out var model, out _))
        {
            aim = model;
        }
        else if (beat.At is { } point)
        {
            // Stage A4: turning to a point (an attack at a point).
            aim = Ground(point);
        }
        else
        {
            return;
        }
        var from = beat.From is { } origin ? new Vector3((float)origin.X, 0, (float)origin.Y) : Where(beat.Model, out var at, out _) ? at : (Vector3?)null;
        if (from is not { } f || new Vector2(aim.X - f.X, aim.Z - f.Z).Length() < 0.01f)
        {
            return;
        }
        var was = FacingNow(beat.Model);
        var to = FacingTo(f.X, f.Z, aim.X, aim.Z);
        // The short way round.
        to = was + Mathf.Wrap(to - was, -Mathf.Pi, Mathf.Pi);
        _facing[beat.Model] = Mathf.Wrap(to, -Mathf.Pi, Mathf.Pi);
        _turns.Add(new FigureTurn(beat.Model, start, beat.Seconds, was, to));
        TurnsStarted++;
    }

    // Animation stage A3: a fall (or a rise) of a model's figure: it tips backwards over its feet, away from the model
    // that downed it, turning first to face that model, and settles lying over its base (a rise is the same, backwards).
    // A falling model is drawn standing until its fall ends (_held), then lying, as the server's status has it.
    private sealed record FallTrack(ModelId Model, double Start, double Seconds, bool Rising, float FromYaw, float ToYaw, float LyingScale, float Height, float Length)
    {
        // Down and up by the ragdoll (2026-11-25): a figure with a skeleton falls limp as a model put Out of Action does,
        // kept over its base (DownFall), and stands up from the very pose it lies in (Ragdoll.Rise). Frames: the rise's,
        // or the fall's once worked out (DownFall.Fall); its pace is the beat's (Fast: faster). Null: it tips over.
        public DownFall? Down { get; init; }

        public IReadOnlyList<Joint3[]>? RiseFrames { get; init; }

        public IReadOnlyList<Joint3[]>? Frames => RiseFrames ?? (Down is { Fall.Ready: true } d ? d.Fall.Frames : null);

        public bool Ragdoll => Down is not null || RiseFrames is not null;

        // How long it plays: a fall its own length at the beat's pace (until it is worked out, not yet known); a rise
        // the beat.
        public double Plays => RiseFrames is not null ? Seconds
            : Frames is { Count: > 1 } f ? Math.Max(Seconds, (f.Count - 1) * Client.Core.Ragdoll.FrameSeconds * Seconds / Choreography.FallSeconds)
            : double.MaxValue;
    }

    // An Out of Action model drawn again as it goes (its figure and base, not in the scene): it falls (unless it was Down),
    // then sinks and fades, and is freed.
    private sealed record CasualtyFigure(Node3D Root, Node3D Holder, List<ShaderMaterial> Shaders, List<StandardMaterial3D> Paints, double Start, double FallSeconds,
        double SinkSeconds, bool Falls, float Yaw, float LyingScale, float Height, float Length)
    {
        // Audit item B2: the model it stands for, and (when its own move of the update is still to play) the rest of that
        // move's glide: the figure is drawn at the glide's point (rest plus the glide's offset) until the glide ends.
        public ModelId Model { get; init; }

        public Vector3? Rides { get; init; }

        // Where it falls: the server's last place for the model.
        public Vector3 Home { get; init; }

        // Movement review (2026-11-23): the facing it had, from which it turns to face the attacker over the first half of
        // its fall (it used to face the attacker from the moment it appeared).
        public float FromYaw { get; init; }

        // Bodies (2026-11-23): the base's paints, which fade as it goes; Paints are the figure's, which darken.
        public List<StandardMaterial3D> Base { get; init; } = [];

        // Ragdoll (2026-11-24): a figure with a skeleton falls limp (null: it tips over), pushed this way.
        public RagdollRig? Rig { get; init; }

        public Vector3 Push { get; init; }

        // Where on the body the hit landed (0 the feet, 1 the head).
        public float Hit { get; init; } = (float)RagdollImpacts.ChestHigh;

        public IReadOnlyList<Color> PaintColours { get; } = [.. Paints.Select(p => p.AlbedoColor)];
    }

    // A plain body stands this tall (as DrawModel draws it); a figure stands at its catalog height (the art pipeline's,
    // 0.5" to 6"); lying, either one's centre is this high.
    private const float StandingTall = 1.2f;
    private const float LyingCentre = 0.15f;

    private readonly List<FallTrack> _falls = [];
    private readonly List<CasualtyFigure> _ghosts = [];
    private readonly Dictionary<ModelId, ModelStatus> _held = [];
    private readonly Dictionary<ModelId, (Basis Basis, Vector3 Offset)> _fallen = [];
    private bool _fallEnded;
    private readonly List<ModelId> _endedFalls = [];

    // How much smaller a figure lying across its base is than standing (it lies within the base, as DrawModel fits it).
    private static float LyingScale(double radius, float height) => Math.Min(1f, (float)(2 * radius * 0.95) / height);

    // Animation stage A5: how much smaller the figure is drawn lying than standing, exactly as DrawModel fits it
    // (Visuals.FitOnBase: standing to its catalog height, lying on its back to the lying height, both within the base), so a
    // fall hands over to the lying drawing without a jump. From the figure's own unscaled bounds; the estimate without them.
    // Also the length the figure is drawn standing (its catalog height, or less for a figure so wide its base limits it,
    // seen on screen with the Reliquary Walker), which a fall tips over its feet.
    private static (float Scale, float Length) LyingFit(Node3D? figure, double radius, float height)
    {
        if (figure is null || Visuals.Bounds(figure).Size is not { Y: > 0.0001f, Z: > 0.0001f } size)
        {
            return (LyingScale(radius, height), height);
        }
        float Fit(float up, float across, float along, float tall)
        {
            var scale = tall / up;
            var widest = Math.Max(across, along) * scale;
            return widest > 2 * radius ? scale * (float)(2 * radius) / widest : scale;
        }
        // Lying on its back (turned -90 degrees about X): its depth is the height it lies at, its height lies along.
        var standing = Fit(size.Y, size.X, size.Z, height);
        return (Fit(size.Z, size.X, size.Y, LyingCentre * 2) / standing, size.Y * standing);
    }

    // How tall a model stands as DrawModel draws it: its figure's catalog height, or the plain body's (high contrast, or
    // no figure in the catalog).
    private float StandingHeight(ModelShape model)
    {
        var unitKey = model.Type is { } type ? VisualCatalog.UnitKey(type) : VisualCatalog.DefaultUnitKey;
        return !_highContrast && Visuals.Catalog.PathFor(unitKey, VisualCatalog.DefaultUnitKey) is not null
            ? (float)Visuals.Catalog.HeightFor(unitKey, VisualCatalog.DefaultUnitKey)
            : StandingTall;
    }

    // The facing towards the model that downed it, or the facing it has.
    private float FallYaw(ModelId id, ModelId? by, Vector3 at)
    {
        if (by is { } other && Where(other, out var from, out _) && new Vector2(from.X - at.X, from.Z - at.Z).Length() > 0.01f)
        {
            var was = FacingNow(id);
            var to = FacingTo(at.X, at.Z, from.X, from.Z);
            return was + Mathf.Wrap(to - was, -Mathf.Pi, Mathf.Pi);
        }
        return FacingNow(id);
    }

    private void StartFall(Beat beat, double start)
    {
        if (!Where(beat.Model, out var at, out var radius))
        {
            return;
        }
        var rising = beat.Kind == BeatKind.Rise;
        var from = FacingNow(beat.Model);
        var to = rising ? from : FallYaw(beat.Model, beat.Other, at);
        _facing[beat.Model] = Mathf.Wrap(to, -Mathf.Pi, Mathf.Pi);
        var height = _lastDrawn.Find(_scene, beat.Model) is { } shape ? StandingHeight(shape) : StandingTall;
        // The standing figure (the holder's first child when drawn standing; a rising model is drawn standing too).
        var figure = _figures.TryGetValue(beat.Model, out var holder) && IsInstanceValid(holder) && holder.GetChildCount() > 0 && holder.GetChild(0) is Node3D standing
            && standing.Name != "Lying" ? standing : null;
        var (lying, length) = LyingFit(figure, radius, height);
        var track = new FallTrack(beat.Model, start, beat.Seconds, rising, from, to, lying, height, length);
        if (figure is not null && CachedRig(figure) is { } rig)
        {
            // Ragdoll (2026-11-25): it keeps its facing (it does not turn round first) and goes limp from the hit.
            _facing[beat.Model] = Mathf.Wrap(from, -Mathf.Pi, Mathf.Pi);
            var place = new Vector3(at.X, at.Y, at.Z);
            if (rising)
            {
                // From the pose it lies in (its fall's last frame; worked out now when it was drawn Down without one).
                var down = DownPoseOf(beat.Model, rig, place, radius, height, from);
                _downFalls.Remove(beat.Model);
                var land = LandAt(beat.Model, place, radius);
                track = track with { ToYaw = from, RiseFrames = Ragdoll.Rise(down.Fall.Wait()[^1], StandingJoints(rig), height, land) };
            }
            else
            {
                var push = Backwards(from) * (float)RagdollImpacts.Shot;
                var hit = (float)RagdollImpacts.ChestHigh;
                if (beat.Other is { } by && Where(by, out var from2, out _) && new Vector2(at.X - from2.X, at.Z - from2.Z).Length() > 0.01f)
                {
                    push = new Vector3(at.X - from2.X, 0, at.Z - from2.Z).Normalized() * (float)RagdollImpacts.Shot;
                }
                if (_recentImpact.TryGetValue(beat.Model, out var impact))
                {
                    var thrown = impact.Hit.Push;
                    push = new Vector3((float)thrown.X, (float)thrown.Y, (float)thrown.Z);
                    hit = (float)impact.Hit.Hit;
                    // Seamless: limp from the hit itself when it lands now or later.
                    if (impact.At <= start && impact.At >= Now - 0.05)
                    {
                        track = track with { Start = impact.At, Seconds = beat.Seconds + (start - impact.At) };
                    }
                }
                var fall = new RagdollRig { Skeleton = rig.Skeleton, Joint = rig.Joint, Chest = rig.Chest, Order = rig.Order };
                Simulate(fall, place, radius, push, height, beat.Model, hit, _fallObstacles[beat.Model] = Obstacles(beat.Model, place), down: true);
                _downFalls[beat.Model] = new DownFall { Place = place, Yaw = from, Fall = fall };
                track = track with { ToYaw = from, Down = _downFalls[beat.Model] };
                RagdollFalls++;
            }
        }
        _falls.Add(track);
        if (rising)
        {
            RisesStarted += beat.Seq > 0 ? 1 : 0;
            return;
        }
        // Drawn standing until the fall ends (its label, base and blood drops are drawn as they were).
        if (_scene?.Find(beat.Model) is { Status: ModelStatus.Down })
        {
            _held[beat.Model] = ModelStatus.Standing;
            Rebuild();
        }
        FallsStarted += beat.Seq > 0 ? 1 : 0;
    }

    private void StartCasualty(Beat beat, double start)
    {
        // As it was last drawn (in play it has just left the table; the UI check plays one on a model still there).
        if (_lastDrawn.Find(_scene, beat.Model) is not { } shape || !_effects.IsInsideTree())
        {
            return;
        }
        // The beat's time is a fall (when it was standing) then the sink, both played faster together when the update is.
        var scale = beat.Seconds / ((beat.Hit ? Choreography.FallSeconds : 0) + Choreography.SinkSeconds);
        var falls = beat.Hit;
        var fallSeconds = falls ? Choreography.FallSeconds * scale : 0;
        var sinkSeconds = Choreography.SinkSeconds * scale;
        var fallStart = start;
        // Taken Down and then Out of Action in one update: the figure falls at its fall, then goes.
        if (_falls.FindIndex(f => f.Model == beat.Model && !f.Rising) is >= 0 and var pending)
        {
            fallStart = _falls[pending].Start;
            fallSeconds = _falls[pending].Seconds;
            falls = true;
            sinkSeconds += Math.Max(0, start - (fallStart + fallSeconds));
            _falls.RemoveAt(pending);
            _held.Remove(beat.Model);
        }
        // Audit item B2: where the server's table last had the model (beat.At, from StatusChange.At): a model hit by a mine
        // during its own move goes where the server halted it, not where it started. Without one (the UI checks' made-up
        // casualties), where it was drawn.
        var at = beat.At is { } server ? new Vector3((float)server.X, (float)LevelAt(server.X, server.Y), (float)server.Y)
            : new Vector3((float)shape.X, (float)shape.Standing, (float)shape.Y);
        // And when its own move of this update is still to play or playing (its glide ends at that halt), the casualty
        // figure travels it first, as the model would have, then falls there.
        Vector3? rides = null;
        foreach (var glide in _glides)
        {
            if (glide.Model == beat.Model)
            {
                rides = new Vector3(glide.Rest.X, glide.RestHeight, glide.Rest.Y);
            }
        }
        var yaw = FallYaw(beat.Model, beat.Other, at);
        var fromYaw = falls ? FacingNow(beat.Model) : yaw;
        var (root, holder, shaders, paints, under, tall, lyingScale, length) = BuildCasualty(shape, at, fromYaw, _effects);
        // Ragdoll (2026-11-24, "the impact influences how far they ragdoll; seamless, no hesitation"): thrown as its last hit
        // threw it (else a light push backwards); a figure with a skeleton goes limp on that hit itself when it is still to
        // land or landing now, not on its Injury Roll's beat after it.
        var rig = falls ? RigOf(root) : null;
        // Ragdoll (2026-11-25): a model already lying Down (drawn in its own fall's pose) goes on lying in that pose.
        if (!falls && RigOf(root) is { } lying && _downFalls.TryGetValue(beat.Model, out var down) && down.Fall.Wait() is { Count: > 0 } downFrames)
        {
            rig = lying;
            rig.Frames = [downFrames[^1]];
        }
        var push = Backwards(yaw) * (float)RagdollImpacts.Crumple;
        var hit = (float)RagdollImpacts.ChestHigh;
        if (_recentImpact.TryGetValue(beat.Model, out var impact))
        {
            var thrown = impact.Hit.Push;
            push = new Vector3((float)thrown.X, (float)thrown.Y, (float)thrown.Z);
            hit = (float)impact.Hit.Hit;
            if (rig is not null && impact.At <= fallStart && impact.At >= Now - 0.05)
            {
                fallStart = impact.At;
            }
        }
        var ghost = new CasualtyFigure(root, holder, shaders, paints, fallStart, fallSeconds, sinkSeconds, falls, yaw, lyingScale, tall, length)
        {
            Model = beat.Model,
            Rides = rides,
            Home = at,
            FromYaw = fromYaw,
            Base = under,
            Rig = rig,
            Push = push,
            Hit = hit,
        };
        // Which way it fell, for its body drawn again later.
        _fallPush[beat.Model] = (ghost.Push, ghost.Hit);
        if (rig is not null && falls)
        {
            // Its fall, from where it falls (its place, facing as it was), set going now so it is there when it comes due.
            root.Position = at;
            holder.Basis = new Basis(Vector3.Up, fromYaw);
            var blocks = _fallObstacles[beat.Model] = Obstacles(beat.Model, at);
            Simulate(rig, at, (float)shape.Radius, push, tall, beat.Model, hit, blocks);
            RagdollFalls++;
        }
        if (rides is not null)
        {
            CasualtiesRiding++;
        }
        _ghosts.Add(ghost);
        PoseGhost(ghost, Now);
        CasualtiesStarted += beat.Seq > 0 ? 1 : 0;
    }

    // A casualty figure's nodes (its figure, or plain body, and its base, as the model was drawn), each with its own copy of
    // every material: standing at a point facing yaw, added to parent. Used by the casualty animation and, since the bodies
    // change (2026-11-23), for a body drawn without one (Off, a catch-up, a rejoin).
    private (Node3D Root, Node3D Holder, List<ShaderMaterial> Shaders, List<StandardMaterial3D> Paints, List<StandardMaterial3D> Base, float Tall, float LyingScale, float Length)
        BuildCasualty(ModelShape shape, Vector3 at, float yaw, Node3D parent)
    {
        var root = new Node3D { Name = $"Casualty{shape.Id.Value}", Position = at };
        var tall = StandingHeight(shape);
        var holder = new Node3D { Position = new Vector3(0, tall / 2, 0), Rotation = new Vector3(0, yaw, 0) };
        var colour = BodyColour(shape);
        Node3D body;
        Node3D? art = null;
        if (!_highContrast && Visuals.Model(shape.Type is { } type ? VisualCatalog.UnitKey(type) : VisualCatalog.DefaultUnitKey, VisualCatalog.DefaultUnitKey) is { } figure)
        {
            Visuals.FitOnBase(figure, new Vector3(0, -tall / 2, 0), (float)shape.Radius, tall);
            Visuals.PaintAccent(figure, new StandardMaterial3D { AlbedoColor = colour, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true });
            body = figure;
            art = figure;
        }
        else
        {
            body = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = (float)shape.Radius, BottomRadius = (float)shape.Radius, Height = tall, RadialSegments = 24 },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = colour },
            };
        }
        holder.AddChild(body);
        root.AddChild(holder);
        // Stage A5: its base drawn as the model's own (dark edge, rim in the body colour, sandstone top), so nothing jumps
        // when the casualty figure takes the model's place; each part with its own paint, to fade.
        var r = (float)shape.Radius;
        var edge = UnitPalette.Rim((colour.R, colour.G, colour.B));
        var top = UnitPalette.BaseTop;
        foreach (var (radius, height, paint) in new[]
        {
            (r, 0.05f, new Color(0.10f, 0.10f, 0.11f)),
            (r - 0.03f, 0.055f, new Color(edge.R, edge.G, edge.B)),
            (r - UnitPalette.RimWidth, 0.06f, new Color(top.R, top.G, top.B)),
        })
        {
            root.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = height, RadialSegments = 16 },
                Position = new Vector3(0, height / 2, 0),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = paint, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
            });
        }
        parent.AddChild(root);
        // Stage A5: every material of the casualty figure is its own copy, so it can fade without touching the model it
        // was copied from: the figure's PS1 shader dissolves (fade), every plain paint goes see-through.
        var shaders = new List<ShaderMaterial>();
        var paints = new List<StandardMaterial3D>();
        var under = new List<StandardMaterial3D>();
        foreach (var mesh in root.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().Append(body as MeshInstance3D).OfType<MeshInstance3D>().Distinct())
        {
            if (mesh.MaterialOverride is StandardMaterial3D own)
            {
                // Bodies (2026-11-23): the base fades away (so a body never looks like a Down model, which keeps its base);
                // the figure (here the plain body) stays, darkened.
                var copy = (StandardMaterial3D)own.Duplicate();
                var isBase = mesh.GetParent() == root;
                if (isBase)
                {
                    copy.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                    under.Add(copy);
                }
                else
                {
                    paints.Add(copy);
                }
                mesh.MaterialOverride = copy;
                continue;
            }
            for (var i = 0; mesh.Mesh is { } m && i < m.GetSurfaceCount(); i++)
            {
                switch (mesh.GetSurfaceOverrideMaterial(i) ?? m.SurfaceGetMaterial(i))
                {
                    case ShaderMaterial psx:
                        var dissolve = (ShaderMaterial)psx.Duplicate();
                        mesh.SetSurfaceOverrideMaterial(i, dissolve);
                        shaders.Add(dissolve);
                        break;
                    case StandardMaterial3D plain:
                        var own2 = (StandardMaterial3D)plain.Duplicate();
                        mesh.SetSurfaceOverrideMaterial(i, own2);
                        paints.Add(own2);
                        break;
                }
            }
        }
        var (lyingScale, length) = LyingFit(art, shape.Radius, tall);
        return (root, holder, shaders, paints, under, tall, lyingScale, length);
    }

    private void FreeGhost(CasualtyFigure ghost)
    {
        if (IsInstanceValid(ghost.Root))
        {
            ghost.Root.GetParent()?.RemoveChild(ghost.Root);
            ghost.Root.QueueFree();
        }
        GhostsFreed++;
    }

    // The pose of a figure tipped backwards by p (0 standing, 1 lying) about its feet, facing yaw: its basis, and the
    // offset of its centre from where it stands upright (it ends lying over its base, its centre LyingCentre up).
    private static (Basis Basis, Vector3 Offset) Tipped(float p, float yaw, float lyingScale, float height, float length)
    {
        // The holder's centre is height/2 above the ground (the figure's feet); the figure's body is length long (stage A5:
        // less than height for a figure its base limits). It tips about its feet, slides back so it ends centred over its
        // base, and ends lying LyingCentre up, shrunk to the lying drawing's size.
        var back = -new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw));
        var axis = Vector3.Up.Cross(back).Normalized();
        var tip = new Basis(axis, Mathf.Pi / 2 * p);
        var scale = Mathf.Lerp(1f, lyingScale, p);
        var basis = tip * new Basis(Vector3.Up, yaw).Scaled(Vector3.One * scale);
        var feet = new Vector3(0, -height / 2, 0);
        var centre = feet + new Vector3(0, length / 2, 0);
        var lands = feet + tip * new Vector3(0, length / 2 * scale, 0) - back * (length / 2 * scale * p) + new Vector3(0, LyingCentre * p, 0);
        return (basis, lands - basis * centre);
    }

    // Puts a casualty figure where it is now: falling, then sinking and fading. False once it has gone.
    private bool PoseGhost(CasualtyFigure ghost, double now)
    {
        if (!IsInstanceValid(ghost.Root))
        {
            return false;
        }
        var fallT = !ghost.Falls || ghost.FallSeconds <= 0 ? 1f : (float)Math.Clamp((now - ghost.Start) / ghost.FallSeconds, 0, 1);
        var p = ghost.Falls ? FallCurve(fallT) : 1f;
        var turn = Mathf.Clamp(fallT / 0.5f, 0, 1);
        var yaw = ghost.Falls ? Mathf.Lerp(ghost.FromYaw, ghost.Yaw, turn * turn * (3 - 2 * turn)) : ghost.Yaw;
        // Audit item B2: still travelling its model's move: at the glide's point, turning along its way as the model would.
        if (ghost.Rides is { } rest && now < ghost.Start)
        {
            if (_pose.TryGetValue(ghost.Model, out var travelled))
            {
                ghost.Root.Position = rest + travelled;
            }
            if (_turned.TryGetValue(ghost.Model, out var turning))
            {
                yaw = turning.Yaw;
            }
            // Walk cycle (2026-11-26): it walks its move as the model would, then falls where it halted.
            if (_stride.TryGetValue(ghost.Model, out var step) && CachedRig(ghost.Root) is { } walker)
            {
                PoseWalk(walker, step.Phase, step.Weight);
                GhostWalkFrames++;
            }
        }
        else if (ghost.Rides is not null)
        {
            ghost.Root.Position = ghost.Home;
        }
        if (ghost.Rig is { } rig)
        {
            // Ragdoll: the figure stands as it was (it does not turn round first) and goes limp from the hit, thrown as it
            // was hit; its fall plays at its own pace (Fast: faster, as every beat), then it lies as a body.
            ghost.Holder.Basis = new Basis(Vector3.Up, ghost.Rides is not null && now < ghost.Start ? yaw : ghost.FromYaw);
            ghost.Holder.Position = new Vector3(0, ghost.Height / 2, 0);
            // Its fall was set going off the main thread when the casualty was planned (StartCasualty); due before it is
            // there, it is waited for then (no hesitation on the hit).
            if (!ghost.Falls && rig.Ready)
            {
                // Already Down: it lies as it lay.
                PoseRig(rig, 1);
            }
            else if (now >= ghost.Start && rig.Wait() is { Count: > 0 })
            {
                PoseRig(rig, (now - ghost.Start) / RagdollSeconds(ghost, rig));
            }
        }
        else
        {
            var (basis, offset) = Tipped(p, yaw, ghost.LyingScale, ghost.Height, ghost.Length);
            ghost.Holder.Basis = basis;
            ghost.Holder.Position = new Vector3(0, ghost.Height / 2, 0) + offset;
        }
        var sinkStart = ghost.Start + (ghost.Falls ? ghost.FallSeconds : 0);
        var q = (float)Math.Clamp((now - sinkStart) / ghost.SinkSeconds, 0, 1);
        var end = sinkStart + ghost.SinkSeconds;
        if (ghost.Rig is { Ready: true } fallen)
        {
            // A ragdoll darkens and loses its base over its whole fall, however long that takes.
            end = Math.Max(end, ghost.Start + RagdollSeconds(ghost, fallen));
            q = (float)Math.Clamp((now - ghost.Start) / (end - ghost.Start), 0, 1);
        }
        // Bodies (Jason, 2026-11-23: "bodies lay on the ground, not disappear"; stage A5 dissolved it): the figure stays
        // lying where it fell and darkens to a body's shade; its base fades away, so it no longer reads as a model in play.
        Darken(ghost.Shaders, ghost.Paints, ghost.PaintColours, Mathf.Lerp(1f, BodyShade, q));
        foreach (var paint in ghost.Base)
        {
            paint.AlbedoColor = paint.AlbedoColor with { A = 1 - q };
        }
        return now < end;
    }

    // How long a ragdoll's fall plays: its own length, at the beat's pace (Fast: faster).
    private static double RagdollSeconds(CasualtyFigure ghost, RagdollRig rig) =>
        Math.Max(1e-3, ((rig.Frames?.Count ?? 1) - 1) * Ragdoll.FrameSeconds
            * (ghost.FallSeconds + ghost.SinkSeconds) / (Choreography.FallSeconds + Choreography.SinkSeconds));

    // Puts a figure back upright, facing as it was left, at its rest (after a rise or when finishing at once).
    private void Upright(ModelId id)
    {
        if (_figures.TryGetValue(id, out var figure) && IsInstanceValid(figure) && _modelNodes.TryGetValue(id, out var nodes))
        {
            figure.Basis = new Basis(Vector3.Up, FacingNow(id));
            // A ragdoll that stood up: its skeleton back at rest.
            if (figure.GetChildCount() > 0 && figure.GetChild(0) is Node3D art && CachedRig(art) is { } rig)
            {
                rig.Skeleton.ResetBonePoses();
            }
            foreach (var (node, rest) in nodes)
            {
                if (node == figure)
                {
                    figure.Position = rest;
                }
            }
        }
    }

    private void StartLean(Beat beat, double start)
    {
        _leans.Add(new Lean(beat.Model, start, beat.Seconds));
        LeansStarted++;
    }

    // The height a base centred at this table point stands at: a tower's height inside its footprint, a trench's floor
    // when the centre is in one (trench-maps stage S3), else the ground (as Rules.Core.Geometry.Towers.ElevationAt and the
    // rules' U81; display only).
    private double LevelAt(double x, double y) =>
        _scene?.Terrain.FirstOrDefault(t => t.Tower && t.Area.X0 < x && x < t.Area.X1 && t.Area.Y0 < y && y < t.Area.Y1)?.Height
        ?? (_scene is { } scene && TrenchPits.At(scene, x, y) is { } trench ? -trench.Depth : 0);

    private bool OnScreen(Vector2? at) => at is { } p && p.X >= 0 && p.Y >= 0 && p.X <= Size.X && p.Y <= Size.Y;

    // Draws each model that is travelling or flinching at its point now; a finished one is back on the server's position.
    // Per model, the glide playing now is the earliest that has not finished (one still to start holds the model where it
    // starts, so it does not jump ahead of the picture); a flinch adds its knock.
    private void ApplyPoses()
    {
        if (_glides.Count == 0 && _knocks.Count == 0 && _turns.Count == 0 && _leans.Count == 0 && _falls.Count == 0 && _ghosts.Count == 0 && _gestures.Count == 0 && _wounds.Count == 0)
        {
            return;
        }
        var now = Now;
        _pose.Clear();
        _bob.Clear();
        _stride.Clear();
        _posed.Clear();
        for (var i = 0; i < _glides.Count; i++)
        {
            var glide = _glides[i];
            var t = (now - glide.Start) / glide.Seconds;
            if (t >= 1)
            {
                _glides.RemoveAt(i--);
                _pose.TryAdd(glide.Model, Vector3.Zero);
                continue;
            }
            if (_posed.Add(glide.Model))
            {
                _pose[glide.Model] = GlideOffset(glide, (float)Math.Max(t, 0));
                if (t > 0 && glide.Total > 0.2f)
                {
                    var eased = (float)(t * t * (3 - 2 * t));
                    var travelled = eased * glide.Total;
                    // Walk cycle (2026-11-25): a figure with a skeleton strides, a step every StepLength of its height; it
                    // rises as its legs pass each other (high when they are together) and settles as they spread.
                    var tall = _lastDrawn.Find(_scene, glide.Model) is { } walker ? StandingHeight(walker) : StandingTall;
                    // (No more than MaxStepsPerSecond: a fast move takes long running strides rather than a blur of steps.)
                    var stride = Math.Max(tall * StepLength, glide.Total / (float)glide.Seconds / MaxStepsPerSecond);
                    var weight = Mathf.Clamp(Mathf.Min((float)t, 1 - (float)t) / 0.15f, 0, 1);
                    _stride[glide.Model] = (travelled / stride * Mathf.Pi, weight);
                    _bob[glide.Model] = StepBob * Mathf.Abs(Mathf.Cos(Mathf.Pi * travelled / stride)) * Mathf.Sin(Mathf.Pi * (float)t);
                }
            }
        }
        for (var i = 0; i < _knocks.Count; i++)
        {
            var knock = _knocks[i];
            var t = (float)((now - knock.Start) / knock.Seconds);
            // Out to the peak (eased out), then back to rest (eased in).
            var push = t is <= 0 or >= 1 ? 0f
                : t < knock.Peak ? Mathf.Sin(Mathf.Pi / 2 * t / knock.Peak)
                : Mathf.Cos(Mathf.Pi / 2 * (t - knock.Peak) / (1 - knock.Peak));
            _pose[knock.Model] = _pose.GetValueOrDefault(knock.Model) + knock.Dir * push;
            if (t >= 1)
            {
                _knocks.RemoveAt(i--);
            }
        }
        // Animation stage A2: per model, the turn playing now is the earliest not finished (one still to start holds the
        // figure at its old facing); a lean tilts it forward, eased in over its first 30% and out over its last 25%.
        _turned.Clear();
        _posed.Clear();
        for (var i = 0; i < _turns.Count; i++)
        {
            var turn = _turns[i];
            // (Off's turns take no time: done once started.)
            var t = turn.Seconds > 0 ? (float)((now - turn.Start) / turn.Seconds) : now >= turn.Start ? 1f : -1f;
            if (t >= 1)
            {
                _turns.RemoveAt(i--);
                _turned.TryAdd(turn.Model, (0, turn.To));
                continue;
            }
            if (_posed.Add(turn.Model))
            {
                var eased = Math.Max(t, 0);
                eased = eased * eased * (3 - 2 * eased);
                _turned[turn.Model] = (0, Mathf.Lerp(turn.From, turn.To, eased));
            }
        }
        for (var i = 0; i < _leans.Count; i++)
        {
            var lean = _leans[i];
            var t = (float)((now - lean.Start) / lean.Seconds);
            var tilt = t is <= 0 or >= 1 ? 0f : t < 0.3f ? t / 0.3f : t > 0.75f ? (1 - t) / 0.25f : 1f;
            var yaw = _turned.TryGetValue(lean.Model, out var turned) ? turned.Yaw : FacingNow(lean.Model);
            _turned[lean.Model] = (Mathf.DegToRad(LeanDegrees) * tilt, yaw);
            if (t >= 1)
            {
                _leans.RemoveAt(i--);
            }
        }
        ApplyGestures(now);
        // Animation stage A3: falls and rises tip the figure (a fall still to start holds it upright, a rise still to start
        // holds it lying); a fall that ends lets the model be drawn lying, a rise that ends puts it upright.
        _fallen.Clear();
        _fallEnded = false;
        _endedFalls.Clear();
        for (var i = 0; i < _falls.Count; i++)
        {
            var fall = _falls[i];
            var t = (float)Math.Clamp((now - fall.Start) / fall.Seconds, 0, 1);
            if (now - fall.Start >= (fall.Ragdoll ? fall.Plays : fall.Seconds))
            {
                _falls.RemoveAt(i--);
                if (fall.Rising)
                {
                    _fallen[fall.Model] = (new Basis(Vector3.Up, fall.ToYaw), Vector3.Zero);
                    if (fall.Ragdoll && _figures.TryGetValue(fall.Model, out var stood) && IsInstanceValid(stood) && stood.GetChildCount() > 0
                        && stood.GetChild(0) is Node3D art && CachedRig(art) is { } rig)
                    {
                        rig.Skeleton.ResetBonePoses();
                    }
                }
                else
                {
                    EndFallHold(fall.Model);
                    _fallEnded = true;
                    _endedFalls.Add(fall.Model);
                }
                continue;
            }
            // Movement review (2026-11-23): a track still to start (a rise after a fall of the same update) holds its pose
            // only once the earlier tracks of that model are done; before, a waiting rise held the model lying before it fell.
            if (now < fall.Start && _falls.Take(i).Any(f => f.Model == fall.Model))
            {
                continue;
            }
            // Ragdoll: a rise waits for its own fall to have settled (a ragdoll's fall plays on past its beat), and then
            // starts from there, so it gets up from the pose the fall ended in.
            if (fall.Ragdoll && fall.Rising && _falls.Take(i).Any(f => f.Model == fall.Model))
            {
                _falls[i] = fall with { Start = Math.Max(fall.Start, now) };
                continue;
            }
            if (fall.Ragdoll)
            {
                // Due before its worker is done: waited for (no hesitation on the hit).
                if (now >= fall.Start && fall.Down is { } due)
                {
                    due.Fall.Wait();
                }
                // Upright and facing as it stood; its skeleton does the falling (or the rising).
                _fallen[fall.Model] = (new Basis(Vector3.Up, fall.ToYaw), Vector3.Zero);
                if (fall.Frames is { } frames && _figures.TryGetValue(fall.Model, out var drawn) && IsInstanceValid(drawn)
                    && drawn.GetChildCount() > 0 && drawn.GetChild(0) is Node3D art && CachedRig(art) is { } posed)
                {
                    PoseRig(posed, frames, (now - fall.Start) / (fall.RiseFrames is not null ? fall.Seconds : fall.Plays));
                }
                continue;
            }
            var p = fall.Rising ? RiseCurve(t) : FallCurve(t);
            // Movement review (2026-11-23): turned to face the attacker over the first half of the fall (was the first 30%:
            // a model shot from behind spun round in a tenth of a second).
            var turn = Mathf.Clamp(t / 0.5f, 0, 1);
            var yaw = fall.Rising ? fall.ToYaw : Mathf.Lerp(fall.FromYaw, fall.ToYaw, turn * turn * (3 - 2 * turn));
            _fallen[fall.Model] = Tipped(p, yaw, fall.LyingScale, fall.Height, fall.Length);
        }
        for (var i = 0; i < _ghosts.Count; i++)
        {
            if (!PoseGhost(_ghosts[i], now))
            {
                EndGhost(_ghosts[i]);
                _ghosts.RemoveAt(i--);
            }
        }
        if (_fallEnded)
        {
            // Drawn lying now, as the server's status has it (only the models whose drawing changed are drawn again); one
            // the server has standing again is put upright.
            Rebuild();
            foreach (var id in _endedFalls)
            {
                if (_scene?.Find(id) is { Status: ModelStatus.Standing } && !_showcaseHolds.ContainsKey(id) && !_falls.Any(f => f.Model == id && f.Rising))
                {
                    Upright(id);
                }
            }
        }
        SetPoses();
        ApplyWalk();
        ApplyWounds(now);
    }

    private static Vector3 GlideOffset(Glide glide, float t)
    {
        // Eased so it starts and stops gently.
        var eased = t * t * (3 - 2 * t);
        var along = eased * glide.Total;
        var i = 1;
        while (i < glide.Points.Length - 1 && glide.Lengths[i] < along)
        {
            i++;
        }
        var leg = glide.Lengths[i] - glide.Lengths[i - 1];
        var part = leg <= 0 ? 1 : (along - glide.Lengths[i - 1]) / leg;
        var at = glide.Points[i - 1].Lerp(glide.Points[i], part);
        var height = Mathf.Lerp(glide.Heights[i - 1], glide.Heights[i], part);
        return new Vector3(at.X - glide.Rest.X, height - glide.RestHeight, at.Y - glide.Rest.Y);
    }

    // Puts each posed model's nodes at their rest plus this frame's offset.
    private void SetPoses()
    {
        foreach (var (id, offset) in _pose)
        {
            if (_modelNodes.TryGetValue(id, out var nodes))
            {
                foreach (var (node, rest) in nodes)
                {
                    if (IsInstanceValid(node))
                    {
                        node.Position = rest + offset;
                    }
                }
            }
        }
        foreach (var (id, bob) in _bob)
        {
            if (!_fallen.ContainsKey(id) && _figures.TryGetValue(id, out var stepping) && IsInstanceValid(stepping))
            {
                stepping.Position += new Vector3(0, bob, 0);
            }
        }
        foreach (var (id, (pitch, yaw)) in _turned)
        {
            if (!_fallen.ContainsKey(id) && _figures.TryGetValue(id, out var figure) && IsInstanceValid(figure))
            {
                figure.Rotation = new Vector3(pitch, yaw, 0);
            }
        }
        foreach (var (id, (basis, offset)) in _fallen)
        {
            if (_figures.TryGetValue(id, out var figure) && IsInstanceValid(figure) && _modelNodes.TryGetValue(id, out var nodes))
            {
                figure.Basis = basis;
                foreach (var (node, rest) in nodes)
                {
                    if (node == figure)
                    {
                        figure.Position = rest + _pose.GetValueOrDefault(id) + offset;
                    }
                }
            }
        }
        // Labels moved (turns and leans move none): place them again next frame.
        if (_pose.Count > 0)
        {
            _labelsPlaced = "";
        }
    }
}
