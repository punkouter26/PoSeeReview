namespace Po.SeeReview.Infrastructure.Configuration;

/// <summary>
/// Configuration options for Azure AI Foundry service (uses Azure.AI.OpenAI SDK).
/// Primary endpoint should point to Azure AI Foundry for text generation.
/// Fallback endpoint can point to classic Azure OpenAI for image generation (DALL-E).
/// </summary>
public class AzureOpenAIOptions
{
    public const string SectionName = "AzureOpenAI";

    /// <summary>
    /// Primary Azure AI Foundry endpoint URL for text generation (GPT models).
    /// Format: https://{resource-name}.cognitiveservices.azure.com/
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// API key for primary Azure AI Foundry service
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Deployment name for GPT model (e.g., gpt-4o, gpt-4o-mini)
    /// </summary>
    public string DeploymentName { get; set; } = string.Empty;

    /// <summary>
    /// Deployment name for DALL-E model (e.g., dall-e-3).
    /// Used with DalleEndpoint if specified, otherwise uses primary Endpoint.
    /// </summary>
    public string DalleDeploymentName { get; set; } = string.Empty;

    /// <summary>
    /// Optional fallback endpoint for DALL-E image generation.
    /// Use this when DALL-E is deployed on a different resource than GPT models.
    /// If not set, uses the primary Endpoint.
    /// </summary>
    public string? DalleEndpoint { get; set; }

    /// <summary>
    /// Optional API key for the DALL-E fallback endpoint.
    /// If not set, uses the primary ApiKey.
    /// </summary>
    public string? DalleApiKey { get; set; }
}
