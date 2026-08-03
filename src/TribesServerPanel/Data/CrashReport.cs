namespace TribesServerPanel.Data;

/// <summary>
/// One record per unexpected game exit (access violation / unhandled fault), so
/// server hosts can report reproducible crashes for the container image to patch.
/// </summary>
public class CrashReport
{
    public long Id { get; set; }
    public long StartedAt { get; set; }            // unix seconds: when this game instance launched
    public long CrashedAt { get; set; }            // unix seconds: when it exited
    public int? ExitCode { get; set; }
    public string? FaultAddress { get; set; }      // e.g. 0x00555026
    public string? FaultInstruction { get; set; }  // e.g. cmp word ptr ss:[ebp+eax*1-0x29C], 0x01
    public string? Module { get; set; }            // e.g. Tribes2.exe
    public string? LaunchParams { get; set; }
    public string? Details { get; set; }           // console tail + CRASHLOG.TXT
}
