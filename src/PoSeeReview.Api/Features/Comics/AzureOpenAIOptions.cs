namespace PoSeeReview.Api.Features.Comics;

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
    /// Deployment name for the GPT model. PoSeeReview uses the single shared
    /// <c>gpt-5.4-nano</c> deployment in <c>po-aiservices-shared</c>
    /// (verified 2026-06-14 — only deployment in the resource).
    /// </summary>
    public string DeploymentName { get; set; } = "gpt-5.4-nano";
}
