namespace Jellyfin.Plugin.SubtitleSync.Core;

/// <summary>A single subtitle entry.</summary>
public sealed record SubtitleCue(int Index, TimeSpan Start, TimeSpan End, string Text);
