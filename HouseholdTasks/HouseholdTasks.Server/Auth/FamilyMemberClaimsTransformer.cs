using System.Security.Claims;
using HouseholdTasks.Server.Data;
using HouseholdTasks.Server.Data.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace HouseholdTasks.Server.Auth;

/// <summary>
/// After a successful Google login, makes sure the signed-in email exists in the
/// FamilyMembers table (auto-registers first-time known emails as non-admin, unless
/// they're in the InitialAdminEmails config list), and stamps role/id claims so
/// the rest of the app can trust ClaimsPrincipal without re-querying the DB.
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
            var isInitialAdmin = initialAdmins.Contains(email, StringComparer.OrdinalIgnoreCase);

            member = new FamilyMember
            {
                Name = principal.FindFirstValue(ClaimTypes.Name) ?? email,
                Email = email,
                IsAdmin = isInitialAdmin
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
