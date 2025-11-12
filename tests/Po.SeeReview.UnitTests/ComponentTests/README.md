# bUnit Component Testing Guide

This directory contains bUnit tests for Blazor components in the Po.SeeReview application. bUnit is a testing library for Blazor components that makes it easy to write comprehensive unit tests.

## Test Organization

- **SampleComponentTests.cs** - Basic rendering patterns and parameter binding
- **InteractionTests.cs** - User interactions and EventCallback testing
- **MockedDependencyTests.cs** - Dependency injection and HttpClient mocking

## Getting Started

### Prerequisites

```xml
<PackageReference Include="bUnit" />
<PackageReference Include="bUnit.web" />
<PackageReference Include="xunit" />
<PackageReference Include="Moq" />
```

### Basic Test Structure

All bUnit tests inherit from `TestContext`:

```csharp
public class MyComponentTests : TestContext
{
    [Fact]
    public void MyComponent_Renders_Correctly()
    {
        // Arrange - set up any required services or parameters
        
        // Act - render the component
        var cut = RenderComponent<MyComponent>();
        
        // Assert - verify the output
        Assert.NotNull(cut);
    }
}
```

## Testing Patterns

### 1. Basic Rendering Tests

Test that components render with expected markup and structure.

```csharp
[Fact]
public void LoadingIndicator_WithDefaultMessage_RendersCorrectly()
{
    // Act
    var cut = RenderComponent<LoadingIndicator>();

    // Assert - verify exact markup structure
    cut.MarkupMatches(@"
        <div class=""loading-container"">
            <div class=""loading-spinner""></div>
            <div class=""loading-message"">Loading...</div>
        </div>
    ");
}
```

**Key APIs:**
- `RenderComponent<T>()` - Renders a component and returns a rendered component instance
- `MarkupMatches()` - Asserts exact HTML match (whitespace-insensitive)
- `Find()` - Finds an element by CSS selector
- `Markup` - Gets the rendered HTML as string

### 2. Parameter Binding Tests

Verify components correctly use parameters.

```csharp
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
```

**Key APIs:**
- `Add(p => p.Parameter, value)` - Sets a component parameter
- `AddChildContent(markup)` - Adds child content
- `AddCascadingValue<T>(value)` - Provides cascading parameters

### 3. User Interaction Tests

Test click events, form inputs, and other user interactions.

```csharp
[Fact]
public void RestaurantCard_WithEnabledCard_FiresOnClickCallback()
{
    // Arrange
    var callbackInvoked = false;
    var testRestaurant = new RestaurantDto { /* ... */ };

    var cut = RenderComponent<RestaurantCard>(parameters => parameters
        .Add(p => p.Restaurant, testRestaurant)
        .Add(p => p.IsDisabled, false)
        .Add(p => p.OnClick, EventCallback.Factory.Create(this, () => callbackInvoked = true)));

    // Act
    var cardElement = cut.Find(".restaurant-card");
    cardElement.Click();

    // Assert
    Assert.True(callbackInvoked);
}
```

**Key APIs:**
- `Click()` - Simulates a click event
- `Change(value)` - Simulates input change
- `EventCallback.Factory.Create()` - Creates test EventCallback
- `Input(text)` - Simulates typing text

### 4. Conditional Rendering Tests

Verify components show/hide elements based on state.

```csharp
[Fact]
public void RestaurantCard_WithDisabledCard_ShowsDisabledOverlay()
{
    // Arrange
    var testRestaurant = new RestaurantDto { ReviewCount = 10 };

    // Act
    var cut = RenderComponent<RestaurantCard>(parameters => parameters
        .Add(p => p.Restaurant, testRestaurant)
        .Add(p => p.IsDisabled, true));

    // Assert
    var overlayElements = cut.FindAll(".disabled-overlay");
    Assert.Single(overlayElements);
}
```

**Key APIs:**
- `FindAll(selector)` - Finds all matching elements
- `Assert.Single()` - Verifies exactly one element
- `Assert.Empty()` - Verifies no elements found

### 5. CSS Class Tests

Verify correct CSS classes are applied.

```csharp
[Fact]
public void RestaurantCard_AppliesDisabledClass_WhenReviewsAreLow()
{
    // Arrange & Act
    var cut = RenderComponent<RestaurantCard>(parameters => parameters
        .Add(p => p.Restaurant, new RestaurantDto { ReviewCount = 5 })
        .Add(p => p.IsDisabled, true));

    // Assert
    var cardElement = cut.Find(".restaurant-card");
    Assert.Contains("card-disabled", cardElement.ClassName);
}
```

### 6. Mocking Dependencies

Mock HttpClient, services, and other injected dependencies.

