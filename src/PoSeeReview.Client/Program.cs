using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.FluentUI.AspNetCore.Components;
using PoSeeReview.Client;
using PoSeeReview.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Register Fluent UI services
builder.Services.AddFluentUIComponents();

// BFF auth (NET_RULES 4.1/4.2): no tokens in WASM — auth state mirrors the server cookie
// session via /auth/me through a custom AuthenticationStateProvider.
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthenticationStateProvider, BffAuthenticationStateProvider>();

// Register application services
builder.Services.AddScoped<GeolocationService>();
builder.Services.AddScoped<DevSessionClient>();
builder.Services.AddScoped<ApiClient>();
builder.Services.AddScoped<ShareService>();
builder.Services.AddScoped<AudioService>();
builder.Services.AddSingleton<DevSessionStateService>();

await builder.Build().RunAsync();
