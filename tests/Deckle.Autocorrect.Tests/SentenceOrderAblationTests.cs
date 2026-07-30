using Deckle.Autocorrect.Probe;
using Xunit;

namespace Deckle.Autocorrect.Tests;

[Trait("Category", "unit")]
public sealed class SentenceOrderAblationTests
{
    [Fact]
    public void ArgumentsSelectFrozenOrderAblationDesign()
    {
        ProbeArguments? parsed = ProbeArguments.Parse(
            ["--sentence-order-ablation", "--model", "judge", "--provider", "dml"]);

        Assert.NotNull(parsed);
        Assert.Equal(ProbeMode.SentenceOrderAblation, parsed.Mode);
        Assert.Equal("dml", parsed.Provider);
        Assert.Equal("judge", Assert.Single(parsed.Models).Directory);
        Assert.Null(ProbeArguments.Parse(
            ["--sentence-order-ablation", "--model", "judge", "--iterations", "7"]));
    }

    [Fact]
    public void BaseQualityScheduleBalancesEveryMethodAcrossCallPositions()
    {
        var calls = Enumerable.Range(0, CorrectionBenchmarkCorpus.All.Count)
            .SelectMany(caseIndex => SentenceOrderAblationFixture
                .QualityMethods(caseIndex, repetition: 0)
                .Select((method, position) => new
                {
                    CaseIndex = caseIndex,
                    Position = position,
                    Method = method,
                }))
            .ToArray();

        foreach (int caseIndex in Enumerable.Range(0, CorrectionBenchmarkCorpus.All.Count))
        {
            Assert.Equal(
                Enum.GetValues<SentenceOrderAblationMethod>().Order(),
                calls.Where(call => call.CaseIndex == caseIndex)
                    .Select(call => call.Method)
                    .Order());
        }

        foreach (SentenceOrderAblationMethod method in
            Enum.GetValues<SentenceOrderAblationMethod>())
        {
            int[] positions = Enumerable.Range(0, 3)
                .Select(position => calls.Count(call =>
                    call.Method == method && call.Position == position))
                .ToArray();
            Assert.InRange(positions.Max() - positions.Min(), 0, 1);
        }
    }

    [Fact]
    public void PriorDisagreementCasesReceiveThreeDistinctMethodOrders()
    {
        string[] corpusIds = CorrectionBenchmarkCorpus.All
            .Select(static benchmarkCase => benchmarkCase.Id)
            .ToArray();

        Assert.Equal(9, SentenceOrderAblationFixture.DisagreementCaseIds.Count);
        Assert.All(
            SentenceOrderAblationFixture.DisagreementCaseIds,
            caseId => Assert.Contains(caseId, corpusIds));

        foreach (string caseId in SentenceOrderAblationFixture.DisagreementCaseIds)
        {
            int caseIndex = Array.IndexOf(corpusIds, caseId);
            SentenceOrderAblationMethod[][] orders = Enumerable.Range(0, 3)
                .Select(repetition => SentenceOrderAblationFixture
                    .QualityMethods(caseIndex, repetition)
                    .ToArray())
                .ToArray();
            Assert.Equal(
                3,
                orders.Select(order => string.Join(',', order)).Distinct().Count());
            foreach (SentenceOrderAblationMethod method in
                Enum.GetValues<SentenceOrderAblationMethod>())
            {
                Assert.Equal(
                    Enumerable.Range(0, 3),
                    orders.Select(order => Array.IndexOf(order, method)).Order());
            }
            Assert.Equal(3, SentenceOrderAblationFixture.QualityRepetitions(caseId));
        }

        Assert.Equal(
            1,
            SentenceOrderAblationFixture.QualityRepetitions("unseen_case"));
    }

    [Fact]
    public void LatencyBlocksBalanceEveryMethodAcrossCallPositions()
    {
        var calls = Enumerable.Range(0, SentenceOrderAblationFixture.LatencyBlocks)
            .SelectMany(block => SentenceOrderAblationFixture.LatencyMethods(block)
                .Select((method, position) => new
                {
                    Block = block,
                    Position = position,
                    Method = method,
                }))
            .ToArray();

        Assert.Equal(60, calls.Length);
        foreach (int block in Enumerable.Range(0, SentenceOrderAblationFixture.LatencyBlocks))
            Assert.Equal(
                Enum.GetValues<SentenceOrderAblationMethod>().Order(),
                calls.Where(call => call.Block == block)
                    .Select(call => call.Method)
                    .Order());

        foreach (SentenceOrderAblationMethod method in
            Enum.GetValues<SentenceOrderAblationMethod>())
        {
            Assert.Equal(20, calls.Count(call => call.Method == method));
            int[] positions = Enumerable.Range(0, 3)
                .Select(position => calls.Count(call =>
                    call.Method == method && call.Position == position))
                .ToArray();
            Assert.InRange(positions.Max() - positions.Min(), 0, 1);
        }
    }
}
