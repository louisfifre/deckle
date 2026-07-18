using Xunit;

namespace Deckle.Diagnostics.Logging.Tests;

public sealed class LogTransferTests
{
    private static readonly string[] Entries = ["one", "two", "three"];

    [Fact]
    public void All_IgnoresFilterAndSelection()
    {
        IReadOnlyList<string> result = LogTransferScopeResolver.Resolve(
            LogTransferScope.All,
            Entries,
            value => value.Contains('t'),
            new HashSet<string> { "two" });

        Assert.Equal(Entries, result);
    }

    [Fact]
    public void Filtered_EvaluatesTheFullJournal()
    {
        IReadOnlyList<string> result = LogTransferScopeResolver.Resolve(
            LogTransferScope.Filtered,
            Entries,
            value => value.Contains('t'),
            new HashSet<string>());

        Assert.Equal(["two", "three"], result);
    }

    [Fact]
    public void Selection_PreservesJournalOrder()
    {
        IReadOnlyList<string> result = LogTransferScopeResolver.Resolve(
            LogTransferScope.Selection,
            Entries,
            _ => true,
            new HashSet<string> { "three", "one" });

        Assert.Equal(["one", "three"], result);
    }

    [Fact]
    public void Selection_WithNothingSelected_IsEmptyForCtrlC()
    {
        IReadOnlyList<string> result = LogTransferScopeResolver.Resolve(
            LogTransferScope.Selection,
            Entries,
            _ => true,
            new HashSet<string>());

        Assert.Empty(result);
    }

    [Fact]
    public void Format_PreservesCompleteLongText()
    {
        string longText = new('x', 20_000);

        string result = LogTransferText.Format([longText], value => value);

        Assert.Equal(longText + Environment.NewLine, result);
    }
}
