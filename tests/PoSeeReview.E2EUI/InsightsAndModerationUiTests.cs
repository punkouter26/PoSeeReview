using Microsoft.Playwright;

namespace PoSeeReview.E2EUI;

/// <summary>
/// The pages added alongside the share card, kept comics and the discovery map: <c>/insights</c>
/// and <c>/moderation</c>.
/// <para>
/// Two of these guard contracts rather than content. The primary nav must still be exactly two
/// items — <c>/insights</c> goes in the right-hand session zone beside My Comics, for the same
/// reason My Comics did — and neither page may overflow horizontally at 320px, which is how
/// <c>/diagnostics</c> once shipped a 628px-wide document inside a 390px viewport.
/// </para>
/// </summary>
[Collection("e2e-ui")]
[Trait("Tier", "E2EUI")]
public sealed class InsightsAndModerationUiTests(PlaywrightFixture fixture)
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

    [Theory]
    [MemberData(nameof(PlaywrightFixture.Viewports), MemberType = typeof(PlaywrightFixture))]
    public async Task Insights_RendersItsHeading(string viewport)
    {
        var page = await SignedInAsync(viewport, "/insights");

        await Assertions.Expect(page.Locator("h1.page-hero-title, h1.page-shell-title"))
            .ToHaveTextAsync("Insights");
    }

    [Theory]
    [MemberData(nameof(PlaywrightFixture.Viewports), MemberType = typeof(PlaywrightFixture))]
    public async Task Insights_ResolvesToChartsOrAnEmptyState_NeverAnErrorBanner(string viewport)
    {
        var page = await SignedInAsync(viewport, "/insights");

        // A fresh environment has no scored restaurants, so the honest render is the page-level
        // empty state. What must never appear is the error banner: that would mean the endpoint
        // failed, and "no data yet" and "the read broke" are different things.
        await page.Locator(".insights-panel, .state-card").First
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = RenderTimeout });

        await Assertions.Expect(page.Locator(".alert-danger")).ToHaveCountAsync(0);
    }

    [Theory]
    [MemberData(nameof(PlaywrightFixture.Viewports), MemberType = typeof(PlaywrightFixture))]
    public async Task Insights_LinkIsInTheSessionZone_AndPrimaryNavStaysTwoItems(string viewport)
    {
        var page = await SignedInAsync(viewport, "/insights");

        await Assertions.Expect(page.Locator(".nav-user-zone .nav-insights")).ToHaveCountAsync(1);

        // The header contract. A third .nav-item would both break HeaderContractUiTests and
        // dilute a two-destination app.
        await Assertions.Expect(page.Locator("nav.nav-links .nav-item")).ToHaveCountAsync(2);
    }

    [Fact]
    public async Task Insights_DoesNotOverflowHorizontallyOnASmallPhone()
    {
        var page = await fixture.NewPageAsync("mobile");
        await page.SetViewportSizeAsync(320, 720);

        await page.GotoAsync($"{fixture.BaseUrl}/login");
        await page.Locator(".login-container").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = RenderTimeout });
        await page.GetByRole(AriaRole.Button, new() { NameRegex = GuestButton }).ClickAsync();
        await page.Locator(".nav-user-zone").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = RenderTimeout });

        await page.GotoAsync($"{fixture.BaseUrl}/insights");
        await page.Locator(".insights-panel, .state-card").First
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = RenderTimeout });

        var overflow = await page.EvaluateAsync<int>(
            "() => document.documentElement.scrollWidth - document.documentElement.clientWidth");

        Assert.True(overflow <= 1, $"/insights overflowed by {overflow}px at a 320px viewport.");
    }

    [Theory]
    [MemberData(nameof(PlaywrightFixture.Viewports), MemberType = typeof(PlaywrightFixture))]
    public async Task Moderation_WithoutTheRole_ShowsTheRefusalRatherThanTheQueue(string viewport)
    {
        var page = await SignedInAsync(viewport, "/moderation");

        // A guest session holds no roles. The client check is UI-only — every endpoint enforces
        // the same policy server-side — but it should still be the thing on screen rather than
        // an empty queue that looks like there is nothing to moderate.
        await Assertions.Expect(page.Locator(".state-card-title")).ToHaveTextAsync("Moderators only");
        await Assertions.Expect(page.Locator(".moderation-list")).ToHaveCountAsync(0);
    }

    [Theory]
    [MemberData(nameof(PlaywrightFixture.Viewports), MemberType = typeof(PlaywrightFixture))]
    public async Task Moderation_HasNoPrimaryNavEntry(string viewport)
    {
        var page = await SignedInAsync(viewport, "/moderation");

        // Same posture as /diagnostics: operator tooling is reachable by URL and stays out of a
        // consumer app's navigation.
        await Assertions.Expect(page.Locator("nav.nav-links a[href*='moderation']")).ToHaveCountAsync(0);
    }
}
