using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.SubtitleSync;

/// <summary>Subtitle sync plugin: runtime offset adjustment and automatic sync against a reference subtitle.</summary>
public class Plugin : BasePlugin<BasePluginConfiguration>
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
    }

    public override string Name => "Subtitle Sync";

    public override Guid Id => Guid.Parse("6f1c2a54-3b0e-4d57-9a6e-5d1b8f2c7e31");

    public override string Description => "Adjust subtitle offset on demand or sync automatically against another subtitle.";
}
