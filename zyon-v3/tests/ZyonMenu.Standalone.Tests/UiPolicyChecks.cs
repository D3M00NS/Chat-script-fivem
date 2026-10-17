// C# / tests/ZyonMenu.Standalone.Tests/UiPolicyChecks.cs / .NET 8 / portable standalone UI lifecycle checks.
using ZyonMenu.Windows;

namespace ZyonMenu.Standalone.Tests;

internal static class UiPolicyChecks
{
    public static int Run()
    {
        var empty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Check(StandaloneUiPolicy.CanEditConfiguration("aim", false, empty), "Aim must be editable before a target exists.");
        Check(StandaloneUiPolicy.CanEditConfiguration("weapons.noSpread", false, empty), "Weapon choices can be prepared while capabilities are unknown.");
        Check(!StandaloneUiPolicy.CanEditConfiguration("silent", false, empty), "Unimplemented Silent Aim must stay disabled.");
        Check(!StandaloneUiPolicy.CanEditConfiguration("player.noclip", false, empty), "Noclip needs explicitly confirmed backend support.");
        Check(StandaloneUiPolicy.CanEditConfiguration("esp.boxes", false, empty), "A verified planned ESP sub-option can be prepared before detection.");
        Check(!StandaloneUiPolicy.CanEditConfiguration("esp.names", false, empty), "Unverified player names must not be editable while capabilities are unknown.");
        Check(!StandaloneUiPolicy.CanEditConfiguration("aim.visibility", false, empty), "Unverified Aim line-of-sight checks must stay disabled.");
        Check(!StandaloneUiPolicy.CanEditConfiguration("esp.visibility", false, empty), "Unverified ESP line-of-sight checks must stay disabled.");
        var capabilities = new HashSet<string>(new[] { "aim", "esp", "player.godMode" }, StringComparer.OrdinalIgnoreCase);
        Check(StandaloneUiPolicy.CanEditConfiguration("aim", true, capabilities), "Known supported Aim must stay editable.");
        Check(!StandaloneUiPolicy.CanEditConfiguration("weapons.noSpread", true, capabilities), "A known unsupported option must be disabled.");
        Check(!StandaloneUiPolicy.CanEditConfiguration("esp.boxes", true, capabilities), "A broad ESP capability must not imply an unsupported sub-option.");
        Check(!StandaloneUiPolicy.CanInvokeAction("player.revive", true, true, true, true, capabilities), "Revive must not claim missing implementation.");
        var reviveCapabilities = new HashSet<string>(new[] { "player.revive" }, StringComparer.OrdinalIgnoreCase);
        Check(!StandaloneUiPolicy.CanInvokeAction("player.revive", true, true, false, false, reviveCapabilities), "Implemented actions must remain blocked until an actual player session exists.");
        Check(!StandaloneUiPolicy.CanInvokeAction("player.revive", true, true, true, true, reviveCapabilities), "A retired native-only action must remain unavailable even with a stale capability.");
        var mixed = new HashSet<string>(new[] { "player.godMode", "player.suicide", "player.revive", "player.noclip" }, StringComparer.OrdinalIgnoreCase);
        Check(StandaloneUiPolicy.CanInvokeAction("player.godMode", true, true, true, true, mixed),
            "An advertised memory GodMode must remain usable in the external backend.");
        Check(StandaloneUiPolicy.CanInvokeAction("player.suicide", true, true, true, true, mixed),
            "An advertised memory health action must remain usable in the external backend.");
        Check(!StandaloneUiPolicy.CanInvokeAction("player.revive", true, true, true, true, mixed),
            "A stale capability cannot unlock removed native revive.");
        Check(!StandaloneUiPolicy.CanInvokeAction("player.noclip", true, true, true, true, mixed),
            "A stale capability cannot unlock removed native noclip.");

        foreach (var retired in new[] { "silent", "player.revive", "player.noclip", "player.invisible", "weapons.freezeAmmo", "vehicle.lock", "vehicle.unlock" })
        {
            var stale = new HashSet<string>(new[] { retired }, StringComparer.OrdinalIgnoreCase);
            Check(!StandaloneUiPolicy.CanEditConfiguration(retired, true, stale), "A stale capability must not enable retired plugin controls.");
            Check(!StandaloneUiPolicy.CanInvokeAction(retired, true, true, true, true, stale), "A stale capability must not dispatch a removed plugin action.");
        }

        var lifecycle = new StandaloneUiPolicy();
        Check(!lifecycle.Observe(true, false, false, false), "Missing game must preserve first pending choices.");
        Check(!lifecycle.Observe(false, false, false, false), "First detection errors must preserve pending choices.");
        Check(!lifecycle.Observe(true, true, true, false, 42), "First attachment before application must preserve choices.");
        Check(!lifecycle.Observe(true, true, true, true, 42), "First applied session must preserve the submitted choices.");
        Check(lifecycle.Observe(true, true, true, false, 42), "A lost applied session must reset active effects.");
        Check(!lifecycle.Observe(true, false, false, false), "Loss reset must happen once rather than erase new deliberate pending choices.");
        Check(!lifecycle.Observe(true, true, true, false, 42), "Recovery must keep reset effects off until submitted again.");
        Check(!lifecycle.Observe(true, true, true, true, 42), "A recovered application must establish the current session.");
        Check(lifecycle.Observe(true, true, true, true, 84), "Changing target process must reset old active effects.");
        Check(lifecycle.Observe(false, true, true, true, 84), "Backend failure after application must reset active effects.");
        lifecycle.Disconnect();
        Check(!lifecycle.Observe(true, true, true, false, 84), "Explicit detection after disconnect starts as a fresh pending session.");
        return 43;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
