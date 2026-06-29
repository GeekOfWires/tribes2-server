namespace TribesServerPanel.Data;

/// <summary>
/// Single-row persisted server state. Until <see cref="Configured"/> is set by root
/// (first-time setup), the game does not run. <see cref="AutoStart"/> controls whether
/// the ASP.NET host launches the game automatically on startup.
/// </summary>
public class ServerSettings
{
    public int Id { get; set; } = 1; // singleton row
    public bool Configured { get; set; }
    public bool AutoStart { get; set; }
    public string? LaunchParams { get; set; } // optional override of the LAUNCH_PARAMS env
}
