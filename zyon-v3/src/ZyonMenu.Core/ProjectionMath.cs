// C# / src/ZyonMenu.Core/ProjectionMath.cs / .NET 8 / FiveM b3258 row-major viewport and bone geometry.
using System.Numerics;

namespace ZyonMenu.Core;

public static class ProjectionMath
{
    private const float MinimumClipW = 0.001f;

    /// <summary>
    /// Projects a point using b3258's raw viewport layout: columns 2, 3 and 4
    /// contain clip X, Y and W. Finite coordinates outside the viewport remain
    /// valid so callers can clip partially visible geometry themselves.
    /// </summary>
    public static bool TryProject(Vector3 world, Matrix4x4 view, int width, int height, out Vector2 screen)
    {
        screen = default;
        if (width <= 0 || height <= 0 || !IsFinite(world) || !IsFinite(view)) return false;

        var clipX = (double)world.X * view.M12 + (double)world.Y * view.M22 + (double)world.Z * view.M32 + view.M42;
        var clipY = (double)world.X * view.M13 + (double)world.Y * view.M23 + (double)world.Z * view.M33 + view.M43;
        var clipW = (double)world.X * view.M14 + (double)world.Y * view.M24 + (double)world.Z * view.M34 + view.M44;
        if (!double.IsFinite(clipW) || clipW <= MinimumClipW) return false;

        var x = (float)(width * 0.5 + clipX / clipW * width * 0.5);
        var y = (float)(height * 0.5 - clipY / clipW * height * 0.5);
        if (!float.IsFinite(x) || !float.IsFinite(y)) return false;
        screen = new Vector2(x, y);
        return true;
    }

    /// <summary>Transforms a local bone using the ped's row-major affine matrix, including row-four translation.</summary>
    public static Vector3 TransformBone(Vector3 local, Matrix4x4 ped)
    {
        if (!IsFinite(local) || !IsFinite(ped))
            throw new ArgumentException("A posição e a matriz do osso precisam ser finitas.");
        var transformed = Vector3.Transform(local, ped);
        if (!IsFinite(transformed))
            throw new ArgumentException("A transformação do osso excedeu o intervalo numérico.");
        return transformed;
    }

    /// <summary>Checks a circular FOV radius in pixels, including its boundary.</summary>
    public static bool TargetDistanceWithinFov(Vector2 point, Vector2 center, float fov)
    {
        if (!IsFinite(point) || !IsFinite(center) || !float.IsFinite(fov) || fov < 0) return false;
        var dx = (double)point.X - center.X;
        var dy = (double)point.Y - center.Y;
        return dx * dx + dy * dy <= (double)fov * fov;
    }

    /// <summary>
    /// Returns the target delta divided by smooth (1 to 20). Fractions are kept:
    /// callers producing integer input should accumulate them before rounding.
    /// </summary>
    public static Vector2 SmoothDelta(Vector2 target, Vector2 center, int smooth)
    {
        if (smooth is < 1 or > 20)
            throw new ArgumentOutOfRangeException(nameof(smooth), "A suavização precisa estar entre 1 e 20.");
        if (!IsFinite(target) || !IsFinite(center))
            throw new ArgumentException("As posições da mira precisam ser finitas.");
        var delta = new Vector2((float)(((double)target.X - center.X) / smooth),
            (float)(((double)target.Y - center.Y) / smooth));
        if (!IsFinite(delta))
            throw new ArgumentException("O deslocamento da mira excedeu o intervalo numérico.");
        return delta;
    }

    private static bool IsFinite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);
    private static bool IsFinite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static bool IsFinite(Matrix4x4 value) =>
        float.IsFinite(value.M11) && float.IsFinite(value.M12) && float.IsFinite(value.M13) && float.IsFinite(value.M14) &&
        float.IsFinite(value.M21) && float.IsFinite(value.M22) && float.IsFinite(value.M23) && float.IsFinite(value.M24) &&
        float.IsFinite(value.M31) && float.IsFinite(value.M32) && float.IsFinite(value.M33) && float.IsFinite(value.M34) &&
        float.IsFinite(value.M41) && float.IsFinite(value.M42) && float.IsFinite(value.M43) && float.IsFinite(value.M44);
}
