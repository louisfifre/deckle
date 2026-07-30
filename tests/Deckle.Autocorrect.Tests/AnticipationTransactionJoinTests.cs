using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Deckle.Autocorrect.Probe;
using Xunit;

namespace Deckle.Autocorrect.Tests;

[Trait("Category", "unit")]
public sealed class AnticipationTransactionJoinTests
{
    [Fact]
    public void ParsesTheAggregateOnlyJoinMode()
    {
        ProbeArguments? parsed = ProbeArguments.Parse(
            ["--anticipation-transaction-join", "--stream", "typing.jsonl", "--stream-bytes", "123"]);

        Assert.NotNull(parsed);
        Assert.Equal(ProbeMode.AnticipationTransactionJoin, parsed.Mode);
        Assert.Equal("typing.jsonl", parsed.StreamPath);
        Assert.Equal(123, parsed.StreamBytes);
    }

    [Fact]
    public void TerminalBranchChangesOnlyTheExactFinalPunctuation()
    {
        const string literal = "je suis la?";

        Assert.Equal("je suis la.", AnticipationTransactionJoinCommand.ReplaceTerminal(literal, '.'));
        Assert.Equal("je suis la!", AnticipationTransactionJoinCommand.ReplaceTerminal(literal, '!'));
        Assert.Throws<ArgumentException>(() =>
            AnticipationTransactionJoinCommand.ReplaceTerminal("je suis la", '.'));
    }

