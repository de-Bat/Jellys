using Jellyfin.Plugin.Recap.Configuration;

namespace Jellyfin.Plugin.Recap.Llm;

public interface ILlmProviderFactory
{
    ILlmProvider Create(PluginConfiguration config);
}

public class LlmProviderFactory : ILlmProviderFactory
{
    private readonly IHttpClientFactory _httpClientFactory;

    public LlmProviderFactory(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public ILlmProvider Create(PluginConfiguration config)
    {
        var http = _httpClientFactory.CreateClient("Recap");
        http.Timeout = TimeSpan.FromSeconds(Math.Max(5, config.TimeoutSeconds));
        return config.Provider switch
        {
            LlmProviderKind.Gemini => new GeminiProvider(http, config),
            _ => new OpenAiCompatibleProvider(http, config)
        };
    }
}
