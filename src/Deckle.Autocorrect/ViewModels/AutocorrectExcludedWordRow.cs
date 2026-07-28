using System;
using CommunityToolkit.Mvvm.Input;

namespace Deckle.Autocorrect;

// One entry of the exclusion register on AutocorrectPage: a word the user
// pulled out of correction's reach, and the gesture that puts it back. Same
// discipline as the other rows — the row never persists anything, the undo
// calls back into the view-model.
//
// The word is shown exactly as it is stored, lowercased: it names a lexicon
// key, and dressing it up would suggest the exclusion is case-sensitive when it
// is not.
public sealed partial class AutocorrectExcludedWordRow
{
    private readonly Action<AutocorrectExcludedWordRow> _onIncluded;

    public string Word { get; }

    public AutocorrectExcludedWordRow(string word, Action<AutocorrectExcludedWordRow> onIncluded)
    {
        Word = word;
        _onIncluded = onIncluded;
    }

    [RelayCommand]
    private void Include() => _onIncluded(this);
}