```csharp
[Fact]
public async Task Diagnostics_WithMockedHttpClient_LoadsHealthStatus()
{
    // Arrange - Create mock HttpMessageHandler
    var mockHandler = new Mock<HttpMessageHandler>();
    mockHandler
        .Protected()
        .Setup<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .ReturnsAsync(new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent("{\"status\":\"Healthy\"}")
        });

    var httpClient = new HttpClient(mockHandler.Object)
    {
        BaseAddress = new Uri("https://localhost:5001/")
    };

    // Register in service collection
    Services.AddSingleton(httpClient);

    // Act
    var cut = RenderComponent<Diagnostics>();
    await Task.Delay(100); // Wait for async initialization

    // Assert
    Assert.Contains("Healthy", cut.Markup);
}
```

**Key APIs:**
- `Services.AddSingleton<T>(instance)` - Registers service
- `Mock<T>` - Creates Moq mock object
- `Protected().Setup()` - Mocks protected methods
- `ItExpr.IsAny<T>()` - Matches any argument

## Best Practices

### 1. Use Descriptive Test Names

Follow the pattern: `ComponentName_Scenario_ExpectedBehavior`

```csharp
[Fact]
public void LoadingIndicator_WithCustomMessage_RendersCustomMessage() { }

[Fact]
public void RestaurantCard_WithDisabledCard_DoesNotFireCallback() { }
```

### 2. Arrange-Act-Assert Pattern

Structure all tests with clear sections:

```csharp
[Fact]
public void MyTest()
{
    // Arrange - set up test data and dependencies
    var expected = "test value";
    
    // Act - perform the action being tested
    var cut = RenderComponent<MyComponent>();
    
    // Assert - verify the results
    Assert.Equal(expected, cut.Instance.Value);
}
```

### 3. Test One Thing Per Test

Keep tests focused on a single behavior:

```csharp
// Good - tests one specific behavior
[Fact]
public void RestaurantCard_WithLowReviews_ShowsDisabledOverlay() { }

[Fact]
public void RestaurantCard_WithLowReviews_DoesNotFireClickCallback() { }

// Bad - tests multiple behaviors
[Fact]
public void RestaurantCard_WithLowReviews_BehavesCorrectly() { }
```

### 4. Use Component Instances for Assertions

Access component state and methods via the `Instance` property:

```csharp
var cut = RenderComponent<MyComponent>();
Assert.Equal("expected", cut.Instance.PublicProperty);
```

### 5. Handle Async Operations

For components with `OnInitializedAsync`, allow time for completion:

```csharp
var cut = RenderComponent<MyAsyncComponent>();
await Task.Delay(100); // Or use bUnit's async helpers
Assert.True(cut.Instance.IsLoaded);
```

### 6. Clean Up Resources

bUnit's `TestContext` automatically disposes components, but you can add custom cleanup:

```csharp
public class MyTests : TestContext
{
    public override void Dispose()
    {
        // Custom cleanup
        base.Dispose();
    }
}
```

## Common Assertions

```csharp
// Element existence
var element = cut.Find(".my-class");
Assert.NotNull(element);

// Element count
var elements = cut.FindAll(".my-class");
Assert.Equal(3, elements.Count);
Assert.Single(elements);
Assert.Empty(elements);

// Text content
Assert.Equal("expected", element.TextContent);
Assert.Contains("partial", element.TextContent);

// Attributes
Assert.Equal("value", element.GetAttribute("data-id"));
Assert.True(element.HasAttribute("disabled"));

// CSS classes
Assert.Contains("active", element.ClassName);
Assert.DoesNotContain("hidden", element.ClassName);

// Markup matching
cut.MarkupMatches("<div>Expected HTML</div>");
```

## Running Tests

```powershell
# Run all component tests
dotnet test --filter "FullyQualifiedName~ComponentTests"

# Run specific test class
dotnet test --filter "FullyQualifiedName~SampleComponentTests"

# Run with coverage
dotnet test --collect:"XPlat Code Coverage" --filter "FullyQualifiedName~ComponentTests"
```

## Resources

- [bUnit Documentation](https://bunit.dev/)
- [bUnit GitHub](https://github.com/bUnit-dev/bUnit)
- [Blazor Testing Best Practices](https://learn.microsoft.com/en-us/aspnet/core/blazor/test)
- [Moq Documentation](https://github.com/moq/moq4)

## Contributing

When adding new component tests:

1. Create tests in the appropriate category (rendering, interaction, or mocking)
2. Follow existing naming conventions and patterns
3. Add XML documentation comments for complex test scenarios
4. Ensure tests are independent and can run in any order
5. Update this README if introducing new testing patterns
