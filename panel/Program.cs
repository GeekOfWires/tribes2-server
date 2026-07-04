using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TribesServerPanel.Auth;
using TribesServerPanel.Data;
using TribesServerPanel.Services;
using TribesServerPanel.Tls;

namespace TribesServerPanel;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Environment variables are already a configuration source (no prefix), so
        // cfg["GAME_DIR"], cfg["SELF_SIGNED_CERT"], etc. read the container env directly.
        var cfg = builder.Configuration;

        // ---- TLS / Kestrel endpoints (self-signed | Let's Encrypt | plain HTTP) ----
        TlsConfigurator.Configure(builder);

        // ---- database (EF Core on the libSQL-compatible SQLite file) ----------------
        var dbPath = cfg["PANEL_DB_PATH"] ?? "/data/panel.db";
        var dataDir = Path.GetDirectoryName(Path.GetFullPath(dbPath))!;
        Directory.CreateDirectory(dataDir);
        builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));

        // Persist data-protection keys so auth cookies survive restarts/redeploys.
        var keysDir = cfg["DATAPROTECTION_DIR"] ?? Path.Combine(dataDir, "keys");
        Directory.CreateDirectory(keysDir);
        builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysDir));

        // ---- ASP.NET Core Identity --------------------------------------------------
        builder.Services
            .AddIdentity<ApplicationUser, ApplicationRole>(o =>
            {
                // Length-only policy (set every flag explicitly so there are no hidden
                // Identity defaults). Otherwise RequireDigit/RequireLowercase silently
                // reject an operator's ROOT_PASSWORD and no root user gets seeded.
                o.Password.RequiredLength = 8;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireDigit = false;
                o.User.RequireUniqueEmail = false;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        builder.Services.ConfigureApplicationCookie(o =>
        {
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.SlidingExpiration = true;
            o.ExpireTimeSpan = TimeSpan.FromHours(8);
            // API-friendly: return status codes instead of HTML redirects.
            o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
            o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
        });

        // ---- authorization: rank-based policies (role rank >= threshold) ------------
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(Roles.PolicyUser, p => p.RequireAssertion(c => MaxRank(c.User) >= Roles.Rank(Roles.User)))
            .AddPolicy(Roles.PolicyAdmin, p => p.RequireAssertion(c => MaxRank(c.User) >= Roles.Rank(Roles.Admin)))
            .AddPolicy(Roles.PolicySuperAdmin, p => p.RequireAssertion(c => MaxRank(c.User) >= Roles.Rank(Roles.SuperAdmin)))
            .AddPolicy(Roles.PolicyRoot, p => p.RequireAssertion(c => MaxRank(c.User) >= Roles.Rank(Roles.Root)));

        // ---- game supervisor (singleton + hosted worker service) --------------------
        builder.Services.AddSingleton<ConsoleHub>();
        builder.Services.AddSingleton<GameSupervisor>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<GameSupervisor>());

        // ---- file browser/editor scope resolver -------------------------------------
        builder.Services.AddSingleton<TribesServerPanel.Services.FileAccess>();

        // Allow large file uploads through the panel (mod packs, maps). Admin-only surface.
        builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = long.MaxValue);
        builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = null);

        var app = builder.Build();

        // ---- migrate + seed roles and the root user --------------------------------
        await Bootstrap.InitializeAsync(app.Services, cfg);

        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseWebSockets();           // root web terminal (PTY) rides on this
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapPanelEndpoints();
        app.MapFileEndpoints();
        app.MapTerminalEndpoints();
        app.MapFallbackToFile("index.html"); // SPA client-side routing

        await app.RunAsync();
    }

    // Highest rank across the principal's role claims (0 if none).
    private static int MaxRank(ClaimsPrincipal u) =>
        u.FindAll(ClaimTypes.Role).Select(c => Roles.Rank(c.Value)).DefaultIfEmpty(0).Max();
}
