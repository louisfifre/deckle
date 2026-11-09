using System.Text.Json;
using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

[Trait("Category", "unit")]
public sealed class NativeCommandTests
{
    [Fact]
    public void TheFixedCommandsAreSingleCompactLines()
    {
        Assert.Equal("""{"t":"stop"}""", NativeCommand.Stop);
        Assert.Equal("""{"t":"ping"}""", NativeCommand.Ping);
    }

    // A document read from a pretty-printed file must still be one line.
    [Fact]
    public void ApplyWrapsAPrettyPrintedDocumentOnOneLine()
    {
        string line = NativeCommand.Apply("{\n  \"rules\": [ 1,\n 2 ],\n  \"on\": true\n}");

        Assert.Equal("""{"t":"apply","doc":{"rules":[1,2],"on":true}}""", line);
        Assert.DoesNotContain('\n', line);
        JsonLineFraming.Frame(line);   // does not throw: it is one line
    }

    [Fact]
    public void ApplyRefusesADocumentThatIsNotJson()
    {
        Assert.ThrowsAny<JsonException>(() => NativeCommand.Apply("{ nope"));
    }
}
