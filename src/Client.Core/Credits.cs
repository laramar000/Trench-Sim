namespace Client.Core;

// Stage M1 (main menu, 2026-10-28): the Credits screen's words. Every author and licence in client/assets/ASSETS.md is
// named here (CreditsTests checks that against the file), with the engine and runtime the client is built on.
public static class Credits
{
    public const string NoticeHeading = "Unofficial fan project";

    public static IReadOnlyList<(string Heading, string Text)> Entries { get; } =
    [
        ("Trenches", "A two-player skirmish game made by the Trench Sim project (Claude), with original art made for this project."),
        // Jason 2026-10-29: the game says plainly that it is an unofficial, non-profit fan project.
        (NoticeHeading, "Trenches is an unofficial, non-profit fan project, made for fun and given away free. It is not affiliated with, "
            + "endorsed, sponsored or approved by Trench Crusade, its creators (including Mike Franchina and Tuomas Pirinen) or its "
            + "publishers. Trench Crusade and its names, terms and marks belong to their owners; any used here are used only to "
            + "describe this fan project, and no claim to them is made. Trenches is not for sale. Please support the official game."),
        ("Figures", "Built on the MakeHuman base body by the MakeHuman team (CC0-1.0); pose, equipment and textures by the Trench Sim project (Claude)."),
        ("Font", "Pirata One by Rodrigo Fuenzalida and Nicolas Massi, under the SIL Open Font License 1.1 (OFL-1.1). The licence is in assets/ui/PirataOne-OFL.txt."),
        ("Ground, terrain and effects", "Original textures and models made for this project by the Trench Sim project (Claude)."),
        ("Engine", "Godot Engine (MIT licence), by Juan Linietsky, Ariel Manzur and the Godot Engine contributors."),
        ("Runtime", ".NET (MIT licence), by the .NET Foundation and contributors."),
    ];
}
