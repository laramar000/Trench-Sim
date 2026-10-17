using System.Globalization;
using Rules.Core;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Client.Core;

// M7.4: the battlefield as the 3D view draws it, in inches, built only from the server's state. No Godot here: the
// Godot client turns this into shapes, so the same description serves placeholder shapes now and real art later.
//
// The rules are 2D with two levels (ground, or on a High platform); they have no heights (U17). The heights below are
// display conventions only, chosen by Jason (M7.4 Q2): they change the picture, never a rule. Line of sight and cover
// always come from the rules geometry, not from the picture.
public static class DisplayHeight
{
    public const double Low = 0.25;
    public const double Cover = 0.75;
    public const double Wall = 3.0;

    public static double Of(TerrainProfile profile) => profile switch
    {
        TerrainProfile.Low => Low,
        TerrainProfile.Cover => Cover,
        _ => Wall,
    };
}

// A rectangle on the table, in inches: x from X0 to X1, y from Y0 to Y1.
public readonly record struct TableRect(double X0, double Y0, double X1, double Y1)
{
    public double Width => X1 - X0;
    public double Depth => Y1 - Y0;
    public double CentreX => (X0 + X1) / 2;
    public double CentreY => (Y0 + Y1) / 2;

    public static TableRect Of(Rect rect) => new(Inches(rect.Min.X), Inches(rect.Min.Y), Inches(rect.Max.X), Inches(rect.Max.Y));

    public static double Inches(Length length) => length.Thousandths / 1000.0;
}

public sealed record ZoneShape(string Id, TableRect Area, string Label, string? SeatLetter = null)
{
    // Proposal G: the short label drawn at the zone's edge ("ZONE A"), or the zone's own id before a seat has it.
    public string Short => SeatLetter is { } letter ? $"ZONE {letter}" : $"ZONE {Id.ToUpperInvariant()}";
}

// Words carry every fact (profile, kind, high ground, Objective), so nothing depends on colour or shape alone.
public sealed record TerrainShape(string Id, TableRect Area, TerrainProfile Profile, double Height, bool Platform, bool Objective, string Label);

// A model on the table. Standing is the height of what it stands on: 0 on the ground, the platform's display height
// on a High platform. Models not on the table (not deployed, out of action) are not in the scene.
public sealed record ModelShape(
    ModelId Id,
    string Name,
    Seat Seat,
    string SeatLetter,
    double X,
    double Y,
    double Radius,
    bool HighGround,
    double Standing,
    ModelStatus Status,
    bool Active,
    bool Yours,
    bool Activated = false,
    bool Eligible = false)
{
    // Activated: the server's ActivatedThisTurn (the rules clear it at the next turn). Eligible: it can still activate
    // this turn (the activation phase, deployed, on the table, not activated), the same conditions the rules check for
    // ActivateModel apart from whose turn it is; for the seat to activate next they are exactly the units the rules offer
    // (TurnSequence.LegalCommands, a test checks). Both are display only: they never decide anything.
    public string Label => $"[{SeatLetter}] {Name}{(Active ? ", ACTIVE" : Activated ? ", activated" : "")}{(Status == ModelStatus.Standing ? "" : $", {Words.Status(Status)}")}";

    // Proposal G: the label drawn over the model on the battlefield: its short id ("A2"), then a mark when it has
    // activated this turn (✓) and when it is Down (↓). The full name and state are in the panel beside the battlefield.
    public string ShortLabel(string activatedMark = "✓", string downMark = "↓") =>
        $"{Id.Value.ToUpperInvariant()}{(Activated && !Active ? " " + activatedMark : "")}{(Status == ModelStatus.Down ? " " + downMark : "")}";

    // How the battlefield draws the model's turn state (colour is never the only sign: the label says it too).
    public UnitLook Look => Active ? UnitLook.Active : Activated ? UnitLook.Activated : Eligible ? UnitLook.Eligible : UnitLook.Waiting;

    // One line for the selected model: what it is, where (in inches) and on which level.
    public string Text => $"[{SeatLetter}] {Name}: {Words.Status(Status)}{(Active ? ", ACTIVE" : "")}, at ({BoardScene.Inch(X)}, {BoardScene.Inch(Y)}), " +
        $"{(HighGround ? "on high ground" : "on the ground")}, base {BoardScene.Inch(Radius * 2)}.";
}

// A model's turn state on the battlefield: activating now, already activated this turn, can still activate, or neither
// (deployment, or the match is between phases).
public enum UnitLook
{
    Waiting,
    Eligible,
    Active,
    Activated,
}

