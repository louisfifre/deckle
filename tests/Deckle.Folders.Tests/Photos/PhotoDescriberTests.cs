using Xunit;

namespace Deckle.Folders.Tests;

// The describer hands the model a reduced picture and the one prompt, and
// answers each failure as a type a view can word.
[Collection(FoldersProviderCollection.Name)]
[Trait("Category", "integration")]
public sealed class PhotoDescriberTests
{
    [Fact]
    public async Task TheModelGetsAReducedJpegAndThePrompt()
    {
        using var scratch = new ScratchFolder();
        string photo = await PhotoPictureReducerTests.WriteJpegAsync(scratch, "a.JPG", 2400, 1600);
        var model = new RecordingModel(PhotoDescriptionResult.Of(new PhotoDescription("Un dégradé.", "m", DateTimeOffset.Now)));
        using var describer = new PhotoDescriber(model);

        PhotoDescriptionResult result = await describer.DescribeAsync(photo, CancellationToken.None);

        Assert.Equal("Un dégradé.", result.Description?.Text);
        Assert.Equal(PhotoDescriptionPrompt.Text, model.Prompt);
        Assert.NotNull(model.Jpeg);
        Assert.True(model.Jpeg.Length < new FileInfo(photo).Length);
        Assert.Equal(0xFF, model.Jpeg[0]);
        Assert.Equal(0xD8, model.Jpeg[1]);
    }

    [Fact]
    public async Task APhotoThatCannotBeReadNeverReachesTheModel()
    {
        var model = new RecordingModel(PhotoDescriptionResult.Failed(PhotoDescriptionFailure.Other));
        using var describer = new PhotoDescriber(model, (_, _, _) => Task.FromResult<byte[]?>(null));

        PhotoDescriptionResult result = await describer.DescribeAsync(@"C:\nowhere\a.JPG", CancellationToken.None);

        Assert.Equal(PhotoDescriptionFailure.PhotoUnreadable, result.Failure);
        Assert.Null(model.Jpeg);
    }

    [Fact]
    public async Task StoppingWhileThePictureIsMadeAnswersStopped()
    {
        using var stop = new CancellationTokenSource();
        var model = new RecordingModel(PhotoDescriptionResult.Failed(PhotoDescriptionFailure.Other));
        using var describer = new PhotoDescriber(model, async (_, _, ct) =>
        {
            stop.Cancel();
            await Task.Delay(Timeout.Infinite, ct);
            return null;
        });

        PhotoDescriptionResult result = await describer.DescribeAsync("a.JPG", stop.Token);

        Assert.Equal(PhotoDescriptionFailure.Stopped, result.Failure);
        Assert.Null(model.Jpeg);
    }

    [Theory]
    [InlineData(PhotoDescriptionFailure.NoModelChosen)]
    [InlineData(PhotoDescriptionFailure.ServerUnreachable)]
    [InlineData(PhotoDescriptionFailure.ImagesNotSupported)]
    [InlineData(PhotoDescriptionFailure.EndpointNotLocal)]
    [InlineData(PhotoDescriptionFailure.Stopped)]
    [InlineData(PhotoDescriptionFailure.Other)]
    public async Task TheModelsFailureReachesTheView(PhotoDescriptionFailure failure)
    {
        var model = new RecordingModel(PhotoDescriptionResult.Failed(failure));
        using var describer = new PhotoDescriber(model, (_, _, _) => Task.FromResult<byte[]?>([0xFF, 0xD8]));

        PhotoDescriptionResult result = await describer.DescribeAsync("a.JPG", CancellationToken.None);

        Assert.Equal(failure, result.Failure);
        Assert.False(result.Succeeded);
    }

    sealed class RecordingModel(PhotoDescriptionResult answer) : IPhotoDescriptionModel
    {
        public byte[]? Jpeg { get; private set; }
        public string? Prompt { get; private set; }

        public Task<PhotoDescriptionResult> DescribeAsync(byte[] jpeg, string prompt, CancellationToken cancellationToken)
        {
            Jpeg = jpeg;
            Prompt = prompt;
            return Task.FromResult(answer);
        }
    }
}
