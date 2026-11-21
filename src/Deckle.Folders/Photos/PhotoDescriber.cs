namespace Deckle.Folders;

// Describes one photo: reduces it to the picture a model reads
// (PhotoPictureReducer), then asks the model the one prompt
// (PhotoDescriptionPrompt). The model behind IPhotoDescriptionModel is the
// host's adapter over a local server; this class knows no server.
//
// It owns the shell thread the reducer's last source needs, and ends it on
// Dispose.
public sealed class PhotoDescriber : IPhotoDescriber, IDisposable
{
    private readonly IPhotoDescriptionModel _model;
    private readonly Func<string, ShellWorkQueue?, CancellationToken, Task<byte[]?>> _reduce;
    private readonly Lazy<ShellWorkQueue> _shell = new(() => new ShellWorkQueue("Folders photo descriptions"));

    public PhotoDescriber(IPhotoDescriptionModel model)
        : this(model, (path, shell, ct) => PhotoPictureReducer.ReduceAsync(path, shell, ct)) { }

    // The tests stand in for the reduction to reach the failures it leads to.
    internal PhotoDescriber(IPhotoDescriptionModel model, Func<string, ShellWorkQueue?, CancellationToken, Task<byte[]?>> reduce)
    {
        _model = model;
        _reduce = reduce;
    }

    public async Task<PhotoDescriptionResult> DescribeAsync(string photoPath, CancellationToken cancellationToken)
    {
        try
        {
            byte[]? jpeg = await _reduce(photoPath, _shell.Value, cancellationToken);
            if (jpeg is null) return PhotoDescriptionResult.Failed(PhotoDescriptionFailure.PhotoUnreadable);

            cancellationToken.ThrowIfCancellationRequested();
            return await _model.DescribeAsync(jpeg, PhotoDescriptionPrompt.Text, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PhotoDescriptionResult.Failed(PhotoDescriptionFailure.Stopped);
        }
    }

    public void Dispose()
    {
        if (_shell.IsValueCreated) _shell.Value.Dispose();
    }
}
