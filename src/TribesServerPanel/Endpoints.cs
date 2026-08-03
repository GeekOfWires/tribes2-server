using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TribesServerPanel.Auth;
using TribesServerPanel.Data;
using TribesServerPanel.Services;

namespace TribesServerPanel;

public static class Endpoints
{
    public record LoginDto(string Username, string Password);
    public record CommandDto(string Cmd);
    public record CreateUserDto(string Username, string Password, string Role);
    public record SetRoleDto(string Role);
    public record SetActiveDto(bool Active);
    public record SetDeveloperDto(bool Enabled);
    public record PasswordDto(string Password);
    public record CompleteConfigDto(bool AutoStart, string? LaunchParams, string? Ruleset);
    public record AutoStartDto(bool Enabled);
    public record RulesetDto(string? Ruleset);
    public record EditorFontDto(string? Font);

    private static (string name, string role) Actor(ClaimsPrincipal u) =>
        (u.Identity?.Name ?? "?", u.FindFirstValue(ClaimTypes.Role) ?? Roles.User);

    private static async Task Audit(AppDbContext db, ClaimsPrincipal u, string action, string? target = null, string? detail = null, bool ok = true)
    {
        var (name, role) = Actor(u);
        db.AuditEntries.Add(new AuditEntry { Actor = name, ActorRole = role, Action = action, Target = target, Detail = detail, Success = ok });
        await db.SaveChangesAsync();
    }

