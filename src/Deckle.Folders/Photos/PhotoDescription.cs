namespace Deckle.Folders;

// A written description of one photo, as the panel shows it (12.5): the text,
// the model that wrote it, and when. "A model on this computer wrote it" is
// the panel's own sentence; the model's name is kept so a later description
// by another model can be told apart.
public sealed record PhotoDescription(string Text, string Model, DateTimeOffset WrittenAt);

// Why a photo got no description, in words a view turns into a sentence. The
// first four are about the setup and say the same thing for every photo; the
// next two are about this photo or this request; Stopped is the person's own
// gesture.
public enum PhotoDescriptionFailure
{
    // No local provider and model are chosen for descriptions, or the chosen
    // provider is gone or turned off.
    NoModelChosen,

    // The chosen server did not answer: not running, wrong port.
    ServerUnreachable,

    // The chosen model is text-only: it cannot read a picture.
    ImagesNotSupported,

    // The chosen provider is not on this machine (a LAN address, a hosted
    // API): no picture is sent there (spec, Behaviours).
    EndpointNotLocal,

    // The photo could not be turned into a picture for the model: gone,
    // locked, or a format nothing on this machine decodes (Open question 33,
    // "a photo that cannot be read").
    PhotoUnreadable,

    // The person stopped it.
    Stopped,

    // Anything else: the server refused the request, answered nothing, or
    // took longer than a photo is allowed.
    Other,
}

// What one request answers: a description, or the reason there is none.
public sealed record PhotoDescriptionResult(PhotoDescription? Description, PhotoDescriptionFailure? Failure)
{
    public bool Succeeded => Description is not null;

    public static PhotoDescriptionResult Of(PhotoDescription description) => new(description, null);

    public static PhotoDescriptionResult Failed(PhotoDescriptionFailure failure) => new(null, failure);

    // The failures that hold for every photo of a run: once one comes back, a
    // folder run stops there and says why, instead of counting the same
    // failure four hundred times.
    public static bool IsSetupFailure(PhotoDescriptionFailure failure) => failure is
        PhotoDescriptionFailure.NoModelChosen or
        PhotoDescriptionFailure.ServerUnreachable or
        PhotoDescriptionFailure.ImagesNotSupported or
        PhotoDescriptionFailure.EndpointNotLocal;
}
