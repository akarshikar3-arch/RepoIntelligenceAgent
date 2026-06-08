using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RepoIntel.Application.Abstractions;
using RepoIntel.Infrastructure.Configuration;

namespace RepoIntel.Infrastructure.Llm;

/// <summary>
/// Groq Cloud LLM provider. Talks to the OpenAI-compatible
/// /openai/v1/chat/completions endpoint with stream=true and surfaces the
/// streamed deltas as <see cref="TokenChunk"/>s.
/// </summary>
public sealed class GroqLlmProvider : ILlmProvider
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IOptions<AiOptions> _options;
    private readonly ILogger<GroqLlmProvider> _logger;

    public GroqLlmProvider(
        IHttpClientFactory httpFactory,
        IOptions<AiOptions> options,
        ILogger<GroqLlmProvider> logger)
    {
        _httpFactory = httpFactory;
        _options = options;
        _logger = logger;
    }

    public async IAsyncEnumerable<TokenChunk> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        LlmOptions options,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var llm = _options.Value.Llm;
        if (string.IsNullOrWhiteSpace(llm.ApiKey))
            throw new InvalidOperationException(
                "AI:Llm:ApiKey is not configured. Set it in appsettings.Development.json or via environment variable.");

        var baseUrl = string.IsNullOrWhiteSpace(llm.BaseUrl)
            ? "https://api.groq.com/openai/v1"
            : llm.BaseUrl.TrimEnd('/');
        var model = options.Model ?? llm.Model;

        using var client = _httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(2);

        var payload = new GroqChatRequest
        {
            Model = model,
            Temperature = options.Temperature,
            MaxTokens = options.MaxTokens,
            Stream = true,
            Messages = messages.Select(m => new GroqChatMessage(m.Role, m.Content)).ToArray(),
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions")
        {
            Content = JsonContent.Create(payload, options: SerializerOptions),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", llm.ApiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogError("Groq returned {Status}: {Body}", (int)resp.StatusCode, Truncate(body, 500));
            throw new InvalidOperationException($"Groq HTTP {(int)resp.StatusCode}: {Truncate(body, 200)}");
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line[5..].Trim();
            if (data == "[DONE]") { yield return new TokenChunk(string.Empty, IsFinal: true); yield break; }

            string? delta = null;
            try
            {
                using var doc = JsonDocument.Parse(data);
                if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                    choices.GetArrayLength() > 0 &&
                    choices[0].TryGetProperty("delta", out var deltaEl) &&
                    deltaEl.TryGetProperty("content", out var contentEl) &&
                    contentEl.ValueKind == JsonValueKind.String)
                {
                    delta = contentEl.GetString();
                }
            }
            catch (JsonException ex)
            {
                _logger.LogDebug(ex, "Skipping malformed SSE chunk: {Data}", Truncate(data, 200));
                continue;
            }

            if (!string.IsNullOrEmpty(delta))
                yield return new TokenChunk(delta);
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private sealed record GroqChatMessage(string Role, string Content);

    private sealed class GroqChatRequest
    {
        public string Model { get; set; } = "";
        public GroqChatMessage[] Messages { get; set; } = Array.Empty<GroqChatMessage>();
        public double Temperature { get; set; }
        [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; }
        public bool Stream { get; set; }
    }
}
