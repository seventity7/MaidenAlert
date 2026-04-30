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
            MinimumSize = new Vector2(320, 180),
            MaximumSize = new Vector2(500, 240),
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

        ImGui.Spacing();

        if (ImGui.Button("Test notification"))
            plugin.TriggerTestAlert();
    }

    private void DrawCheckboxRow(string label, string id, bool currentValue, Action<bool> setter)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
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
}
