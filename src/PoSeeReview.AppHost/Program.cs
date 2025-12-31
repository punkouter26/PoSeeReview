using Projects;

var builder = DistributedApplication.CreateBuilder(args);

// Azure Storage - use emulator for local dev, real storage in production
var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(emulator => emulator
        .WithLifetime(ContainerLifetime.Persistent)
        .WithBlobPort(10000)
        .WithQueuePort(10001)
        .WithTablePort(10002));

var blobs = storage.AddBlobs("blobs");
var tables = storage.AddTables("tables");

// API Service - main backend
var api = builder.AddProject<Po_SeeReview_Api>("api")
    .WithReference(blobs)
    .WithReference(tables)
    .WaitFor(storage)
    .WithHttpHealthCheck("/health", endpointName: "http");

// Blazor WASM Client - served by API in production, but can be separate in dev
// The API project already serves the Blazor WASM client, so we just need the API
// If you want hot-reload for Blazor, uncomment and configure the client separately:
// var client = builder.AddProject<Po_SeeReview_Client>("client")
//     .WithReference(api)
//     .WaitFor(api);

builder.Build().Run();
