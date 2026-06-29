using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TribesServerPanel.Auth;
using TribesServerPanel.Data;
using TribesServerPanel.Services;
using FileAccess = TribesServerPanel.Services.FileAccess;   // disambiguate from System.IO.FileAccess

namespace TribesServerPanel;

public static class FileEndpoints
{
    public record SaveDto(string Path, string Content);
    public record CreateDto(string Path, bool IsDir);
    public record PathDto(string Path);

    public static void MapFileEndpoints(this WebApplication app)
    {
        // Any authenticated user passes the gate; scope (Developer under GameData / root
        // anywhere) is enforced per-request against the resolved canonical path.
        var files = app.MapGroup("/api/files").RequireAuthorization(Roles.PolicyUser);

        files.MapGet("/list", async (string? path, ClaimsPrincipal u, UserManager<ApplicationUser> um, FileAccess fa) =>
        {
            var a = await Resolve(path ?? fa.GameDataRoot, u, um, fa);
            if (!a.ok) return a.deny;
            if (!Directory.Exists(a.canon)) return Results.NotFound(new { error = "not a directory" });

            var entries = new List<object>();
            foreach (var d in Directory.EnumerateDirectories(a.canon))
            {
                var di = new DirectoryInfo(d);
                entries.Add(new { name = di.Name, isDir = true, size = 0L, mtime = new DateTimeOffset(di.LastWriteTimeUtc).ToUnixTimeSeconds() });
            }
            foreach (var f in Directory.EnumerateFiles(a.canon))
            {
                var fi = new FileInfo(f);
                entries.Add(new { name = fi.Name, isDir = false, size = fi.Length, mtime = new DateTimeOffset(fi.LastWriteTimeUtc).ToUnixTimeSeconds() });
            }
            var parent = Path.GetDirectoryName(a.canon);
            // developers may not navigate above GameData
            var parentAllowed = parent is not null && (a.isRoot || fa.UnderGameData(parent));
            return Results.Ok(new { path = a.canon, parent = parentAllowed ? parent : null, gameDataRoot = fa.GameDataRoot, entries });
        });

        files.MapGet("/read", async (string path, ClaimsPrincipal u, UserManager<ApplicationUser> um, FileAccess fa) =>
        {
            var a = await Resolve(path, u, um, fa);
            if (!a.ok) return a.deny;
            if (Directory.Exists(a.canon)) return Results.BadRequest(new { error = "path is a directory" });
            if (!File.Exists(a.canon)) return Results.NotFound(new { error = "not found" });

            var len = new FileInfo(a.canon).Length;
            if (len > FileAccess.MaxEditableBytes)
                return Results.Ok(new { path = a.canon, tooLarge = true, size = len });

            var bytes = await File.ReadAllBytesAsync(a.canon);
            if (FileAccess.LooksBinary(bytes.AsSpan(0, Math.Min(bytes.Length, 8000))))
                return Results.Ok(new { path = a.canon, isBinary = true, size = len });

            return Results.Ok(new { path = a.canon, content = Encoding.UTF8.GetString(bytes), size = len });
        });

        files.MapPost("/save", async (SaveDto dto, ClaimsPrincipal u, UserManager<ApplicationUser> um, FileAccess fa, AppDbContext db) =>
        {
            var a = await Resolve(dto.Path, u, um, fa);
            if (!a.ok) return a.deny;
            if (Directory.Exists(a.canon)) return Results.BadRequest(new { error = "path is a directory" });
            var parent = Path.GetDirectoryName(a.canon);
            if (parent is null || !Directory.Exists(parent)) return Results.BadRequest(new { error = "parent directory does not exist" });

            var existed = File.Exists(a.canon);
            var (prev, trunc) = existed ? await Snapshot(a.canon) : (null, false);
            var data = Encoding.UTF8.GetBytes(dto.Content ?? "");
            await File.WriteAllBytesAsync(a.canon, data);

            await Record(db, a, existed ? "edit" : "create", isDir: false, existed, prev, trunc, data.LongLength);
            return Results.Ok(new { saved = true, size = data.LongLength });
        });

        files.MapPost("/create", async (CreateDto dto, ClaimsPrincipal u, UserManager<ApplicationUser> um, FileAccess fa, AppDbContext db) =>
        {
            var a = await Resolve(dto.Path, u, um, fa);
            if (!a.ok) return a.deny;
            if (File.Exists(a.canon) || Directory.Exists(a.canon)) return Results.BadRequest(new { error = "already exists" });
            var parent = Path.GetDirectoryName(a.canon);
            if (parent is null || !Directory.Exists(parent)) return Results.BadRequest(new { error = "parent directory does not exist" });

            if (dto.IsDir) Directory.CreateDirectory(a.canon);
            else await File.WriteAllBytesAsync(a.canon, Array.Empty<byte>());

            await Record(db, a, "create", dto.IsDir, previousExisted: false, prev: null, trunc: false, newSize: 0);
            return Results.Ok(new { created = true });
        });

        files.MapPost("/delete", async (PathDto dto, ClaimsPrincipal u, UserManager<ApplicationUser> um, FileAccess fa, AppDbContext db) =>
        {
            var a = await Resolve(dto.Path, u, um, fa);
            if (!a.ok) return a.deny;
            if (a.canon == fa.GameDataRoot) return Results.BadRequest(new { error = "refusing to delete GameData root" });

            if (Directory.Exists(a.canon))
            {
                if (Directory.EnumerateFileSystemEntries(a.canon).Any())
                    return Results.BadRequest(new { error = "directory not empty" });
                Directory.Delete(a.canon);
                await Record(db, a, "delete", isDir: true, previousExisted: true, prev: null, trunc: false, newSize: 0);
                return Results.Ok(new { deleted = true });
            }
            if (!File.Exists(a.canon)) return Results.NotFound(new { error = "not found" });

            var (prev, trunc) = await Snapshot(a.canon);
            File.Delete(a.canon);
            await Record(db, a, "delete", isDir: false, previousExisted: true, prev, trunc, newSize: 0);
            return Results.Ok(new { deleted = true });
        });

        // ---- file-change audit + revert (root only) ----------------------------
        var edits = app.MapGroup("/api/files/edits").RequireAuthorization(Roles.PolicyRoot);

        edits.MapGet("/", async (AppDbContext db) =>
        {
            var rows = await db.FileEdits.OrderByDescending(e => e.Id).Take(300).ToListAsync();
            return Results.Ok(rows.Select(e => new
            {
                e.Id, e.Ts, e.Actor, e.ActorRole, e.Path, e.Action, e.IsDirectory,
                e.PreviousExisted, e.NewSize, e.Reverted,
                canRevert = !e.PreviousTruncated && e.Action != "revert",
            }));
        });

        edits.MapPost("/{id:long}/revert", async (long id, ClaimsPrincipal u, UserManager<ApplicationUser> um, FileAccess fa, AppDbContext db) =>
        {
            var rec = await db.FileEdits.FindAsync(id);
            if (rec is null) return Results.NotFound();
            if (rec.PreviousTruncated) return Results.BadRequest(new { error = "previous content was too large to snapshot; revert unavailable" });

            // re-check that the actor performing the revert may write the target path
            var a = await Resolve(rec.Path, u, um, fa);
            if (!a.ok) return a.deny;

            // capture current state so the revert is itself auditable/undoable
            var nowExists = File.Exists(rec.Path);
            var (curPrev, curTrunc) = nowExists ? await Snapshot(rec.Path) : (null, false);

            if (rec.IsDirectory)
            {
                if (rec.PreviousExisted) Directory.CreateDirectory(rec.Path);
                else if (Directory.Exists(rec.Path))
                {
                    if (Directory.EnumerateFileSystemEntries(rec.Path).Any())
                        return Results.BadRequest(new { error = "cannot revert: directory is no longer empty" });
                    Directory.Delete(rec.Path);
                }
            }
            else if (rec.PreviousExisted)
            {
                var parent = Path.GetDirectoryName(rec.Path);
                if (parent is not null) Directory.CreateDirectory(parent);
                await File.WriteAllTextAsync(rec.Path, rec.PreviousContent ?? "");
            }
            else if (File.Exists(rec.Path))
            {
                File.Delete(rec.Path);
            }

            rec.Reverted = true;
            await Record(db, a, "revert", rec.IsDirectory, previousExisted: nowExists, prev: curPrev, trunc: curTrunc,
                         newSize: rec.PreviousExisted ? (rec.PreviousContent?.Length ?? 0) : 0,
                         detail: $"revert of #{rec.Id}");
            return Results.Ok(new { reverted = true });
        });
    }

