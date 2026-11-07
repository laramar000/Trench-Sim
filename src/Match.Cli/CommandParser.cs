using System.Globalization;
using Rules.Core.Commands;
using Rules.Core.Geometry;
using Rules.Core.State;

namespace Match.Cli;

// Turns a typed line into a command for the player's own seat. Positions are in inches: "12.5,3".
public static class CommandParser
{
    public const string Help = """
        zone <id>                 choose a deployment zone     attacker One|Two      Trench Warfare: who attacks
        deploy <model>            deploy (no battlefield)      place <model> <x,y> [x|y] set up at a point
        first One|Two             choose who activates first   order One|Two         Morale order
        activate <model>          start an activation          end                   end the activation
        move <model> <x,y>...     move along waypoints         dash <model> <x,y>... dash
        shoot <model> <target>    ranged attack                markers <n>           spend Blood Markers
        charge <model> <target>   charge                       fight <model> <target> melee attack
        retreat <model> <x,y>...  retreat                      free <model>          take a free attack
        decline                   decline free attacks         friendly <model>      choose the friendly victim
        morale BecomeShaken|SoundRetreat                       answer a failed Morale Check
        scatter <x,y>             where a missed SCATTER attack lands
        blast <model> <x,y>       aim a BLAST weapon at a point
        turn <model> x|y          turn an oval base to lie along that axis (free)
        bless <model> <target>    God is With Us! ACTION
        loud <model>              Loudspeakers ACTION         puppet <model> <target> <x,y>   Puppet Master ACTION
        """;

    public static Command Parse(string line, Seat seat)
    {
        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            throw new FormatException("Empty command.");
        }
        var args = words[1..];
        return words[0].ToLowerInvariant() switch
        {
            "zone" => new ChooseDeploymentZone(seat, One(args)),
            "deploy" => new DeployModel(seat, Model(One(args))),
            "place" => new PlaceModel(seat, Model(Arg(args, 0)), Point(Arg(args, 1))) { Axis = args.Length > 2 ? Enum.Parse<BaseAxis>(args[2], ignoreCase: true) : BaseAxis.Y },
            "first" => new ChooseFirstActivation(seat, SeatName(One(args))),
            "attacker" => new ChooseAttacker(seat, SeatName(One(args))),
            "order" => new ChooseMoraleOrder(seat, SeatName(One(args))),
            "activate" => new ActivateModel(seat, Model(One(args))),
            "end" => new EndActivation(seat),
            "move" => new MoveModel(seat, Model(Arg(args, 0)), [.. args[1..].Select(Point)]),
            "dash" => new DashModel(seat, Model(Arg(args, 0)), [.. args[1..].Select(Point)]),
            "retreat" => new RetreatModel(seat, Model(Arg(args, 0)), [.. args[1..].Select(Point)]),
            "shoot" => new Shoot(seat, Model(Arg(args, 0)), Model(Arg(args, 1))),
            "charge" => new ChargeModel(seat, Model(Arg(args, 0)), Model(Arg(args, 1))) { CarryOn = args.Length > 2 && args[2].Equals("carry", StringComparison.OrdinalIgnoreCase) },
            "fight" => new FightModel(seat, Model(Arg(args, 0)), Model(Arg(args, 1))),
            "markers" => new SpendMarkers(seat, int.Parse(Arg(args, 0), CultureInfo.InvariantCulture)) { Bloodbath = args.Length > 1 && args[1].Equals("bloodbath", StringComparison.OrdinalIgnoreCase) },
            "free" => new FreeFight(seat, Model(One(args))),
            "decline" => new DeclineFreeFights(seat),
            "friendly" => new ChooseFriendlyTarget(seat, Model(One(args))),
            "scatter" => new ScatterAttack(seat, Point(One(args))),
            "bless" => new TakeSelfAction(seat, Model(Arg(args, 0)), SelfActionKind.GodIsWithUs) { Target = Model(Arg(args, 1)) },
            "loud" => new TakeSelfAction(seat, Model(One(args)), SelfActionKind.Loudspeakers),
            "puppet" => new TakeSelfAction(seat, Model(Arg(args, 0)), SelfActionKind.PuppetMaster) { Target = Model(Arg(args, 1)), Direction = Point(Arg(args, 2)) },
            "turn" => new TurnBase(seat, Model(Arg(args, 0)), Enum.Parse<BaseAxis>(Arg(args, 1), ignoreCase: true)),
            "guard" => new ChooseBodyguard(seat, Model(One(args))),
            "noguard" => new ChooseBodyguard(seat, null),
            "talisman" => new UseTalisman(seat),
            "keep" => new DeclineTalisman(seat),
            "blast" => new ShootPoint(seat, Model(Arg(args, 0)), Point(Arg(args, 1))),
            "morale" => new ResolveFailedMorale(seat, Enum.Parse<MoraleFailureChoice>(One(args))),
            _ => throw new FormatException($"Unknown command '{words[0]}'. Type 'help'."),
        };
    }

    private static string One(string[] args) => args.Length == 1 ? args[0] : throw new FormatException("Expected one argument.");

    private static string Arg(string[] args, int index) => index < args.Length ? args[index] : throw new FormatException("Missing argument.");

    private static ModelId Model(string text) => new(text);

    private static Seat SeatName(string text) => Enum.Parse<Seat>(text, ignoreCase: true);

    private static Point2 Point(string text)
    {
        var parts = text.Split(',');
        if (parts.Length != 2)
        {
            throw new FormatException($"'{text}' is not a point; write x,y in inches.");
        }
        return new Point2(Inches(parts[0]), Inches(parts[1]));
    }

    private static Length Inches(string text) =>
        Length.FromThousandths((long)Math.Round(decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture) * 1000m));
}
