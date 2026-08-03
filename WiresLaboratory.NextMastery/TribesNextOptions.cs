namespace WiresLaboratory.NextMastery;

/// <summary>
/// TribesNext server-side settings, mirroring the <c>$Host::TN::*</c> console variables the
/// community patch reads out of a ruleset's <c>prefs/serverPrefs.cs</c>.
/// </summary>
/// <remarks>
/// These names are taken from the prefs the shipped servers actually write, so a managed
/// backend can honour an operator's existing configuration untouched rather than inventing
/// a parallel one.
/// </remarks>
public sealed record TribesNextOptions
{
    /// <summary>Master server host (<c>$Host::TN::master</c>).</summary>
    public string Master { get; init; } = "master.tribesnext.com";

    /// <summary>Seconds between master heartbeats (<c>$Host::TN::beat</c>).</summary>
    public int HeartbeatSeconds { get; init; } = 3;

    /// <summary>Echo each heartbeat to the console (<c>$Host::TN::echo</c>).</summary>
    public bool EchoHeartbeat { get; init; } = true;

    /// <summary>
    /// Validates the values the same way an operator would expect the patch to behave:
    /// a non-empty master and a positive interval.
    /// </summary>
    public bool IsUsable => !string.IsNullOrWhiteSpace(Master) && HeartbeatSeconds > 0;
}
