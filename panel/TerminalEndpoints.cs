using System.Security.Claims;
using TribesServerPanel.Auth;
using TribesServerPanel.Data;
using TribesServerPanel.Services;

namespace TribesServerPanel;

public static class TerminalEndpoints
{
    public static void MapTerminalEndpoints(this WebApplication app)
    {
        // Interactive container shell over a WebSocket-backed PTY. root only.
        app.Map("/api/terminal/ws", async (HttpContext ctx, AppDbContext db, ClaimsPrincipal u) =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest)
                return Results.BadRequest(new { error = "expected a websocket request" });

            var actor = u.Identity?.Name ?? "?";
            db.AuditEntries.Add(new AuditEntry { Actor = actor, ActorRole = Roles.Root, Action = "terminal.open" });
            await db.SaveChangesAsync();

            using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
            try { await TerminalSession.RunAsync(ws, ctx.RequestAborted); }
            finally
            {
                db.AuditEntries.Add(new AuditEntry { Actor = actor, ActorRole = Roles.Root, Action = "terminal.close" });
                await db.SaveChangesAsync();
            }
            return Results.Empty;
        }).RequireAuthorization(Roles.PolicyRoot);
    }
}
