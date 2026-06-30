using Microsoft.Playwright;

namespace PoSeeReview.E2EUI;

/// <summary>
/// Shared Playwright browser for the E2E UI suite (NET_RULES 2.2: E2EUI = C# Playwright).
/// Target the running app via the <c>E2E_BASE_URL</c> environment variable
/// (defaults to the local HTTPS port 5001, NET_RULES 3.1). Browsers are provisioned
/// once via <c>pwsh bin/Debug/net10.0/playwright.ps1 install</c> after a build.
/// </summary>
public sealed class PlaywrightFixture : IAsyncLifetime
{
    public IBrowser Browser { get; private set; } = null!;
    public string BaseUrl { get; } =
        Environment.GetEnvironmentVariable("E2E_BASE_URL") ?? "https://localhost:5001";

    private IPlaywright _playwright = null!;

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
    }

    public async Task<IPage> NewPageAsync()
    {
        var context = await Browser.NewContextAsync(new() { IgnoreHTTPSErrors = true });
        return await context.NewPageAsync();
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.DisposeAsync();
        }

        _playwright?.Dispose();
    }
}

[CollectionDefinition("e2e-ui")]
public sealed class PlaywrightCollection : ICollectionFixture<PlaywrightFixture>;
