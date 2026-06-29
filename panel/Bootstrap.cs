using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TribesServerPanel.Auth;
using TribesServerPanel.Data;

namespace TribesServerPanel;

public static class Bootstrap
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration cfg)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Bootstrap");

        var db = sp.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        var roleMgr = sp.GetRequiredService<RoleManager<ApplicationRole>>();
        foreach (var r in Roles.All)
            if (!await roleMgr.RoleExistsAsync(r))
                await roleMgr.CreateAsync(new ApplicationRole(r));

        var userMgr = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var username = cfg["PANEL_ROOT_USERNAME"];
        var password = cfg["PANEL_ROOT_PASSWORD"];

        var anyRoot = (await userMgr.GetUsersInRoleAsync(Roles.Root)).Count > 0;
        if (anyRoot) return;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            log.LogWarning("No root user exists and PANEL_ROOT_USERNAME/PANEL_ROOT_PASSWORD are not set; cannot bootstrap. Set them and restart.");
            return;
        }

        if (await userMgr.FindByNameAsync(username) is not null) return;

        var user = new ApplicationUser
        {
            UserName = username,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        var result = await userMgr.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            log.LogError("Failed to seed root user: {Errors}", string.Join("; ", result.Errors.Select(e => e.Description)));
            return;
        }
        await userMgr.AddToRoleAsync(user, Roles.Root);
        db.AuditEntries.Add(new AuditEntry { Actor = "system", ActorRole = Roles.Root, Action = "bootstrap.seed_root", Target = username });
        await db.SaveChangesAsync();
        log.LogInformation("Seeded root user '{User}'.", username);
    }
}
