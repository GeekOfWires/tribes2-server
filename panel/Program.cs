using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TribesServerPanel;
using TribesServerPanel.Auth;
using TribesServerPanel.Data;
using TribesServerPanel.Services;
using TribesServerPanel.Tls;

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
        o.Password.RequiredLength = 8;
        o.Password.RequireNonAlphanumeric = false;
        o.Password.RequireUppercase = false;
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
static int MaxRank(ClaimsPrincipal u) =>
    u.FindAll(ClaimTypes.Role).Select(c => Roles.Rank(c.Value)).DefaultIfEmpty(0).Max();

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Roles.PolicyUser, p => p.RequireAssertion(c => MaxRank(c.User) >= 10))
    .AddPolicy(Roles.PolicyAdmin, p => p.RequireAssertion(c => MaxRank(c.User) >= 20))
    .AddPolicy(Roles.PolicySuperAdmin, p => p.RequireAssertion(c => MaxRank(c.User) >= 30))
    .AddPolicy(Roles.PolicyRoot, p => p.RequireAssertion(c => MaxRank(c.User) >= 40));

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

app.Run();
