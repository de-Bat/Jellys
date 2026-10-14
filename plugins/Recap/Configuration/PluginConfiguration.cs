using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Recap.Configuration;

public enum LlmProviderKind
{
    /// <summary>Any OpenAI-compatible chat completions API: OpenAI, Ollama, LM Studio, llama.cpp, vLLM, OpenRouter...</summary>
    OpenAiCompatible = 0,

    /// <summary>Google Gemini generateContent API.</summary>
    Gemini = 1
}

public class PluginConfiguration : BasePluginConfiguration
{
    public LlmProviderKind Provider { get; set; } = LlmProviderKind.OpenAiCompatible;

    /// <summary>Base URL up to and including /v1 (OpenAI-compatible). Ignored by Gemini unless it is a googleapis URL.</summary>
    public string BaseUrl { get; set; } = "http://localhost:11434/v1";

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "llama3.1";

    public double Temperature { get; set; } = 0.3;

    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>Max transcript characters sent to the model. Oldest text is dropped first.</summary>
    public int MaxTranscriptChars { get; set; } = 24000;

    /// <summary>System prompt. {language} is replaced by the output language.</summary>
    public string SystemPrompt { get; set; } =
        "You write spoiler-safe recaps of TV episodes. You are given the subtitle transcript of an episode up to the point the viewer stopped watching. " +
        "Summarize what has happened so far in 3-6 short sentences: who is involved, what is going on, and any open questions. " +
        "Never speculate about or mention anything beyond the transcript. Write in {language}.";

    /// <summary>Output language for the recap. Phase 2 lets clients override per request.</summary>
    public string DefaultLanguage { get; set; } = "English";
}
