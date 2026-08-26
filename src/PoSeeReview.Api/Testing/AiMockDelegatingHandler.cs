using System.Net;
using System.Text.Json;
using System.Text;
using PoSeeReview.Api.Abstractions;

namespace PoSeeReview.Api.Testing;

/// <summary>
/// Test-only HTTP boundary that intercepts outbound calls to AI providers
/// (Azure AI Foundry / Azure OpenAI and Google Gemini/Imagen) and returns canned,
/// deterministic responses. This guarantees no real tokens are spent and no cost
/// leaks outside Production when integration/E2E suites exercise the real service
/// classes. Non-AI requests fall through to the inner handler unchanged.
/// </summary>
public sealed class AiMockDelegatingHandler : DelegatingHandler, IMockable
{
    // Superset JSON: satisfies both StrangenessAnalysisResult (score/panel/narrative)
    // and PanelCaptionsResult (captions) — unknown members are ignored by each parser.
    private const string OpenAiChatJson =
        "{\"strangenessScore\":42,\"panelCount\":2,\"narrative\":\"A mock narrative for tests.\","
        + "\"captions\":[\"Scene one.\",\"Scene two.\"]}";

    // 1x1 transparent PNG — valid image bytes for the overlay pipeline.
    private const string TinyPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var url = request.RequestUri?.AbsoluteUri ?? string.Empty;
        var host = request.RequestUri?.Host ?? string.Empty;

        // Azure AI Foundry / Azure OpenAI chat completions
        if (host.EndsWith("openai.azure.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("cognitiveservices.azure.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("services.ai.azure.com", StringComparison.OrdinalIgnoreCase)
            || url.Contains("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return Json(BuildChatCompletion());
        }

        // Google Gemini / Imagen image generation
        if (host.EndsWith("generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase))
        {
            return Json($"{{\"predictions\":[{{\"bytesBase64Encoded\":\"{TinyPngBase64}\"}}]}}");
        }

        // Not an AI endpoint — pass through (no AI tokens spent regardless).
        return await base.SendAsync(request, cancellationToken);
    }

    private static string BuildChatCompletion()
    {
        // The service reads choices[0].message.content as a JSON *string* and re-parses it.
        var content = JsonSerializer.Serialize(OpenAiChatJson);
        return "{\"id\":\"chatcmpl-mock\",\"object\":\"chat.completion\",\"created\":1700000000,\"model\":\"mock\","
            + "\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":" + content + "},\"finish_reason\":\"stop\"}],"
            + "\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1,\"total_tokens\":2}}";
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };
}
