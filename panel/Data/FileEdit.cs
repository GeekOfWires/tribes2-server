namespace TribesServerPanel.Data;

/// <summary>
/// One record per filesystem mutation made through the panel (Developer edits under
/// GameData; root edits anywhere). Stores the PRE-change snapshot so root can revert.
/// </summary>
public class FileEdit
{
    public long Id { get; set; }
    public long Ts { get; set; }                 // unix seconds
    public string Actor { get; set; } = "";
    public string ActorRole { get; set; } = "";
    public string Path { get; set; } = "";       // absolute container path
    public string Action { get; set; } = "";     // edit | create | delete | revert
    public bool IsDirectory { get; set; }

    public bool PreviousExisted { get; set; }    // did the path exist before this change?
    public string? PreviousContent { get; set; } // pre-change text (null if new, binary, or too large)
    public bool PreviousTruncated { get; set; }  // previous content too large to store -> revert unavailable
    public long NewSize { get; set; }            // bytes after the change (0 for delete)

    public bool Reverted { get; set; }           // a later revert undid this record
}
