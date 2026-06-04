using Microsoft.Windows.ApplicationModel.DynamicDependency;

namespace Deckle.TestSupport;

// ── WindowsAppSdkBootstrap ──────────────────────────────────────────────────
//
// Test projects are unpackaged OutputType=Exe (Microsoft Testing Platform /
// xUnit v3). WinAppSDK APIs consumed by tests touching Deckle.Shell or
// Deckle.Vision (`DispatcherQueueController.CreateOnDedicatedThread`,
// composition, etc.) require dynamic bootstrap initialization before the first
// call; otherwise `REGDB_E_CLASSNOTREG (0x80040154)` occurs during WinRT
// activation because WinAppSDK package factory classes are not registered in
// the unpackaged process until the package is resolved through the Dynamic
// Dependency API.
//
// `WindowsAppSdkModuleInitializer` is linked into each test assembly through
// `tests/Directory.Build.props`, then calls this method before any `[Fact]`
// runs. `TryInitialize` is idempotent in the "returns false if already
// initialized" sense; accept the bool without testing it because an init
// failure will cause a clear throw at the first WinAppSDK test site, and that
// is the right behavior.
//
// `majorMinorVersion = 0x00010008` corresponds to WinAppSDK 1.8, the version
// aligned across the solution. Bump together with the cross-cutting package
// bump.
public static class WindowsAppSdkBootstrap
{
    public static void Initialize()
    {
        Bootstrap.TryInitialize(0x00010008, out _);
    }
}
