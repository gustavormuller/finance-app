using System.Net.Http.Headers;
using System.Text.Json;
using Finance.Api.Application.Ai;
using Microsoft.Extensions.Options;

namespace Finance.Api.Infrastructure.Ai;

/// <summary>
/// OpenAI's Chat Completions API: <c>POST v1/chat/completions</c> with
/// <c>Authorization: Bearer</c>, answering <c>choices[0].message</c>, a
/// <c>finish_reason</c> and <c>usage.prompt_tokens</c> / <c>completion_tokens</c> (the billed
/// counts; reasoning tokens are inside <c>completion_tokens</c>).
/// </summary>
/// <remarks>
/// The body is <c>model</c>, a system and a user message, and <c>max_completion_tokens</c>
/// (the reasoning models refuse the older <c>max_tokens</c>); no sampling parameters. An
/// answer is a success only on <c>stop</c> with some content and no <c>refusal</c>:
/// <c>length</c> (truncated), <c>content_filter</c>, a refusal and anything else fail with the
/// tokens they billed. No retries: the gateway records one usage row per call.
/// </remarks>
public sealed class OpenAiProvider(HttpClient http, IOptions<AiOptions> options) : IAiProvider
{
    public const string Name = "OpenAI";

    public async Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken ct)
    {
        var settings = options.Value.OpenAi;
        AiHttp.RequireKey(settings.ApiKey, Name, "Ai:OpenAi:ApiKey");

        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(settings.BaseUrl), "v1/chat/completions"))
        {
            Content = JsonContent.Create(new
            {
                model = request.Model,
                messages = new[]
                {
                    new { role = "system", content = request.System },
                    new { role = "user", content = request.User },
                },
                max_completion_tokens = request.MaxTokens,
            }),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        using var response = await AiHttp.SendAsync(http, message, Name, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw await AiHttp.FailureAsync(response, Name, request, settings.ApiKey, ct);
        }

        var (text, refusal, finishReason, input, output) = await AiHttp.ReadAsync(response, Name, Read, ct);

        if (refusal is not null)
        {
            throw new AiProviderException($"{Name} refused the request.", input, output);
        }

        return finishReason switch
        {
            "stop" when !string.IsNullOrEmpty(text) => new AiCompletion(text, input, output),
            "stop" => throw new AiProviderException($"{Name} answered stop with no content.", input, output),
            "length" => throw new AiProviderException(
                $"{Name} stopped at length ({request.MaxTokens} tokens); the answer is truncated.", input, output),
            _ => throw new AiProviderException($"{Name} stopped with finish_reason '{finishReason}'.", input, output),
        };
    }

    private static (string? Text, string? Refusal, string? FinishReason, int Input, int Output) Read(JsonElement root)
    {
        var usage = root.GetProperty("usage");
        var choice = root.GetProperty("choices")[0];
        var message = choice.GetProperty("message");
        var refusal = message.TryGetProperty("refusal", out var refused) ? refused.GetString() : null;

        return (message.GetProperty("content").GetString(), refusal, choice.GetProperty("finish_reason").GetString(),
            usage.GetProperty("prompt_tokens").GetInt32(), usage.GetProperty("completion_tokens").GetInt32());
    }
}
