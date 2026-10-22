using System.Runtime.CompilerServices;
using Deckle.Core;

namespace Deckle.Speech.Tests;

// SpeechSettingsService.Instance resolves its file under AppPaths.UserDataRoot,
// which is fixed the first time AppPaths is touched. Pointing DECKLE_DATA_ROOT
// at a throwaway folder before any test runs keeps every test of this assembly
// off the developer's real %LOCALAPPDATA%\Deckle, so the defaults the engine
// tests read are the same on every machine.
internal static class IsolatedDataRoot
{
    [ModuleInitializer]
    internal static void Redirect()
    {
        string root = Path.Combine(
            Path.GetTempPath(), "Deckle-Tests", "speech-" + Path.GetRandomFileName());
        Environment.SetEnvironmentVariable(AppPaths.DataRootEnvVar, root);
    }
}
