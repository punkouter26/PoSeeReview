namespace Po.SeeReview.Infrastructure.Configuration;

/// <summary>
/// Configuration options for Azure OpenAI service
/// </summary>
public class AzureOpenAIOptions
{
    public const string SectionName = "AzureOpenAI";

    /// <summary>
    /// Azure OpenAI service endpoint
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// API key for Azure OpenAI service
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Deployment name for GPT model (GPT-4o-mini)
    /// </summary>
    public string DeploymentName { get; set; } = string.Empty;

    /// <summary>
    /// Deployment name for DALL-E model (DALL-E 3)
    /// </summary>
    public string DalleDeploymentName { get; set; } = string.Empty;
}
