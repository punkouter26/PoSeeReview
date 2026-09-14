using System.Net.Http.Json;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoSeeReview.Api.Features.Restaurants;
using PoSeeReview.Api;
using PoSeeReview.Shared.Dtos;
using Xunit.Abstractions;
using Xunit;
using PoSeeReview.Shared.Contracts;
using PoSeeReview.Shared.Ids;
using PoSeeReview.Shared.Enums;

namespace PoSeeReview.Integration.Api;

/// <summary>
/// Integration tests for GET /api/restaurants/nearby, focused on the parameter contract.
/// <para>
/// Three tests were deleted here rather than fixed, because none of them could fail:
/// <c>WithDifferentLocations</c> ended in <c>Assert.True(true)</c> and relied on a human reading
/// the log output; <c>WithLimit</c> and <c>CheckResponseSchema</c> wrapped every assertion in
/// <c>if (response.IsSuccessStatusCode)</c>, so a 400, 429 or 503 made them pass having asserted
/// nothing.
/// </para>
/// <para>
/// Known gap, and a real one: nothing covers the success path or the response <em>shape</em>
/// (field ranges, required members). <c>RestaurantsEndpointTests.GetNearbyRestaurants_ValidCoordinates_Returns200</c>
/// is not a substitute — verified, it reports <c>[SKIP]</c> without a live Google Maps key, which
/// is every local and CI run. Closing this needs a stubbed Maps client injected into the factory,
/// not a softer assertion.
/// </para>
/// </summary>
[Trait("Tier", "Integration")]
[Trait("Domain", "Api")]
public class NearbyRestaurantsEndpointTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private readonly CustomWebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _output;

    public NearbyRestaurantsEndpointTests(
        CustomWebApplicationFactory<Program> factory,
        ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    [Theory]
    [InlineData(-1)]   // Limit < 1
    [InlineData(0)]    // Limit = 0
    [InlineData(51)]   // Limit > 50
    [InlineData(100)]  // Limit way over 50
    public async Task GetNearbyRestaurants_InvalidLimit_ShouldReturn400(int limit)
    {
        // Arrange
        var client = _factory.CreateClient();
        var latitude = 47.6062;
        var longitude = -122.3321;

        // Act
        var response = await client.GetAsync(
            $"/api/restaurants/nearby?latitude={latitude}&longitude={longitude}&limit={limit}");

        // Assert
        _output.WriteLine($"Limit {limit}: Status {response.StatusCode}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Contains("limit", problemDetails.Detail ?? "", StringComparison.OrdinalIgnoreCase);
    }

}
