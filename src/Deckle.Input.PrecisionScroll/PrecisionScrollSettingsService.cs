using System.Text.Json;
using Deckle.Core;

namespace Deckle.Input.PrecisionScroll;

public sealed class PrecisionScrollSettingsService
{
    private static readonly Lazy<PrecisionScrollSettingsService> _instance =
        new(() => new PrecisionScrollSettingsService());

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly JsonSettingsStore<PrecisionScrollSettings> _store;
    private event Action? _changed;

    public static PrecisionScrollSettingsService Instance => _instance.Value;

    public PrecisionScrollSettings Current => _store.Current;

    public event Action? Changed
    {
        add => _changed += value;
        remove => _changed -= value;
    }

    private PrecisionScrollSettingsService()
    {
        string directory = AppPaths.GetModuleDirectory("precision-scroll");
        Directory.CreateDirectory(directory);
        _store = new JsonSettingsStore<PrecisionScrollSettings>(
            path: Path.Combine(directory, "settings.json"),
            mutexName: $"{AppPaths.AppFolderName}-Settings-PrecisionScroll-Save",
            jsonOptions: _jsonOptions);
    }

    // Runtime consumers react to the in-memory value synchronously. Only the
    // disk write is debounced, so a toggle or slider never waits on storage.
    public void Save()
    {
        _changed?.Invoke();
        _store.Save();
    }

    public void Flush() => _store.Flush();
}
