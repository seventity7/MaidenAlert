using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;
using Lumina.Excel;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace MaidenAlert;

public sealed class MaidenOverlayRenderer
{
    private const float SearchTimeoutSeconds = 20.0f;
    private const float MissingTimeoutSeconds = 3.0f;

    private const uint MaidenIconId = 60508;
    private const uint DirectionArrowIconId = 60541;
    private const string MaidenOverlayName = "Maiden Forlorn";

    private static readonly string[] MaidenNames =
    {
        "Forlorn Maiden",
        "Forlon Maiden",
        "The Forlorn",
    };

    private const int CompassRadius = 750;
    private const int IconScaleFactor = 100;
    private const int IconOpacity = 100;
    private const int SafeZoneOffsetWidth = 0;
    private const int SafeZoneOffsetHeight = 0;
    private const int CenterPointXOffset = 0;
    private const int CenterPointYOffset = 0;


    private readonly IObjectTable objectTable;
    private readonly IGameGui gameGui;
    private readonly HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<ClientLanguage> getLanguage;

    private bool tracking;
    private ulong trackedObjectId;
    private DateTime searchUntilUtc = DateTime.MinValue;
    private DateTime lastSeenUtc = DateTime.MinValue;

    private Vector2? stableScreenPosition;
    private Vector2? stableCompassPosition;
    private string cachedDistanceLabel = string.Empty;
    private int cachedDistanceYalms = -1;
    private ClientLanguage cachedDistanceLanguage = ClientLanguage.English;
    private DateTime lastDistanceLabelUpdateUtc = DateTime.MinValue;

    public MaidenOverlayRenderer(IObjectTable objectTable, IGameGui gameGui, IDataManager dataManager, Func<ClientLanguage> getLanguage)
    {
        this.objectTable = objectTable;
        this.gameGui = gameGui;
        this.getLanguage = getLanguage;

        foreach (var name in MaidenNames)
            this.names.Add(name);

        this.PullLocalizedNames(dataManager);
    }

    public void StartTracking()
    {
        tracking = true;
        trackedObjectId = 0;
        searchUntilUtc = DateTime.UtcNow.AddSeconds(SearchTimeoutSeconds);
        lastSeenUtc = DateTime.MinValue;
        ResetStabilizedState();
    }

    public void StopTracking()
    {
        tracking = false;
        trackedObjectId = 0;
        searchUntilUtc = DateTime.MinValue;
        lastSeenUtc = DateTime.MinValue;
        ResetStabilizedState();
    }

    public void Draw(bool overlayEnabled, float hideDistance, float overlayScale)
    {
        if (!tracking || gameGui.GameUiHidden)
            return;

        var player = objectTable.LocalPlayer;
        if (player == null || !player.IsValid())
            return;

        overlayScale = Math.Clamp(overlayScale, 0.50f, 2.00f);

        var maiden = ResolveTrackedMaiden(player);
        if (maiden == null)
        {
            stableScreenPosition = null;
            stableCompassPosition = null;
            return;
        }

        var distance = Vector2.Distance(
            new Vector2(player.Position.X, player.Position.Z),
            new Vector2(maiden.Position.X, maiden.Position.Z));

        var effectiveHideDistance = Math.Clamp(hideDistance, Plugin.MinTrackerDistance, Plugin.MaxTrackerDistance);
        if (!overlayEnabled || distance <= effectiveHideDistance)
            return;

        var drawPosition = maiden.Position + new Vector3(0f, MathF.Max(1.6f, maiden.HitboxRadius + 1.0f), 0f);

        if (gameGui.WorldToScreen(drawPosition, out var screenPosition, out var inView) && inView && IsFinite(screenPosition))
        {
            var stabilized = StabilizePosition(ref stableScreenPosition, SnapToPixel(screenPosition), 2.0f, 24f);
            DrawWorldMarker(ImGui.GetBackgroundDrawList(), stabilized, distance, overlayScale);
            stableCompassPosition = null;
            return;
        }

        stableScreenPosition = null;
        DrawCompassMarker(player.Position, drawPosition, distance, overlayScale);
    }

    private IGameObject? ResolveTrackedMaiden(IGameObject player)
    {
        var now = DateTime.UtcNow;
        IGameObject? current = null;

        if (trackedObjectId != 0)
            current = objectTable.SearchById(trackedObjectId);

        if (!IsValidMaiden(current))
            current = FindClosestMaiden(player);

        if (IsValidMaiden(current))
        {
            trackedObjectId = current!.GameObjectId;
            lastSeenUtc = now;
            return current;
        }

        if (lastSeenUtc == DateTime.MinValue && now <= searchUntilUtc)
            return null;

        if (lastSeenUtc != DateTime.MinValue && now - lastSeenUtc <= TimeSpan.FromSeconds(MissingTimeoutSeconds))
            return null;

        StopTracking();
        return null;
    }