    [Fact]
    public void PrefixMutationKeepsTheHashOfBytesActuallyAnalyzed()
    {
        byte[] before = Encoding.UTF8.GetBytes("before\n");
        byte[] after = Encoding.UTF8.GetBytes("after!\n");

        TypingStreamPrefixIdentity identity =
            AnticipationTransactionJoinCommand.CreatePrefixIdentity(
                before,
                after,
                lineBoundary: true);

        Assert.Equal(Convert.ToHexString(SHA256.HashData(before)), identity.AnalyzedSha256);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(after)), identity.ObservedAfterSha256);
        Assert.False(identity.Stable);
    }

    [Theory]
    [InlineData(false, 1, 0, 1, 0, 0, 0, 0, false, "invalid_run_no_eligibility_interpretation")]
    [InlineData(true, 0, 0, 1, 0, 0, 0, 0, false, "invalid_run_no_eligibility_interpretation")]
    [InlineData(true, 1, 1, 1, 0, 0, 0, 0, false, "invalid_run_no_eligibility_interpretation")]
    [InlineData(true, 1, 0, 1, 0, 0, 0, 0, true, "valid_zero_eligible_observations")]
    [InlineData(true, 1, 0, 1, 0, 0, 0, 2, true, "eligible_observations_present")]
    public void EligibilityIsInterpretedOnlyForTechnicallyValidRuns(
        bool prefixStable,
        int parsedRuns,
        int malformedRuns,
        int terminalGestures,
        int suffixMismatches,
        int transactionMismatches,
        int protocolViolations,
        int joinedEligibleTransactions,
        bool expectedValidity,
        string expectedOutcome)
    {
        JoinValidity validity = AnticipationTransactionJoinCommand.ClassifyValidity(
            prefixStable,
            parsedRuns,
            malformedRuns,
            terminalGestures,
            suffixMismatches,
            transactionMismatches,
            protocolViolations,
            joinedEligibleTransactions);

        Assert.Equal(expectedValidity, validity.TechnicallyValid);
        Assert.Equal(expectedOutcome, validity.EligibilityOutcome);
    }

    [Theory]
    [InlineData("prefix je suis la.", "je suis la.", '.', true)]
    [InlineData("prefix je suis la?", "je suis la.", '.', false)]
    [InlineData("prefix je suis la.", "je suis la.", '?', false)]
    public void TransactionMustMatchTheVisibleSuffixAndTerminal(
        string visible,
        string literal,
        char terminal,
        bool expected) =>
        Assert.Equal(
            expected,
            AnticipationTransactionJoinCommand.MatchesVisibleTransaction(
                visible,
                literal,
                terminal));

    [Fact]
    public void JoinsAnEligibleTransactionWithoutEmittingPrivateText()
    {
        const string sentinel = "Il y a une seul erreur.";
        string setupLine = JsonSerializer.Serialize(new
        {
            payload = new
            {
                process = "private-process",
                text = "setup",
                erased = 0,
                closure = "enter",
                timing = "0,0,0,0,0",
            },
        }) + "\n";
        string targetLine = JsonSerializer.Serialize(new
        {
            payload = new
            {
                process = "private-process",
                text = sentinel,
                erased = 0,
                closure = "enter",
                timing = string.Join(',', Enumerable.Repeat("0", sentinel.Length - 1).Append("600")),
            },
        }) + "\n";
        string stream = setupLine + targetLine;
        string path = Path.Combine(Path.GetTempPath(), $"deckle-join-{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(path, stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        try
        {
            AnticipationTransactionJoinFileReport report =
                AnticipationTransactionJoinCommand.AnalyzeFile(
                    path,
                    Encoding.UTF8.GetByteCount(stream),
                    ResolveDataDirectory());

            Assert.True(report.AnalyzedPrefixStable);
            Assert.True(report.TechnicallyValid);
            Assert.Equal(
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stream))),
                report.AnalyzedSha256);
            Assert.Equal(1, report.Replay.FirstTerminalGestures);
            Assert.Equal(1, report.Replay.SubmittedTransactions);
            Assert.Equal(1, report.Replay.JoinedEligibleTransactions);
            Assert.Equal(0, report.Replay.SuffixMismatches);
            Assert.Equal(0, report.Replay.TransactionMismatches);
            Assert.Equal("eligible_observations_present", report.Replay.EligibilityOutcome);
            KeyValuePair<int, int> candidateCount = Assert.Single(
                report.Replay.CandidateCountHistogram);
            Assert.True(candidateCount.Key >= 2);
            Assert.Equal(1, candidateCount.Value);

            BranchPolicyReport dot = Assert.Single(
                report.Replay.Branches,
                branch => branch.Policy == "dot_only");
            Assert.Equal(1, dot.ExactHits);
            Assert.Equal(0, dot.WastedJobs);

            ReadinessReport readiness = Assert.Single(
                report.Replay.Readiness,
                item => item.Policy == "terminal_oracle"
                    && item.TriggerDelayMilliseconds == 0
                    && item.DecisionMilliseconds == 150.0);
            Assert.Equal(1, readiness.ReadyBeforeTerminal);
            Assert.Equal(1.0, readiness.ReadyRate);

            string json = JsonSerializer.Serialize(report);
            Assert.DoesNotContain(sentinel, json, StringComparison.Ordinal);
            Assert.DoesNotContain("private-process", json, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ValidReplayCanHaveNoSubmittedTransaction()
    {
        const string sentinel = "bonjour.";
        string setupLine = JsonSerializer.Serialize(new
        {
            payload = new
            {
                process = "private-process",
                text = "setup",
                erased = 0,
                closure = "enter",
                timing = "0,0,0,0,0",
            },
        }) + "\n";
        string targetLine = JsonSerializer.Serialize(new
        {
            payload = new
            {
                process = "private-process",
                text = sentinel,
                erased = 0,
                closure = "enter",
                timing = string.Join(',', Enumerable.Repeat("0", sentinel.Length - 1).Append("600")),
            },
        }) + "\n";
        string stream = setupLine + targetLine;
        string path = Path.Combine(Path.GetTempPath(), $"deckle-join-{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(path, stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        try
        {
            AnticipationTransactionJoinFileReport report =
                AnticipationTransactionJoinCommand.AnalyzeFile(
                    path,
                    Encoding.UTF8.GetByteCount(stream),
                    ResolveDataDirectory());

            Assert.True(report.TechnicallyValid);
            Assert.Equal(1, report.Replay.FirstTerminalGestures);
            Assert.Equal(0, report.Replay.SubmittedTransactions);
            Assert.Equal(1, report.Replay.NoTransactionSubmitted);
            Assert.Equal(0, report.Replay.UnknownBoundaryTerminalGestures);
            Assert.Equal(1, report.Replay.KnownBoundaryTerminalGestures);
            Assert.Equal(1, report.Replay.KnownBoundaryNoTransactionSubmitted);
            Assert.Equal(0, report.Replay.UnknownBoundaryNoTransactionSubmitted);
            Assert.Equal(0, report.Replay.JoinedEligibleTransactions);
            Assert.Equal("valid_zero_eligible_observations", report.Replay.EligibilityOutcome);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void InitialUnknownBoundaryTerminalIsNotGeneratorEvidence()
    {
        const string sentinel = "Il y a une seul erreur.";
        string line = JsonSerializer.Serialize(new
        {
            payload = new
            {
                process = "private-process",
                text = sentinel,
                erased = 0,
                closure = "enter",
                timing = string.Join(',', Enumerable.Repeat("0", sentinel.Length - 1).Append("600")),
            },
        }) + "\n";
        string path = Path.Combine(Path.GetTempPath(), $"deckle-join-{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(path, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        try
        {
            AnticipationTransactionJoinFileReport report =
                AnticipationTransactionJoinCommand.AnalyzeFile(
                    path,
                    Encoding.UTF8.GetByteCount(line),
                    ResolveDataDirectory());

            Assert.True(report.TechnicallyValid);
            Assert.Equal(1, report.Replay.FirstTerminalGestures);
            Assert.Equal(0, report.Replay.KnownBoundaryTerminalGestures);
            Assert.Equal(1, report.Replay.UnknownBoundaryTerminalGestures);
            Assert.Equal(0, report.Replay.SubmittedTransactions);
            Assert.Equal(1, report.Replay.UnknownBoundaryNoTransactionSubmitted);
            Assert.Equal(0, report.Replay.KnownBoundaryNoTransactionSubmitted);
            Assert.Equal(0, report.Replay.JoinedEligibleTransactions);
            Assert.Equal("valid_zero_eligible_observations", report.Replay.EligibilityOutcome);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EnterBoundaryDoesNotCrossAProcessChange()
    {
        string setup = Run("first-process", "setup", 0, "enter");
        string target = Run("second-process", "Il y a une seul erreur.", 0, "enter");

        AnticipationTransactionJoinFileReport report = AnalyzeTemporary(setup + target);

        Assert.True(report.TechnicallyValid);
        Assert.Equal(1, report.Replay.SurfaceSwitches);
        Assert.Equal(0, report.Replay.KnownBoundaryTerminalGestures);
        Assert.Equal(1, report.Replay.UnknownBoundaryTerminalGestures);
        Assert.Equal(0, report.Replay.SubmittedTransactions);
    }

    [Fact]
    public void ErasureBeyondObservedSpanRevokesTheEnterBoundary()
    {
        string setup = Run("private-process", "setup", 0, "enter");
        string repair = Run("private-process", "abc", 0, "repair");
        string target = Run("private-process", "Il y a une seul erreur.", 4, "enter");

        AnticipationTransactionJoinFileReport report = AnalyzeTemporary(setup + repair + target);

        Assert.True(report.TechnicallyValid);
        Assert.Equal(0, report.Replay.KnownBoundaryTerminalGestures);
        Assert.Equal(1, report.Replay.UnknownBoundaryTerminalGestures);
        Assert.Equal(0, report.Replay.SubmittedTransactions);
    }

    [Fact]
    public void ExactObservedErasurePreservesTheEnterBoundary()
    {
        string setup = Run("private-process", "setup", 0, "enter");
        string repair = Run("private-process", "abc", 0, "repair");
        string target = Run("private-process", "Il y a une seul erreur.", 3, "enter");

        AnticipationTransactionJoinFileReport report = AnalyzeTemporary(setup + repair + target);

        Assert.True(report.TechnicallyValid);
        Assert.Equal(1, report.Replay.KnownBoundaryTerminalGestures);
        Assert.Equal(0, report.Replay.UnknownBoundaryTerminalGestures);
        Assert.Equal(1, report.Replay.SubmittedTransactions);
    }

    private static AnticipationTransactionJoinFileReport AnalyzeTemporary(string stream)
    {
        string path = Path.Combine(Path.GetTempPath(), $"deckle-join-{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(path, stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        try
        {
            return AnticipationTransactionJoinCommand.AnalyzeFile(
                path,
                Encoding.UTF8.GetByteCount(stream),
                ResolveDataDirectory());
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string Run(
        string process,
        string text,
        int erased,
        string closure) =>
        JsonSerializer.Serialize(new
        {
            payload = new
            {
                process,
                text,
                erased,
                closure,
                timing = string.Join(',', Enumerable.Repeat("0", text.Length)),
            },
        }) + "\n";

    private static string ResolveDataDirectory()
    {
        string direct = Path.Combine(AppContext.BaseDirectory, "Data");
        if (File.Exists(Path.Combine(direct, "lexicon-fr.tsv.gz")))
            return direct;
        return Path.Combine(AppContext.BaseDirectory, "Deckle.Autocorrect", "Data");
    }
}
