using Microsoft.Playwright;

namespace PoSeeReview.E2EUI;

/// <summary>
/// Enforces the header layout contract (NET_RULES 4.1): Left (branding) | Center (actions) |
/// Right (session / logout). The right zone regressed once by being wrapped in a
/// Development-only conditional, so it is asserted explicitly in every state.
/// </summary>
[Collection("e2e-ui")]
[Trait("Tier", "E2EUI")]
public sealed class HeaderContractUiTests(PlaywrightFixture fixture)
{
    private const int RenderTimeout = 15_000;

    private async Task<IPage> SignedInPageAsync(string viewport)
    {
        var page = await fixture.NewPageAsync(viewport);
        await page.GotoAsync($"{fixture.BaseUrl}/login");
        await page.Locator(".login-container").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = RenderTimeout });
        await page.GetByRole(AriaRole.Button, new() { NameRegex = new("continue as guest", System.Text.RegularExpressions.RegexOptions.IgnoreCase) }).ClickAsync();
        await Assertions.Expect(page.Locator(".nav-user-zone")).ToBeVisibleAsync(new() { Timeout = RenderTimeout });
        return page;
    }

    [Theory]
    [MemberData(nameof(PlaywrightFixture.Viewports), MemberType = typeof(PlaywrightFixture))]
    public async Task Header_LeftZone_ShowsBranding(string viewport)
    {
        var page = await SignedInPageAsync(viewport);

        await Assertions.Expect(page.Locator(".navbar-brand .brand-name")).ToHaveTextAsync("PoSeeReview");
    }

    [Theory]
    [MemberData(nameof(PlaywrightFixture.Viewports), MemberType = typeof(PlaywrightFixture))]
    public async Task Header_CenterZone_ShowsPrimaryNavigation(string viewport)
    {
        var page = await SignedInPageAsync(viewport);

        var nav = page.Locator("nav.nav-links");
        await Assertions.Expect(nav).ToBeVisibleAsync(new() { Timeout = RenderTimeout });
        await Assertions.Expect(nav.Locator(".nav-item")).ToHaveCountAsync(2);
    }

    [Theory]
    [MemberData(nameof(PlaywrightFixture.Viewports), MemberType = typeof(PlaywrightFixture))]
    public async Task Header_RightZone_ShowsSessionAndSignOut(string viewport)
    {
        var page = await SignedInPageAsync(viewport);

        await Assertions.Expect(page.Locator(".nav-user-zone .nav-user-badge")).ToBeVisibleAsync(new() { Timeout = RenderTimeout });
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new()
        {
            NameRegex = new("sign out", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
        })).ToBeVisibleAsync(new() { Timeout = RenderTimeout });
    }

    [Theory]
    [MemberData(nameof(PlaywrightFixture.Viewports), MemberType = typeof(PlaywrightFixture))]
    public async Task Header_RightZone_ShowsSignInWhenUnauthenticated(string viewport)
    {
        var page = await fixture.NewPageAsync(viewport);
        await page.GotoAsync($"{fixture.BaseUrl}/login");
        await page.Locator(".login-container").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = RenderTimeout });

        // Unauthenticated must still render a right zone — an empty one was the original bug.
        await Assertions.Expect(page.Locator(".nav-user-zone .nav-user-badge--inactive"))
            .ToBeVisibleAsync(new() { Timeout = RenderTimeout });
    }

    [Theory]
    [MemberData(nameof(PlaywrightFixture.Viewports), MemberType = typeof(PlaywrightFixture))]
    public async Task Header_ZonesAreOrderedLeftCenterRight(string viewport)
    {
        var page = await SignedInPageAsync(viewport);

        var brandBox = await page.Locator(".navbar-brand").BoundingBoxAsync();
        var sessionBox = await page.Locator(".nav-user-zone").BoundingBoxAsync();

        Assert.NotNull(brandBox);
        Assert.NotNull(sessionBox);
        // The session zone must sit to the right of (or below, when the header wraps on
        // mobile) the branding — never to its left.
        Assert.True(
            sessionBox.X >= brandBox.X || sessionBox.Y > brandBox.Y,
            $"session zone at ({sessionBox.X},{sessionBox.Y}) is left of branding ({brandBox.X},{brandBox.Y})");
    }

    [Theory]
    [MemberData(nameof(PlaywrightFixture.Viewports), MemberType = typeof(PlaywrightFixture))]
    public async Task Header_MockDataBanner_ReflectsServerMockStatus(string viewport)
    {
        var page = await SignedInPageAsync(viewport);

        // /diag/mock-status is the single source of truth (NET_RULES 4.2); the badge must
        // agree with it rather than guessing from the environment name.
        var status = await page.APIRequest.GetAsync($"{fixture.BaseUrl}/diag/mock-status");
        var json = await status.JsonAsync();
        var isMockActive = json?.GetProperty("isMockActive").GetBoolean() ?? false;

        var badge = page.Locator(".nav-mock-badge");
        if (isMockActive)
        {
            await Assertions.Expect(badge).ToBeVisibleAsync(new() { Timeout = RenderTimeout });
            await Assertions.Expect(badge).ToHaveTextAsync("USING MOCK DATA");
        }
        else
        {
            await Assertions.Expect(badge).ToHaveCountAsync(0);
        }
    }

    [Theory]
    [MemberData(nameof(PlaywrightFixture.Viewports), MemberType = typeof(PlaywrightFixture))]
    public async Task Header_BrandLink_NavigatesHome(string viewport)
    {
        var page = await SignedInPageAsync(viewport);
        await page.GotoAsync($"{fixture.BaseUrl}/leaderboard");
        await page.Locator(".leaderboard-container").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = RenderTimeout });

        await page.Locator(".navbar-brand").ClickAsync();

        await Assertions.Expect(page.Locator(".index-container")).ToBeVisibleAsync(new() { Timeout = RenderTimeout });
    }
}
