using System;
using Windows.Globalization.NumberFormatting;

namespace Deckle.Catalog;

// ── SentinelNumberFormatter ───────────────────────────────────────────────────
//
// The formatter of a number field whose value carries one sentinel, a number
// that stands for a word ("-1" for "let the engine decide"). The field shows the
// word in the sentinel's place and reads it back, so the person reads "Auto"
// rather than decoding a magic number; every other value goes through the
// field's own DecimalFormatter. Typing the number itself still works: the
// inner parser reads it and the field then shows the word.
//
// NumberBox takes its NumberFormatter as an INumberFormatter2 and requires it
// to be an INumberParser too, so this class implements both. Partial, so
// CsWinRT can generate the WinRT projection of a managed implementation.
public sealed partial class SentinelNumberFormatter : INumberFormatter2, INumberParser
{
    private readonly DecimalFormatter _inner;
    private readonly double _sentinel;
    private readonly string _word;

    public SentinelNumberFormatter(DecimalFormatter inner, double sentinel, string word)
    {
        _inner = inner;
        _sentinel = sentinel;
        _word = word;
    }

    public string FormatDouble(double value) => value == _sentinel ? _word : _inner.FormatDouble(value);

    public string FormatInt(long value) => value == _sentinel ? _word : _inner.FormatInt(value);

    public string FormatUInt(ulong value) => value == _sentinel ? _word : _inner.FormatUInt(value);

    public double? ParseDouble(string text) => IsWord(text) ? _sentinel : _inner.ParseDouble(text);

    public long? ParseInt(string text) => IsWord(text) ? (long)_sentinel : _inner.ParseInt(text);

    // An unsigned field has no negative sentinel to read back; the word reads
    // as the sentinel only when it is itself unsigned.
    public ulong? ParseUInt(string text)
        => IsWord(text) && _sentinel >= 0 ? (ulong)_sentinel : _inner.ParseUInt(text);

    // The word as typed, whatever its case and the spaces around it.
    private bool IsWord(string? text)
        => text is not null && string.Equals(text.Trim(), _word, StringComparison.CurrentCultureIgnoreCase);
}
