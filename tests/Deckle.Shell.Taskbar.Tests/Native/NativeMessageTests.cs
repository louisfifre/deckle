using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

[Trait("Category", "unit")]
public sealed class NativeMessageTests
{
    [Fact]
    public void AHelloCarriesTheProtocolTheProcessAndTheModule()
    {
        var message = NativeMessage.Parse(
            """{"t":"hello","protocol":"1","pid":"4242","module":"C:\\x\\Deckle.Shell.Taskbar.Native.dll"}""");

        var hello = Assert.IsType<NativeHello>(message);
        Assert.Equal(1, hello.Protocol);
        Assert.Equal(4242, hello.ProcessId);
        Assert.Equal(@"C:\x\Deckle.Shell.Taskbar.Native.dll", hello.ModulePath);
    }

    [Fact]
    public void ALogRecordCarriesItsLevelEventMessageAndFields()
    {
        var message = NativeMessage.Parse(
            """{"t":"log","level":"warning","event":"unadvise_failed","msg":"It failed","fields":{"hr":"0x80004005","n":"3"}}""");

        var record = Assert.IsType<NativeLogRecord>(message);
        Assert.Equal(NativeLogLevel.Warning, record.Level);
        Assert.Equal("unadvise_failed", record.Event);
        Assert.Equal("It failed", record.Message);
        Assert.Equal("0x80004005", record.Fields["hr"]);
        Assert.Equal("hr=0x80004005 | n=3", record.FieldsText());
    }

    [Theory]
    [InlineData("verbose", "Verbose")]
    [InlineData("info", "Info")]
    [InlineData("warning", "Warning")]
    [InlineData("error", "Error")]
    [InlineData("something-newer", "Info")]
    public void EveryLevelNameMapsAndAnUnknownOneReadsAsInfo(string name, string expected)
    {
        Assert.Equal(Enum.Parse<NativeLogLevel>(expected), NativeLogLevelNames.Parse(name));
    }

    // A 64-bit handle is a string on the wire; a bare number in a field still
    // reads, as its raw text, rather than being dropped.
    [Fact]
    public void AFieldThatIsNotAStringKeepsItsRawText()
    {
        var record = Assert.IsType<NativeLogRecord>(NativeMessage.Parse(
            """{"t":"log","level":"info","event":"e","msg":"m","fields":{"count":12}}"""));

        Assert.Equal("12", record.Fields["count"]);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("""{"t":"mystery"}""")]
    [InlineData("""{"no_t":true}""")]
    public void ALineTheHostCannotUseComesBackWholeAndNeverThrows(string line)
    {
        var message = NativeMessage.Parse(line);

        Assert.Equal(line, Assert.IsType<NativeUnreadableMessage>(message).Line);
    }
}

