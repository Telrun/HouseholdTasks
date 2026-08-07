using System.Security.Claims;
using HouseholdTasks.Shared.Dtos;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;

namespace HouseholdTasks.Server.Endpoints;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/account");

        // Kicks off Google sign-in. Called by a plain <a href> from the Blazor client
        // (not fetch) so the browser follows the redirect chain to Google and back.
        group.MapGet("/login", (string? returnUrl, HttpContext ctx) =>
        {
            var redirectUri = string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl;
            return Results.Challenge(
                new AuthenticationProperties { RedirectUri = redirectUri },
                new[] { GoogleDefaults.AuthenticationScheme });
        });

        group.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok();
        });

        group.MapGet("/user", (ClaimsPrincipal user) =>
        {
            if (user.Identity?.IsAuthenticated != true)
            {
                return Results.Ok(new UserInfoDto { IsAuthenticated = false });
            }

            var idClaim = user.FindFirstValue("household_member_id");
            return Results.Ok(new UserInfoDto
            {
                IsAuthenticated = true,
                FamilyMemberId = idClaim is null ? null : int.Parse(idClaim),
                Name = user.FindFirstValue(ClaimTypes.Name),
                Email = user.FindFirstValue(ClaimTypes.Email),
                IsAdmin = user.IsInRole("Admin")
            });
        });
    }
}
