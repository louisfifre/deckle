using System.Runtime.CompilerServices;

namespace Deckle.TestSupport;

internal static class WindowsAppSdkModuleInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        WindowsAppSdkBootstrap.Initialize();
    }
}
