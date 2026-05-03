using Po.SeeReview.Shared.Dtos;

namespace Po.SeeReview.Client.Services;

/// <summary>
/// Singleton that broadcasts dev-session changes so the NavMenu can react
/// without re-initializing the layout component.
/// </summary>
public class DevSessionStateService
{
    private DevSessionDto? _current;

    public DevSessionDto? Current => _current;

    public event Action? OnChanged;

    public void Set(DevSessionDto? session)
    {
        _current = session;
        OnChanged?.Invoke();
    }

    public void Clear()
    {
        _current = null;
        OnChanged?.Invoke();
    }
}
