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
            Timeout = TimeSpan.FromMilliseconds(settings.TimeoutMs)
        };
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public bool IsEnabled => _settings.Enabled;

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

    public async Task<string?> CompleteWithHistoryAsync(
        string systemPrompt,
        IReadOnlyList<(string role, string content)> messages,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            return null;
        }

        try
        {
            var request = new OllamaChatRequest
            {
                Model = _settings.Model,
                Stream = false,
                Options = new OllamaOptions
                {
                    Temperature = _settings.Temperature,
                    NumPredict = _settings.MaxTokens
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

            return result?.Message?.Content?.Trim();
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
        public required string Role { get; set; }
        public required string Content { get; set; }
    }

    private sealed class OllamaChatResponse
    {
        public OllamaMessage? Message { get; set; }
        public bool Done { get; set; }
    }
}
