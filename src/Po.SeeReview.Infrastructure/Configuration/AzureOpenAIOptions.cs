namespace Po.SeeReview.Infrastructure.Configuration;

/// <summary>
/// Configuration options for Azure AI Foundry service (uses Azure.AI.OpenAI SDK).
/// Used for GPT-based text generation (strangeness analysis, narrative, panel dialogue).
/// Image generation is handled separately by GeminiComicService.
/// </summary>
public class AzureOpenAIOptions
{
    public const string SectionName = "AzureOpenAI";

    /// <summary>
    /// Azure AI Foundry endpoint URL for text generation (GPT models).
    /// Format: https://{resource-name}.cognitiveservices.azure.com/
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// API key for Azure AI Foundry service.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Deployment name for GPT model (e.g., gpt-4o, gpt-4o-mini).
    /// </summary>
    public string DeploymentName { get; set; } = string.Empty;
}
