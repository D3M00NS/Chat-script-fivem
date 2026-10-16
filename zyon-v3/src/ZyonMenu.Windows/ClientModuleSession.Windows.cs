// C# / Windows factory / the injectable provider is also tested with real portable pipes.
namespace ZyonMenu.Windows;

internal sealed partial class ClientModuleSession
{
    public ClientModuleSession() : this(new LocalGameSession(), DiscoverProcess, ConnectPipeAsync,
        () => ClientModuleInstallation.Refresh(force: true)) { }
}
