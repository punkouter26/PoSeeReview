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

    /// <summary>
    /// Viewport matrix every E2E UI test runs against to guarantee visual parity
    /// across form factors (NET_RULES / directive #5: mobile-first + desktop landscape).
    /// Exposed as xUnit theory data (string keys keep it trivially serializable).
    /// </summary>
    public static TheoryData<string> Viewports => new() { "mobile", "desktop" };

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        // Headless by default (CI); set HEADED=1 to watch the run in a real Chrome window.
        var headed = string.Equals(Environment.GetEnvironmentVariable("HEADED"), "1", StringComparison.OrdinalIgnoreCase);
        Browser = await _playwright.Chromium.LaunchAsync(new() { Headless = !headed });
    }

    /// <summary>
    /// Creates a fresh page in the requested viewport. "mobile" emulates a portrait
    /// phone (touch); "desktop" uses a landscape desktop viewport.
    /// </summary>
    public async Task<IPage> NewPageAsync(string viewport = "desktop")
    {
        var options = new BrowserNewContextOptions { IgnoreHTTPSErrors = true };

        if (string.Equals(viewport, "mobile", StringComparison.OrdinalIgnoreCase))
        {
            options.ViewportSize = new ViewportSize { Width = 390, Height = 844 };
            options.IsMobile = true; // Chromium-only — this suite launches Chromium.
            options.HasTouch = true;
        }
        else
        {
            options.ViewportSize = new ViewportSize { Width = 1366, Height = 768 };
        }

        var context = await Browser.NewContextAsync(options);
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
