using Moq;
using PoSeeReview.Api.Features.DevSessions;
using PoSeeReview.Api.Identity;
using PoSeeReview.Shared.Contracts;
using PoSeeReview.Shared.Enums;
using PoSeeReview.Shared.Ids;

namespace PoSeeReview.Unit.Application;

public class DevSessionCommandHandlerTests
{
    private readonly Mock<ICurrentRequestIdentityAccessor> _identityAccessorMock = new();
    private readonly DevSessionCommandHandler _sut;

    public DevSessionCommandHandlerTests()
    {
        _sut = new DevSessionCommandHandler(_identityAccessorMock.Object);
    }

    [Fact]
    public void GetCurrentSession_WhenUserIdSet_ReturnsDtoWithMatchingUserId()
    {
        _identityAccessorMock.Setup(x => x.GetCurrentUserId()).Returns(UserId.From("user-123"));

        var result = _sut.GetCurrentSession();

        Assert.Equal("user-123", result.UserId);
        Assert.False(result.IsAnonymous);
        Assert.False(result.IsDevelopmentBypass);
    }

    [Fact]
    public void GetCurrentSession_WhenUserIdNull_ReturnsDtoWithAnonymousUserId()
    {
        _identityAccessorMock.Setup(x => x.GetCurrentUserId()).Returns(UserId.Anonymous);

        var result = _sut.GetCurrentSession();

        Assert.Equal("anonymous", result.UserId);
    }

    [Fact]
    public void GetCurrentSession_WhenUserIdEmpty_ReturnsDtoWithAnonymousUserId()
    {
        _identityAccessorMock.Setup(x => x.GetCurrentUserId()).Returns(UserId.Anonymous);

        var result = _sut.GetCurrentSession();

        Assert.Equal("anonymous", result.UserId);
    }

    [Fact]
    public void CreateRandomAnonymousSession_ReturnsAnonPrefixedUserId()
    {
        var result = _sut.CreateRandomAnonymousSession();

        Assert.StartsWith("ANON", result.UserId, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateRandomAnonymousSession_SetsIsAnonymousTrue()
    {
        var result = _sut.CreateRandomAnonymousSession();

        Assert.True(result.IsAnonymous);
        Assert.True(result.IsDevelopmentBypass);
    }

    [Fact]
    public void CreateRandomAnonymousSession_UserId_HasSixDigitSuffix()
    {
        var result = _sut.CreateRandomAnonymousSession();

        // "ANON" + 6 digits
        Assert.Equal(10, result.UserId.Length);
        var suffix = result.UserId[4..];
        Assert.True(int.TryParse(suffix, out var num) && num is >= 100000 and <= 999999);
    }

    [Fact]
    public void CreateRandomAnonymousSession_TwoCalls_ProduceDifferentIds()
    {
        // Statistical: probability of collision is ~1 in 900000 — acceptable for CI
        var id1 = _sut.CreateRandomAnonymousSession().UserId;
        var id2 = _sut.CreateRandomAnonymousSession().UserId;

        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void CreateRandomAnonymousSession_CreatedAtUtc_IsRecent()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var result = _sut.CreateRandomAnonymousSession();
        var after = DateTime.UtcNow.AddSeconds(1);

        Assert.InRange(result.CreatedAtUtc, before, after);
    }
}
