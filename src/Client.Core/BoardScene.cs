using System.Globalization;
using Rules.Core;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Client.Core;

// M7.4: the battlefield as the 3D view draws it, in inches, built only from the server's state. No Godot here: the
// Godot client turns this into shapes, so the same description serves placeholder shapes now and real art later.
//
// Terrain is drawn at the heights the rules' line of sight uses (SightHeights, U77; chosen by Jason in M7.4 Q2 and kept
// for line-of-sight stage L2). A test checks they stay equal, so what is drawn and what the rules see cannot drift
// apart. Line of sight and cover always come from the rules, never from the picture. A tower is drawn at its height.
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

    public bool Contains(double x, double y) => x >= X0 && x <= X1 && y >= Y0 && y <= Y1;
}

public sealed record ZoneShape(string Id, TableRect Area, string Label, string? SeatLetter = null)
{
    // Proposal G: the short label drawn at the zone's edge ("ZONE A"), or the zone's own id before a seat has it.
    public string Short => SeatLetter is { } letter ? $"ZONE {letter}" : $"ZONE {Id.ToUpperInvariant()}";
}

// Words carry every fact (profile, kind, high ground, Objective), so nothing depends on colour or shape alone.
public sealed record TerrainShape(string Id, TableRect Area, TerrainProfile Profile, double Height, bool Platform, bool Objective, string Label, Seat? Controller = null)
{
    // QoL H4: an Objective's compact mark, "◆2" for obj-2, with the letter of the seat that would control it if it were
    // scored now ("◆2 A"); the rules decide who that is (Rules.Core.Objectives.ControllerNow).
    public string Mark => $"{ObjectiveMark}{(Id.StartsWith("obj-", StringComparison.OrdinalIgnoreCase) ? Id[4..] : Id)}";

    public string MarkWithController => Controller is { } seat ? $"{Mark} {Words.SeatLetter(seat)}" : Mark;

    public const string ObjectiveMark = "◆";

    // Towers proposal, stage T4: a tower's ladders (empty for any other piece). Its Height is the tower's own height from
    // the rules (not a display convention), and its platform is reached only by these ladders.
    public IReadOnlyList<LadderShape> Ladders { get; init; } = [];

    // Trench-maps stage S2: the rectangles the piece is made of, in the rules' fixed order. Just Area for one rectangle;
    // Area is then the bounding box of the parts.
    public IReadOnlyList<TableRect> Parts { get; init; } = [Area];

    // Trench-maps stage S3: a trench's depth below the ground in inches (the rules' own depth), zero for any other piece.
    // A trench has nothing above the ground, so its Height is zero; it is drawn as a pit.
    public double Depth { get; init; }

    public bool Trench => Depth > 0;

    // Is the table point inside one of the piece's parts (edges included)?
    public bool Holds(double x, double y) => Parts.Any(p => p.Contains(x, y));

    public bool Tower => Ladders.Count > 0;

    // The short words drawn on a tower's platform: "TOWER 2.0"".
    public string TowerMark => $"TOWER {BoardScene.Inch(Height)}";
}

