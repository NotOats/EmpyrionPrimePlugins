namespace FactionPlayfieldKicker;

internal class PluginSettings
{
    public string BootMessage { get; set; } = "You are not allowed to enter this faction's playfield.";

    public int ImmunePermissionLevel { get; set; } = int.MaxValue;

    public Dictionary<string, int> FactionHomeWorlds { get; set; } = new();
}
