namespace Jellyfin.Plugin.Recap.Llm;

public interface ILlmProvider
{
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken);
}

public class LlmException : Exception
{
    public LlmException(string message)
        : base(message)
    {
    }
}
