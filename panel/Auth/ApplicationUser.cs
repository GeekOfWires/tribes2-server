using Microsoft.AspNetCore.Identity;

namespace TribesServerPanel.Auth;

// Standard ASP.NET Core Identity user/role, stored via EF Core in the
// libSQL-compatible SQLite database. A user holds exactly one role.
public class ApplicationUser : IdentityUser
{
    public bool IsActive { get; set; } = true;
    public long CreatedAt { get; set; }

    // Additive capability, assignable on top of any base role: grants file editing
    // under GameData. root has this implicitly. Orthogonal to the rank-based roles.
    public bool IsDeveloper { get; set; }
}

public class ApplicationRole : IdentityRole
{
    public ApplicationRole() { }
    public ApplicationRole(string name) : base(name) { }
}
