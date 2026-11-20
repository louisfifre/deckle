using Deckle.Transcription.Parakeet.Tokenization;
using Xunit;

namespace Deckle.Transcription.Parakeet.Tests.Tokenization;

// Reads the real vocab.txt from the maintainer's model folder. It is a system test because it
// depends on a machine-local download; it skips when the folder is absent, and nothing of the
// file is copied into the repository.
[Trait("Category", "system")]
public sealed class ParakeetVocabularySystemTests
{
    private static readonly string ModelFolder = Environment.GetEnvironmentVariable("DECKLE_PARAKEET_MODEL_DIR") ?? "";

    [Fact]
    public void RealVocabularyLoadsWithItsBlankLast()
    {
        string path = new ParakeetModelFiles(ModelFolder).Vocabulary;
        Assert.SkipUnless(File.Exists(path), $"No model at {path}.");

        ParakeetVocabulary vocabulary = ParakeetVocabulary.Load(path);

        Assert.Equal(vocabulary.Count - 1, vocabulary.BlankId);
        Assert.True(vocabulary.IsSilent(vocabulary.BlankId));
        Assert.True(vocabulary.IsSilent(0)); // <unk>
    }
}
