using FirebaseAdmin.Messaging;
using HouseholdTasks.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace HouseholdTasks.Server.Services;

/// <summary>
/// Sends push notifications to a family member's registered devices via Firebase Cloud
/// Messaging. Deliberately fails soft everywhere — a notification that doesn't go out
/// should never break the task/family-member action that triggered it (e.g. creating a
/// task must still succeed even if FCM is unreachable, misconfigured, or a token is
/// stale). Errors are logged, not thrown.
/// </summary>
public class PushNotificationSender
{
    private readonly AppDbContext _db;
    private readonly ILogger<PushNotificationSender> _logger;
    private readonly bool _isConfigured;

    public PushNotificationSender(AppDbContext db, ILogger<PushNotificationSender> logger, IConfiguration config)
    {
        _db = db;
        _logger = logger;
        // FirebaseApp.DefaultInstance is set up once at startup in Program.cs — if that
        // never happened (no service account configured), skip sending entirely rather
        // than throwing on every task creation.
        _isConfigured = FirebaseAdmin.FirebaseApp.DefaultInstance is not null;
    }

    public async Task SendToMemberAsync(int familyMemberId, string title, string body, string url = "/my-tasks")
    {
        if (!_isConfigured) return;

        var tokens = await _db.DeviceTokens
            .Where(d => d.FamilyMemberId == familyMemberId)
            .Select(d => d.Token)
            .ToListAsync();

        if (tokens.Count == 0) return;

        var message = new MulticastMessage
        {
            Tokens = tokens,
            Notification = new FirebaseAdmin.Messaging.Notification { Title = title, Body = body },
            Data = new Dictionary<string, string> { ["url"] = url },
            // Webpush-specific: without this, some browsers won't show a notification at
            // all when the tab isn't focused, since "urgency" defaults conservatively.
            Webpush = new WebpushConfig
            {
                Headers = new Dictionary<string, string> { ["Urgency"] = "high" }
            }
        };

        try
        {
            var response = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(message);

            if (response.FailureCount > 0)
            {
                // Prune tokens FCM says are no longer valid (uninstalled, permission
                // revoked, expired) so we stop trying to send to them every time.
                var deadTokens = new List<string>();
                for (var i = 0; i < response.Responses.Count; i++)
                {
                    if (!response.Responses[i].IsSuccess)
                    {
                        deadTokens.Add(tokens[i]);
                        _logger.LogWarning(
                            response.Responses[i].Exception,
                            "Push notification failed for one token; removing it.");
                    }
                }

                if (deadTokens.Count > 0)
                {
                    var toRemove = await _db.DeviceTokens
                        .Where(d => deadTokens.Contains(d.Token))
                        .ToListAsync();
                    _db.DeviceTokens.RemoveRange(toRemove);
                    await _db.SaveChangesAsync();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send push notification to family member {MemberId}.", familyMemberId);
        }
    }
}
