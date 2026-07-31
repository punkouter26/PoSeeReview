using Microsoft.Playwright;

namespace PoSeeReview.E2EUI;

/// <summary>
/// Verifies system-aware light/dark theming (NET_RULES 4.3). The page must follow the OS
/// preference by default, and an explicit <c>data-theme</c> choice must beat it in both
/// directions. Card surfaces are asserted too, because those were the tokens most likely
/// to stay stuck on a hardcoded white.
/// </summary>
[Collection("e2e-ui")]
[Trait("Tier", "E2EUI")]
public sealed class ThemeUiTests(PlaywrightFixture fixture)
{
    private const int RenderTimeout = 15_000;

    private static async Task<string> TokenAsync(IPage page, string token) =>
        (await page.EvaluateAsync<string>(
            $"() => getComputedStyle(document.documentElement).getPropertyValue('{token}').trim()")) ?? string.Empty;

    private async Task<IPage> LoginPageAsync(string viewport, ColorScheme scheme)
    {
        var page = await fixture.NewPageAsync(viewport);
        await page.EmulateMediaAsync(new() { ColorScheme = scheme });
        await page.GotoAsync($"{fixture.BaseUrl}/login");
        await page.Locator(".login-container").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = RenderTimeout });
        return page;
    }

    [Theory]
    [MemberData(nameof(PlaywrightFixture.Viewports), MemberType = typeof(PlaywrightFixture))]
    public async Task Theme_FollowsOsLightPreference(string viewport)
    {
        var page = await LoginPageAsync(viewport, ColorScheme.Light);

        Assert.Equal("#F8F7FF", await TokenAsync(page, "--color-surface"));
    }

    [Theory]
    [MemberData(nameof(PlaywrightFixture.Viewports), MemberType = typeof(PlaywrightFixture))]
    public async Task Theme_FollowsOsDarkPreference(string viewport)
    {
        var page = await LoginPageAsync(viewport, ColorScheme.Dark);

        Assert.Equal("#12101A", await TokenAsync(page, "--color-surface"));
    }

    [Theory]
    [MemberData(nameof(PlaywrightFixture.Viewports), MemberType = typeof(PlaywrightFixture))]
    public async Task Theme_ExplicitDarkOverridesLightOs(string viewport)
    {
        var page = await LoginPageAsync(viewport, ColorScheme.Light);

        await page.EvaluateAsync("() => document.documentElement.setAttribute('data-theme', 'dark')");

        Assert.Equal("#12101A", await TokenAsync(page, "--color-surface"));
    }

    [Theory]
    [MemberData(nameof(PlaywrightFixture.Viewports), MemberType = typeof(PlaywrightFixture))]
    public async Task Theme_ExplicitLightOverridesDarkOs(string viewport)
    {
        var page = await LoginPageAsync(viewport, ColorScheme.Dark);

        await page.EvaluateAsync("() => document.documentElement.setAttribute('data-theme', 'light')");

        Assert.Equal("#F8F7FF", await TokenAsync(page, "--color-surface"));
    }

    [Theory]
    [MemberData(nameof(PlaywrightFixture.Viewports), MemberType = typeof(PlaywrightFixture))]
    public async Task Theme_CardSurfaceFlipsWithScheme(string viewport)
    {
        var page = await LoginPageAsync(viewport, ColorScheme.Dark);

        // Regression guard: scoped stylesheets used to hardcode `background: white`, which
        // survived the theme switch and left unreadable panels in dark mode.
        Assert.Equal("#1C1926", await TokenAsync(page, "--color-card"));
    }

    [Theory]
    [MemberData(nameof(PlaywrightFixture.Viewports), MemberType = typeof(PlaywrightFixture))]
    public async Task Theme_BodyBackgroundTracksSurfaceToken(string viewport)
    {
        var page = await LoginPageAsync(viewport, ColorScheme.Dark);

        var background = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.body).backgroundColor");

        // rgb(18, 16, 26) == #12101A
        Assert.Equal("rgb(18, 16, 26)", background);
    }
}
