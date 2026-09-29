#:project ../src/PoSeeReview.Api/PoSeeReview.Api.csproj
#:property PublishAot=false

// Golden-set eval for the strangeness analysis prompt. Runs every case in prompt-eval.json
// through the REAL chat service classes (same prompt, same parse, same score formula) several
// times and reports what a prompt change should be judged on: does the score land in the
// expected band, how much does it wobble run to run, does every panel come back with its own
// scene, and how often does the image backstop still have to blunt a word.
//
//   dotnet run SCRIPTS/prompt-eval.cs                                 # Ollama, configured model
//   dotnet run SCRIPTS/prompt-eval.cs -- --model gemma3:4b --runs 3
//   $env:AZURE_OPENAI_ENDPOINT = "..."; $env:AZURE_OPENAI_API_KEY = "..."
//   dotnet run SCRIPTS/prompt-eval.cs -- --provider azure              # paid, ~$0.0003 per call
//
// Not a test: it calls a model, and its verdict is a spread, not a pass/fail.

using System.Text.Json;
using Azure.AI.OpenAI;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PoSeeReview.Api.Features.Comics;

var provider = Arg("--provider") ?? "ollama";
var runs = int.Parse(Arg("--runs") ?? "3");
var casesPath = Path.Combine(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? "SCRIPTS", "prompt-eval.json");
var cases = JsonSerializer.Deserialize<List<EvalCase>>(File.ReadAllText(casesPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

var telemetry = new TelemetryClient(new TelemetryConfiguration());
var costs = new AiCostTracker(Options.Create(new AiPricingOptions()), telemetry);

IChatCompletionService chat;
string label;
if (provider == "azure")
{
    var options = new AzureOpenAIOptions
    {
        Endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("Set AZURE_OPENAI_ENDPOINT"),
        ApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY") ?? throw new InvalidOperationException("Set AZURE_OPENAI_API_KEY"),
        DeploymentName = Arg("--model") ?? new AzureOpenAIOptions().DeploymentName
    };
    chat = new AzureOpenAIChatService(
        new AzureOpenAIClient(new Uri(options.Endpoint), new Azure.AzureKeyCredential(options.ApiKey)),
        Options.Create(options), NullLogger<AzureOpenAIChatService>.Instance, telemetry, costs);
    label = $"azure/{options.DeploymentName}";
}
else
{
    var options = new OllamaOptions { ChatModel = Arg("--model") ?? new OllamaOptions().ChatModel };
    chat = new OllamaChatService(Options.Create(options), NullLogger<OllamaChatService>.Instance, costs);
    label = $"ollama/{options.ChatModel}";
}

Console.WriteLine($"{label}, {cases.Count} cases x {runs} runs\n");
Console.WriteLine($"{"case",-24} {"band",-8} {"scores",-16} {"spread",6} {"in band",8}");

int inBand = 0, failures = 0, calls = 0, ownScenes = 0, panels = 0, blunted = 0;
var spreads = new List<int>();

foreach (var c in cases)
{
    var scores = new List<int>();
    foreach (var _ in Enumerable.Range(0, runs))
    {
        calls++;
        try
        {
            var a = await chat.AnalyzeStrangenessAsync([.. c.Reviews]);
            var captions = ChatPrompts.NormalizeCaptions(a.Captions, a.Narrative, a.PanelCount);
            var scenes = ChatPrompts.NormalizeScenes(a.Scenes, captions, a.PanelCount);
            scores.Add(a.StrangenessScore);
            panels += a.PanelCount;
            ownScenes += scenes.Where((s, i) => s != captions[i]).Count(); // a scene that is not the caption fallback
            blunted += ComicImagePrompt.Build(a.Narrative, scenes).BluntedTerms.Count;
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"  {c.Name}: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
        }
    }

    if (scores.Count == 0) continue;
    var mean = scores.Average();
    var hit = mean >= c.Expect[0] && mean <= c.Expect[1];
    inBand += hit ? 1 : 0;
    spreads.Add(scores.Max() - scores.Min());
    Console.WriteLine($"{c.Name,-24} {$"{c.Expect[0]}-{c.Expect[1]}",-8} {string.Join(",", scores),-16} {spreads[^1],6} {(hit ? "yes" : "NO"),8}");
}

Console.WriteLine($"""

in band        {inBand}/{cases.Count}
mean spread    {(spreads.Count > 0 ? spreads.Average() : 0):0.0} points (run-to-run wobble; lower is steadier)
parse failures {failures}/{calls}
own scenes     {ownScenes}/{panels} panels (the rest fell back to the caption)
blunted terms  {blunted} across all runs (the image backstop still rewriting words)
""");

string? Arg(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

record EvalCase(string Name, int[] Expect, string[] Reviews);
