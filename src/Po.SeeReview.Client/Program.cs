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

// Register application services

builder.Services.AddScoped<GeolocationService>();
builder.Services.AddScoped<ApiClient>();
builder.Services.AddScoped<ShareService>();

await builder.Build().RunAsync();
