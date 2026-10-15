// C# / src/ZyonMenu.Core/AimTiming.cs / .NET 8 / frame-rate-independent aim interpolation.
using System;

namespace ZyonMenu.Core;

public static class AimTiming
{
    /// <summary>Preserves the original 30 Hz smoothing response and caps recovery after a stalled update.</summary>
    public static float BlendFactor(int smooth, double elapsedSeconds)
    {
        if (smooth is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(smooth));
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (smooth == 1) return 1;
        double referenceFrames = Math.Min(elapsedSeconds, 0.1) * 30;
        return (float)(1 - Math.Pow(1 - 1d / smooth, referenceFrames));
    }
}
