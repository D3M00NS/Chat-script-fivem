// C# / src/ZyonMenu.Windows/StandaloneUiPolicy.cs / .NET 8 / portable standalone UI state policy.
namespace ZyonMenu.Windows;

internal sealed class StandaloneUiPolicy
{
    private bool _appliedSessionObserved;
    private int? _processId;

    public bool Observe(bool healthy, bool ready, bool localPlayerReady, bool sessionActive, int? processId = null)
    {
        var available = healthy && ready && localPlayerReady && sessionActive;
        var changedProcess = available && _appliedSessionObserved && _processId is not null && processId != _processId;
        var reset = _appliedSessionObserved && (!available || changedProcess);
        if (!available)
        {
            _appliedSessionObserved = false;
            _processId = null;
        }
        else
        {
            _appliedSessionObserved = true;
            _processId = processId;
        }
        return reset;
    }

    public void Disconnect()
    {
        _appliedSessionObserved = false;
        _processId = null;
    }

    public static bool CanEditConfiguration(string capability, bool clientCapabilitiesKnown, ISet<string> clientCapabilities)
    {
        if (capability is "silent" or "player.revive") return false;
        if (clientCapabilitiesKnown) return clientCapabilities.Contains(capability);
        return capability is "aim" or "aim.playersOnly" or "esp" or "esp.boxes" or "esp.skeletons" or "esp.distance" or "esp.playersOnly" or
            "player.godMode" or "weapons.noRecoil" or "weapons.noSpread";
    }

    public static bool CanInvokeAction(string capability, bool healthy, bool ready, bool localPlayerReady,
        bool sessionActive, ISet<string> clientCapabilities) =>
        healthy && ready && localPlayerReady && sessionActive && clientCapabilities.Contains(capability);
}
