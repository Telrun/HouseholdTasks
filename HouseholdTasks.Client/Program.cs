using HouseholdTasks.Client;
using HouseholdTasks.Client.Auth;
using HouseholdTasks.Client.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Same-origin HttpClient — cookies flow automatically to the API on this host.
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
});

builder.Services.AddAuthorizationCore(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
});
builder.Services.AddScoped<AuthenticationStateProvider, CookieAuthenticationStateProvider>();
builder.Services.AddScoped(sp => (CookieAuthenticationStateProvider)sp.GetRequiredService<AuthenticationStateProvider>());
builder.Services.AddScoped<NotificationService>();

var host = builder.Build();

// Registering the service worker doesn't request notification permission or do anything
// visible — it just makes the app installable as a PWA (required on iOS before push can
// ever work, and generally good practice everywhere else too). Actually enabling
// notifications is a separate, explicit, user-initiated action (see MyTasks.razor).
using (var scope = host.Services.CreateAsyncScope())
{
    var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
    await notifications.RegisterServiceWorkerAsync();
}

await host.RunAsync();
