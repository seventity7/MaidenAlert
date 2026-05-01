using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace MaidenAlert;

public sealed class MaidenOverlayRenderer
{
    private const float HideDistance = 15.0f;
    private const float SearchTimeoutSeconds = 20.0f;
    private const float MissingTimeoutSeconds = 3.0f;
    private const float OffscreenPreferredInset = 500.0f;
    private const float OffscreenMaxViewportFactor = 0.30f;
    private const float OffscreenMinimumCenterDistance = 90.0f;
    private const float OffscreenMinimumEdgeMargin = 42.0f;

    private static readonly string[] MaidenNames =
    {
        "Forlorn Maiden",
        "Forlon Maiden",
        "The Forlorn",
    };

    private readonly IObjectTable objectTable;
    private readonly IGameGui gameGui;

    private bool tracking;
    private ulong trackedObjectId;
    private DateTime searchUntilUtc = DateTime.MinValue;
    private DateTime lastSeenUtc = DateTime.MinValue;

    public MaidenOverlayRenderer(IObjectTable objectTable, IGameGui gameGui)
    {
        this.objectTable = objectTable;
        this.gameGui = gameGui;
    }

    public void StartTracking()
    {
        this.tracking = true;
        this.trackedObjectId = 0;
        this.searchUntilUtc = DateTime.UtcNow.AddSeconds(SearchTimeoutSeconds);
        this.lastSeenUtc = DateTime.MinValue;
    }

    public void StopTracking()
    {
        this.tracking = false;
        this.trackedObjectId = 0;
        this.searchUntilUtc = DateTime.MinValue;
        this.lastSeenUtc = DateTime.MinValue;
    }

    public void Draw()
    {
        if (!this.tracking || this.gameGui.GameUiHidden)
            return;

        var player = this.objectTable.LocalPlayer;
        if (player == null || !player.IsValid())
            return;

        var maiden = this.ResolveTrackedMaiden(player);
        if (maiden == null)
            return;

        var distance = Vector3.Distance(player.Position, maiden.Position);
        if (distance <= HideDistance)
            return;

        if (!this.TryGetMarkerPosition(player, maiden, out var markerPosition, out var clampedToEdge, out var isBehindCamera))
            return;

        this.DrawMarker(markerPosition, distance, clampedToEdge, isBehindCamera);
    }

    private IGameObject? ResolveTrackedMaiden(IGameObject player)
    {
        var now = DateTime.UtcNow;
        IGameObject? current = null;

        if (this.trackedObjectId != 0)
            current = this.objectTable.SearchById(this.trackedObjectId);

        if (!IsValidMaiden(current))
            current = this.FindClosestMaiden(player);

        if (IsValidMaiden(current))
        {
            this.trackedObjectId = current!.GameObjectId;
            this.lastSeenUtc = now;
            return current;
        }

        // The game can emit the spawn log message a little before the object enters the object table.
        if (this.lastSeenUtc == DateTime.MinValue && now <= this.searchUntilUtc)
            return null;

        // If the object was already seen and then disappears from the object table, treat it as defeated/despawned
        // after a small grace period to avoid one-frame flicker.
        if (this.lastSeenUtc != DateTime.MinValue && now - this.lastSeenUtc <= TimeSpan.FromSeconds(MissingTimeoutSeconds))
            return null;

        this.StopTracking();
        return null;
    }

