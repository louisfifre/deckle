using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace Deckle.Catalog;

// ── TelemetryConsent ──────────────────────────────────────────────────────────
//
// Static delegate registry for the telemetry consent dialogs. The dialogs
// themselves are ContentDialogs in the shell (Deckle.Settings); the App wires
// each one's ShowAsync into these slots at boot (through the shell's
// TelemetryConsentWiring shim), and module settings pages consume them via
// Setting.confirmOnEnable — so a module opt-in can gate its enable behind a
// consent dialog without referencing the shell.
//
// Same lib-exposes-slots / App-owns-wiring pattern as
// SettingsComposer.PathControlFactory: Catalog holds the seam, the App fills it,
// the module call sites invoke it. An unwired slot DENIES (returns false) so a
// telemetry opt-in never enables without a wired consent — the privacy-safe
// fallback for tests/previews where no shell is present.
//
// ApplicationLogToDisk is deliberately absent: it stays on the Diagnostics page
// (shell), which invokes its dialog directly, so no registry slot is needed.
public static class TelemetryConsent
{
    public static Func<XamlRoot, Task<bool>>? Microphone { get; set; }
    public static Func<XamlRoot, Task<bool>>? Corpus { get; set; }
    public static Func<XamlRoot, Task<bool>>? AudioCorpus { get; set; }
    public static Func<XamlRoot, Task<bool>>? AutocorrectDecisions { get; set; }
    public static Func<XamlRoot, Task<bool>>? AutocorrectText { get; set; }

    // Null-safe invokers the module manifests pass to Setting.confirmOnEnable as a
    // method group. Deny (false) when the matching slot is unwired.
    public static Task<bool> RequestMicrophone(XamlRoot root) =>
        Microphone?.Invoke(root) ?? Task.FromResult(false);

    public static Task<bool> RequestCorpus(XamlRoot root) =>
        Corpus?.Invoke(root) ?? Task.FromResult(false);

    public static Task<bool> RequestAudioCorpus(XamlRoot root) =>
        AudioCorpus?.Invoke(root) ?? Task.FromResult(false);

    public static Task<bool> RequestAutocorrectDecisions(XamlRoot root) =>
        AutocorrectDecisions?.Invoke(root) ?? Task.FromResult(false);

    public static Task<bool> RequestAutocorrectText(XamlRoot root) =>
        AutocorrectText?.Invoke(root) ?? Task.FromResult(false);
}
