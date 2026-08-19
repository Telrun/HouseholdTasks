using Microsoft.JSInterop;

namespace HouseholdTasks.Client.Services;

public enum PushNotificationStatus
{
    Unsupported,
    IosNeedsInstall,
    Denied,
    NotRequested,
    Granted
}

/// <summary>
/// Thin C# wrapper around wwwroot/js/notifications.js. Kept deliberately dumb — all the
/// actual Firebase logic lives in JS, since the Firebase Web SDK has no first-class .NET
/// binding and re-implementing it in C#/WASM would be a lot of surface area for no benefit
/// over just calling the real JS SDK via interop.
/// </summary>
public class NotificationService : IDisposable, IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private DotNetObjectReference<NotificationService>? _selfRef;

    public event Action<string, string>? ForegroundNotificationReceived;

    public NotificationService(IJSRuntime js)
    {
        _js = js;
    }

    public Task RegisterServiceWorkerAsync() => _js.InvokeVoidAsync("hhNotify.registerServiceWorker").AsTask();

    public async Task<PushNotificationStatus> GetStatusAsync()
    {
        var status = await _js.InvokeAsync<string>("hhNotify.getNotificationStatus");
        return status switch
        {
            "ios-needs-install" => PushNotificationStatus.IosNeedsInstall,
            "denied" => PushNotificationStatus.Denied,
            "granted" => PushNotificationStatus.Granted,
            "not-requested" => PushNotificationStatus.NotRequested,
            _ => PushNotificationStatus.Unsupported
        };
    }

    public Task<string?> RequestPermissionAndGetTokenAsync() =>
        _js.InvokeAsync<string?>("hhNotify.requestPermissionAndGetToken").AsTask();

    public async Task StartListeningForForegroundMessagesAsync()
    {
        _selfRef ??= DotNetObjectReference.Create(this);
        await _js.InvokeVoidAsync("hhNotify.listenForForegroundMessages", _selfRef);
    }

    [JSInvokable]
    public void OnForegroundNotification(string title, string body)
    {
        ForegroundNotificationReceived?.Invoke(title, body);
    }

    // Synchronous fallback disposal to satisfy DI container requirements
    public void Dispose()
    {
        // Safely dispose the object reference synchronously if needed
        _selfRef?.Dispose();
        _selfRef = null;
    }

    // Asynchronous disposal path
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}