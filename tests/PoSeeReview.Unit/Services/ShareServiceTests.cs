using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Moq;
using PoSeeReview.Client.Services;
using Xunit;

namespace PoSeeReview.Unit.Services;

/// <summary>
/// ShareService is the one share path: Web Share, then the clipboard, then a toast either way.
/// </summary>
public class ShareServiceTests
{
    private readonly Mock<IJSRuntime> _js = new();
    private readonly ToastService _toasts = new();
    private readonly ShareService _share;

    public ShareServiceTests()
    {
        // Minting the short link 404s, which is the "fall back to the canonical URL" path.
        var api = new ApiClient(new HttpClient(new NotFoundHandler()) { BaseAddress = new Uri("https://app.test/") });
        _share = new ShareService(_js.Object, api, new FakeNavigation(), _toasts, new FxService(_js.Object));
    }

    private void ShareReturns(string outcome) =>
        _js.Setup(x => x.InvokeAsync<string>("shareUtils.share", It.IsAny<object[]>())).ReturnsAsync(outcome);

    [Fact]
    public async Task ShareAsync_WhenShared_PassesThePlaceIdAndConfirms()
    {
        ShareReturns("shared");

        var result = await _share.ShareAsync("Title", "text", "https://app.test/s/abc", "place123");

        // The place id is what lets the JS side attach the image itself.
        Assert.Equal(ShareOutcome.Shared, result);
        Assert.Equal("Shared!", _toasts.Message);
        _js.Verify(x => x.InvokeAsync<string>(
            "shareUtils.share",
            It.Is<object[]>(args => args.Length == 4
                && (string)args[0] == "Title"
                && (string)args[2] == "https://app.test/s/abc"
                && (string)args[3] == "place123")), Times.Once);
    }

    [Theory]
    [InlineData("unsupported")]
    [InlineData(null)]   // interop threw
    public async Task ShareAsync_WithoutWebShare_CopiesTheLinkInstead(string? jsOutcome)
    {
        if (jsOutcome is null)
        {
            _js.Setup(x => x.InvokeAsync<string>("shareUtils.share", It.IsAny<object[]>()))
               .ThrowsAsync(new JSException("no Web Share API"));
        }
        else
        {
            ShareReturns(jsOutcome);
        }

        var result = await _share.ShareAsync("Title", "text", "https://app.test/s/abc");

        Assert.Equal(ShareOutcome.Copied, result);
        Assert.Equal("Link copied to clipboard", _toasts.Message);
        _js.Verify(x => x.InvokeAsync<Microsoft.JSInterop.Infrastructure.IJSVoidResult>(
            "shareUtils.copyToClipboard",
            It.Is<object[]>(args => (string)args[0] == "https://app.test/s/abc")), Times.Once);
    }

    [Fact]
    public async Task ShareAsync_WhenUserCancels_DoesNothingElse()
    {
        // A dismissed share sheet must NOT be mistaken for "no Web Share API", or the caller
        // silently copies a link the user just declined to share.
        ShareReturns("cancelled");

        var result = await _share.ShareAsync("Title", "text", "https://app.test/s/abc");

        Assert.Equal(ShareOutcome.Cancelled, result);
        Assert.Null(_toasts.Message);
        _js.Verify(x => x.InvokeAsync<Microsoft.JSInterop.Infrastructure.IJSVoidResult>(
            "shareUtils.copyToClipboard", It.IsAny<object[]>()), Times.Never);
    }

    [Fact]
    public async Task ShareComicAsync_WhenNoShortLink_SharesTheCanonicalComicUrl()
    {
        ShareReturns("shared");

        await _share.ShareComicAsync("place123", "Odd Diner", 87);

        _js.Verify(x => x.InvokeAsync<string>(
            "shareUtils.share",
            It.Is<object[]>(args => (string)args[2] == "https://app.test/comic/place123"
                && ((string)args[1]).Contains("87/100"))), Times.Once);
    }

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
    }

    private sealed class FakeNavigation : NavigationManager
    {
        public FakeNavigation() => Initialize("https://app.test/", "https://app.test/");
    }
}
