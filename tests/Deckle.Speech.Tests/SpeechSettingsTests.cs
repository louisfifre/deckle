using System;
using System.IO;
using System.Text.Json;
using Deckle.Core;
using Deckle.Speech;
using Xunit;

namespace Deckle.Speech.Tests;

// Covers the SpeechSettings contract: the shipped defaults, and that the POCO
// survives a save/reload through the JsonSettingsStore the service uses (with
// the same serializer options). The store is exercised on a temp file so the
// test never touches the real %LOCALAPPDATA% location or the singleton.
public class SpeechSettingsTests
{
    private static JsonSerializerOptions Options() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    [Trait("Category", "unit")]
    public void Defaults_AreDisabledPierreMidTemperature()
    {
        var s = new SpeechSettings();
        Assert.False(s.Enabled);
        Assert.Equal(SpeechVoice.Pierre, s.Voice);
        Assert.Equal(0.6, s.Temperature, 3);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void Persistence_RoundTripsThroughStore()
    {
        string path = Path.Combine(Path.GetTempPath(), $"deckle-speech-test-{Guid.NewGuid():N}.json");
        const string mutex = "Deckle-Speech-Test-Save";
        try
        {
            var store = new JsonSettingsStore<SpeechSettings>(path, mutex, Options());
            store.Current.Enabled = true;
            store.Current.Voice = SpeechVoice.Jessica;
            store.Current.Temperature = 0.7;
            store.Flush();

            var reloaded = new JsonSettingsStore<SpeechSettings>(path, mutex, Options());
            Assert.True(reloaded.Current.Enabled);
            Assert.Equal(SpeechVoice.Jessica, reloaded.Current.Voice);
            Assert.Equal(0.7, reloaded.Current.Temperature, 3);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
