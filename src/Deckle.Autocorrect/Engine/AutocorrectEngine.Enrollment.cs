namespace Deckle.Autocorrect;

public sealed partial class AutocorrectEngine
{
    private static bool IsEnabledFor(AutocorrectSettings settings, string processName)
        => settings.IsEnabledFor(processName);

    // The user has answered for this app (on or off) — absent means never met.
    private static bool IsDecided(AutocorrectSettings settings, string processName)
        => processName.Length > 0 && settings.Apps.ContainsKey(processName);

    private void MaybeSuggestEnrollment(string processName)
    {
        if (processName.Length == 0 || !_suggested.Add(processName)) return;
        DeckleAutocorrectSource.Log.EnrollmentSuggested(processName);
        EnrollmentSuggested?.Invoke(processName);
    }
}