using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Moq.Protected;
using Po.SeeReview.Client.Pages;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Po.SeeReview.UnitTests.ComponentTests;

/// <summary>
/// Sample bUnit tests demonstrating dependency injection and mocking patterns.
/// These tests show how to mock HttpClient, services, and other dependencies.
/// </summary>
public class MockedDependencyTests : TestContext
{
    [Fact]
    public async Task Diagnostics_WithMockedHttpClient_LoadsHealthStatus()
    {
        // Arrange - Create a mock HttpMessageHandler
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        
        // Setup the mock to return a predefined health status response
        var healthResponse = new
        {
            status = "Healthy",
            timestamp = DateTime.UtcNow,
            duration = TimeSpan.FromMilliseconds(50),
            checks = new[]
            {
                new
                {
                    name = "AzureTableStorage",
                    status = "Healthy",
                    description = "Azure Table Storage connection successful",
                    duration = TimeSpan.FromMilliseconds(25),
                    exception = (string?)null
                }
            }
        };

        var responseContent = JsonSerializer.Serialize(healthResponse);
        var httpResponse = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseContent, System.Text.Encoding.UTF8, "application/json")
        };

        mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(httpResponse);

        // Create HttpClient with mocked handler
        var httpClient = new HttpClient(mockHttpMessageHandler.Object)
        {
            BaseAddress = new Uri("https://localhost:5001/")
        };

        // Register the mocked HttpClient in the service collection
        Services.AddSingleton(httpClient);

        // Act - Render the component (it will call OnInitializedAsync)
        var cut = RenderComponent<Diagnostics>();

        // Wait for component to finish async initialization
        await Task.Delay(100); // Small delay to allow async operations

        // Assert - Verify the component displays "Healthy" status
        var markup = cut.Markup;
        Assert.Contains("Healthy", markup);
    }

    [Fact]
    public async Task Diagnostics_WithHttpClientError_DisplaysErrorMessage()
    {
        // Arrange - Setup mock to return error response
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        
        var httpResponse = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.ServiceUnavailable,
            Content = new StringContent("Service temporarily unavailable")
        };

        mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(httpResponse);

        var httpClient = new HttpClient(mockHttpMessageHandler.Object)
        {
            BaseAddress = new Uri("https://localhost:5001/")
        };

        Services.AddSingleton(httpClient);

        // Act
        var cut = RenderComponent<Diagnostics>();
        await Task.Delay(100);

        // Assert - Component should display error state
        var markup = cut.Markup;
        Assert.Contains("Unable to retrieve health status", markup);
    }

    [Fact]
    public void MockedHttpClient_VerifiesRequestWasMade()
    {
        // Arrange
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        
        mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.RequestUri!.AbsolutePath.Contains("/health")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{\"status\":\"Healthy\",\"checks\":[]}", 
                    System.Text.Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(mockHttpMessageHandler.Object)
        {
            BaseAddress = new Uri("https://localhost:5001/")
        };

        Services.AddSingleton(httpClient);

        // Act
        var cut = RenderComponent<Diagnostics>();

        // Assert - Verify the HTTP request was made
        mockHttpMessageHandler.Protected().Verify(
            "SendAsync",
            Times.AtLeastOnce(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public void ComponentWithInjectedService_CanUseMockedService()
    {
        // This demonstrates the general pattern for mocking any injected service
        
        // Arrange - Create a mock service (example with IHttpClientFactory)
        var mockHttpClientFactory = new Mock<IHttpClientFactory>();
        var mockHttpClient = new HttpClient(new Mock<HttpMessageHandler>().Object)
        {
            BaseAddress = new Uri("https://api.example.com/")
        };

        mockHttpClientFactory
            .Setup(factory => factory.CreateClient(It.IsAny<string>()))
            .Returns(mockHttpClient);

        // Register the mock in the service collection
        Services.AddSingleton(mockHttpClientFactory.Object);

        // Act & Assert - Components can now use the mocked IHttpClientFactory
        // var cut = RenderComponent<YourComponent>();
        // ... verify expected behavior with mocked dependency ...
        
        Assert.NotNull(mockHttpClientFactory.Object);
    }
}
