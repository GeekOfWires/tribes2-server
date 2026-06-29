namespace TribesServerPanel.Auth;

/// <summary>
/// Fixed role set with an integer rank. Authorization is a single rank comparison
/// (rank >= required), so higher roles inherit lower-role capabilities.
/// </summary>
public static class Roles
{
    public const string User = "User";
    public const string Admin = "Admin";
    public const string SuperAdmin = "SuperAdmin";
    public const string Root = "root";

    public static readonly string[] All = { User, Admin, SuperAdmin, Root };

    public static int Rank(string? role) => role switch
    {
        User => 10,
        Admin => 20,
        SuperAdmin => 30,
        // root is the unbounded ceiling: nothing can ever outrank it, and there is room
        // to insert new roles below it. (Mirror this in the SPA's RANK map.)
        Root => int.MaxValue,
        _ => 0,
    };

    public static string Display(string? role) => role switch
    {
        Root => "root",
        SuperAdmin => "Super Admin",
        _ => role ?? "Unknown",
    };

    // Authorization policy names.
    public const string PolicyUser = "RequireUser";
    public const string PolicyAdmin = "RequireAdmin";
    public const string PolicySuperAdmin = "RequireSuperAdmin";
    public const string PolicyRoot = "RequireRoot";
}
