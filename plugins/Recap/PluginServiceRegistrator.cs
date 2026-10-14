using Jellyfin.Plugin.Recap.Llm;
using Jellyfin.Plugin.Recap.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Recap;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHttpClient("Recap");
        serviceCollection.AddSingleton<ILlmProviderFactory, LlmProviderFactory>();
        serviceCollection.AddSingleton<TranscriptService>();
        serviceCollection.AddSingleton<RecapStore>();
        serviceCollection.AddSingleton<RecapQueue>();
        serviceCollection.AddSingleton<RecapService>();
        serviceCollection.AddHostedService<RecapWorker>();
    }
}
