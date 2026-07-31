namespace PoSeeReview.Api.Features.Takedowns;

/// <summary>
/// Configuration contract for the takedown slice (NET_RULES 1.5 — zero magic strings).
/// The API key is a secret: it is supplied by Key Vault in Azure and by user-secrets locally
/// (<c>dotnet user-secrets set "Takedowns:ApiKey" "&lt;value&gt;"</c>). It is deliberately absent from
/// every appsettings file, so an unconfigured environment fails closed with 503.
/// </summary>
public static class TakedownOptions
{
    public const string SectionName = "Takedowns";

    /// <summary>Configuration path of the static X-Api-Key secret.</summary>
    public const string ApiKeyConfigurationKey = SectionName + ":ApiKey";
}
