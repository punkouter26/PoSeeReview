using System.Text.Json;
using OpenAI.Chat;

namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// The body of every OpenAI-wire-compatible chat provider.
/// <para>
/// HuggingFace and Ollama differ in exactly three things: base URL, whether a key is required,
/// and the completion cap. Everything else — the messages, the JSON contract, the leniency of
/// the parse, the clamping, the caption normalisation, the usage accounting — is the same code,
/// and it used to be the same code copied. <see cref="ChatPrompts"/> already made this argument
/// for the prompt text; this is the same argument for the call around it.
/// </para>
/// <para>
/// <see cref="AzureOpenAIChatService"/> deliberately does NOT use this: it parses strictly rather
/// than leniently (a hosted deployment honours <c>response_format</c>, so a malformed response is
/// a real fault and should be loud) and it routes its retry policy through Polly.
/// </para>
/// </summary>
internal static class OpenAiWireChat
{
    /// <summary>One analysis call plus what it cost in tokens.</summary>
    internal sealed record Result(StrangenessAnalysis Analysis, long InputTokens, long OutputTokens);

    public static async Task<Result> AnalyzeAsync(
        ChatClient client,
        List<string> reviews,
        float temperature,
        int? maxCompletionTokens,
        bool isReasoningModel,
        CancellationToken cancellationToken)
    {
        var validReviews = reviews.Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
        if (validReviews.Count == 0)
        {
            throw new ArgumentException("No valid reviews provided", nameof(reviews));
        }

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(ChatPrompts.AnalysisSystemMessage),
            new UserChatMessage(ChatPrompts.BuildAnalysisPrompt(validReviews))
        };

        var response = await client.CompleteChatAsync(
            messages,
            ChatTokenBudget.Build(temperature, maxCompletionTokens, isReasoningModel),
            cancellationToken);

        var completion = response.Value;

        // A completion with no parts is not hypothetical: a reasoning deployment whose budget is
        // spent thinking returns zero content parts, and indexing [0] turned that into an
        // IndexOutOfRangeException pointing at the SDK rather than at the model.
        if (completion.Content.Count == 0)
        {
            throw new InvalidOperationException("Chat provider returned an empty completion.");
        }

        var result = DeserializeLenient<StrangenessAnalysisResult>(completion.Content[0].Text)
            ?? throw new InvalidOperationException("Failed to parse chat response");

        var score = Math.Clamp(result.StrangenessScore, 0, 100);
        var panelCount = Math.Clamp(result.PanelCount, 1, 2);
        var narrative = result.Narrative ?? string.Empty;

        return new Result(
            new StrangenessAnalysis(
                score,
                panelCount,
                narrative,
                ChatPrompts.NormalizeCaptions(result.Captions, narrative, panelCount)),
            completion.Usage?.InputTokenCount ?? 0,
            completion.Usage?.OutputTokenCount ?? 0);
    }

    /// <summary>
    /// Tolerant JSON parse: open models sometimes wrap JSON in a ```json fence, or bracket it
    /// with a sentence of preamble, despite the <c>response_format</c> hint.
    /// </summary>
    private static T? DeserializeLenient<T>(string content) where T : class
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var text = content.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline >= 0)
            {
                text = text[(firstNewline + 1)..];
            }

            text = text.TrimEnd('`', '\n', '\r', ' ').TrimEnd('`');
        }

        var start = text.IndexOfAny(['{', '[']);
        var end = text.LastIndexOfAny(['}', ']']);
        if (start >= 0 && end > start)
        {
            text = text[start..(end + 1)];
        }

        return JsonSerializer.Deserialize<T>(text);
    }
}
