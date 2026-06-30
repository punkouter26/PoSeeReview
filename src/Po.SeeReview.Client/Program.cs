using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.FluentUI.AspNetCore.Components;
using Po.SeeReview.Client;
using Po.SeeReview.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Register Fluent UI services
builder.Services.AddFluentUIComponents();

// Read login mode preference persisted from the previous session (popup → redirect fallback).
// getMsalLoginMode() clears the value in sessionStorage so it is a one-shot setting.
var loginMode = LoginModeInterop.GetMsalLoginMode();

// Register MSAL authentication
builder.Services.AddMsalAuthentication(options =>
{
#pragma warning disable IL2026 // MSAL options binding — slated for removal in BFF cookie-auth rework (NET_RULES 4.2)
    builder.Configuration.Bind("AzureAd", options.ProviderOptions.Authentication);
#pragma warning restore IL2026
    options.ProviderOptions.DefaultAccessTokenScopes.Add(
        "api://cf43692d-ff5a-421e-bd7c-cc59c88414aa/access_as_user");
    options.ProviderOptions.LoginMode = loginMode;
});

// Register application services
builder.Services.AddScoped<GeolocationService>();
builder.Services.AddScoped<DevSessionClient>();
builder.Services.AddScoped<ApiClient>();
builder.Services.AddScoped<ShareService>();
builder.Services.AddScoped<AudioService>();
builder.Services.AddSingleton<DevSessionStateService>();

await builder.Build().RunAsync();
