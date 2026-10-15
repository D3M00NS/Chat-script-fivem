// C# / src/ZyonMenu.Windows/ExternalReadback.cs / .NET 8 / pure validation of data observations.
using System.Numerics;

namespace ZyonMenu.Windows;

internal static class ExternalReadback
{
    /// <summary>Confirms the sampled vector, without treating the observation as proof of a gameplay effect.</summary>
    public static bool DirectionMatches(Vector3 requested, Vector3 observed, float tolerance = 0.0001f)
    {
        if (!float.IsFinite(tolerance) || tolerance < 0 || !Finite(requested) || !Finite(observed)) return false;
        return Math.Abs((double)requested.X - observed.X) <= tolerance &&
            Math.Abs((double)requested.Y - observed.Y) <= tolerance &&
            Math.Abs((double)requested.Z - observed.Z) <= tolerance;
    }

    public static int? UniqueFieldOffset(IReadOnlyList<int> candidates) =>
        candidates.Count == 1 && candidates[0] is >= 0x20 and <= 0x1000 ? candidates[0] : null;

    public static int SignedDisplacement8(byte value) => unchecked((sbyte)value);

    public static bool ProjectionCoefficientsValid(Matrix4x4 value) =>
        float.IsFinite(value.M11) && float.IsFinite(value.M21) && float.IsFinite(value.M31) && float.IsFinite(value.M41) &&
        float.IsFinite(value.M12) && float.IsFinite(value.M22) && float.IsFinite(value.M32) && float.IsFinite(value.M42) &&
        float.IsFinite(value.M13) && float.IsFinite(value.M23) && float.IsFinite(value.M33) && float.IsFinite(value.M43) &&
        float.IsFinite(value.M14) && float.IsFinite(value.M24) && float.IsFinite(value.M34) && float.IsFinite(value.M44) &&
        (value.M14 != 0 || value.M24 != 0 || value.M34 != 0 || value.M44 != 0);

    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