// A ladder on a tower's side: the point the rules climb at (U71) and the side it is on. Display only.
public sealed record LadderShape(string Id, TablePoint At, Side Side)
{
    public string SideWord => Side.ToString().ToLowerInvariant();
}

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
    bool Eligible = false,
    int BloodMarkers = 0)
{
    // Activated: the server's ActivatedThisTurn (the rules clear it at the next turn). Eligible: it can still activate
    // this turn (the activation phase, deployed, on the table, not activated), the same conditions the rules check for
    // ActivateModel apart from whose turn it is; for the seat to activate next they are exactly the units the rules offer
    // (TurnSequence.LegalCommands, a test checks). Both are display only: they never decide anything.
    public string Label => $"[{SeatLetter}] {Name}{(Active ? ", ACTIVE" : Activated ? ", activated" : "")}{(Status == ModelStatus.Standing ? "" : $", {Words.Status(Status)}")}";

    // Proposal G: the label drawn over the model on the battlefield: its short id ("A2"), then a mark when it has
    // activated this turn (✓) and when it is Down (↓). Its Blood Markers are the row of drops under it (visual pass
    // Stage 3, BloodDrops; before, "•2" in the label). The full name and state are in the panel beside the battlefield.
    public string ShortLabel(string activatedMark = "✓", string downMark = "↓") =>
        $"{Id.Value.ToUpperInvariant()}{(Activated && !Active ? " " + activatedMark : "")}{(Status == ModelStatus.Down ? " " + downMark : "")}";

    public const string MarkerMark = "•";
    public const string ActivatedMark = "✓";
    public const string DownMark = "↓";

    // How the battlefield draws the model's turn state (colour is never the only sign: the label says it too).
    public UnitLook Look => Active ? UnitLook.Active : Activated ? UnitLook.Activated : Eligible ? UnitLook.Eligible : UnitLook.Waiting;

    // One line for the selected model: what it is, where (in inches) and on which level.
    public string Text => $"[{SeatLetter}] {Name}: {Words.Status(Status)}{(Active ? ", ACTIVE" : "")}, at ({BoardScene.Inch(X)}, {BoardScene.Inch(Y)}), " +
        $"{Where}, base {BoardScene.Inch(Radius * 2)}.";

    // Towers proposal, stage T4: the tower the model stands on, if any (its Standing is then the tower's height).
    public string? TowerId { get; init; }

    // The model's type from the content ("Sharpshooter"), which picks its figure (graphics stage G3); null if unknown.
    public string? Type { get; init; }

    public string Where => TowerId is not null ? $"on the tower ({BoardScene.Inch(Standing)} up)" : HighGround ? "on high ground" : "on the ground";
}

// Trench Warfare's Mine Markers still on the table (p. 162). Known says what this seat knows: the attacker sees only
// markers; the defender, who picked the duds, sees which are duds and which are live. Display only.
public enum MineKnown
{
    Unknown,
    Dud,
    Live,
}

public sealed record MineShape(string Id, double X, double Y, double Radius, MineKnown Known)
{
    // The short mark drawn on the marker: "M3", with "dud" or "live" for the defender.
    public string Mark => $"M{Id[(Id.LastIndexOf('-') + 1)..]}{Known switch { MineKnown.Dud => " dud", MineKnown.Live => " live", _ => "" }}";

    public string Text => $"Mine Marker {Id} at ({BoardScene.Inch(X)}, {BoardScene.Inch(Y)}){Known switch { MineKnown.Dud => ", a dud (only you know)", MineKnown.Live => ", live (only you know)", _ => ", live or a dud (secret)" }}.";
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
    // QoL I5: the enemy pairs the rules say are engaged now (Rules.Core.Engagement.Pairs, decision D1), each drawn as a
    // short line between the two bases. Display only.
    public IReadOnlyList<EngagedPair> Links { get; init; } = [];

    // Trench Warfare: the Mine Markers on the table, and while they are still to be set up the Mine Placement Area.
    public IReadOnlyList<MineShape> Mines { get; init; } = [];

