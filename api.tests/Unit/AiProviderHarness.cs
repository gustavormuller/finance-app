using System.Net;
using System.Text;
using System.Text.Json;
using Finance.Api.Application.Ai;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// What the 009 adapter tests share: hand-written fixtures, a handler that answers without
/// the network and keeps what was sent, and options with fake keys and the real base URLs.
/// </summary>
internal static class AiProviderHarness
{
    public const string AnthropicKey = "sk-ant-test-key-never-real";

    public const string OpenAiKey = "sk-openai-test-key-never-real";

    public static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Ai", name));

    public static Microsoft.Extensions.Options.IOptions<AiOptions> Options(string anthropicKey = AnthropicKey, string openAiKey = OpenAiKey)
    {
        var options = AiOptionsTests.Sound();
        options.Anthropic.ApiKey = anthropicKey;
        options.OpenAi.ApiKey = openAiKey;
        return Microsoft.Extensions.Options.Options.Create(options);
    }

    public static AiRequest Request(string model = "better") =>
        new(model, "Responda em pt-BR.", "{\"mes\":\"2026-08\"}", 1024);
}

/// <summary>One request as the adapter sent it, read before the adapter disposes it.</summary>
internal sealed record SentRequest(HttpMethod Method, Uri Uri, IReadOnlyDictionary<string, string> Headers, string? MediaType, string Body)
{
    public JsonElement Json => JsonDocument.Parse(Body).RootElement;
}

/// <summary>Answers with <paramref name="answer"/>, or throws what it throws; keeps each request.</summary>
internal sealed class AiHttpHandler(Func<HttpResponseMessage> answer) : HttpMessageHandler
{
    public AiHttpHandler(HttpStatusCode status, string body)
        : this(() => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") })
    {
    }

    public List<SentRequest> Requests { get; } = [];

    public HttpClient Client() => new(this);

    public SentRequest Single() => Assert.Single(Requests);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var headers = request.Headers
            .Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .ToDictionary(header => header.Key.ToLowerInvariant(), header => string.Join(",", header.Value));
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new SentRequest(request.Method, request.RequestUri!, headers, request.Content?.Headers.ContentType?.MediaType, body));
        var response = answer();
        response.RequestMessage = request;
        return response;
    }
}
