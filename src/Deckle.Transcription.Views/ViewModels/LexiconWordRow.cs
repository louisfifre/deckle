using System;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Deckle.Localization;

namespace Deckle.Transcription.Views;

// One word of the lexicon on LexiconPage, shown with the capitals the person
// typed. The row persists nothing: its remove calls back into the view model.
public sealed partial class LexiconWordRow
{
    private readonly Action<LexiconWordRow> _onRemoved;

    public string Word { get; }

    // What assistive technology announces on the remove button: the word it
    // removes, since every row's button looks the same.
    public string RemoveAutomationName { get; }

    public LexiconWordRow(string word, Action<LexiconWordRow> onRemoved)
    {
        Word = word;
        _onRemoved = onRemoved;
        string? format = Loc.GetFromOptional("Deckle.Transcription.Views", "LexiconRemoveButton_Format");
        RemoveAutomationName = format is null ? word : string.Format(CultureInfo.CurrentCulture, format, word);
    }

    [RelayCommand]
    private void Remove() => _onRemoved(this);
}
