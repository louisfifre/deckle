namespace Deckle.Folders;

// The one prompt that asks a model for a photo's description. It is read by a
// model, not by the person, so it is written in English; it asks for the
// answer in French, the language of the panel that shows it.
//
// What it asks for follows the spec: a short, factual text that search can
// find later ("Find a photo later": Louis types what he remembers). No
// guessing at names, feelings or a place the picture does not show: the
// capture data already carries the date and the place, and face recognition
// is another piece of work.
public static class PhotoDescriptionPrompt
{
    public const string Text =
        "Describe this photo in French, in two or three short factual sentences. " +
        "Say what is visible: the main subject, the people and what they are doing, the setting, " +
        "notable objects, the light and the time of day if it is clear. " +
        "Do not guess names, emotions or a location that the picture does not show. " +
        "Do not start with \"Cette photo\" or \"L'image\". Answer with the description only.";
}
