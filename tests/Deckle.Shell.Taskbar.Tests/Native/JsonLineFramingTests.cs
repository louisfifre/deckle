using System.Text;
using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

[Trait("Category", "unit")]
public sealed class JsonLineFramingTests
{
    [Fact]
    public void AMessageIsItsJsonFollowedByOneLineFeed()
    {
        byte[] bytes = JsonLineFraming.Frame("""{"t":"stop"}""");

        Assert.Equal("{\"t\":\"stop\"}\n", Encoding.UTF8.GetString(bytes));
    }

    // The DLL reads UTF-8 with no byte order mark to skip.
    [Fact]
    public void TheFramedBytesCarryNoByteOrderMark()
    {
        byte[] bytes = JsonLineFraming.Frame("{}");

        Assert.NotEqual(0xEF, bytes[0]);
    }

    [Fact]
    public void NonAsciiTextTravelsAsUtf8()
    {
        byte[] bytes = JsonLineFraming.Frame("{\"msg\":\"é∶\"}");

        Assert.Equal("{\"msg\":\"é∶\"}\n", JsonLineFraming.Utf8.GetString(bytes));
        Assert.Contains((byte)0xC3, bytes);
    }

    [Theory]
    [InlineData("{\"a\":1,\n\"b\":2}")]
    [InlineData("{\"a\":1}\r")]
    public void TextWithALineBreakIsRefusedBecauseItWouldReadAsTwoMessages(string json)
    {
        Assert.Throws<ArgumentException>(() => JsonLineFraming.Frame(json));
    }
}
