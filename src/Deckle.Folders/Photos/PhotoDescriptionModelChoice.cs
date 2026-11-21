using System.Text.Json;

namespace Deckle.Folders;

// Which local provider and which model describe photos: one choice, stored in
// the module's own data folder (deckle-modularity: a module carries its own
// persistence), read by the host's adapter on every request so a change
// applies to the next photo without a restart. No model name is compiled in:
// with nothing stored, there is no choice, and a description answers
// "no model chosen".
//
// ProviderId names an entry of Deckle's provider list (the one the rewrite
// settings edit) by its stable Id; this library does not read that list, the
// adapter does.
public sealed record PhotoDescriptionModelChoice(string ProviderId, string Model)
{
    public bool IsComplete => !string.IsNullOrWhiteSpace(ProviderId) && !string.IsNullOrWhiteSpace(Model);
}

// <module>\photo-description-model.json. A file that cannot be read is no
// choice: the person picks again, and the log says why.
public sealed class PhotoDescriptionModelChoiceStore
{
    public const string FileName = "photo-description-model.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _path;

    public PhotoDescriptionModelChoiceStore(string moduleDirectory) =>
        _path = Path.Combine(moduleDirectory, FileName);

    public string FilePath => _path;

    public PhotoDescriptionModelChoice? Read()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var choice = JsonSerializer.Deserialize<PhotoDescriptionModelChoice>(File.ReadAllText(_path), Json);
            return choice is { IsComplete: true } ? choice : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            DeckleFoldersSource.Log.PhotoDescriptionModelChoiceUnreadable(ex.GetType().Name);
            return null;
        }
    }

    // Null clears the choice.
    public void Write(PhotoDescriptionModelChoice? choice)
    {
        try
        {
            if (choice is null || !choice.IsComplete)
            {
                File.Delete(_path);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(choice, Json));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DeckleFoldersSource.Log.PhotoDescriptionModelChoiceSaveFailed(ex.GetType().Name);
        }
    }
}
