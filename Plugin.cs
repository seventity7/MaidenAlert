using System;
using System.Collections.Generic;
using Dalamud.Game.Chat;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.Toast;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using MaidenAlert.Windows;

namespace MaidenAlert;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandShort = "/malert";
    private const string CommandLong = "/maidenalert";
    private const string CommandTest = "/malerttest";
    private const uint MaidenSpawnLogMessageId = 2838;
    private const string AlertMessage = "[Maiden Alert] A maiden has just spawned in this fate right now!";

    public const int MinSoundEffectId = 1;
    public const int MaxSoundEffectId = 16;
    public const int MinTrackerDistance = 10;
    public const int MaxTrackerDistance = 40;

    // UIColor row used by UIForegroundPayload. This keeps the same chat color behavior from the current build.
    private const ushort PinkUIColor = 576;

    [PluginService]
    internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    internal static ICommandManager CommandManager { get; private set; } = null!;

    [PluginService]
    internal static IChatGui ChatGui { get; private set; } = null!;

    [PluginService]
    internal static IObjectTable ObjectTable { get; private set; } = null!;

    [PluginService]
    internal static IGameGui GameGui { get; private set; } = null!;

    [PluginService]
    internal static IToastGui ToastGui { get; private set; } = null!;

    [PluginService]
    internal static IPluginLog Log { get; private set; } = null!;

    public readonly WindowSystem WindowSystem = new("MaidenAlert");

    public Configuration Configuration { get; }

    private readonly MainWindow mainWindow;
    private readonly MaidenOverlayRenderer maidenOverlayRenderer;
    private DateTime lastAlertUtc = DateTime.MinValue;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Validate();

        mainWindow = new MainWindow(this);
        maidenOverlayRenderer = new MaidenOverlayRenderer(ObjectTable, GameGui);
        WindowSystem.AddWindow(mainWindow);

        CommandManager.AddHandler(CommandShort, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Maiden Alert window.",
        });

        CommandManager.AddHandler(CommandLong, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Maiden Alert window.",
        });

        CommandManager.AddHandler(CommandTest, new CommandInfo(OnTestCommand)
        {
            ShowInHelp = false,
        });

        ChatGui.LogMessage += OnLogMessage;
        ToastGui.Toast += OnToast;
        ToastGui.QuestToast += OnQuestToast;
        ToastGui.ErrorToast += OnErrorToast;

        PluginInterface.UiBuilder.Draw += DrawUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleMainUi;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= DrawUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleMainUi;

        ChatGui.LogMessage -= OnLogMessage;
        ToastGui.Toast -= OnToast;
        ToastGui.QuestToast -= OnQuestToast;
        ToastGui.ErrorToast -= OnErrorToast;

        CommandManager.RemoveHandler(CommandShort);
        CommandManager.RemoveHandler(CommandLong);
        CommandManager.RemoveHandler(CommandTest);

        WindowSystem.RemoveAllWindows();
        mainWindow.Dispose();
    }

    public void ToggleMainUi() => mainWindow.Toggle();

    public void TriggerTestAlert() => TriggerAlert(ignoreDuplicateGuard: true);

    public void PlaySelectedSoundOnly() => PlaySoundEffect(GetSelectedSoundId());

    private void DrawUi()
    {
        WindowSystem.Draw();
        maidenOverlayRenderer.Draw(Configuration.TrackOverlay, Configuration.TrackerDistance);
    }

    private void OnCommand(string command, string args) => ToggleMainUi();

    private void OnTestCommand(string command, string args) => TriggerTestAlert();

    private void OnLogMessage(ILogMessage message)
    {
        if (message.LogMessageId != MaidenSpawnLogMessageId)
            return;

        TriggerAlert(ignoreDuplicateGuard: false);
        maidenOverlayRenderer.StartTracking();
    }

    private void OnToast(ref SeString message, ref ToastOptions options, ref bool isHandled)
        => StopOverlayIfMaidenDissipated(message);

    private void OnQuestToast(ref SeString message, ref QuestToastOptions options, ref bool isHandled)
        => StopOverlayIfMaidenDissipated(message);

    private void OnErrorToast(ref SeString message, ref bool isHandled)
        => StopOverlayIfMaidenDissipated(message);

    private void StopOverlayIfMaidenDissipated(SeString message)
    {
        var text = message.TextValue;
        if (text.Contains("forlorn", StringComparison.OrdinalIgnoreCase) &&
            (text.Contains("dissipates", StringComparison.OrdinalIgnoreCase) ||
             text.Contains("disappears", StringComparison.OrdinalIgnoreCase) ||
             text.Contains("disappear", StringComparison.OrdinalIgnoreCase)))
        {
            maidenOverlayRenderer.StopTracking();
        }
    }

    private void TriggerAlert(bool ignoreDuplicateGuard)
    {
        if (!ignoreDuplicateGuard && IsDuplicateAlert())
            return;

        if (Configuration.MessageAlert)
            PrintAlertMessage();

        if (!Configuration.DisableSound)
            PlaySoundEffect(GetSelectedSoundId());
    }

    private bool IsDuplicateAlert()
    {
        var now = DateTime.UtcNow;
        if (now - lastAlertUtc < TimeSpan.FromSeconds(2))
            return true;

        lastAlertUtc = now;
        return false;
    }

    private int GetSelectedSoundId()
    {
        var soundId = Math.Clamp(Configuration.SoundId, MinSoundEffectId, MaxSoundEffectId);
        if (soundId != Configuration.SoundId)
        {
            Configuration.SoundId = soundId;
            Configuration.Save();
        }

        return soundId;
    }

    private static void PrintAlertMessage()
    {
        var message = new SeString(
            new UIForegroundPayload(PinkUIColor),
            new UIGlowPayload(PinkUIColor),
            BoldPayload(true),
            new TextPayload(AlertMessage),
            BoldPayload(false),
            UIGlowPayload.UIGlowOff,
            UIForegroundPayload.UIForegroundOff);

        ChatGui.Print(message);
    }

    private static unsafe void PlaySoundEffect(int soundId)
    {
        try
        {
            UIGlobals.PlayChatSoundEffect((uint)Math.Clamp(soundId, MinSoundEffectId, MaxSoundEffectId));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to play Maiden Alert sound effect.");
        }
    }

    private static RawPayload BoldPayload(bool enabled) => CreateMacroPayload(0x19, EncodeUIntExpression(enabled ? 1u : 0u));

    private static RawPayload CreateMacroPayload(byte macroCode, byte[] expressionBytes)
    {
        var lengthBytes = EncodeUIntExpression((uint)expressionBytes.Length);
        var payload = new byte[3 + lengthBytes.Length + expressionBytes.Length];

        payload[0] = 0x02;
        payload[1] = macroCode;
        Array.Copy(lengthBytes, 0, payload, 2, lengthBytes.Length);
        Array.Copy(expressionBytes, 0, payload, 2 + lengthBytes.Length, expressionBytes.Length);
        payload[^1] = 0x03;

        return new RawPayload(payload);
    }

    private static byte[] EncodeUIntExpression(uint value)
    {
        if (value < 0xCF)
            return new[] { (byte)(value + 1) };

        var bytes = new List<byte>(5);
        var type = 0xF0;

        if ((value & 0xFF000000) != 0)
            type |= 0x08;

        if ((value & 0x00FF0000) != 0)
            type |= 0x04;

        if ((value & 0x0000FF00) != 0)
            type |= 0x02;

        if ((value & 0x000000FF) != 0)
            type |= 0x01;

        bytes.Add((byte)(type - 1));

        var b = (byte)(value >> 24);
        if (b != 0)
            bytes.Add(b);

        b = (byte)(value >> 16);
        if (b != 0)
            bytes.Add(b);

        b = (byte)(value >> 8);
        if (b != 0)
            bytes.Add(b);

        b = (byte)value;
        if (b != 0)
            bytes.Add(b);

        return bytes.ToArray();
    }
}
