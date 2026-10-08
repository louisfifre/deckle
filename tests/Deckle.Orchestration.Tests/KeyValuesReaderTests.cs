using Deckle.Orchestration;
using Xunit;

namespace Deckle.Orchestration.Tests;

[Trait("Category", "unit")]
public sealed class KeyValuesReaderTests
{
    [Fact]
    public void NestedBlocksBecomeChildren()
    {
        var nodes = KeyValuesReader.Parse("""
            "root"
            {
                "a" "1"
                "inner" { "b" "2" }
            }
            "second" "x"
            """);

        Assert.Equal(2, nodes.Count);
        KeyValuesNode root = nodes[0];
        Assert.Null(root.Value);
        Assert.Equal("1", root.GetValue("a"));
        Assert.Equal("2", root["inner"]!.GetValue("b"));
        Assert.Equal("x", nodes[1].Value);
    }

    [Fact]
    public void KeysAreFoundWithoutRegardToCase()
    {
        var root = KeyValuesReader.Parse("\"AppState\" { \"InstallDir\" \"Game\" }")[0];

        Assert.Equal("Game", root.GetValue("installdir"));
    }

    [Fact]
    public void QuotedStringsUnderstandTheFourEscapes()
    {
        var node = KeyValuesReader.Parse(@"""k"" ""C:\\Games\\a \""b\""\n\t""")[0];

        Assert.Equal("C:\\Games\\a \"b\"\n\t", node.Value);
    }

    [Fact]
    public void CommentsAndIncludeLinesAreSkipped()
    {
        var nodes = KeyValuesReader.Parse("""
            #base "other.vdf"
            #include "more.vdf"
            // a comment line
            "root" // trailing comment
            {
                "a" "1" // another
            }
            """);

        KeyValuesNode root = Assert.Single(nodes);
        Assert.Equal("1", root.GetValue("a"));
    }

    [Fact]
    public void BareTokensWorkAsKeysAndValues()
    {
        var root = KeyValuesReader.Parse("root { count 3 \"quoted\" bare }")[0];

        Assert.Equal("3", root.GetValue("count"));
        Assert.Equal("bare", root.GetValue("quoted"));
    }

    [Theory]
    [InlineData("\"root\" { \"a\" \"1\"", 1)]       // block never closed
    [InlineData("\"a\" \"1\"\n}", 2)]               // stray closing brace
    [InlineData("\"a\"", 1)]                        // key without a value
    [InlineData("\"a\"\n\"unterminated", 2)]        // string never closed
    [InlineData("{ \"a\" \"1\" }", 1)]              // block without a key
    public void MalformedTextThrowsWithTheLineNumber(string text, int line)
    {
        var error = Assert.Throws<FormatException>(() => KeyValuesReader.Parse(text));

        Assert.Contains($"line {line}", error.Message);
    }
}
