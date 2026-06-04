using Microsoft.Windows.ApplicationModel.DynamicDependency;

namespace Deckle.TestSupport;

// ── WindowsAppSdkBootstrap ──────────────────────────────────────────────────
//
// Les projets de tests sont OutputType=Exe (Microsoft Testing Platform / xUnit v3)
// non-packagés. Les APIs WinAppSDK consommées par les tests qui touchent à
// Deckle.Shell ou Deckle.Vision (`DispatcherQueueController.CreateOnDedicatedThread`,
// composition, etc.) requièrent que le bootstrap dynamique soit initialisé
// avant tout premier appel — sinon `REGDB_E_CLASSNOTREG (0x80040154)` au moment
// de l'activation WinRT, parce que les factory classes du package WinAppSDK
// ne sont pas enregistrées dans le process unpackaged tant qu'on n'a pas
// résolu le package via la Dynamic Dependency API.
//
// `WindowsAppSdkModuleInitializer` est lié dans chaque assembly de test via
// `tests/Directory.Build.props`, puis appelle cette méthode avant qu'aucun
// `[Fact]` ne tourne. `TryInitialize` est idempotent au sens "retourne false si
// déjà initialisé" — on accepte le bool sans le tester parce qu'un échec d'init
// causera un throw clair au premier site WinAppSDK du test et c'est le bon
// comportement.
//
// `majorMinorVersion = 0x00010008` correspond à WinAppSDK 1.8, la version
// alignée sur toute la solution. Bumper conjointement avec le bump du package
// transverse.
public static class WindowsAppSdkBootstrap
{
    public static void Initialize()
    {
        Bootstrap.TryInitialize(0x00010008, out _);
    }
}
