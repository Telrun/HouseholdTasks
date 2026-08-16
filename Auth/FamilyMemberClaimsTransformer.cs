using System.Security.Claims;
using HouseholdTasks.Server.Data;
using HouseholdTasks.Server.Data.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace HouseholdTasks.Server.Auth;

/// <summary>
/// Runs on every authenticated request. Looks up the FamilyMember behind the signed-in
/// email and stamps id/role claims so the rest of the app can trust ClaimsPrincipal
/// without re-querying the DB each time.
///
/// Access control itself does NOT happen here — by the time this runs, the cookie is
/// already issued. The actual gate ("is this email even allowed to sign in?") is in
/// Program.cs's Google OnCreatingTicket handler, which runs during the OAuth callback,
/// before any cookie exists. The one exception is the bootstrap admin email(s) listed in
/// InitialAdminEmails config, which get their FamilyMember row created here on first
/// login since nothing else would ever create it for them.
/// </summary>
public class FamilyMemberClaimsTransformer : IClaimsTransformation
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;

    public FamilyMemberClaimsTransformer(IServiceScopeFactory scopeFactory, IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _config = config;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
            return principal;

        if (principal.HasClaim(c => c.Type == "household_member_id"))
            return principal;

        var email = principal.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email))
            return principal;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var member = await db.FamilyMembers.FirstOrDefaultAsync(m => m.Email == email);
        if (member is null)
        {
            var initialAdmins = _config.GetSection("InitialAdminEmails").Get<string[]>() ?? Array.Empty<string>();
            if (!initialAdmins.Contains(email, StringComparer.OrdinalIgnoreCase))
            {
                // Shouldn't normally happen — OnCreatingTicket already blocked anyone who
                // isn't a known member or a bootstrap admin. If it does (e.g. their row
                // was deleted after this cookie was issued), leave the principal without
                // household_member_id/role claims: they stay "authenticated" but have no
                // linked member, so every permission check in the app treats them as no one.
                return principal;
            }

            member = new FamilyMember
            {
                Name = principal.FindFirstValue(ClaimTypes.Name) ?? email,
                Email = email,
                IsAdmin = true
            };
            db.FamilyMembers.Add(member);
            await db.SaveChangesAsync();
        }

        var identity = (ClaimsIdentity)principal.Identity!;
        identity.AddClaim(new Claim("household_member_id", member.Id.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Role, member.IsAdmin ? "Admin" : "Member"));
        identity.AddClaim(new Claim(ClaimTypes.Name, member.Name));

        return principal;
    }
}
