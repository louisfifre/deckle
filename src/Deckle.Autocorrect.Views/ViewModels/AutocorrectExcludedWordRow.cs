using System;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Deckle.Localization;

namespace Deckle.Autocorrect.Views;

// One entry of the exclusion register on AutocorrectPage. The row never
// persists anything: the undo calls back into the view-model.
//
// The word is shown exactly as it is stored, lowercased: it names a lexicon
// key, and dressing it up would suggest the exclusion is case-sensitive when it
// is not.
public sealed partial class AutocorrectExcludedWordRow
{
    private readonly Action<AutocorrectExcludedWordRow> _onIncluded;

    public string Word { get; }

    // Accessible name of the row's button: says which word it puts back.
    public string IncludeLabel { get; }

    public AutocorrectExcludedWordRow(string word, Action<AutocorrectExcludedWordRow> onIncluded)
    {
        Word = word;
        IncludeLabel = string.Format(
            CultureInfo.CurrentCulture,
            Loc.GetFrom(AutocorrectSettingsModule.ResourceLibrary, "AutocorrectPage_IncludeButton_NameFormat"),
            word);
        _onIncluded = onIncluded;
    }

    [RelayCommand]
    private void Include() => _onIncluded(this);
}
