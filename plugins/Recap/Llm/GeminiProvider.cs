using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.Recap.Configuration;

namespace Jellyfin.Plugin.Recap.Llm;

public class GeminiProvider : ILlmProvider
{
    private const string DefaultRoot = "https://generativelanguage.googleapis.com/v1beta";

    private readonly HttpClient _http;
    private readonly PluginConfiguration _config;

    public GeminiProvider(HttpClient http, PluginConfiguration config)
    {
        _http = http;
        _config = config;
    }

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        var root = _config.BaseUrl.Contains("googleapis", StringComparison.OrdinalIgnoreCase)
            ? _config.BaseUrl.TrimEnd('/')
            : DefaultRoot;
        var url = $"{root}/models/{Uri.EscapeDataString(_config.Model)}:generateContent";

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new
            {
                systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
                contents = new[] { new { role = "user", parts = new[] { new { text = userPrompt } } } },
                generationConfig = new { temperature = _config.Temperature }
            })
        };
        request.Headers.Add("x-goog-api-key", _config.ApiKey);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new LlmException($"Gemini returned {(int)response.StatusCode}: {(body.Length <= 300 ? body : body[..300])}");
        }

        using var doc = JsonDocument.Parse(body);
        var sb = new StringBuilder();
        foreach (var part in doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts").EnumerateArray())
        {
            if (part.TryGetProperty("text", out var t))
            {
                sb.Append(t.GetString());
            }
        }

        return sb.ToString().Trim();
    }
}
