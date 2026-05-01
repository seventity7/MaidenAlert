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
        DrawCheckboxRow("Disable sound", nameof(plugin.Configuration.DisableSound), plugin.Configuration.DisableSound, value => plugin.Configuration.DisableSound = value);
        DrawCheckboxRow("Message Alert", nameof(plugin.Configuration.MessageAlert), plugin.Configuration.MessageAlert, value => plugin.Configuration.MessageAlert = value);
        DrawSoundRow();
        DrawCheckboxRow(
            "Track overlay",
            nameof(plugin.Configuration.TrackOverlay),
            plugin.Configuration.TrackOverlay,
            value => plugin.Configuration.TrackOverlay = value,
            "Enable/Disable on-screen tracker overlay.");
        DrawTrackerDistanceRow();

        ImGui.Spacing();

        if (ImGui.Button("Test notification"))
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
        ImGui.TextUnformatted("Sound");
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

        if (ImGui.Button("Test##MaidenAlertSoundTest"))
            plugin.PlaySelectedSoundOnly();
    }

    private void DrawTrackerDistanceRow()
    {
        const string trackerDistanceTooltip = "Distance less than or equal to the chosen value,\nthe overlay disappears temporarily.";

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Tracker distance");
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