    public TableRect? MineArea { get; init; }

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
            // A tower is drawn at its own height, with its ladders (towers proposal, stage T4).
            return new TerrainShape(t.Id, TableRect.Of(t.Footprint.Bounds), t.Profile, t.Climb is { } climb ? TableRect.Inches(climb.Height) : t.IsTrench ? 0 : DisplayHeight.Of(t.Profile),
                t.IsPlatform, objective, TerrainLabel(t, objective), objective ? Rules.Core.Objectives.ControllerNow(state, t.Id) : null)
            {
                Ladders = t.Climb is { } c ? [.. c.Ladders.Select(l => new LadderShape(l.Id, TablePoint.Of(l.At), Climb.SideOf(t.Footprint.Bounds, l.At)!.Value))] : [],
                Parts = [.. t.Footprint.Parts.Select(TableRect.Of)],
                Depth = t.Depth is { } depth ? TableRect.Inches(depth) : 0,
            };
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
            // On a tower: the tower's own height (towers proposal, stage T4); on another platform: its display height.
            var tower = Towers.TowerAt(field, at);
            // In a trench: its floor, below the ground (trench-maps stage S3; the rules' own test, U81).
            var standing = tower?.Climb is { } climb ? TableRect.Inches(climb.Height)
                : level.Level == ModelLevel.High && field.Terrain.FirstOrDefault(t => t.Id == level.PlatformId) is { } platform ? DisplayHeight.Of(platform.Profile)
                : Trenches.Of(field, body) is not null ? -TableRect.Inches(field.TrenchDepth!.Value)
                : 0;
            models.Add(new ModelShape(model.Id, names.Of(model.Id), model.Seat, Words.SeatLetter(model.Seat),
                TableRect.Inches(body.Centre.X), TableRect.Inches(body.Centre.Y), TableRect.Inches(body.Radius),
                level.Level == ModelLevel.High, standing, model.Status, state.ActiveModel == model.Id, model.Seat == you,
                model.ActivatedThisTurn, CanStillActivate(state, model), model.BloodMarkers) { TowerId = tower?.Id, Type = names.TypeOf(model.Id) });
        }
        return new BoardScene(TableRect.Inches(field.Width), TableRect.Inches(field.Height), zones, terrain, models)
        {
            Links = [.. Engagement.Pairs(state).Where(p => models.Any(m => m.Id == p.One) && models.Any(m => m.Id == p.Two))],
            Mines = [.. state.Mines.Select(m => new MineShape(m.Id, TableRect.Inches(m.At.X), TableRect.Inches(m.At.Y), TableRect.Inches(Rules.Core.Geometry.Mines.MarkerRadius),
                !state.DudsKnown ? MineKnown.Unknown : state.Duds.Contains(m.Id) ? MineKnown.Dud : MineKnown.Live))],
            MineArea = state.Pending is { Kind: DecisionKind.LayMines } && state.Attacker is { } attacker
                && state.SeatFor(attacker.Opponent()).ZoneId is { } defending && state.SeatFor(attacker).ZoneId is { } attacking
                && Rules.Core.Geometry.Mines.PlacementArea(field, defending, attacking) is { } area ? TableRect.Of(area) : null,
        };
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

    // QoL H2 (key J): the next (or previous) of your units that may be chosen to activate now, in the same fixed order as
    // Next; null when none may. canChoose is the rules' own offer (Interaction.CanChoose).
    public ModelShape? NextEligible(ModelId? current, Func<ModelId, bool> canChoose, bool forward = true)
    {
        ArgumentNullException.ThrowIfNull(canChoose);
        var order = Models.Where(m => m.Yours && canChoose(m.Id)).OrderBy(m => m.Id.Value, StringComparer.Ordinal).ToList();
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

    // The Objective piece at a table point (inches), if any (QoL H2: a double-click centres on it).
    public TerrainShape? ObjectiveAt(double x, double y) => Terrain.FirstOrDefault(t => t.Objective && t.Area.Contains(x, y));

    public TerrainShape? Objective(string id) => Terrain.FirstOrDefault(t => t.Objective && t.Id == id);

    public static string Inch(double inches) => inches.ToString("0.0", CultureInfo.InvariantCulture) + "\"";

    private static double Sq(double v) => v * v;

    private static string TerrainLabel(TerrainBlock t, bool objective)
    {
        if (t.Climb is { } climb)
        {
            // Towers proposal, stage T4: what is accessible, in words.
            var sides = string.Join(" and ", climb.Ladders.Select(l => Climb.SideOf(t.Footprint.Bounds, l.At)!.Value).Order().Select(side => side.ToString().ToLowerInvariant()));
            return $"Tower, {Inch(TableRect.Inches(climb.Height))} high: models stand and fight on its platform, reached by its ladders ({sides} side{(climb.Ladders.Length > 1 ? "s" : "")}) or by climbing a side with a Risky roll; from the ground it blocks movement and sight";
        }
        if (t.Depth is { } depth)
        {
            // Trench-maps stage S3: what a trench does, in words (U80 to U83).
            return $"Trench, {Inch(TableRect.Inches(depth))} deep: its sides are crossed as open ground; a model more than half in it stands on the floor, hidden below the edge from far away and in cover while partly seen{(objective ? "; Objective" : "")}";
        }
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
