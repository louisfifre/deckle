using Deckle.Modules;
using Xunit;

namespace Deckle.Settings.Tests;

// The legacy combined settings.json keeps its Whisper section under "whisp".
// It must be written to the folder the transcription store reads, which is
// the module's catalog id, not the section's old name.
[Trait("Category", "regression")]
public sealed class SettingsBootstrapLegacyTargetTests
{
    [Fact]
    public void TheLegacyWhispSectionLandsInTheFolderTheTranscriptionStoreReads()
        => Assert.Equal(ModuleIds.Transcription, SettingsBootstrap.TranscriptionModuleId);
}