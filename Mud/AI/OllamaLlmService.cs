using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using JitRealm.Mud.Configuration;

namespace JitRealm.Mud.AI;

/// <summary>
/// LLM service implementation using local Ollama.
/// </summary>
public sealed class OllamaLlmService : ILlmService, IDisposable
{
    private readonly LlmSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public OllamaLlmService(LlmSettings settings)
    {
        _settings = settings;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(settings.OllamaUrl),
            // Use the larger of NPC/story timeouts (story generations can take longer).
            Timeout = TimeSpan.FromMilliseconds(Math.Max(settings.TimeoutMs, settings.StoryTimeoutMs))
        };

        // Add API key authentication if configured (for cloud endpoints)
        var apiKey = settings.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // Fall back to environment variable
            apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        }
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        }

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public bool IsEnabled => _settings.Enabled;

    public bool IsEmbeddingEnabled => _settings.Enabled && !string.IsNullOrWhiteSpace(_settings.EmbeddingModel);

    public async Task<string?> CompleteAsync(
        string systemPrompt,
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<(string role, string content)>
        {
            ("user", userMessage)
        };
        return await CompleteWithHistoryAsync(systemPrompt, messages, cancellationToken);
    }

    public async Task<string?> CompleteAsync(
        string systemPrompt,
        string userMessage,
        LlmProfile profile,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<(string role, string content)>
        {
            ("user", userMessage)
        };
        return await CompleteWithHistoryAsync(systemPrompt, messages, profile, cancellationToken);
    }

    public async Task<string?> CompleteWithHistoryAsync(
        string systemPrompt,
        IReadOnlyList<(string role, string content)> messages,
        CancellationToken cancellationToken = default)
    {
        return await CompleteWithHistoryAsync(systemPrompt, messages, LlmProfile.Npc, cancellationToken);
    }

    public async Task<string?> CompleteWithHistoryAsync(
        string systemPrompt,
        IReadOnlyList<(string role, string content)> messages,
        LlmProfile profile,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            return null;
        }

        var (model, temperature, maxTokens) = profile switch
        {
            LlmProfile.Story => (
                string.IsNullOrWhiteSpace(_settings.StoryModel) ? _settings.Model : _settings.StoryModel,
                _settings.StoryTemperature,
                _settings.StoryMaxTokens),
            _ => (_settings.Model, _settings.Temperature, _settings.MaxTokens)
        };

        try
        {
            var request = new OllamaChatRequest
            {
                Model = model,
                Stream = false,
                Options = new OllamaOptions
                {
                    Temperature = temperature,
                    NumPredict = maxTokens
                },
                Messages = BuildMessages(systemPrompt, messages)
            };

            var response = await _httpClient.PostAsJsonAsync(
                "/api/chat",
                request,
                _jsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                Console.WriteLine($"[LLM] Ollama error: {response.StatusCode} - {error}");
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(
                _jsonOptions,
                cancellationToken);

            // Try message.content first, then fall back to response field
            var content = result?.Message?.Content ?? result?.Response;
            return content?.Trim();
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine("[LLM] Request timed out");
            return null;
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"[LLM] Connection error: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LLM] Error: {ex.Message}");
            return null;
        }
    }

    private static List<OllamaMessage> BuildMessages(
        string systemPrompt,
        IReadOnlyList<(string role, string content)> messages)
    {
        var result = new List<OllamaMessage>
        {
            new() { Role = "system", Content = systemPrompt }
        };

        foreach (var (role, content) in messages)
        {
            result.Add(new OllamaMessage { Role = role, Content = content });
        }

        return result;
    }

    public async Task<float[]?> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        if (!IsEmbeddingEnabled)
        {
            return null;
        }

        try
        {
            var request = new OllamaEmbedRequest
            {
                Model = _settings.EmbeddingModel,
                Input = text
            };

            var response = await _httpClient.PostAsJsonAsync(
                "/api/embed",
                request,
                _jsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                Console.WriteLine($"[LLM] Ollama embed error: {response.StatusCode} - {error}");
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(
                _jsonOptions,
                cancellationToken);

            // Ollama returns embeddings as array of arrays (for batch support)
            // We only send one input, so take the first embedding
            if (result?.Embeddings is { Count: > 0 })
            {
                return result.Embeddings[0];
            }

            return null;
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine("[LLM] Embed request timed out");
            return null;
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"[LLM] Embed connection error: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LLM] Embed error: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    // Ollama API request/response types
    private sealed class OllamaChatRequest
    {
        public required string Model { get; set; }
        public bool Stream { get; set; }
        public OllamaOptions? Options { get; set; }
        public required List<OllamaMessage> Messages { get; set; }
    }

    private sealed class OllamaOptions
    {
        public double Temperature { get; set; }
        public int NumPredict { get; set; }
    }

    private sealed class OllamaMessage
    {
        public string? Role { get; set; }
        public string? Content { get; set; }
    }

    private sealed class OllamaChatResponse
    {
        public OllamaMessage? Message { get; set; }
        public bool Done { get; set; }
        // Some models/APIs return response directly instead of message.content
        public string? Response { get; set; }
    }

    private sealed class OllamaEmbedRequest
    {
        public required string Model { get; set; }
        public required string Input { get; set; }
    }

    private sealed class OllamaEmbedResponse
    {
        public List<float[]>? Embeddings { get; set; }
    }
}
