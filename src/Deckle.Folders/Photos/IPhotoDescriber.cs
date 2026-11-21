namespace Deckle.Folders;

// The seam a view and the description queue hold: a photo's path in, a
// description and the model that wrote it out, or the reason there is none.
// It never throws for a photo or a model that fails; the result says why.
// PhotoDescriber is the one implementation: it reduces the photo and hands
// the picture to an IPhotoDescriptionModel.
public interface IPhotoDescriber
{
    Task<PhotoDescriptionResult> DescribeAsync(string photoPath, CancellationToken cancellationToken);
}

// What a model on this machine answers for one picture. This library knows
// no model and no server: the host gives it the adapter over Deckle.Llm
// (Deckle.App, FoldersPhotoDescriptions/LlmPhotoDescriptionModel.cs), which
// reads the stored choice of provider and model, sends only to a local
// server, and words its failures as PhotoDescriptionFailure.
public interface IPhotoDescriptionModel
{
    /// <param name="jpeg">The reduced picture (PhotoPictureReducer), never
    /// the original file.</param>
    /// <param name="prompt">PhotoDescriptionPrompt.Text.</param>
    Task<PhotoDescriptionResult> DescribeAsync(byte[] jpeg, string prompt, CancellationToken cancellationToken);
}
