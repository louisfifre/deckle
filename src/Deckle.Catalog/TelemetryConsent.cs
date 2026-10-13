using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace Deckle.Catalog;

// ── TelemetryConsent ──────────────────────────────────────────────────────────
//
// Delegate slots for the telemetry consent dialogs. The dialogs live in
// Deckle.Settings; the App wires them at boot, and module manifests pass the
// Request* invokers to Setting.confirmOnEnable, so a module opt-in gates on
// consent without referencing the shell.
//
// An unwired slot denies (returns false): a telemetry opt-in never enables
// without a wired consent, as in tests or previews with no shell.
public static class TelemetryConsent
{
    public static Func<XamlRoot, Task<bool>>? ApplicationLog { get; set; }
    public static Func<XamlRoot, Task<bool>>? Microphone { get; set; }
    public static Func<XamlRoot, Task<bool>>? Corpus { get; set; }
    public static Func<XamlRoot, Task<bool>>? AudioCorpus { get; set; }
    public static Func<XamlRoot, Task<bool>>? AutocorrectDecisions { get; set; }
    public static Func<XamlRoot, Task<bool>>? AutocorrectText { get; set; }

    public static Task<bool> RequestApplicationLog(XamlRoot root) =>
        ApplicationLog?.Invoke(root) ?? Task.FromResult(false);

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
