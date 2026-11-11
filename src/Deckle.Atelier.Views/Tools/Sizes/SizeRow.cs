using System.ComponentModel;
using Microsoft.UI.Xaml;

namespace Deckle.Atelier.Views;

// What a row of the sizes list stands for. The list is flat (spec, Sizes: "as
// a list"): the place's own files first, then one row per sub-folder, and, when
// the person picks one of the heaviest files in the panel, that file under the
// row of the folder that holds it, so the list shows where it is (12.21).
internal enum SizeRowKind
{
    LooseFiles,
    Folder,
    HeavyFile,
}

// One row of the sizes list. Rows are kept across the snapshots of a running
// analysis, keyed by what they stand for, so a row that gets its size or moves
// up the list is the same object: the list updates in place instead of being
// rebuilt under the person's pointer.
internal sealed class SizeRow : INotifyPropertyChanged
{
    private string _name = "";
    private string _size = "";
    private double _share;
    private string _where = "";
    private string _warning = "";

    public SizeRow(SizeRowKind kind, string key, string? path)
    {
        Kind = kind;
        Key = key;
        Path = path;
        Glyph = kind == SizeRowKind.Folder ? AtelierGlyphs.Folder : AtelierGlyphs.File;
        // A heavy file sits one step in, under the row of its folder, the way
        // a tree indents a child (12.21).
        Indent = kind == SizeRowKind.HeavyFile ? new Thickness(28, 0, 0, 0) : new Thickness(0);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SizeRowKind Kind { get; }

    // What identifies the row from one snapshot to the next.
    public string Key { get; }

    // The folder or the file; null for the place's own files.
    public string? Path { get; }

    public string Glyph { get; }
    public Thickness Indent { get; }

    public string Name { get => _name; set => Set(ref _name, value, nameof(Name)); }

    // "2,4 Go", or "…" while the folder is not measured yet (12.23).
    public string Size { get => _size; set => Set(ref _size, value, nameof(Size)); }

    // The bar's length, the part of the place's total, between 0 and 1.
    public double Share { get => _share; set => Set(ref _share, value, nameof(Share)); }

    // For a heavy file deeper than the folder it sits under: the rest of the
    // way ("HEIC"), so the row says where it is without repeating the name.
    public string Where
    {
        get => _where;
        set
        {
            Set(ref _where, value, nameof(Where));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WhereVisibility)));
        }
    }

    public Visibility WhereVisibility => _where.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

    // Said when folders beneath this one could not be read: its size leaves
    // them out, and the row says so instead of hiding it.
    public string Warning
    {
        get => _warning;
        set
        {
            Set(ref _warning, value, nameof(Warning));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WarningVisibility)));
        }
    }

    public Visibility WarningVisibility => _warning.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

    // What Narrator reads for the row: ListViewItem names an item it has no
    // other name for by its text.
    public override string ToString()
    {
        string text = AtelierText.Format("Sizes_Row_Automation_Format", Name, Size);
        return _warning.Length == 0 ? text : $"{text}, {_warning}";
    }

    private void Set<T>(ref T field, T value, string name)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
