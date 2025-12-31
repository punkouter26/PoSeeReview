using Bunit;
using Po.SeeReview.Client.Components;
using Xunit;

namespace Po.SeeReview.UnitTests.ComponentTests;

/// <summary>
/// Sample bUnit tests demonstrating basic component rendering patterns.
/// These tests verify that components render correctly with expected markup and parameters.
/// </summary>
public class SampleComponentTests : TestContext
{
    [Fact]
    public void LoadingIndicator_WithDefaultMessage_RendersCorrectly()
    {
        // Arrange & Act
        var cut = RenderComponent<LoadingIndicator>();

        // Assert - Verify structure exists (FluentProgressRing renders as fluent-progress-ring)
        Assert.NotNull(cut.Find(".loading-indicator"));
        Assert.NotNull(cut.Find("fluent-progress-ring"));
        var message = cut.Find(".loading-message");
        Assert.Equal("Loading...", message.TextContent);
    }

    [Fact]
    public void LoadingIndicator_WithCustomMessage_RendersCustomMessage()
    {
        // Arrange
        var customMessage = "Processing your request...";

        // Act
        var cut = RenderComponent<LoadingIndicator>(parameters => parameters
            .Add(p => p.Message, customMessage));

        // Assert
        var messageElement = cut.Find(".loading-message");
        Assert.Equal(customMessage, messageElement.TextContent);
    }

    [Fact]
    public void LoadingIndicator_HasRequiredCssClasses()
    {
        // Arrange & Act
        var cut = RenderComponent<LoadingIndicator>();

        // Assert - Verify structural CSS classes exist (uses FluentProgressRing)
        Assert.NotNull(cut.Find(".loading-indicator"));
        Assert.NotNull(cut.Find("fluent-progress-ring")); // FluentProgressRing replaces .spinner
        Assert.NotNull(cut.Find(".loading-message"));
    }

    [Fact]
    public void LoadingIndicator_MessageParameter_IsOptional()
    {
        // Arrange & Act - Render without explicitly setting Message parameter
        var cut = RenderComponent<LoadingIndicator>();

        // Assert - Should use default value
        var messageElement = cut.Find(".loading-message");
        Assert.Equal("Loading...", messageElement.TextContent);
    }
}
