using Dalamud.Configuration;
using System;

namespace MaidenAlert;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 3;

    public bool DisableSound { get; set; } = false;

    public bool MessageAlert { get; set; } = true;

    public bool TrackOverlay { get; set; } = true;

    public int TrackerDistance { get; set; } = 15;

    // UIGlobals.PlayChatSoundEffect accepts chat sound IDs from 1 to 16.
    // 8 keeps the same default sound used by the first Maiden Alert build.
    public int SoundId { get; set; } = 8;

    public void Validate()
    {
        SoundId = Math.Clamp(SoundId, 1, 16);
        TrackerDistance = Math.Clamp(TrackerDistance, Plugin.MinTrackerDistance, Plugin.MaxTrackerDistance);
    }

    public void Save()
    {
        Validate();
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
