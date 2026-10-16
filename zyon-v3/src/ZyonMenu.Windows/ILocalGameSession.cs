// C# / local providers / shared UI contract; no remote or arbitrary native invocation.
using ZyonMenu.Core;

namespace ZyonMenu.Windows;

internal interface ILocalGameSession : IDisposable
{
    bool DetectionEnabled { get; set; }
    long PlayerEpoch { get; }
    Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<MenuEvents> GetEventsAsync(long since, CancellationToken cancellationToken = default);
    Task PushSettingsAsync(MenuSettings settings, CancellationToken cancellationToken = default);
    Task ReviveAsync(CancellationToken cancellationToken = default);
    Task ReviveAsync(long expectedPlayerEpoch, CancellationToken cancellationToken = default);
    Task SuicideAsync(CancellationToken cancellationToken = default);
    Task SuicideAsync(long expectedPlayerEpoch, CancellationToken cancellationToken = default);
    Task SetVehicleLockAsync(bool locked, CancellationToken cancellationToken = default);
    Task SetVehicleLockAsync(bool locked, long expectedPlayerEpoch, CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    string CaptureDiagnostics();
}
