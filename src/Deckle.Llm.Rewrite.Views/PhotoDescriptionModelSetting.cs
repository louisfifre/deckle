namespace Deckle.Llm.Rewrite.Views;

// The model that describes Deckle Folders' photos, as the Rewriting page edits
// it. The choice is Deckle Folders' own (it lives in that module's data folder),
// and this library does not reference Deckle Folders: the host that knows both
// hands the page these two functions (LlmSettingsModule.PhotoDescriptionModel),
// as it hands TranscriptionStreams. Unset, the page shows no such section.
public sealed record PhotoDescriptionModelSetting(
    Func<PhotoDescriptionModelPick?> Read,
    Action<PhotoDescriptionModelPick?> Write);

// A provider of the list (by its stable Id) and a model name it serves.
public sealed record PhotoDescriptionModelPick(string ProviderId, string Model);
