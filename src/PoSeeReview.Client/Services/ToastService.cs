namespace PoSeeReview.Client.Services;

/// <summary>The one transient confirmation, rendered by the <c>Toast</c> component in MainLayout.</summary>
public sealed class ToastService
{
    private int _version;

    public string? Message { get; private set; }

    public event Action? Changed;

    public void Show(string message)
    {
        var version = ++_version;
        Message = message;
        Changed?.Invoke();
        _ = ClearAfterAsync(version);
    }

    private async Task ClearAfterAsync(int version)
    {
        await Task.Delay(3000);

        // A newer toast owns the element now; its own timer clears it.
        if (version == _version)
        {
            Message = null;
            Changed?.Invoke();
        }
    }
}
