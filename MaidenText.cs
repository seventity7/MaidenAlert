using Dalamud.Game;

namespace MaidenAlert;

internal static class MaidenText
{
    public static string AlertMessage(ClientLanguage lang) => lang switch
    {
        ClientLanguage.Japanese => "[Maiden Alert] フォーローン・メイデンがこのF.A.T.E.に出現しました！",
        ClientLanguage.German => "[Maiden Alert] Eine Verlorene Maid ist gerade in diesem FATE erschienen!",
        ClientLanguage.French => "[Maiden Alert] Une demoiselle délaissée vient d'apparaître dans cet ALÉA !",
        _ => "[Maiden Alert] A maiden has just spawned in this fate right now!",
    };

    public static string MaidenFallbackName(ClientLanguage lang) => lang switch
    {
        ClientLanguage.Japanese => "フォーローン・メイデン",
        ClientLanguage.German => "Verlorene Maid",
        ClientLanguage.French => "Demoiselle délaissée",
        _ => "Forlorn Maiden",
    };

    public static string Behind(ClientLanguage lang) => lang switch
    {
        ClientLanguage.Japanese => "後方",
        ClientLanguage.German => "hinten",
        ClientLanguage.French => "derrière",
        _ => "behind",
    };

    public static string DisableSound(ClientLanguage lang) => lang switch
    {
        ClientLanguage.Japanese => "サウンドを無効化",
        ClientLanguage.German => "Sound deaktivieren",
        ClientLanguage.French => "Désactiver le son",
        _ => "Disable sound",
    };

    public static string MessageAlert(ClientLanguage lang) => lang switch
    {
        ClientLanguage.Japanese => "チャット通知",
        ClientLanguage.German => "Chat-Benachrichtigung",
        ClientLanguage.French => "Notification dans le chat",
        _ => "Message Alert",
    };

    public static string Sound(ClientLanguage lang) => lang switch
    {
        ClientLanguage.Japanese => "サウンド",
        ClientLanguage.German => "Sound",
        ClientLanguage.French => "Son",
        _ => "Sound",
    };

    public static string Test(ClientLanguage lang) => lang switch
    {
        ClientLanguage.Japanese => "テスト",
        ClientLanguage.German => "Testen",
        ClientLanguage.French => "Tester",
        _ => "Test",
    };

    public static string TestNotification(ClientLanguage lang) => lang switch
    {
        ClientLanguage.Japanese => "通知をテスト",
        ClientLanguage.German => "Benachrichtigung testen",
        ClientLanguage.French => "Tester la notification",
        _ => "Test notification",
    };

    public static string TrackOverlay(ClientLanguage lang) => lang switch
    {
        ClientLanguage.Japanese => "トラッカーオーバーレイ",
        ClientLanguage.German => "Tracker-Overlay",
        ClientLanguage.French => "Overlay de suivi",
        _ => "Track overlay",
    };

    public static string TrackOverlayTip(ClientLanguage lang) => lang switch
    {
        ClientLanguage.Japanese => "画面上の追跡オーバーレイを有効/無効にします。",
        ClientLanguage.German => "Aktiviert/deaktiviert das Tracker-Overlay auf dem Bildschirm.",
        ClientLanguage.French => "Active/désactive l'overlay de suivi à l'écran.",
        _ => "Enable/Disable on-screen tracker overlay.",
    };

    public static string TrackerDistance(ClientLanguage lang) => lang switch
    {
        ClientLanguage.Japanese => "非表示距離",
        ClientLanguage.German => "Tracker-Distanz",
        ClientLanguage.French => "Distance du suivi",
        _ => "Tracker distance",
    };

    public static string TrackerDistanceTip(ClientLanguage lang) => lang switch
    {
        ClientLanguage.Japanese => "対象がこの距離以下になると、\nオーバーレイは一時的に非表示になります。",
        ClientLanguage.German => "Bei einer Entfernung kleiner oder gleich diesem Wert\nwird das Overlay vorübergehend ausgeblendet.",
        ClientLanguage.French => "Si la distance est inférieure ou égale à cette valeur,\nl'overlay disparaît temporairement.",
        _ => "Distance less than or equal to the chosen value,\nthe overlay disappears temporarily.",
    };

    public static string MarkerLabel(ClientLanguage lang, string? name, float distance, bool behind)
    {
        var n = string.IsNullOrWhiteSpace(name) ? MaidenFallbackName(lang) : name.Trim();
        return behind
            ? $"{n} - {distance:0}m - {Behind(lang)}"
            : $"{n} - {distance:0}m";
    }
}
