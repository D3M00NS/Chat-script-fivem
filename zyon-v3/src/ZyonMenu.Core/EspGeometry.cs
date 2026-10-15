// C# / src/ZyonMenu.Core/EspGeometry.cs / .NET 8 / partial root-bone geometry.
using System;
using System.Numerics;

namespace ZyonMenu.Core;

public readonly record struct EspBounds(float CenterX, float Top, float Bottom, float Width, float Height);

public static class EspGeometry
{
    /// <summary>A projected head, hip and either foot are enough for a box; a missing foot does not hide the actor.</summary>
    public static bool TryGetBounds(ReadOnlySpan<Vector2?> bones, int viewportHeight, out EspBounds bounds)
    {
        bounds = default;
        if (bones.Length != 9 || viewportHeight <= 0 || bones[0] is not { } head || bones[8] is not { } hip ||
            !Finite(head) || !Finite(hip)) return false;
        bool leftValid = bones[1] is { } left && Finite(left);
        bool rightValid = bones[2] is { } right && Finite(right);
        if (!leftValid && !rightValid) return false;

        float bottom = leftValid ? bones[1]!.Value.Y : bones[2]!.Value.Y;
        if (rightValid) bottom = Math.Max(bottom, bones[2]!.Value.Y);
        bottom += 3;
        float top = head.Y - 5;
        float height = bottom - top;
        float center = (float)(((double)head.X + hip.X) / 2);
        if (!float.IsFinite(top) || !float.IsFinite(bottom) || !float.IsFinite(center) || !float.IsFinite(height) ||
            height <= 4 || height > (double)viewportHeight * 4) return false;
        bounds = new EspBounds(center, top, bottom, height * 0.46f, height);
        return true;
    }

    private static bool Finite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);
}
