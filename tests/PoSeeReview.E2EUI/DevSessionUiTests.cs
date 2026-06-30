using Microsoft.Playwright;

namespace PoSeeReview.E2EUI;

/// <summary>
/// C# Playwright port of the dev-session ANON login UI flow. Requires the app running
/// in Development (the ANON section renders only when the Blazor env is "Development").
/// Every test runs against both a mobile portrait viewport and a desktop landscape
/// viewport (directive #5) to guarantee visual parity across form factors.
/// </summary>
[Collection("e2e-ui")]
[Trait("Tier", "E2EUI")]
public sealed class DevSessionUiTests(PlaywrightFixture fixture)
{
    private const int RenderTimeout = 15_000;
    private const int AssertTimeout = 10_000;

    private async Task<IPage> GoHomeCleanAsync(string viewport)
    {
        var page = await fixture.NewPageAsync(viewport);
        await page.GotoAsync($"{fixture.BaseUrl}/");
        await page.EvaluateAsync("() => localStorage.clear()");
        return page;
    }

    private static async Task WaitForDevBannerAsync(IPage page) =>
        await page.Locator(".dev-session-banner").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = RenderTimeout });

    [Theory]
    [MemberData(nameof(PlaywrightFixture.Viewports), MemberType = typeof(PlaywrightFixture))]
    public async Task AnonLoginButton_IsVisible_InDevMode(string viewport)
    {
        var page = await GoHomeCleanAsync(viewport);
        await page.GotoAsync($"{fixture.BaseUrl}/");
        await WaitForDevBannerAsync(page);

        var anonButton = page.GetByRole(AriaRole.Button, new() { NameRegex = new("anon login", System.Text.RegularExpressions.RegexOptions.IgnoreCase) });
        await Assertions.Expect(anonButton).ToBeVisibleAsync(new() { Timeout = AssertTimeout });
    }

    [Theory]
    [MemberData(nameof(PlaywrightFixture.Viewports), MemberType = typeof(PlaywrightFixture))]
    public async Task ClickingAnonLogin_DisplaysAnonIdentity(string viewport)
    {
        var page = await GoHomeCleanAsync(viewport);
        await page.GotoAsync($"{fixture.BaseUrl}/");
        await WaitForDevBannerAsync(page);

        await page.GetByRole(AriaRole.Button, new() { NameRegex = new("anon login", System.Text.RegularExpressions.RegexOptions.IgnoreCase) }).ClickAsync();

        var anonIdentity = page.Locator("text=/ANON\\d{6}/");
        await Assertions.Expect(anonIdentity).ToBeVisibleAsync(new() { Timeout = AssertTimeout });
    }

    [Theory]
    [MemberData(nameof(PlaywrightFixture.Viewports), MemberType = typeof(PlaywrightFixture))]
    public async Task AnonSession_IsStored_InLocalStorage(string viewport)
    {
        var page = await GoHomeCleanAsync(viewport);
        await page.GotoAsync($"{fixture.BaseUrl}/");
        await WaitForDevBannerAsync(page);

        await page.GetByRole(AriaRole.Button, new() { NameRegex = new("anon login", System.Text.RegularExpressions.RegexOptions.IgnoreCase) }).ClickAsync();
        await Assertions.Expect(page.Locator("text=/ANON\\d{6}/")).ToBeVisibleAsync(new() { Timeout = AssertTimeout });

        var stored = await page.EvaluateAsync<string?>("() => localStorage.getItem('posee_dev_session')");
        Assert.False(string.IsNullOrEmpty(stored));
        Assert.Matches("\"userId\":\"ANON\\d{6}\"", stored!.Replace(" ", string.Empty));
    }

    [Theory]
    [MemberData(nameof(PlaywrightFixture.Viewports), MemberType = typeof(PlaywrightFixture))]
    public async Task DiagnosticsPage_Loads_AndShowsEnvironment(string viewport)
    {
        var page = await fixture.NewPageAsync(viewport);
        await page.GotoAsync($"{fixture.BaseUrl}/diagnostics");
        await page.Locator(".diagnostics-container").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = RenderTimeout });

        await Assertions.Expect(page.Locator("text=/Development|Production|Staging/")).ToBeVisibleAsync(new() { Timeout = RenderTimeout });
    }
}
