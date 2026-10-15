// C# / tests/ZyonMenu.Standalone.Tests/AffinePaddingChecks.cs / .NET 8 / affine padding regression checks.
using System.Numerics;
using ZyonMenu.Core;

namespace ZyonMenu.Standalone.Tests;

internal static class AffinePaddingChecks
{
    public static int Run()
    {
        int assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }

        void RejectsBone(Vector3 local, Matrix4x4 matrix, string message)
        {
            assertions++;
            try { ProjectionMath.TransformBone(local, matrix); }
            catch (ArgumentException) { return; }
            throw new InvalidOperationException(message);
        }

        var affine = new Matrix4x4(
            0, 1, 0, 0,
            -1, 0, 0, 0,
            0, 0, 2, 0,
            100, 200, 300, 1);
        var local = new Vector3(1, 2, 3);
        var expected = new Vector3(98, 201, 306);
        Check(ProjectionMath.TransformBone(local, affine) == expected,
            "The affine fixture must rotate, scale and translate the bone.");

        var invalidValues = new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity };
        int[] paddingFields = [3, 7, 11, 15];
        foreach (int field in paddingFields)
        {
            foreach (float value in invalidValues)
            {
                var matrix = WithField(affine, field, value);
                Check(ProjectionMath.TransformBone(local, matrix) == expected,
                    $"Nonfinite affine padding at matrix field {field} must not discard a valid bone.");
                Check(ProjectionMath.TransformBone(Vector3.Zero, matrix) == new Vector3(100, 200, 300),
                    $"Padding at matrix field {field} must not change row-four XYZ translation.");
            }
        }

        var mixedPadding = affine;
        mixedPadding.M14 = float.NaN;
        mixedPadding.M24 = float.PositiveInfinity;
        mixedPadding.M34 = float.NegativeInfinity;
        mixedPadding.M44 = float.NaN;
        Check(ProjectionMath.TransformBone(local, mixedPadding) == expected,
            "All four unused padding fields may be nonfinite without changing affine XYZ.");

        int[] usedFields = [0, 1, 2, 4, 5, 6, 8, 9, 10, 12, 13, 14];
        foreach (int field in usedFields)
            foreach (float value in invalidValues)
                RejectsBone(local, WithField(affine, field, value),
                    $"Nonfinite XYZ input at matrix field {field} must still reject the bone.");

        foreach (float value in invalidValues)
        {
            RejectsBone(new Vector3(value, 2, 3), affine, "Nonfinite local bone X must be rejected.");
            RejectsBone(new Vector3(1, value, 3), affine, "Nonfinite local bone Y must be rejected.");
            RejectsBone(new Vector3(1, 2, value), affine, "Nonfinite local bone Z must be rejected.");
        }

        // These fields are padding only in the ped's affine transform. The
        // viewport uses column four to calculate clip W and retains its full
        // finite-matrix contract, including every other matrix component.
        var viewport = new Matrix4x4(
            17, 0.1f, 0.4f, 0.01f,
            19, 0.2f, 0.5f, 0.02f,
            23, 0.3f, 0.6f, 0.03f,
            29, 0.7f, 0.8f, 2);
        Check(ProjectionMath.TryProject(local, viewport, 800, 600, out _),
            "The finite viewport fixture must remain projectable.");
        for (int field = 0; field < 16; field++)
            foreach (float value in invalidValues)
                Check(!ProjectionMath.TryProject(local, WithField(viewport, field, value), 800, 600, out _),
                    $"Viewport field {field} must remain finite despite relaxed affine padding validation.");

        return assertions;
    }

    private static Matrix4x4 WithField(Matrix4x4 matrix, int field, float value)
    {
        switch (field)
        {
            case 0: matrix.M11 = value; break;
            case 1: matrix.M12 = value; break;
            case 2: matrix.M13 = value; break;
            case 3: matrix.M14 = value; break;
            case 4: matrix.M21 = value; break;
            case 5: matrix.M22 = value; break;
            case 6: matrix.M23 = value; break;
            case 7: matrix.M24 = value; break;
            case 8: matrix.M31 = value; break;
            case 9: matrix.M32 = value; break;
            case 10: matrix.M33 = value; break;
            case 11: matrix.M34 = value; break;
            case 12: matrix.M41 = value; break;
            case 13: matrix.M42 = value; break;
            case 14: matrix.M43 = value; break;
            case 15: matrix.M44 = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(field));
        }
        return matrix;
    }
}
