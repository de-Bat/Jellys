using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jellyfin.Plugin.Recap.Configuration;

namespace Jellyfin.Plugin.Recap.Llm;

/// <summary>Talks to any /chat/completions endpoint (OpenAI, Ollama, LM Studio, vLLM, OpenRouter...).</summary>
public class OpenAiCompatibleProvider : ILlmProvider
{
    private readonly HttpClient _http;
    private readonly PluginConfiguration _config;

    public OpenAiCompatibleProvider(HttpClient http, PluginConfiguration config)
    {
        _http = http;
        _config = config;
    }

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        var url = _config.BaseUrl.TrimEnd('/') + "/chat/completions";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new
            {
                model = _config.Model,
                temperature = _config.Temperature,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                }
            })
        };
        if (!string.IsNullOrWhiteSpace(_config.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);
        }

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new LlmException($"LLM returned {(int)response.StatusCode}: {(body.Length <= 300 ? body : body[..300])}");
        }

        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        return (content ?? string.Empty).Trim();
    }
}