    // ------------------------------------------------------------------ helpers
    private readonly record struct Access(bool ok, string canon, bool isRoot, bool isDev, string actor, string role, IResult deny);

    private static async Task<Access> Resolve(string path, ClaimsPrincipal u, UserManager<ApplicationUser> um, FileAccess fa)
    {
        IResult Deny(string m, int code = StatusCodes.Status403Forbidden) =>
            Results.Json(new { error = m }, statusCode: code);

        if (string.IsNullOrWhiteSpace(path)) return new(false, "", false, false, "", "", Deny("path required", 400));
        string canon;
        try { canon = FileAccess.Canonical(path); }
        catch { return new(false, "", false, false, "", "", Deny("invalid path", 400)); }

        var appUser = await um.GetUserAsync(u);
        var roles = appUser is null ? new List<string>() : (List<string>)await um.GetRolesAsync(appUser);
        var isRoot = roles.Contains(Roles.Root);
        var isDev = appUser?.IsDeveloper ?? false;
        var actor = u.Identity?.Name ?? "?";
        var role = roles.FirstOrDefault() ?? Roles.User;

        if (fa.UnderGameData(canon))
        {
            if (!(isDev || isRoot)) return new(false, canon, isRoot, isDev, actor, role, Deny("Developer or root required"));
        }
        else if (!isRoot)
        {
            return new(false, canon, isRoot, isDev, actor, role, Deny("root required for files outside GameData"));
        }
        return new(true, canon, isRoot, isDev, actor, role, Results.Ok());
    }

    private static async Task<(string? content, bool truncated)> Snapshot(string path)
    {
        var len = new FileInfo(path).Length;
        if (len > FileAccess.MaxSnapshotBytes) return (null, true);
        var bytes = await File.ReadAllBytesAsync(path);
        if (FileAccess.LooksBinary(bytes.AsSpan(0, Math.Min(bytes.Length, 8000)))) return (null, true);
        return (Encoding.UTF8.GetString(bytes), false);
    }

    private static async Task Record(AppDbContext db, Access a, string action, bool isDir,
        bool previousExisted, string? prev, bool trunc, long newSize, string? detail = null)
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        db.FileEdits.Add(new FileEdit
        {
            Ts = ts, Actor = a.actor, ActorRole = a.role, Path = a.canon, Action = action,
            IsDirectory = isDir, PreviousExisted = previousExisted, PreviousContent = prev,
            PreviousTruncated = trunc, NewSize = newSize,
        });
        // mirror a summary into the cross-cutting audit log (Super Admin+ visibility)
        db.AuditEntries.Add(new AuditEntry
        {
            Actor = a.actor, ActorRole = a.role, Action = $"file.{action}", Target = a.canon, Detail = detail, Success = true,
        });
        await db.SaveChangesAsync();
    }
}
