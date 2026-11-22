using System;
using CommunityToolkit.Mvvm.Input;

namespace Deckle.Transcription.Views;

// One word of the lexicon on LexiconPage, shown with the capitals the person
// typed. The row persists nothing: its remove calls back into the view model.
public sealed partial class LexiconWordRow
{
    private readonly Action<LexiconWordRow> _onRemoved;

    public string Word { get; }

    public LexiconWordRow(string word, Action<LexiconWordRow> onRemoved)
    {
        Word = word;
        _onRemoved = onRemoved;
    }

    [RelayCommand]
    private void Remove() => _onRemoved(this);
}
