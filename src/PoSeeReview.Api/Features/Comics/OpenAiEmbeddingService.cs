using System.ClientModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Embeddings;

namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// Embeddings over any OpenAI-compatible <c>/embeddings</c> endpoint.
/// <para>
/// One implementation for every backend the app might point at, because the wire shape is the
/// same for all of them and the differences are a base URL and a model id. The service is
/// deliberately unaware of which one it is talking to.
/// </para>
/// <para>
/// Every failure path returns an empty vector. See <see cref="IEmbeddingService"/> for why that
/// is the contract rather than laziness.
/// </para>
/// </summary>
public sealed class OpenAiEmbeddingService : IEmbeddingService
{
    private readonly EmbeddingClient? _client;
    private readonly string _model;
    private readonly ILogger<OpenAiEmbeddingService> _logger;

    public OpenAiEmbeddingService(
        IOptions<EmbeddingOptions> options,
        ILogger<OpenAiEmbeddingService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var opts = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _model = opts.Model;

        if (!opts.Enabled || string.IsNullOrWhiteSpace(opts.BaseUrl) || string.IsNullOrWhiteSpace(opts.Model))
        {
            // Left null rather than constructed-and-ignored, so a disabled feature costs no
            // HttpClient and cannot accidentally reach the network.
            _client = null;
            return;
        }

        if (!Uri.TryCreate(opts.BaseUrl, UriKind.Absolute, out var endpoint))
        {
            _logger.LogWarning("Embedding:BaseUrl '{BaseUrl}' is not a valid absolute URL; embeddings are off", opts.BaseUrl);
            _client = null;
            return;
        }

        // The key is ignored by a local runtime but the SDK requires one to be present.
        var client = new OpenAIClient(
            new ApiKeyCredential("embeddings"),
            new OpenAIClientOptions
            {
                Endpoint = endpoint,
                NetworkTimeout = TimeSpan.FromSeconds(opts.TimeoutSeconds)
            });

        _client = client.GetEmbeddingClient(opts.Model);

        _logger.LogInformation("Embeddings enabled. Endpoint: {BaseUrl}, model: {Model}", opts.BaseUrl, opts.Model);
    }

    /// <inheritdoc />
    public bool IsEnabled => _client is not null;

    /// <inheritdoc />
    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        if (_client is null || string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        try
        {
            var response = await _client.GenerateEmbeddingAsync(text, cancellationToken: cancellationToken);
            return response.Value.ToFloats().ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Includes the connection-refused case, which is the normal state of a developer
            // machine with the feature switched on and no local runtime running.
            _logger.LogWarning(ex, "Embedding call failed; continuing without a vector");
            return [];
        }
    }
}
