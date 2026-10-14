using Sim;

// dotnet run --project src/Sim -- --seed 42
// dotnet run --project src/Sim -- --seeds 1-20 [--melee | --content path] [--max-commands n] [--events]
var seedFrom = 1;
var seedTo = 1;
var path = Content.DefaultPath();
var maxCommands = Runner.DefaultMaxCommands;
var showEvents = false;
var hashesOnly = false;
var showLog = false;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--seed":
            seedFrom = seedTo = int.Parse(args[++i]);
            showLog = true;
            break;
        case "--seeds":
            var range = args[++i].Split('-');
            seedFrom = int.Parse(range[0]);
            seedTo = int.Parse(range.Length > 1 ? range[1] : range[0]);
            break;
        case "--melee":
            path = Content.MeleePath();
            break;
        case "--content":
            path = args[++i];
            break;
        case "--max-commands":
            maxCommands = int.Parse(args[++i]);
            break;
        case "--hashes":
            hashesOnly = true;
            break;
        case "--events":
            showEvents = true;
            break;
        default:
            Console.Error.WriteLine($"Unknown argument {args[i]}");
            return 2;
    }
}

var warbands = Content.Load(path);
var failures = 0;
for (var seed = seedFrom; seed <= seedTo; seed++)
{
    var result = Runner.Run(seed, warbands, maxCommands);
    var again = Runner.Run(seed, warbands, maxCommands);
    var repeatable = result.FinalHash == again.FinalHash && result.Log.Count == again.Log.Count;
    Console.WriteLine(hashesOnly ? $"{seed} {result.FinalHash}" : result.Describe() + (repeatable ? "" : "  NOT REPEATABLE"));
    if (showLog)
    {
        foreach (var line in result.Log.Select(Narrator.Describe).Where(l => l is not null))
        {
            Console.WriteLine(line);
        }
    }
    if (showEvents)
    {
        foreach (var e in result.Log)
        {
            Console.WriteLine("    " + e.GetType().Name);
        }
    }
    if (!result.Terminal || !repeatable)
    {
        failures++;
    }
}
Console.WriteLine(failures == 0
    ? $"All {seedTo - seedFrom + 1} simulation(s) reached a terminal state."
    : $"{failures} simulation(s) FAILED.");
return failures == 0 ? 0 : 1;
