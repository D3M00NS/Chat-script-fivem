// C# / src/ZyonMenu.Core/OverlayFrameTiming.cs / .NET 8 / monotonic overlay snapshot policy.
using System;

namespace ZyonMenu.Core;

public static class OverlayFrameTiming
{
    /// <summary>Rejects empty, future-dated or stale snapshots without depending on the wall clock.</summary>
    public static bool IsFresh(long capturedTimestamp, long currentTimestamp, long frequency, TimeSpan maximumAge)
    {
        if (capturedTimestamp <= 0 || currentTimestamp < capturedTimestamp || frequency <= 0 || maximumAge < TimeSpan.Zero)
            return false;
        double elapsedSeconds = (currentTimestamp - capturedTimestamp) / (double)frequency;
        return elapsedSeconds <= maximumAge.TotalSeconds;
    }
}
