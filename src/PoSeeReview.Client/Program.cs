using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using PoSeeReview.Client;
using PoSeeReview.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// BFF auth (NET_RULES 4.1/4.2): no tokens in WASM — auth state mirrors the server cookie
// session via /auth/me through a custom AuthenticationStateProvider.
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthenticationStateProvider, BffAuthenticationStateProvider>();

// Register application services
builder.Services.AddScoped<GeolocationService>();
builder.Services.AddScoped<ApiClient>();
builder.Services.AddScoped<ShareService>();
builder.Services.AddScoped<FxService>();
builder.Services.AddScoped<ToastService>();
builder.Services.AddScoped<DiscoveryState>();
builder.Services.AddScoped<BoardMemoryService>();
builder.Services.AddScoped<ComicHistoryService>();
builder.Services.AddScoped<ModerationClient>();
// Scoped, not singleton: it holds the in-flight generation timestamp, which belongs to the
// user's session rather than the process.
builder.Services.AddScoped<AnalyticsService>();

await builder.Build().RunAsync();
