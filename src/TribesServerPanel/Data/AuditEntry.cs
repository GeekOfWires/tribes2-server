namespace TribesServerPanel.Data;

public class AuditEntry
{
    public long Id { get; set; }
    public long Ts { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public string Actor { get; set; } = "";
    public string ActorRole { get; set; } = "";
    public string Action { get; set; } = "";
    public string? Target { get; set; }
    public string? Detail { get; set; }
    public bool Success { get; set; } = true;
}