public sealed record BoardScene(double Width, double Depth, IReadOnlyList<ZoneShape> Zones, IReadOnlyList<TerrainShape> Terrain, IReadOnlyList<ModelShape> Models)
{
    // Null when the match has no battlefield yet (before the Welcome, or a setup without a scenario).
    public static BoardScene? Of(MatchState state, Seat? you, ContentNames names)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(names);
        if (state.Setup?.Scenario is not { } scenario)
        {
            return null;
        }
        var field = scenario.Battlefield;
        var zones = field.Zones.Select(z =>
        {
            var owner = new[] { Seat.One, Seat.Two }.Where(s => state.SeatFor(s).ZoneId == z.ZoneId).Select(s => $", {Words.Seat(s)} [{Words.SeatLetter(s)}]").FirstOrDefault();
            var letter = new[] { Seat.One, Seat.Two }.Where(s => state.SeatFor(s).ZoneId == z.ZoneId).Select(Words.SeatLetter).FirstOrDefault();
            return new ZoneShape(z.ZoneId, TableRect.Of(z.Area.Bounds), $"Zone {z.ZoneId}{owner}", letter);
        }).ToList();
        var terrain = field.Terrain.Select(t =>
        {
            var objective = scenario.ObjectiveIds.Contains(t.Id);
            return new TerrainShape(t.Id, TableRect.Of(t.Footprint.Bounds), t.Profile, DisplayHeight.Of(t.Profile), t.IsPlatform, objective, TerrainLabel(t, objective));
        }).ToList();
        var models = new List<ModelShape>();
        // On the table: a position, Down models included (a Down model still has a base, p. 36), as Rules.Core's Board does.
        foreach (var model in state.Models)
        {
            if (model.Position is not { } at || state.Setup.ProfileOf(model.Id) is not { } profile)
            {
                continue;
            }
            var body = new ModelBody(model.Id.Value, at, profile.BaseRadius);
            var level = Elevation.Of(field, body);
            var standing = level.Level == ModelLevel.High && field.Terrain.FirstOrDefault(t => t.Id == level.PlatformId) is { } platform
                ? DisplayHeight.Of(platform.Profile)
                : 0;
            models.Add(new ModelShape(model.Id, names.Of(model.Id), model.Seat, Words.SeatLetter(model.Seat),
                TableRect.Inches(body.Centre.X), TableRect.Inches(body.Centre.Y), TableRect.Inches(body.Radius),
                level.Level == ModelLevel.High, standing, model.Status, state.ActiveModel == model.Id, model.Seat == you,
                model.ActivatedThisTurn, CanStillActivate(state, model)));
        }
        return new BoardScene(TableRect.Inches(field.Width), TableRect.Inches(field.Height), zones, terrain, models);
    }

    // Whether the model can still activate this turn, from the server's state (see ModelShape). Display only.
    public static bool CanStillActivate(MatchState state, ModelState model) =>
        state.Phase == Phase.Activation && state.Outcome is null && model.Deployed && model.Status != ModelStatus.OutOfAction
        && !model.ActivatedThisTurn && state.ActiveModel != model.Id;

    public string Size => $"{Inch(Width)} x {Inch(Depth)}";

    public ModelShape? Find(ModelId? id) => id is { } wanted ? Models.FirstOrDefault(m => m.Id == wanted) : null;

    // The model whose base contains the table point (x, y), looking at the level the point was picked on: a point picked
    // at height h only matches models standing at h. Nearest centre wins if bases are close. Display only: selecting a
    // model never decides anything.
    public ModelShape? ModelAt(double x, double y, double standing = 0) =>
        Models.Where(m => Math.Abs(m.Standing - standing) < 0.001 && Sq(m.X - x) + Sq(m.Y - y) <= Sq(m.Radius))
            .OrderBy(m => Sq(m.X - x) + Sq(m.Y - y))
            .FirstOrDefault();

    // The heights a pick should try, highest first (platform tops, then the ground).
    public IReadOnlyList<double> StandingLevels => Models.Select(m => m.Standing).Append(0).Distinct().OrderDescending().ToList();

    // Keyboard selection: the next (or previous) model on the table, yours first, in a fixed order; wraps around.
    public ModelShape? Next(ModelId? current, bool forward = true)
    {
        var order = Models.OrderBy(m => m.Yours ? 0 : 1).ThenBy(m => m.Id.Value, StringComparer.Ordinal).ToList();
        if (order.Count == 0)
        {
            return null;
        }
        var at = order.FindIndex(m => m.Id == current);
        if (at < 0)
        {
            return forward ? order[0] : order[^1];
        }
        return order[((at + (forward ? 1 : -1)) % order.Count + order.Count) % order.Count];
    }

    public static string Inch(double inches) => inches.ToString("0.0", CultureInfo.InvariantCulture) + "\"";

    private static double Sq(double v) => v * v;

    private static string TerrainLabel(TerrainBlock t, bool objective)
    {
        var words = new List<string>
        {
            t.Profile switch
            {
                TerrainProfile.Low => "Low",
                TerrainProfile.Cover => "Cover",
                _ => t.IsPlatform ? "Platform" : "Wall",
            },
        };
        if (t.Kind.HasFlag(TerrainKind.Difficult))
        {
            words.Add("difficult");
        }
        if (t.Kind.HasFlag(TerrainKind.Dangerous))
        {
            words.Add("dangerous");
        }
        if (t.Kind.HasFlag(TerrainKind.Impassable))
        {
            words.Add("impassable");
        }
        if (t.IsPlatform)
        {
            words.Add("high ground");
        }
        else if (t.Profile == TerrainProfile.Wall)
        {
            words.Add("blocks sight");
        }
        if (objective)
        {
            words.Add("Objective");
        }
        return string.Join(", ", words);
    }
}
