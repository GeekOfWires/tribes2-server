using Microsoft.AspNetCore.Identity;

namespace TribesServerPanel.Auth;

// Standard ASP.NET Core Identity user/role, stored via EF Core in the
// libSQL-compatible SQLite database. A user holds exactly one role.
public class ApplicationUser : IdentityUser
{
    public bool IsActive { get; set; } = true;
    public long CreatedAt { get; set; }
}

public class ApplicationRole : IdentityRole
{
    public ApplicationRole() { }
    public ApplicationRole(string name) : base(name) { }
}
