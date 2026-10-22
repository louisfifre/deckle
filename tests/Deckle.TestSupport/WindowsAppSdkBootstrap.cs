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
// `WindowsAppSdkModuleInitializer`, linked into each test assembly through
// `tests/Directory.Build.props`, calls this before any `[Fact]` runs. The bool
// from `TryInitialize` is ignored: an init failure throws clearly at the first
// WinAppSDK test site.
//
// `0x00020005` is WinAppSDK 2.5, the `Release.MajorMinor` the SDK's own
// auto-initializer passes (WindowsAppSDK-VersionInfo.cs in the Runtime
// package): bump it with the WindowsAppSDK package version in
// Directory.Packages.props.
public static class WindowsAppSdkBootstrap
{
    public static void Initialize()
    {
        Bootstrap.TryInitialize(0x00020005, out _);
    }
}
