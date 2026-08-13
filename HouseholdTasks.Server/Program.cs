using HouseholdTasks.Server.Auth;
using HouseholdTasks.Server.Data;
using HouseholdTasks.Server.Endpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);
if (File.Exists("/data/options.json"))
{
    builder.Configuration.AddJsonFile(
        "/data/options.json",
        optional: true,
        reloadOnChange: false);
}
// ---- Database (SQLite) ----
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

// ---- Authentication: cookie + Google ----
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        // API-friendly: return 401/403 instead of redirecting to a login HTML page.
        options.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    })
    .AddGoogle(options =>
    {
        options.ClientId =
            builder.Configuration["google_client_id"]
            ?? builder.Configuration["Authentication:Google:ClientId"]
            ?? throw new InvalidOperationException(
                "Google Client ID is not configured.");

        options.ClientSecret =
            builder.Configuration["google_client_secret"]
            ?? builder.Configuration["Authentication:Google:ClientSecret"]
            ?? throw new InvalidOperationException(
                "Google Client Secret is not configured.");

        options.SaveTokens = false;
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
});

builder.Services.AddScoped<IClaimsTransformation, FamilyMemberClaimsTransformer>();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto;

    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

// ---- Ensure DB exists ----
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}


app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapAccountEndpoints();
app.MapFamilyEndpoints();
app.MapTaskEndpoints();

app.MapFallbackToFile("index.html");

app.Run();
