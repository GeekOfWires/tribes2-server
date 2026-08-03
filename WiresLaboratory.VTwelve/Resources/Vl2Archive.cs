using System.IO.Compression;

namespace WiresLaboratory.VTwelve.Resources;

/// <summary>
/// A Tribes 2 <c>.vl2</c> volume.
/// </summary>
/// <remarks>
/// V12 volumes are PKZIP containers (verified against the shipped packs: local-file magic
/// <c>PK\x03\x04</c> with a valid end-of-central-directory record). The shipped packs store
/// entries with compression method 0 (stored), so reads are effectively raw slices — but we
/// go through <see cref="ZipArchive"/> anyway so a deflated entry still works.
/// <para>
/// Volume lookups are case-insensitive: the engine ran on Windows, so scripts reference
/// paths with inconsistent casing that a case-sensitive Linux host would otherwise miss.
/// </para>
/// </remarks>
public sealed class Vl2Archive : IDisposable
{
    private readonly ZipArchive _zip;
    private readonly Dictionary<string, ZipArchiveEntry> _entries;

    private Vl2Archive(string path, ZipArchive zip)
    {
        Path = path;
        _zip = zip;
        _entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in zip.Entries)
        {
            // Later duplicates win, matching the engine's "last mounted wins" behaviour.
            _entries[Normalize(e.FullName)] = e;
        }
    }

    /// <summary>Path this volume was opened from.</summary>
    public string Path { get; }

    /// <summary>Entry names in the volume, normalised to forward slashes.</summary>
    public IReadOnlyCollection<string> Entries => _entries.Keys;

    public static Vl2Archive Open(string path)
    {
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            return new Vl2Archive(path, new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false));
        }
        catch
        {
            fs.Dispose();
            throw;
        }
    }

    public bool Contains(string name) => _entries.ContainsKey(Normalize(name));

    /// <summary>Reads an entry in full, or returns null when it is not present.</summary>
    public byte[]? ReadAllBytes(string name)
    {
        if (!_entries.TryGetValue(Normalize(name), out var entry)) return null;
        using var s = entry.Open();
        using var ms = new MemoryStream(capacity: (int)Math.Min(entry.Length, int.MaxValue));
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static string Normalize(string name) => name.Replace('\\', '/').TrimStart('/');

    public void Dispose() => _zip.Dispose();
}