    public static void MapPanelEndpoints(this WebApplication app)
    {
        // ---- account -------------------------------------------------------
        var account = app.MapGroup("/api/account");
        account.MapPost("/login", async (LoginDto dto, SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users) =>
        {
            var user = await users.FindByNameAsync(dto.Username ?? "");
            if (user is null || !user.IsActive) return Results.Unauthorized();
            var check = await signIn.CheckPasswordSignInAsync(user, dto.Password ?? "", lockoutOnFailure: false);
            if (!check.Succeeded) return Results.Unauthorized();
            await signIn.SignInAsync(user, isPersistent: false);
            var role = (await users.GetRolesAsync(user)).FirstOrDefault() ?? Roles.User;
            return Results.Ok(new { user.UserName, role, rank = Roles.Rank(role), isDeveloper = user.IsDeveloper });
        });
        account.MapPost("/logout", async (SignInManager<ApplicationUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.Ok();
        }).RequireAuthorization();
        account.MapGet("/me", async (ClaimsPrincipal u, UserManager<ApplicationUser> users) =>
        {
            var (name, role) = Actor(u);
            var me = await users.GetUserAsync(u);
            return Results.Ok(new
            {
                userName = name,
                role,
                rank = Roles.Rank(role),
                isDeveloper = me?.IsDeveloper ?? false,
                editorFont = me?.EditorFont,
            });
        }).RequireAuthorization();

        // Personal display setting: each user sets their own editor font. Null/empty
        // restores the bundled default. Not audited — it changes nothing on the server.
        account.MapPost("/editor-font", async (EditorFontDto dto, ClaimsPrincipal u,
                                               UserManager<ApplicationUser> users) =>
        {
            var me = await users.GetUserAsync(u);
            if (me is null) return Results.Unauthorized();

            var font = (dto.Font ?? "").Trim();
            // The value ends up in a Google Fonts URL and a CSS font-family, so keep it to
            // letters/digits/spaces/hyphens rather than trusting the client's validation.
            if (font.Length > 0 && !IsValidFontFamily(font))
                return Results.BadRequest(new { error = "invalid font family" });

            me.EditorFont = font.Length == 0 ? null : font;
            var res = await users.UpdateAsync(me);
            return res.Succeeded
                ? Results.Ok()
                : Results.BadRequest(new { error = string.Join("; ", res.Errors.Select(e => e.Description)) });
        }).RequireAuthorization();

        // ---- live console (SSE), viewable by any authenticated user --------
        app.MapGet("/api/console/stream", async (HttpContext ctx, ConsoleHub hub, CancellationToken ct) =>
        {
            ctx.Response.Headers.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache, no-transform";
            ctx.Response.Headers["X-Accel-Buffering"] = "no";

            var (id, reader) = hub.Subscribe();
            try
            {
                foreach (var line in hub.Snapshot(200)) await WriteSse(ctx, line);
                await ctx.Response.Body.FlushAsync(ct);
                while (!ct.IsCancellationRequested)
                {
                    var readTask = reader.WaitToReadAsync(ct).AsTask();
                    var done = await Task.WhenAny(readTask, Task.Delay(15000, ct));
                    if (done == readTask && readTask.Result)
                        while (reader.TryRead(out var line)) await WriteSse(ctx, line);
                    else
                        await ctx.Response.WriteAsync(": keepalive\n\n", ct);
                    await ctx.Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException) { /* client disconnected */ }
            finally { hub.Unsubscribe(id); }
        }).RequireAuthorization(Roles.PolicyUser);

        // ---- server lifecycle ---------------------------------------------
        var server = app.MapGroup("/api/server");
        server.MapGet("/status", (GameSupervisor g) => Results.Ok(g.Status())).RequireAuthorization(Roles.PolicyUser);

        server.MapPost("/restart", async (GameSupervisor g, AppDbContext db, ClaimsPrincipal u) =>
        {
            await g.RestartAsync(); await Audit(db, u, "server.restart");
            return Results.Ok();
        }).RequireAuthorization(Roles.PolicyAdmin);

        server.MapPost("/start", async (GameSupervisor g, AppDbContext db, ClaimsPrincipal u) =>
        {
            await g.StartAsync(); await Audit(db, u, "server.start");
            return Results.Ok();
        }).RequireAuthorization(Roles.PolicyAdmin);

        // Emergency force-restart of the game is a Super Admin power.
        server.MapPost("/force-restart", async (GameSupervisor g, AppDbContext db, ClaimsPrincipal u) =>
        {
            await g.ForceRestartAsync(); await Audit(db, u, "server.force_restart");
            return Results.Ok();
        }).RequireAuthorization(Roles.PolicySuperAdmin);

        server.MapPost("/stop", async (GameSupervisor g, AppDbContext db, ClaimsPrincipal u) =>
        {
            await g.StopAsync(); await Audit(db, u, "server.stop");
            return Results.Ok();
        }).RequireAuthorization(Roles.PolicySuperAdmin);

        server.MapPost("/command", async (CommandDto dto, GameSupervisor g, AppDbContext db, ClaimsPrincipal u) =>
        {
            var cmd = (dto.Cmd ?? "").Trim();
            if (cmd.Length == 0) return Results.BadRequest(new { error = "empty command" });
            var ok = await g.SendCommandAsync(cmd);
            await Audit(db, u, "console.command", detail: cmd, ok: ok);
            return ok ? Results.Ok() : Results.StatusCode(StatusCodes.Status502BadGateway);
        }).RequireAuthorization(Roles.PolicySuperAdmin);

        // ---- first-time configuration + Auto-Start (persisted in SQLite) ---
        var config = app.MapGroup("/api/config");

        config.MapGet("/", async (AppDbContext db, IConfiguration appCfg) =>
        {
            var s = await db.ServerSettings.FindAsync(1);
            return Results.Ok(new
            {
                configured = s?.Configured ?? false,
                autoStart = s?.AutoStart ?? false,
                launchParams = s?.LaunchParams,
                defaultLaunchParams = appCfg["LAUNCH_PARAMS"] ?? "-online -dedicated",
                ruleset = s?.Ruleset,
                defaultRuleset = NormalizeRuleset(appCfg["SERVER_RULESET"]),
            });
        }).RequireAuthorization(Roles.PolicyUser);

        // root completes first-time setup: marks configured, sets AutoStart + params, starts now.
        config.MapPost("/complete", async (CompleteConfigDto dto, AppDbContext db, GameSupervisor g, ClaimsPrincipal u) =>
        {
            var s = await db.ServerSettings.FindAsync(1);
            if (s is null) { s = new ServerSettings { Id = 1 }; db.ServerSettings.Add(s); }
            if (s.Configured) return Results.BadRequest(new { error = "already configured" });
            s.Configured = true;
            s.AutoStart = dto.AutoStart;
            s.LaunchParams = string.IsNullOrWhiteSpace(dto.LaunchParams) ? null : dto.LaunchParams.Trim();
            s.Ruleset = NormalizeRuleset(dto.Ruleset);
            await db.SaveChangesAsync();
            g.MarkConfigured();
            g.SetLaunchParams(s.LaunchParams);
            g.SetRuleset(s.Ruleset);
            await g.StartAsync();
            await Audit(db, u, "config.complete", detail: $"autoStart={dto.AutoStart}; ruleset={s.Ruleset}; params={s.LaunchParams}");
            return Results.Ok();
        }).RequireAuthorization(Roles.PolicyRoot);

        // root toggles the persisted Auto-Start flag (governs launch on host startup).
        config.MapPost("/auto-start", async (AutoStartDto dto, AppDbContext db, ClaimsPrincipal u) =>
        {
            var s = await db.ServerSettings.FindAsync(1);
            if (s is null || !s.Configured) return Results.BadRequest(new { error = "complete first-time setup first" });
            s.AutoStart = dto.Enabled;
            await db.SaveChangesAsync();
            await Audit(db, u, dto.Enabled ? "config.auto_start_on" : "config.auto_start_off");
            return Results.Ok();
        }).RequireAuthorization(Roles.PolicyRoot);

        // root changes the ruleset/mod (-mod param); takes effect on the next (re)start.
        config.MapPost("/ruleset", async (RulesetDto dto, AppDbContext db, GameSupervisor g, ClaimsPrincipal u) =>
        {
            var s = await db.ServerSettings.FindAsync(1);
            if (s is null || !s.Configured) return Results.BadRequest(new { error = "complete first-time setup first" });
            s.Ruleset = NormalizeRuleset(dto.Ruleset);
            await db.SaveChangesAsync();
            g.SetRuleset(s.Ruleset);
            await Audit(db, u, "config.ruleset", detail: s.Ruleset.Length == 0 ? "base" : s.Ruleset);
            return Results.Ok(new { ruleset = s.Ruleset });
        }).RequireAuthorization(Roles.PolicyRoot);

        // Discover installed rulesets: top-level GameData folders that contain a scripts/ dir
        // (base + any baked mod like Classic/Construction + anything uploaded). The panel shows
        // these as suggestions while still allowing a brand-new ruleset name to be typed.
        config.MapGet("/rulesets", (TribesServerPanel.Services.FileAccess fa) =>
        {
            var found = new List<string>();
            try
            {
                foreach (var d in Directory.EnumerateDirectories(fa.GameDataRoot))
                    if (Directory.Exists(Path.Combine(d, "scripts")))
                        found.Add(Path.GetFileName(d));
            }
            catch { /* GameData missing -> empty */ }
            if (!found.Any(n => n.Equals("base", StringComparison.OrdinalIgnoreCase))) found.Add("base");
            var ordered = found.Distinct()
                .OrderBy(n => n.Equals("base", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(n => n, StringComparer.OrdinalIgnoreCase);
            return Results.Ok(ordered);
        }).RequireAuthorization(Roles.PolicyUser);

        // root edits serverprefs.cs for the chosen ruleset; the editor saves via /api/files/save.
        // Path: GameData/<base|ruleset>/prefs/serverprefs.cs (the prefs dir is created if missing).
        config.MapGet("/serverprefs", (string? ruleset, TribesServerPanel.Services.FileAccess fa) =>
        {
            var dir = NormalizeRuleset(ruleset);
            dir = dir.Length == 0 ? "base" : dir;
            var prefsDir = Path.Combine(fa.GameDataRoot, dir, "prefs");
            var path = Path.Combine(prefsDir, "serverprefs.cs");
            try { Directory.CreateDirectory(prefsDir); } catch { /* best effort */ }
            var exists = File.Exists(path);
            var content = exists ? File.ReadAllText(path) : "";
            return Results.Ok(new { path, content, exists });
        }).RequireAuthorization(Roles.PolicyRoot);

        // ---- panel/container lifecycle (root only) -------------------------
        app.MapPost("/api/panel/shutdown", async (IHostApplicationLifetime life, AppDbContext db, ClaimsPrincipal u) =>
        {
            await Audit(db, u, "panel.force_shutdown");
            // Stopping the host exits the container; the restart policy decides relaunch.
            _ = Task.Run(async () => { await Task.Delay(500); life.StopApplication(); });
            return Results.Ok(new { stopping = true });
        }).RequireAuthorization(Roles.PolicyRoot);

        // ---- user management (root only) ----------------------------------
        var usersApi = app.MapGroup("/api/users").RequireAuthorization(Roles.PolicyRoot);

        usersApi.MapGet("/", async (UserManager<ApplicationUser> users) =>
        {
            var list = new List<object>();
            foreach (var x in users.Users.ToList())
            {
                var role = (await users.GetRolesAsync(x)).FirstOrDefault() ?? Roles.User;
                list.Add(new { id = x.Id, username = x.UserName, role, isActive = x.IsActive, isDeveloper = x.IsDeveloper });
            }
            return Results.Ok(list);
        });

        usersApi.MapPost("/", async (CreateUserDto dto, UserManager<ApplicationUser> users, AppDbContext db, ClaimsPrincipal u) =>
        {
            if (!Roles.All.Contains(dto.Role)) return Results.BadRequest(new { error = "invalid role" });
            var user = new ApplicationUser { UserName = dto.Username, IsActive = true, CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
            var res = await users.CreateAsync(user, dto.Password ?? "");
            if (!res.Succeeded) return Results.BadRequest(new { error = string.Join("; ", res.Errors.Select(e => e.Description)) });
            await users.AddToRoleAsync(user, dto.Role);
            await Audit(db, u, "user.create", dto.Username, $"role={dto.Role}");
            return Results.Ok();
        });

        usersApi.MapPost("/{id}/role", async (string id, SetRoleDto dto, UserManager<ApplicationUser> users, AppDbContext db, ClaimsPrincipal u) =>
        {
            if (!Roles.All.Contains(dto.Role)) return Results.BadRequest(new { error = "invalid role" });
            var user = await users.FindByIdAsync(id);
            if (user is null) return Results.NotFound();
            var current = (await users.GetRolesAsync(user)).FirstOrDefault();
            if (current == Roles.Root && dto.Role != Roles.Root && await IsLastRoot(users))
                return Results.BadRequest(new { error = "cannot demote the last root" });
            if (current is not null) await users.RemoveFromRoleAsync(user, current);
            await users.AddToRoleAsync(user, dto.Role);
            await Audit(db, u, "user.set_role", user.UserName, $"{current} -> {dto.Role}");
            return Results.Ok();
        });

        usersApi.MapPost("/{id}/active", async (string id, SetActiveDto dto, UserManager<ApplicationUser> users, AppDbContext db, ClaimsPrincipal u) =>
        {
            var user = await users.FindByIdAsync(id);
            if (user is null) return Results.NotFound();
            if (!dto.Active && (await users.GetRolesAsync(user)).Contains(Roles.Root) && await IsLastRoot(users))
                return Results.BadRequest(new { error = "cannot deactivate the last root" });
            user.IsActive = dto.Active;
            await users.UpdateAsync(user);
            await users.UpdateSecurityStampAsync(user); // invalidate existing sessions
            await Audit(db, u, dto.Active ? "user.activate" : "user.deactivate", user.UserName);
            return Results.Ok();
        });

        usersApi.MapPost("/{id}/developer", async (string id, SetDeveloperDto dto, UserManager<ApplicationUser> users, AppDbContext db, ClaimsPrincipal u) =>
        {
            var user = await users.FindByIdAsync(id);
            if (user is null) return Results.NotFound();
            user.IsDeveloper = dto.Enabled;
            await users.UpdateAsync(user);
            await Audit(db, u, dto.Enabled ? "user.developer_on" : "user.developer_off", user.UserName);
            return Results.Ok();
        });

        usersApi.MapPost("/{id}/password", async (string id, PasswordDto dto, UserManager<ApplicationUser> users, AppDbContext db, ClaimsPrincipal u) =>
        {
            var user = await users.FindByIdAsync(id);
            if (user is null) return Results.NotFound();
            var token = await users.GeneratePasswordResetTokenAsync(user);
            var res = await users.ResetPasswordAsync(user, token, dto.Password ?? "");
            if (!res.Succeeded) return Results.BadRequest(new { error = string.Join("; ", res.Errors.Select(e => e.Description)) });
            await Audit(db, u, "user.reset_password", user.UserName);
            return Results.Ok();
        });

        usersApi.MapDelete("/{id}", async (string id, UserManager<ApplicationUser> users, AppDbContext db, ClaimsPrincipal u) =>
        {
            var user = await users.FindByIdAsync(id);
            if (user is null) return Results.NotFound();
            if (user.UserName == u.Identity?.Name) return Results.BadRequest(new { error = "cannot delete your own account" });
            if ((await users.GetRolesAsync(user)).Contains(Roles.Root) && await IsLastRoot(users))
                return Results.BadRequest(new { error = "cannot delete the last root" });
            await users.DeleteAsync(user);
            await Audit(db, u, "user.delete", user.UserName);
            return Results.Ok();
        });

        // ---- audit log (Super Admin+) -------------------------------------
        app.MapGet("/api/audit", async (AppDbContext db) =>
        {
            var rows = await db.AuditEntries.OrderByDescending(a => a.Id).Take(300).ToListAsync();
            return Results.Ok(rows.Select(a => new { a.Ts, a.Actor, a.ActorRole, a.Action, a.Target, a.Detail, a.Success }));
        }).RequireAuthorization(Roles.PolicySuperAdmin);

        // ---- crash reports (read-only; Admin+) ----------------------------
        app.MapGet("/api/crashes", async (AppDbContext db) =>
        {
            var rows = await db.Crashes.OrderByDescending(c => c.Id).Take(200).ToListAsync();
            return Results.Ok(rows.Select(c => new
            {
                c.StartedAt, c.CrashedAt, c.ExitCode, c.FaultAddress,
                c.FaultInstruction, c.Module, c.LaunchParams, c.Details,
            }));
        }).RequireAuthorization(Roles.PolicyAdmin);
    }

    // "" / "base" (any case) => no mod. Anything else => the mod/ruleset folder name.
    private static string NormalizeRuleset(string? r)
    {
        r = r?.Trim();
        return string.IsNullOrEmpty(r) || r.Equals("base", StringComparison.OrdinalIgnoreCase) ? "" : r;
    }

    private static async Task<bool> IsLastRoot(UserManager<ApplicationUser> users)
        => (await users.GetUsersInRoleAsync(Roles.Root)).Count(x => x.IsActive) <= 1;

    // A Google Fonts family name: letters, digits, spaces and hyphens only. The value is
    // interpolated into a stylesheet URL and a CSS font-family in the browser, so quotes,
    // semicolons and angle brackets must never get through.
    private static bool IsValidFontFamily(string family) =>
        family.Length <= 48
        && char.IsLetterOrDigit(family[0])
        && family.All(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-');

    private static async Task WriteSse(HttpContext ctx, string line)
        => await ctx.Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {line}\n\n"));
}
