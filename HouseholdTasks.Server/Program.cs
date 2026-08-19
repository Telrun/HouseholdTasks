using HouseholdTasks.Server.Auth;
using HouseholdTasks.Server.Data;
using HouseholdTasks.Server.Endpoints;
using HouseholdTasks.Server.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;
using Google.Apis.Auth.OAuth2;
using System.Security.Claims;

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

        options.CallbackPath = "/signin-google";
        options.SaveTokens = false;

        // Gatekeeping happens here, before a cookie is ever issued — not after. An email
        // that isn't already a FamilyMember (or isn't the bootstrap admin email) never
        // gets an authenticated session at all.
        options.Events.OnCreatingTicket = async context =>
        {
            var email = context.Principal?.FindFirstValue(System.Security.Claims.ClaimTypes.Email);
            if (string.IsNullOrWhiteSpace(email))
            {
                context.Fail("access_denied: Google did not return an email address.");
                return;
            }

            var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var isKnown = await db.FamilyMembers.AnyAsync(m => m.Email == email);

            if (!isKnown)
            {
                var initialAdmins = context.HttpContext.RequestServices
                    .GetRequiredService<IConfiguration>()
                    .GetSection("InitialAdminEmails").Get<string[]>() ?? Array.Empty<string>();
                isKnown = initialAdmins.Contains(email, StringComparer.OrdinalIgnoreCase);
            }

            if (!isKnown)
            {
                context.Fail($"access_denied: {email} is not a registered family member.");
            }
        };

        options.Events.OnRemoteFailure = context =>
        {
            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("GoogleAuth");

            var isAccessDenied = context.Failure?.Message?.StartsWith("access_denied") == true;
            if (isAccessDenied)
            {
                logger.LogWarning("Google sign-in blocked: {Message}", context.Failure!.Message);
                context.Response.Redirect("/?accessDenied=true");
            }
            else
            {
                logger.LogError(context.Failure, "Google sign-in failed.");
                context.Response.Redirect("/?loginError=" + Uri.EscapeDataString(context.Failure?.Message ?? "unknown"));
            }

            context.HandleResponse();
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
});

builder.Services.AddScoped<IClaimsTransformation, FamilyMemberClaimsTransformer>();
builder.Services.AddScoped<PushNotificationSender>();

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

// ---- Firebase Admin (push notifications) — entirely optional ----
// If no service account is configured, FirebaseApp.DefaultInstance stays null and
// PushNotificationSender silently no-ops on every send rather than the app failing to
// start. Two ways to provide the credential, same pattern as the Google OAuth secrets:
// a raw JSON string (HA add-on option / user-secret) or a path to a mounted file.
//var firebaseServiceAccountJson = app.Configuration["Firebase"];
var firebaseServiceAccountPath =
    app.Configuration["firebase_service_account_path"] ?? app.Configuration["Firebase:ServiceAccountPath"];

var firebaseSection = app.Configuration.GetSection("Firebase");
if (firebaseSection.Exists())
{
    // Convert the section back to a JSON string dynamically
    var jsonString = System.Text.Json.JsonSerializer.Serialize(firebaseSection.GetChildren().ToDictionary(x => x.Key, x => x.Value));

    FirebaseAdmin.FirebaseApp.Create(new FirebaseAdmin.AppOptions
    {
        Credential = GoogleCredential.FromJson(jsonString)
    });
}
else if (!string.IsNullOrWhiteSpace(firebaseServiceAccountPath) && File.Exists(firebaseServiceAccountPath))
{
    FirebaseAdmin.FirebaseApp.Create(new FirebaseAdmin.AppOptions
    {
        Credential = GoogleCredential.FromFile(firebaseServiceAccountPath)
    });
    app.Logger.LogInformation("Firebase Admin initialized from {Path}.", firebaseServiceAccountPath);
}
else
{
    app.Logger.LogWarning(
        "No Firebase service account configured — push notifications are disabled. " +
        "Set firebase_service_account_json (or _path) to enable them.");
}

// ---- Ensure DB exists, then bring existing databases up to date ----
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("SchemaMigrator");
    await HouseholdTasks.Server.Data.SchemaMigrator.RunAsync(db, logger);
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

app.Use(async (context, next) =>
{
    if (context.Request.Path == "/signin-google")
    {
        Console.WriteLine(
            $"GOOGLE CALLBACK: {context.Request.Scheme}://{context.Request.Host}{context.Request.Path}{context.Request.QueryString}");
    }

    await next();
});

app.UseAuthentication();
app.UseAuthorization();

app.MapAccountEndpoints();
app.MapFamilyEndpoints();
app.MapTaskEndpoints();
app.MapNotificationEndpoints();

app.MapGet("/Error", (HttpContext ctx) =>
{
    var feature = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
    var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("UnhandledException");
    logger.LogError(feature?.Error, "Unhandled exception.");
    return Results.Problem("Something went wrong. Check the server logs for details.");
}).AllowAnonymous();

app.MapFallbackToFile("index.html");

app.Run();
