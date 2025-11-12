using Microsoft.JSInterop;
using Moq;
using Po.SeeReview.Client.Services;
using Xunit;

namespace Po.SeeReview.UnitTests.Services;

/// <summary>
/// Unit tests for ShareService - handles social media sharing via Web Share API
/// </summary>
public class ShareServiceTests
{
    private readonly Mock<IJSRuntime> _mockJsRuntime;
    private readonly ShareService _shareService;

    public ShareServiceTests()
    {
        _mockJsRuntime = new Mock<IJSRuntime>();
        _shareService = new ShareService(_mockJsRuntime.Object);
    }

    [Fact]
    public async Task ShareComicAsync_WithValidData_ShouldInvokeWebShareAPI()
    {
        // Arrange
        var title = "Strange Restaurant Comic";
        var text = "Check out this weird review comic!";
        var url = "https://poseereviews.com/comic/test123";

        _mockJsRuntime
            .Setup(x => x.InvokeAsync<bool>(
                "shareUtils.share",
                It.IsAny<object[]>()))
            .ReturnsAsync(true);

        // Act
        var result = await _shareService.ShareComicAsync(title, text, url);

        // Assert
        Assert.True(result);
        _mockJsRuntime.Verify(
            x => x.InvokeAsync<bool>(
                "shareUtils.share",
                It.Is<object[]>(args =>
                    args.Length == 3 &&
                    args[0].ToString() == title &&
                    args[1].ToString() == text &&
                    args[2].ToString() == url)),
            Times.Once);
    }

    [Fact]
    public async Task ShareComicAsync_WhenWebShareNotSupported_ShouldReturnFalse()
    {
        // Arrange
        var title = "Test Comic";
        var text = "Test description";
        var url = "https://test.com/comic/123";

        _mockJsRuntime
            .Setup(x => x.InvokeAsync<bool>(
                "shareUtils.share",
                It.IsAny<object[]>()))
            .ThrowsAsync(new JSException("Web Share API not supported"));

        // Act
        var result = await _shareService.ShareComicAsync(title, text, url);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task IsShareSupportedAsync_WhenSupported_ShouldReturnTrue()
    {
        // Arrange
        _mockJsRuntime
            .Setup(x => x.InvokeAsync<bool>(
                "shareUtils.isSupported",
                Array.Empty<object>()))
            .ReturnsAsync(true);

        // Act
        var result = await _shareService.IsShareSupportedAsync();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task IsShareSupportedAsync_WhenNotSupported_ShouldReturnFalse()
    {
        // Arrange
        _mockJsRuntime
            .Setup(x => x.InvokeAsync<bool>(
                "shareUtils.isSupported",
                Array.Empty<object>()))
            .ReturnsAsync(false);

        // Act
        var result = await _shareService.IsShareSupportedAsync();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task CopyToClipboardAsync_WithValidUrl_ShouldNotThrow()
    {
        // Arrange
        var url = "https://poseereviews.com/comic/test123";

        // Act & Assert - Verify no exception is thrown
        // Note: InvokeVoidAsync is an extension method and cannot be directly mocked with Moq
        // This test verifies the method signature and argument validation
        var exception = await Record.ExceptionAsync(
            async () => await _shareService.CopyToClipboardAsync(url));

        // The mock will return default ValueTask which completes successfully
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ShareComicAsync_WithInvalidTitle_ShouldThrowArgumentException(string? invalidTitle)
    {
        // Arrange
        var text = "Test description";
        var url = "https://test.com/comic/123";

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await _shareService.ShareComicAsync(invalidTitle!, text, url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ShareComicAsync_WithInvalidUrl_ShouldThrowArgumentException(string? invalidUrl)
    {
        // Arrange
        var title = "Test Comic";
        var text = "Test description";

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await _shareService.ShareComicAsync(title, text, invalidUrl!));
    }

    [Fact]
    public async Task CopyToClipboardAsync_WithNullUrl_ShouldThrowArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await _shareService.CopyToClipboardAsync(null!));
    }
}
