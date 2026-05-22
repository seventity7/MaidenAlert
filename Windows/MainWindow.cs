using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace MaidenAlert.Windows;

public sealed class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin)
        : base("Maiden Alert##MaidenAlertMain", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.plugin = plugin;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(360, 230),
            MaximumSize = new Vector2(560, 310),
        };
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        var lang = plugin.CurrentLanguage;

        DrawCheckboxRow(MaidenText.DisableSound(lang), nameof(plugin.Configuration.DisableSound), plugin.Configuration.DisableSound, value => plugin.Configuration.DisableSound = value);
        DrawCheckboxRow(MaidenText.MessageAlert(lang), nameof(plugin.Configuration.MessageAlert), plugin.Configuration.MessageAlert, value => plugin.Configuration.MessageAlert = value);
        DrawSoundRow();
        DrawCheckboxRow(
            MaidenText.TrackOverlay(lang),
            nameof(plugin.Configuration.TrackOverlay),
            plugin.Configuration.TrackOverlay,
            value => plugin.Configuration.TrackOverlay = value,
            MaidenText.TrackOverlayTip(lang));
        DrawTrackerDistanceRow();

        ImGui.Spacing();

        if (ImGui.Button($"{MaidenText.TestNotification(lang)}##MaidenAlertTestNotification"))
            plugin.TriggerTestAlert();
    }

    private void DrawCheckboxRow(string label, string id, bool currentValue, Action<bool> setter, string? tooltip = null)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        DrawTooltipIfHovered(tooltip);
        ImGui.SameLine(160f);

        var value = currentValue;
        if (ImGui.Checkbox($"##{id}", ref value))
        {
            setter(value);
            plugin.Configuration.Save();
        }
    }

    private void DrawSoundRow()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(MaidenText.Sound(plugin.CurrentLanguage));
        ImGui.SameLine(160f);

        var selectedSound = Math.Clamp(plugin.Configuration.SoundId, Plugin.MinSoundEffectId, Plugin.MaxSoundEffectId);
        ImGui.SetNextItemWidth(75f);

        if (ImGui.BeginCombo("##MaidenAlertSoundId", selectedSound.ToString()))
        {
            for (var soundId = Plugin.MinSoundEffectId; soundId <= Plugin.MaxSoundEffectId; soundId++)
            {
                var isSelected = selectedSound == soundId;
                if (ImGui.Selectable(soundId.ToString(), isSelected))
                {
                    plugin.Configuration.SoundId = soundId;
                    plugin.Configuration.Save();
                    selectedSound = soundId;
                }

                if (isSelected)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();

        if (ImGui.Button($"{MaidenText.Test(plugin.CurrentLanguage)}##MaidenAlertSoundTest"))
            plugin.PlaySelectedSoundOnly();
    }

    private void DrawTrackerDistanceRow()
    {
        var lang = plugin.CurrentLanguage;
        var trackerDistanceTooltip = MaidenText.TrackerDistanceTip(lang);

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(MaidenText.TrackerDistance(lang));
        DrawTooltipIfHovered(trackerDistanceTooltip);
        ImGui.SameLine(160f);

        var distance = Math.Clamp(plugin.Configuration.TrackerDistance, Plugin.MinTrackerDistance, Plugin.MaxTrackerDistance);
        ImGui.SetNextItemWidth(135f);

        if (ImGui.SliderInt("##MaidenAlertTrackerDistance", ref distance, Plugin.MinTrackerDistance, Plugin.MaxTrackerDistance, "%dm"))
        {
            plugin.Configuration.TrackerDistance = distance;
            plugin.Configuration.Save();
        }

        DrawTooltipIfHovered(trackerDistanceTooltip);
    }

    private static void DrawTooltipIfHovered(string? tooltip)
    {
        if (string.IsNullOrWhiteSpace(tooltip) || !ImGui.IsItemHovered())
            return;

        ImGui.BeginTooltip();
        ImGui.TextUnformatted(tooltip);
        ImGui.EndTooltip();
    }
}
