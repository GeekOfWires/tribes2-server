namespace TribesServerPanel.Services;

/// <summary>
/// Resolves and scopes filesystem access for the file browser/editor:
///   * Developers may read/write under GameData only.
///   * root may read/write anywhere in the container.
/// Paths are normalized (".." collapsed) before the scope check. Developers are
/// semi-trusted operators inside an already-isolated container; root is unrestricted.
/// </summary>
public sealed class FileAccess
{
    public string GameDataRoot { get; }

    // Text files we will store a revert snapshot for, and that Monaco can edit.
    public const long MaxEditableBytes = 5 * 1024 * 1024;   // refuse to open larger as text
    public const long MaxSnapshotBytes = 2 * 1024 * 1024;   // cap stored previous-content

    public FileAccess(IConfiguration cfg)
    {
        GameDataRoot = Path.GetFullPath(
            cfg["GAME_DIR"] ?? "/opt/wineprefix/drive_c/Dynamix/Tribes2/GameData");
    }

    public static string Canonical(string path) => Path.GetFullPath(path);

    public bool UnderGameData(string canonical) =>
        canonical == GameDataRoot ||
        canonical.StartsWith(GameDataRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    /// <summary>True if the byte sample looks like binary (NUL byte present).</summary>
    public static bool LooksBinary(ReadOnlySpan<byte> sample)
    {
        foreach (var b in sample) if (b == 0) return true;
        return false;
    }
}
