using Microsoft.Playwright;

namespace PoSeeReview.E2EUI;

/// <summary>
/// Hall of Fame and Diagnostics page coverage, ported from the former TypeScript suite.
/// Diagnostics doubles as the UI-side check that <c>/diag</c> renders masked values only
/// (NET_RULES 3.2).
/// </summary>
[Collection("e2e-ui")]
[Trait("Tier", "E2EUI")]
public sealed class LeaderboardAndDiagnosticsUiTests(PlaywrightFixture fixture)
{
    private const int RenderTimeout = 20_000;
    private static readonly System.Text.RegularExpressions.Regex GuestButton =
        new("continue as guest", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private async Task<IPage> SignedInAsync(string viewport, string route)
    {
        var page = await fixture.NewPageAsync(viewport);
        await page.GotoAsync($"{fixture.BaseUrl}/login");
        await page.Locator(".login-container").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = RenderTimeout });
        await page.GetByRole(AriaRole.Button, new() { NameRegex = GuestButton }).ClickAsync();
        await page.Locator(".nav-user-zone").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = RenderTimeout });
        await page.GotoAsync($"{fixture.BaseUrl}{route}");
        return page;
    }

    // ── Leaderboard ─────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(PlaywrightFixture.Viewports), MemberType = typeof(PlaywrightFixture))]
    public async Task Leaderboard_RendersHeading(string viewport)
    {
        var page = await SignedInAsync(viewport, "/leaderboard");

        await Assertions.Expect(page.Locator(".leaderboard-header h1")).ToHaveTextAsync("Hall of Fame");
    }

    [Theory]
    [MemberData(nameof(PlaywrightFixture.Viewports), MemberType = typeof(PlaywrightFixture))]
    public async Task Leaderboard_ExposesRegionChips(string viewport)
    {
        var page = await SignedInAsync(viewport, "/leaderboard");

        var chips = page.Locator(".region-chips .region-chip");
        await Assertions.Expect(chips.First).ToBeVisibleAsync(new() { Timeout = RenderTimeout });
        Assert.True(await chips.CountAsync() > 1, "expected more than one selectable region");
    }

    [Theory]
    [MemberData(nameof(PlaywrightFixture.Viewports), MemberType = typeof(PlaywrightFixture))]
    public async Task Leaderboard_SelectingRegion_RefetchesForThatRegion(string viewport)
    {
        var page = await SignedInAsync(viewport, "/leaderboard");
        var chips = page.Locator(".region-chips .region-chip");
        await chips.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = RenderTimeout });

        var request = page.WaitForRequestAsync(r => r.Url.Contains("/api/leaderboard"), new() { Timeout = RenderTimeout });
        await chips.Nth(1).ClickAsync();

        var url = (await request).Url;
        Assert.Contains("region=", url, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(PlaywrightFixture.Viewports), MemberType = typeof(PlaywrightFixture))]
    public async Task Leaderboard_EmptyOrPopulated_NeverShowsErrorBanner(string viewport)
    {
        var page = await SignedInAsync(viewport, "/leaderboard");
        await page.Locator(".leaderboard-container").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = RenderTimeout });

        await Assertions.Expect(page.Locator(".alert-danger")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync(new() { Timeout = RenderTimeout });
    }

    [Theory]
    [MemberData(nameof(PlaywrightFixture.Viewports), MemberType = typeof(PlaywrightFixture))]
    public async Task Leaderboard_LimitControl_IsPresent(string viewport)
    {
        var page = await SignedInAsync(viewport, "/leaderboard");

        await Assertions.Expect(page.Locator(".region-controls-row")).ToBeVisibleAsync(new() { Timeout = RenderTimeout });
    }

    // ── Diagnostics ─────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(PlaywrightFixture.Viewports), MemberType = typeof(PlaywrightFixture))]
    public async Task Diagnostics_RendersSnapshot(string viewport)
    {
        var page = await SignedInAsync(viewport, "/diagnostics");

        await Assertions.Expect(page.Locator(".diagnostics-container").First)
            .ToBeVisibleAsync(new() { Timeout = RenderTimeout });
    }

    [Theory]
    [MemberData(nameof(PlaywrightFixture.Viewports), MemberType = typeof(PlaywrightFixture))]
    public async Task Diagnostics_NeverRendersAnUnmaskedSecret(string viewport)
    {
        var page = await SignedInAsync(viewport, "/diagnostics");
        await page.Locator(".diagnostics-container").First
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = RenderTimeout });

        var rows = page.Locator(".config-row");
        var count = await rows.CountAsync();
        for (var i = 0; i < count; i++)
        {
            var key = (await rows.Nth(i).Locator(".config-key").TextContentAsync() ?? string.Empty).ToLowerInvariant();
            if (!key.Contains("key") && !key.Contains("secret") && !key.Contains("password") && !key.Contains("connectionstring"))
            {
                continue;
            }

            var value = (await rows.Nth(i).Locator(".config-value").TextContentAsync() ?? string.Empty).Trim();
            if (value.Length == 0 || value.Equals("(not set)", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Assert.Contains("***", value, StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(PlaywrightFixture.Viewports), MemberType = typeof(PlaywrightFixture))]
    public async Task Diagnostics_ShowsDependencyHealth(string viewport)
    {
        var page = await SignedInAsync(viewport, "/diagnostics");

        var summary = page.Locator(".health-summary, .diagnostics-section").First;
        await Assertions.Expect(summary).ToBeVisibleAsync(new() { Timeout = RenderTimeout });
    }
}
