namespace FactionPlayfieldKicker;

internal class PluginSettings
{
    public string BootMessage { get; set; } = "You are not allowed to enter this faction's playfield.";

    public int ImmunePermissionLevel { get; set; } = int.MaxValue;

    public string FallbackPlayfield { get; set; } = string.Empty;

    public int DistanceFromPlanet { get; set; } = 20_000;

    public Dictionary<string, int> FactionHomeWorlds { get; set; } = new();
}
