// C# / tests/ZyonMenu.Standalone.Tests/OverlayChecks.cs / .NET 8 / stale-frame and partial-bone regressions.
using System;
using System.Numerics;
using ZyonMenu.Core;

namespace ZyonMenu.Standalone.Tests;

internal static class OverlayChecks
{
    public static int Run()
    {
        int assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }

        var maximumAge = TimeSpan.FromMilliseconds(200);
        Check(OverlayFrameTiming.IsFresh(1000, 1000, 1000, maximumAge), "A newly captured frame must be fresh.");
        Check(OverlayFrameTiming.IsFresh(1000, 1200, 1000, maximumAge), "A frame at the freshness boundary must remain usable.");
        Check(!OverlayFrameTiming.IsFresh(1000, 1201, 1000, maximumAge), "A stale frame must disappear instead of tracking an old position.");
        Check(!OverlayFrameTiming.IsFresh(0, 10, 1000, maximumAge), "The empty frame timestamp must never count as fresh.");
        Check(!OverlayFrameTiming.IsFresh(-1, 10, 1000, maximumAge), "An invalid negative timestamp must be rejected.");
        Check(!OverlayFrameTiming.IsFresh(1000, 999, 1000, maximumAge), "A timestamp in the future must be rejected.");
        Check(!OverlayFrameTiming.IsFresh(1000, 1001, 0, maximumAge), "A zero clock frequency must be rejected.");
        Check(!OverlayFrameTiming.IsFresh(1000, 1001, -1, maximumAge), "A negative clock frequency must be rejected.");
        Check(!OverlayFrameTiming.IsFresh(1000, 1000, 1000, TimeSpan.FromTicks(-1)), "A negative age budget must be rejected.");
        Check(OverlayFrameTiming.IsFresh(1000, 11000, 100000, maximumAge), "Freshness must use the actual monotonic clock frequency.");
        Check(OverlayFrameTiming.IsFresh(long.MaxValue - 100, long.MaxValue, 1000, maximumAge), "Large monotonic timestamps must retain a valid elapsed interval.");

        var bones = new Vector2?[9];
        bones[0] = new Vector2(100, 100);
        bones[1] = new Vector2(80, 300);
        bones[2] = new Vector2(120, 290);
        bones[8] = new Vector2(100, 240);
        Check(EspGeometry.TryGetBounds(bones, 1080, out var complete), "Normal root bones must produce a box.");
        Check(complete.CenterX == 100 && complete.Top == 95 && complete.Bottom == 303 && complete.Height == 208,
            "A complete actor must preserve the existing box coordinates.");
        bones[1] = null;
        Check(EspGeometry.TryGetBounds(bones, 1080, out var partial) && partial.Bottom == 293,
            "A foot behind the camera must not hide a box supported by the other foot.");
        bones[1] = new Vector2(80, 300);
        bones[2] = null;
        Check(EspGeometry.TryGetBounds(bones, 1080, out partial) && partial.Bottom == 303,
            "Either projected foot must be enough for a box.");
        bones[2] = new Vector2(float.NaN, 290);
        Check(EspGeometry.TryGetBounds(bones, 1080, out partial) && partial.Bottom == 303,
            "A corrupt second foot must not contaminate the valid partial bounds.");
        bones[1] = null;
        Check(!EspGeometry.TryGetBounds(bones, 1080, out _), "A box without any valid foot must not be invented.");
        bones[1] = new Vector2(80, 300);
        bones[0] = null;
        Check(!EspGeometry.TryGetBounds(bones, 1080, out _), "A missing head must disable only box geometry.");
        bones[0] = new Vector2(100, 100);
        bones[8] = null;
        Check(!EspGeometry.TryGetBounds(bones, 1080, out _), "A missing hip must not create an arbitrary horizontal center.");
        bones[8] = new Vector2(100, 240);
        Check(!EspGeometry.TryGetBounds(bones, 0, out _), "A zero-sized viewport must be rejected.");
        Check(!EspGeometry.TryGetBounds(bones.AsSpan(0, 8), 1080, out _), "An incomplete root-bone array must be rejected.");
        bones[1] = new Vector2(80, 0);
        Check(!EspGeometry.TryGetBounds(bones, 1080, out _), "Inverted vertical bounds must be rejected.");
        bones[1] = new Vector2(80, 5000);
        Check(!EspGeometry.TryGetBounds(bones, 1080, out _), "Implausibly large projected boxes must be rejected.");

        float blend30 = AimTiming.BlendFactor(10, 1d / 30);
        float blend60 = AimTiming.BlendFactor(10, 1d / 60);
        float remaining30 = MathF.Pow(1 - blend30, 30);
        float remaining60 = MathF.Pow(1 - blend60, 60);
        Check(Math.Abs(blend30 - 0.1f) < 0.000001f, "The 30 Hz response must preserve the original smoothing amount.");
        Check(Math.Abs(remaining30 - remaining60) < 0.000001f,
            "30 and 60 Hz updates must converge by the same amount over one second.");
        Check(AimTiming.BlendFactor(1, 1d / 60) == 1, "Smooth one must remain instantaneous at every update cadence.");
        Check(AimTiming.BlendFactor(10, 0) == 0, "A zero-time update must not move a smoothed camera.");
        Check(AimTiming.BlendFactor(10, 10) == AimTiming.BlendFactor(10, 0.1),
            "Recovery after a stalled update must cap interpolation time instead of jumping to the target.");
        return assertions;
    }
}
