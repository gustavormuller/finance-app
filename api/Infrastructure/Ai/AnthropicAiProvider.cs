using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Finance.Api.Application.Ai;
using Microsoft.Extensions.Options;

namespace Finance.Api.Infrastructure.Ai;

/// <summary>
/// Anthropic's Messages API: <c>POST v1/messages</c> with <c>x-api-key</c> and
/// <c>anthropic-version</c> headers, answering <c>content[]</c> blocks, a <c>stop_reason</c>
/// and <c>usage.input_tokens</c> / <c>output_tokens</c> (the billed counts).
/// </summary>
/// <remarks>
/// The body is <c>model</c>, <c>max_tokens</c>, <c>system</c> and one user message, nothing
/// more: <c>claude-opus-5</c> answers 400 to <c>temperature</c>, <c>top_p</c>, <c>top_k</c>, a
/// thinking budget or an assistant prefill. Its adaptive thinking can put <c>thinking</c>
/// blocks in <c>content</c>; only <c>text</c> blocks are read. An answer is a success only on
/// <c>end_turn</c> or <c>stop_sequence</c> with some text: <c>max_tokens</c> (truncated),
/// <c>refusal</c> and anything else fail with the tokens they billed. No retries: the gateway
/// records one usage row per call.
/// </remarks>
public sealed class AnthropicAiProvider(HttpClient http, IOptions<AiOptions> options) : IAiProvider
{
    public const string Name = "Anthropic";

    /// <summary>The API version this adapter is written against: a wire contract, not configuration.</summary>
    public const string ApiVersion = "2023-06-01";

    public async Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken ct)
    {
        var settings = options.Value.Anthropic;
        AiHttp.RequireKey(settings.ApiKey, Name, "Ai:Anthropic:ApiKey");

        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(settings.BaseUrl), "v1/messages"))
        {
            Content = JsonContent.Create(new
            {
                model = request.Model,
                max_tokens = request.MaxTokens,
                system = request.System,
                messages = new[] { new { role = "user", content = request.User } },
            }),
        };
        message.Headers.Add("x-api-key", settings.ApiKey);
        message.Headers.Add("anthropic-version", ApiVersion);

        using var response = await AiHttp.SendAsync(http, message, Name, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw await AiHttp.FailureAsync(response, Name, request, settings.ApiKey, ct);
        }

        var (text, stopReason, input, output) = await AiHttp.ReadAsync(response, Name, Read, ct);

        return stopReason switch
        {
            "end_turn" or "stop_sequence" when text.Length > 0 => new AiCompletion(text, input, output),
            "end_turn" or "stop_sequence" => throw new AiProviderException($"{Name} answered {stopReason} with no text.", input, output),
            "max_tokens" => throw new AiProviderException(
                $"{Name} stopped at max_tokens ({request.MaxTokens}); the answer is truncated.", input, output),
            _ => throw new AiProviderException($"{Name} stopped with stop_reason '{stopReason}'.", input, output),
        };
    }

    private static (string Text, string? StopReason, int Input, int Output) Read(JsonElement root)
    {
        var usage = root.GetProperty("usage");
        var text = new StringBuilder();
        foreach (var block in root.GetProperty("content").EnumerateArray())
        {
            if (block.GetProperty("type").GetString() == "text")
            {
                text.Append(block.GetProperty("text").GetString());
            }
        }

        return (text.ToString(), root.GetProperty("stop_reason").GetString(),
            usage.GetProperty("input_tokens").GetInt32(), usage.GetProperty("output_tokens").GetInt32());
    }
}
