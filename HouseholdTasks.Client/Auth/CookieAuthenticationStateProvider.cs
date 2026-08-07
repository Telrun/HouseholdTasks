using System.Net.Http.Json;
using System.Security.Claims;
using HouseholdTasks.Shared.Dtos;
using Microsoft.AspNetCore.Components.Authorization;

namespace HouseholdTasks.Client.Auth;

/// <summary>
/// The WASM app has no way to read the auth cookie itself (it's HttpOnly by design),
/// so it asks the server's /api/account/user endpoint who the cookie belongs to.
/// The browser sends the cookie automatically because client and server share an origin.
/// </summary>
public class CookieAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly HttpClient _http;
    public UserInfoDto CurrentUser { get; private set; } = new();

    public CookieAuthenticationStateProvider(HttpClient http)
    {
        _http = http;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        CurrentUser = new UserInfoDto();

        try
        {
            var user = await _http.GetFromJsonAsync<UserInfoDto>("api/account/user");
            if (user is not null) CurrentUser = user;
        }
        catch
        {
            // Server unreachable or not authenticated — treat as anonymous.
        }

        if (!CurrentUser.IsAuthenticated)
        {
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, CurrentUser.Name ?? CurrentUser.Email ?? "Unknown"),
            new(ClaimTypes.Email, CurrentUser.Email ?? string.Empty),
            new(ClaimTypes.Role, CurrentUser.IsAdmin ? "Admin" : "Member")
        };

        var identity = new ClaimsIdentity(claims, "google");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    public void NotifyUserChanged()
    {
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}