    private IGameObject? FindClosestMaiden(IGameObject player)
    {
        IGameObject? best = null;
        var bestDistance = float.MaxValue;

        foreach (var obj in this.objectTable)
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

    private static bool IsValidMaiden(IGameObject? obj)
    {
        if (obj == null || !obj.IsValid())
            return false;

        if (obj.ObjectKind != ObjectKind.BattleNpc)
            return false;

        if (obj.IsDead)
            return false;

        if (obj is ICharacter character && character.CurrentHp == 0)
            return false;

        var name = obj.Name.TextValue;
        foreach (var maidenName in MaidenNames)
        {
            if (string.Equals(name, maidenName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private bool TryGetMarkerPosition(IGameObject player, IGameObject maiden, out Vector2 position, out bool clampedToEdge, out bool isBehindCamera)
    {
        var viewport = ImGui.GetMainViewport();
        var viewportMin = viewport.Pos;
        var viewportMax = viewport.Pos + viewport.Size;
        var viewportCenter = viewport.Pos + viewport.Size / 2f;

        var drawPosition = maiden.Position + new Vector3(0f, MathF.Max(1.6f, maiden.HitboxRadius + 1.0f), 0f);
        var inFrontOfCamera = this.gameGui.WorldToScreen(drawPosition, out var screenPosition, out var inView);

        isBehindCamera = !inFrontOfCamera;

        if (inFrontOfCamera && inView && IsFinite(screenPosition))
        {
            position = screenPosition;
            clampedToEdge = false;
            return true;
        }

        var offscreenInset = CalculateOffscreenInset(viewport.Size);
        var min = viewportMin + offscreenInset;
        var max = viewportMax - offscreenInset;
        var direction = this.GetScreenDirectionToTarget(player, maiden, screenPosition, inFrontOfCamera, viewportCenter);

        position = ProjectDirectionToViewportEdge(viewportCenter, direction, min, max);
        clampedToEdge = true;
        return true;
    }

    private Vector2 GetScreenDirectionToTarget(IGameObject player, IGameObject maiden, Vector2 projectedScreenPosition, bool hasProjectedScreenPosition, Vector2 viewportCenter)
    {
        // If Dalamud can still project the target while it is outside the viewport, use that projected position.
        // This keeps the marker lined up with the exact camera projection when the target is just off-screen.
        if (hasProjectedScreenPosition && IsFinite(projectedScreenPosition))
        {
            var projectedDirection = projectedScreenPosition - viewportCenter;
            if (projectedDirection.LengthSquared() > 1.0f)
                return Vector2.Normalize(projectedDirection);
        }

        var delta = maiden.Position - player.Position;
        var targetAngle = MathF.Atan2(delta.X, delta.Z);
        var cameraDirection = TryGetCameraHorizontalDirection(out var cameraDirH)
            ? cameraDirH
            : player.Rotation;

        var relativeAngle = NormalizeRadians(targetAngle - cameraDirection);

        // Relative to the camera: 0 = ahead/top of screen, +90° = right, 180° = behind/bottom.
        var direction = new Vector2(MathF.Sin(relativeAngle), -MathF.Cos(relativeAngle));
        if (direction.LengthSquared() < 0.001f)
            direction = new Vector2(0f, -1f);

        return Vector2.Normalize(direction);
    }

    private static Vector2 CalculateOffscreenInset(Vector2 viewportSize)
    {
        return new Vector2(
            CalculateOffscreenInsetForAxis(viewportSize.X),
            CalculateOffscreenInsetForAxis(viewportSize.Y));
    }

    private static float CalculateOffscreenInsetForAxis(float viewportLength)
    {
        // Pull off-screen markers inward by roughly 500px on large displays, but scale that value down on
        // smaller resolutions so the marker stays useful and never collapses into the center of the screen.
        var maxByViewport = MathF.Max(OffscreenMinimumEdgeMargin, viewportLength * OffscreenMaxViewportFactor);
        var maxBeforeCenter = MathF.Max(OffscreenMinimumEdgeMargin, (viewportLength / 2f) - OffscreenMinimumCenterDistance);
        return MathF.Min(OffscreenPreferredInset, MathF.Min(maxByViewport, maxBeforeCenter));
    }

    private static Vector2 ProjectDirectionToViewportEdge(Vector2 center, Vector2 direction, Vector2 min, Vector2 max)
    {
        if (direction.LengthSquared() < 0.001f || !IsFinite(direction))
            direction = new Vector2(0f, -1f);

        direction = Vector2.Normalize(direction);

        var tx = float.PositiveInfinity;
        if (direction.X > 0.001f)
            tx = (max.X - center.X) / direction.X;
        else if (direction.X < -0.001f)
            tx = (min.X - center.X) / direction.X;

        var ty = float.PositiveInfinity;
        if (direction.Y > 0.001f)
            ty = (max.Y - center.Y) / direction.Y;
        else if (direction.Y < -0.001f)
            ty = (min.Y - center.Y) / direction.Y;

        var t = MathF.Min(tx, ty);
        if (float.IsNaN(t) || float.IsInfinity(t) || t <= 0f)
            return Vector2.Clamp(center, min, max);

        return Vector2.Clamp(center + direction * t, min, max);
    }

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

    private void DrawMarker(Vector2 center, float distance, bool clampedToEdge, bool isBehindCamera)
    {
        var drawList = ImGui.GetForegroundDrawList();
        var accent = Color(255, 136, 204, 255);
        var accentDark = Color(95, 20, 68, 230);
        var white = Color(255, 255, 255, 255);
        var black = Color(0, 0, 0, 180);

        const float iconRadius = 15f;

        drawList.AddCircleFilled(center, iconRadius + 4f, black, 32);
        drawList.AddCircleFilled(center, iconRadius, accentDark, 32);
        drawList.AddCircle(center, iconRadius, accent, 32, 3f);

        // Small diamond inside the circle.
        drawList.AddQuadFilled(
            center + new Vector2(0f, -8f),
            center + new Vector2(8f, 0f),
            center + new Vector2(0f, 8f),
            center + new Vector2(-8f, 0f),
            accent);

        if (clampedToEdge)
            this.DrawArrow(center, accent, isBehindCamera);

        var label = isBehindCamera
            ? $"Forlorn Maiden - {distance:0}m - behind"
            : $"Forlorn Maiden - {distance:0}m";

        this.DrawLabel(drawList, center + new Vector2(0f, 22f), label, white, black);
    }

    private void DrawArrow(Vector2 center, uint color, bool isBehindCamera)
    {
        var viewport = ImGui.GetMainViewport();
        var viewportCenter = viewport.Pos + viewport.Size / 2f;
        var direction = center - viewportCenter;

        if (direction.LengthSquared() < 0.001f)
            direction = isBehindCamera ? new Vector2(0f, 1f) : new Vector2(0f, -1f);

        direction = Vector2.Normalize(direction);
        var perpendicular = new Vector2(-direction.Y, direction.X);
        var tip = center + direction * 26f;
        var baseCenter = center + direction * 10f;

        ImGui.GetForegroundDrawList().AddTriangleFilled(
            tip,
            baseCenter + perpendicular * 8f,
            baseCenter - perpendicular * 8f,
            color);
    }

    private void DrawLabel(ImDrawListPtr drawList, Vector2 center, string text, uint textColor, uint backgroundColor)
    {
        var textSize = ImGui.CalcTextSize(text);
        var padding = new Vector2(7f, 4f);
        var min = center - textSize / 2f - padding;
        var max = center + textSize / 2f + padding;

        drawList.AddRectFilled(min, max, backgroundColor, 6f);
        drawList.AddText(center - textSize / 2f, textColor, text);
    }

    private static bool IsFinite(Vector2 vector)
        => !float.IsNaN(vector.X) && !float.IsNaN(vector.Y) && !float.IsInfinity(vector.X) && !float.IsInfinity(vector.Y);

    private static uint Color(byte r, byte g, byte b, byte a)
        => (uint)(r | (g << 8) | (b << 16) | (a << 24));
}
