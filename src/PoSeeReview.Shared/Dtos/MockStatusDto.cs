namespace PoSeeReview.Shared.Dtos;

/// <summary>
/// Indicates whether mock/fake services are currently active.
/// When true, the client should display "USING MOCK DATA" in the nav bar.
/// </summary>
public sealed class MockStatusDto
{
    /// <summary>
    /// True when mock services (in-memory fakes) are active instead of real Azure/AI services.
    /// </summary>
    public bool IsMockActive { get; init; }

    /// <summary>
    /// Type names of the active IMockable registrations.
    /// </summary>
    public IReadOnlyList<string> ActiveMocks { get; init; } = [];
}