    private IGameObject? FindClosestMaiden(IGameObject player)
    {
        IGameObject? best = null;
        var bestDistance = float.MaxValue;

        foreach (var obj in objectTable)
        {
            if (!IsValidMaiden(obj))
                continue;

            var distance = Vector3.Distance(player.Position, obj!.Position);
            if (distance >= bestDistance)
                continue;

            best = obj;
            bestDistance = distance;
        }

        return best;
    }

    private bool IsValidMaiden(IGameObject? obj)
    {
        if (obj == null || !obj.IsValid())
            return false;

        if (obj.ObjectKind != ObjectKind.BattleNpc)
            return false;

        if (obj.IsDead)
            return false;

        if (obj is ICharacter character && character.CurrentHp == 0)
            return false;

        return this.names.Contains(obj.Name.TextValue);
    }

    private void PullLocalizedNames(IDataManager dataManager)
    {
        try
        {
            var ids = new HashSet<uint>();
            var english = dataManager.GetExcelSheet<RawRow>(ClientLanguage.English, "BNpcName");

            foreach (var row in english)
            {
                var text = ReadBnpcName(row);
                foreach (var known in MaidenNames)
                {
                    if (string.Equals(text, known, StringComparison.OrdinalIgnoreCase))
                    {
                        ids.Add(row.RowId);
                        break;
                    }
                }
            }

            if (ids.Count == 0)
                return;

            foreach (var lang in Enum.GetValues<ClientLanguage>())
            {
                try
                {
                    var sheet = dataManager.GetExcelSheet<RawRow>(lang, "BNpcName");
                    foreach (var id in ids)
                    {
                        var name = ReadBnpcName(sheet.GetRow(id));
                        if (!string.IsNullOrWhiteSpace(name))
                            this.names.Add(name);
                    }
                }
                catch
                {
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Could not read Maiden BNpcName rows; using fallback names.");
        }
    }

    private static string ReadBnpcName(RawRow row)
    {
        try
        {
            return row.ReadStringColumn(0).ToString().Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    private void DrawWorldMarker(ImDrawListPtr drawList, Vector2 screenPosition, float distance, float overlayScale)
    {
        var visualScale = overlayScale * ImGuiHelpers.GlobalScale;
        var iconSize = 38f * (IconScaleFactor / 100f) * visualScale;
        var iconCenter = SnapToPixel(screenPosition - new Vector2(0f, 8f * visualScale));
        var opacity = IconOpacity / 100f;
        var pulse = GetPulse();

        DrawIcon(drawList, MaidenIconId, iconCenter, new Vector2(iconSize), opacity, pulse);

        var labelPosition = iconCenter + new Vector2(0f, iconSize * 0.62f + 12f * visualScale);

        DrawSoftLabel(
            drawList,
            MaidenOverlayName,
            labelPosition,
            new Vector4(1f, 1f, 1f, 1f),
            new Vector4(0f, 0f, 0f, 1f),
            opacity,
            1.12f * overlayScale,
            true);

        DrawSoftLabel(
            drawList,
            GetDistanceLabel(distance),
            labelPosition + new Vector2(0f, 18f * visualScale),
            new Vector4(1f, 1f, 1f, 1f),
            new Vector4(0f, 0f, 0f, 1f),
            opacity,
            1.12f * overlayScale,
            false);
    }

    private void DrawCompassMarker(Vector3 playerPosition, Vector3 markerPosition, float distance, float overlayScale)
    {
        var drawList = ImGui.GetBackgroundDrawList();
        var viewport = ImGui.GetMainViewport();
        var vpMin = viewport.Pos;
        var vpMax = viewport.Pos + viewport.Size;
        var visualScale = overlayScale * ImGuiHelpers.GlobalScale;
        var iconSize = 35f * (IconScaleFactor / 100f) * visualScale;
        var clampSize = iconSize * 2.5f;
        var opacity = IconOpacity / 100f;
        var pulse = GetPulse();

        Vector2 playerScreen;
        if (!gameGui.WorldToScreen(playerPosition, out playerScreen, out _))
            playerScreen = vpMin + viewport.Size / 2f;

        playerScreen += new Vector2(CenterPointXOffset, CenterPointYOffset);

        var direction = GetDirectionToTarget(playerPosition, markerPosition, playerScreen);
        var iconPos = playerScreen + direction * CompassRadius;
        iconPos.X = Math.Clamp(iconPos.X, vpMin.X + clampSize + SafeZoneOffsetWidth, vpMax.X - clampSize - SafeZoneOffsetWidth);
        iconPos.Y = Math.Clamp(iconPos.Y, vpMin.Y + clampSize + SafeZoneOffsetHeight, vpMax.Y - clampSize - SafeZoneOffsetHeight);
        iconPos = StabilizePosition(ref stableCompassPosition, SnapToPixel(iconPos), 1.5f, 30f);

        DrawIcon(drawList, MaidenIconId, iconPos, new Vector2(iconSize), opacity, pulse);

        var angle = MathF.Atan2(direction.Y, direction.X);
        var arrowSize = 23f * (IconScaleFactor / 100f) * visualScale;
        var arrowCenter = iconPos + direction * (iconSize * 0.75f + arrowSize * 0.55f);
        DrawRotatedIcon(drawList, DirectionArrowIconId, SnapToPixel(arrowCenter), new Vector2(arrowSize * 2f), angle, opacity, pulse);

        var labelPosition = iconPos + new Vector2(0f, iconSize * 0.75f + 14f * visualScale);

        DrawSoftLabel(
            drawList,
            MaidenOverlayName,
            labelPosition,
            new Vector4(1f, 1f, 1f, 1f),
            new Vector4(0f, 0f, 0f, 1f),
            opacity,
            1.12f * overlayScale,
            true);

        DrawSoftLabel(
            drawList,
            GetDistanceLabel(distance),
            labelPosition + new Vector2(0f, 18f * visualScale),
            new Vector4(1f, 1f, 1f, 1f),
            new Vector4(0f, 0f, 0f, 1f),
            opacity,
            1.12f * overlayScale,
            false);
    }

    private Vector2 GetDirectionToTarget(Vector3 playerPosition, Vector3 markerPosition, Vector2 playerScreen)
    {
        if (gameGui.WorldToScreen(markerPosition, out var markerScreen, out _) && IsFinite(markerScreen))
        {
            var projected = markerScreen - playerScreen;
            if (projected.LengthSquared() > 1f)
                return Vector2.Normalize(projected);
        }

        var delta = markerPosition - playerPosition;
        var targetAngle = MathF.Atan2(delta.X, delta.Z);
        var cameraDirection = TryGetCameraHorizontalDirection(out var cameraDirH)
            ? cameraDirH
            : objectTable.LocalPlayer?.Rotation ?? 0f;

        var relativeAngle = NormalizeRadians(targetAngle - cameraDirection);
        var direction = new Vector2(MathF.Sin(relativeAngle), -MathF.Cos(relativeAngle));

        return direction.LengthSquared() < 0.001f ? new Vector2(0f, -1f) : Vector2.Normalize(direction);
    }

    private void DrawIcon(ImDrawListPtr drawList, uint iconId, Vector2 center, Vector2 size, float opacity, float pulse)
    {
        var min = center - size / 2f;
        var max = center + size / 2f;
        var color = ApplyAlpha(0xFFFFFFFF, opacity);
        var wrap = GetIcon(iconId);

        if (wrap != null)
        {
            DrawIconGlow(drawList, wrap.Handle, center, size, opacity, pulse);
            drawList.AddImage(wrap.Handle, min, max, Vector2.Zero, Vector2.One, color);
        }
    }

    private void DrawRotatedIcon(ImDrawListPtr drawList, uint iconId, Vector2 center, Vector2 size, float rotation, float opacity, float pulse)
    {
        var wrap = GetIcon(iconId);
        if (wrap == null)
            return;

        DrawRotatedIconGlow(drawList, wrap.Handle, center, size, rotation, opacity, pulse);

        var half = size / 2f;
        var corners = new[] {
            new Vector2(-half.X, -half.Y),
            new Vector2(half.X, -half.Y),
            new Vector2(half.X, half.Y),
            new Vector2(-half.X, half.Y),
        };

        var cos = MathF.Cos(rotation);
        var sin = MathF.Sin(rotation);

        for (var i = 0; i < corners.Length; i++)
        {
            var c = corners[i];
            corners[i] = center + new Vector2(c.X * cos - c.Y * sin, c.X * sin + c.Y * cos);
        }

        drawList.AddImageQuad(
            wrap.Handle,
            corners[0],
            corners[1],
            corners[2],
            corners[3],
            Vector2.UnitY,
            Vector2.Zero,
            Vector2.UnitX,
            Vector2.One,
            ApplyAlpha(0xFFFFFFFF, opacity));
    }


    private static float GetPulse()
    {
        var t = (float)ImGui.GetTime();
        return 0.5f + 0.5f * MathF.Sin(t * 4.2f);
    }

    private static Vector4 GlowColor(float opacity, float pulse)
        => new(1f, 0.18f, 0.76f, (0.16f + 0.16f * pulse) * opacity);

    private static void DrawIconGlow(ImDrawListPtr drawList, dynamic textureHandle, Vector2 center, Vector2 size, float opacity, float pulse)
    {
        var glow = ImGui.GetColorU32(GlowColor(opacity, pulse));

        for (var layer = 3; layer >= 1; layer--)
        {
            var grow = size * (0.16f + layer * 0.11f + pulse * 0.07f);
            var half = (size + grow) / 2f;
            drawList.AddImage(textureHandle, center - half, center + half, Vector2.Zero, Vector2.One, glow);
        }
    }

    private static void DrawRotatedIconGlow(ImDrawListPtr drawList, dynamic textureHandle, Vector2 center, Vector2 size, float rotation, float opacity, float pulse)
    {
        var glow = ImGui.GetColorU32(GlowColor(opacity, pulse));

        for (var layer = 3; layer >= 1; layer--)
        {
            var grow = size * (0.16f + layer * 0.11f + pulse * 0.07f);
            DrawRotatedImageQuad(drawList, textureHandle, center, size + grow, rotation, glow);
        }
    }

    private static void DrawRotatedImageQuad(ImDrawListPtr drawList, dynamic textureHandle, Vector2 center, Vector2 size, float rotation, uint color)
    {
        var half = size / 2f;
        var corners = new[] {
            new Vector2(-half.X, -half.Y),
            new Vector2(half.X, -half.Y),
            new Vector2(half.X, half.Y),
            new Vector2(-half.X, half.Y),
        };

        var cos = MathF.Cos(rotation);
        var sin = MathF.Sin(rotation);

        for (var i = 0; i < corners.Length; i++)
        {
            var c = corners[i];
            corners[i] = center + new Vector2(c.X * cos - c.Y * sin, c.X * sin + c.Y * cos);
        }

        drawList.AddImageQuad(
            textureHandle,
            corners[0],
            corners[1],
            corners[2],
            corners[3],
            Vector2.UnitY,
            Vector2.Zero,
            Vector2.UnitX,
            Vector2.One,
            color);
    }

    private static void DrawDirectionTriangle(ImDrawListPtr drawList, Vector2 center, float angle, float size, uint color)
    {
        var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var perpendicular = new Vector2(-direction.Y, direction.X);
        var tip = center + direction * (size * 0.45f);
        var baseCenter = center - direction * (size * 0.25f);

        drawList.AddTriangleFilled(
            tip,
            baseCenter + perpendicular * (size * 0.25f),
            baseCenter - perpendicular * (size * 0.25f),
            color);
    }

    private static void DrawSoftLabel(ImDrawListPtr drawList, string text, Vector2 center, Vector4 textColor, Vector4 shadowColor, float opacity, float fontScale = 1.0f, bool bold = false)
    {
        center = SnapToPixel(center);

        var font = ImGui.GetFont();
        var fontSize = ImGui.GetFontSize() * Math.Clamp(fontScale, 0.75f, 2.00f);
        var textSize = ImGui.CalcTextSize(text) * Math.Clamp(fontScale, 0.75f, 2.00f);
        var pos = SnapToPixel(center - textSize / 2f);

        var shadow = shadowColor;
        shadow.W *= opacity;

        for (var layer = 4; layer >= 1; layer--)
        {
            var radius = MathF.Round(layer * 1.3f * ImGuiHelpers.GlobalScale);
            var alpha = shadow.W * (0.16f / layer);
            var c = ImGui.GetColorU32(new Vector4(shadow.X, shadow.Y, shadow.Z, alpha));

            drawList.AddText(font, fontSize, pos + new Vector2(radius, 0f), c, text);
            drawList.AddText(font, fontSize, pos + new Vector2(-radius, 0f), c, text);
            drawList.AddText(font, fontSize, pos + new Vector2(0f, radius), c, text);
            drawList.AddText(font, fontSize, pos + new Vector2(0f, -radius), c, text);
        }

        DrawThinBlackOutlineText(drawList, font, fontSize, pos, text, opacity);

        var color = textColor;
        color.W *= opacity;
        var finalColor = ImGui.GetColorU32(color);
        drawList.AddText(font, fontSize, pos, finalColor, text);

        if (bold)
        {
            var boldStep = MathF.Max(1f, MathF.Round(ImGuiHelpers.GlobalScale));
            drawList.AddText(font, fontSize, pos + new Vector2(boldStep, 0f), finalColor, text);
            drawList.AddText(font, fontSize, pos + new Vector2(0f, boldStep * 0.45f), finalColor, text);
        }
    }

    private static void DrawThinBlackOutlineText(ImDrawListPtr drawList, ImFontPtr font, float fontSize, Vector2 pos, string text, float opacity)
    {
        var outlineColor = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.92f * opacity));
        var outline = MathF.Max(1f, MathF.Round(ImGuiHelpers.GlobalScale));

        drawList.AddText(font, fontSize, pos + new Vector2(-outline, 0f), outlineColor, text);
        drawList.AddText(font, fontSize, pos + new Vector2(outline, 0f), outlineColor, text);
        drawList.AddText(font, fontSize, pos + new Vector2(0f, -outline), outlineColor, text);
        drawList.AddText(font, fontSize, pos + new Vector2(0f, outline), outlineColor, text);
    }

    private string GetDistanceLabel(float distance)
    {
        var yalms = (int)MathF.Ceiling(distance);
        var lang = getLanguage();
        var now = DateTime.UtcNow;

        if ((cachedDistanceYalms != yalms || cachedDistanceLanguage != lang) &&
            (cachedDistanceYalms < 0 || now - lastDistanceLabelUpdateUtc >= TimeSpan.FromMilliseconds(250)))
        {
            cachedDistanceYalms = yalms;
            cachedDistanceLanguage = lang;
            cachedDistanceLabel = MaidenText.DistanceYalms(lang, yalms);
            lastDistanceLabelUpdateUtc = now;
        }

        return string.IsNullOrEmpty(cachedDistanceLabel) ? MaidenText.DistanceYalms(lang, yalms) : cachedDistanceLabel;
    }

    private void ResetStabilizedState()
    {
        stableScreenPosition = null;
        stableCompassPosition = null;
        cachedDistanceLabel = string.Empty;
        cachedDistanceYalms = -1;
        cachedDistanceLanguage = ClientLanguage.English;
        lastDistanceLabelUpdateUtc = DateTime.MinValue;
    }

    private static Vector2 StabilizePosition(ref Vector2? previous, Vector2 target, float deadzonePixels, float snapDistancePixels)
    {
        target = SnapToPixel(target);

        if (previous == null)
        {
            previous = target;
            return target;
        }

        var current = previous.Value;
        var delta = target - current;
        var distanceSquared = delta.LengthSquared();

        if (distanceSquared <= deadzonePixels * deadzonePixels)
            return SnapToPixel(current);

        if (distanceSquared >= snapDistancePixels * snapDistancePixels)
        {
            previous = target;
            return target;
        }

        var alpha = 1f - MathF.Exp(-22f * ImGui.GetIO().DeltaTime);
        current += delta * Math.Clamp(alpha, 0.08f, 0.55f);
        current = SnapToPixel(current);
        previous = current;
        return current;
    }

    private static Vector2 SnapToPixel(Vector2 position)
        => new(MathF.Round(position.X), MathF.Round(position.Y));

    private static unsafe bool TryGetCameraHorizontalDirection(out float direction)
    {
        try
        {
            var cameraManager = CameraManager.Instance();
            var activeCamera = cameraManager != null ? cameraManager->GetActiveCamera() : null;
            if (activeCamera == null)
            {
                direction = 0f;
                return false;
            }

            direction = activeCamera->DirH;
            return !float.IsNaN(direction) && !float.IsInfinity(direction);
        }
        catch
        {
            direction = 0f;
            return false;
        }
    }

    private static float NormalizeRadians(float angle)
    {
        while (angle > MathF.PI)
            angle -= MathF.PI * 2f;

        while (angle < -MathF.PI)
            angle += MathF.PI * 2f;

        return angle;
    }

    private static bool IsFinite(Vector2 vector)
        => !float.IsNaN(vector.X) && !float.IsNaN(vector.Y) && !float.IsInfinity(vector.X) && !float.IsInfinity(vector.Y);

    private static uint ApplyAlpha(uint color, float opacity)
    {
        opacity = Math.Clamp(opacity, 0f, 1f);
        var alpha = (byte)Math.Clamp(((color >> 24) & 0xFF) * opacity, 0, 255);
        return (color & 0x00FFFFFF) | ((uint)alpha << 24);
    }

    private static dynamic? GetIcon(uint iconId)
    {
        try
        {
            return Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup
            {
                IconId = iconId,
                HiRes = true,
            }).GetWrapOrDefault();
        }
        catch
        {
            return null;
        }
    }
}
