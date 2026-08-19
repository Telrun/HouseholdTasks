using System.Security.Claims;
using HouseholdTasks.Server.Data;
using HouseholdTasks.Server.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace HouseholdTasks.Server.Endpoints;

public record RegisterTokenRequest(string Token, string Platform);

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/notifications").RequireAuthorization();

        // Called once the client has a fresh FCM token (see notifications.js /
        // NotificationService.cs). Upserts by token: if this exact token is already
        // registered (same device re-registering, e.g. after a token refresh), just
        // updates LastSeenUtc and — if the token somehow moved to a different signed-in
        // family member (shared device) — reassigns it to whoever is asking now.
        group.MapPost("/register-token", async (RegisterTokenRequest req, AppDbContext db, ClaimsPrincipal user) =>
        {
            if (string.IsNullOrWhiteSpace(req.Token))
                return Results.BadRequest("Token is required.");

            var memberId = GetMemberId(user);
            if (memberId == 0)
                return Results.Unauthorized();

            var existing = await db.DeviceTokens.FirstOrDefaultAsync(d => d.Token == req.Token);
            if (existing is not null)
            {
                existing.FamilyMemberId = memberId;
                existing.Platform = req.Platform;
                existing.LastSeenUtc = DateTime.UtcNow;
            }
            else
            {
                db.DeviceTokens.Add(new DeviceToken
                {
                    FamilyMemberId = memberId,
                    Token = req.Token,
                    Platform = req.Platform
                });
            }

            await db.SaveChangesAsync();
            return Results.Ok();
        });

        // Best-effort cleanup — called when a user explicitly turns notifications off on
        // this device. Not calling this isn't a problem either way: FCM will eventually
        // report the token as invalid once it truly stops being usable, and
        // PushNotificationSender already prunes those automatically.
        group.MapDelete("/register-token", async (string token, AppDbContext db) =>
        {
            var existing = await db.DeviceTokens.FirstOrDefaultAsync(d => d.Token == token);
            if (existing is not null)
            {
                db.DeviceTokens.Remove(existing);
                await db.SaveChangesAsync();
            }
            return Results.Ok();
        });
    }

    private static int GetMemberId(ClaimsPrincipal user)
    {
        var claim = user.FindFirstValue("household_member_id");
        return claim is null ? 0 : int.Parse(claim);
    }
}
