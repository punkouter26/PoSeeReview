namespace PoSeeReview.Api.Abstractions;

/// <summary>
/// Marker for mock/fake service implementations. Every active registration is surfaced
/// through <c>/diag/mock-status</c>, which drives the client's "USING MOCK DATA" banner
/// (NET_RULES 6.5). Register the mock instance as <see cref="IMockable"/> alongside its
/// service interface.
/// </summary>
public interface IMockable;
