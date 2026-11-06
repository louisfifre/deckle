using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

[Trait("Category", "integration")]
public sealed class DeletionApprovalRelayTests
{
    private static string UniquePipe() => "deckle-test-approval-" + Guid.NewGuid().ToString("N");

    [Theory]
    [InlineData(DeletionApprovalOutcome.Approved)]
    [InlineData(DeletionApprovalOutcome.Declined)]
    [InlineData(DeletionApprovalOutcome.Unanswered)]
    public async Task The_person_s_answer_reaches_the_gateway(DeletionApprovalOutcome answer)
    {
        string pipe = UniquePipe();
        var person = new RecordingApproval(answer);
        await using var server = new DeletionApprovalPipeServer(pipe, person);
        server.Start();

        var candidate = new DeletionCandidate("bafy-id", "Name", "task", 3);
        DeletionApprovalOutcome outcome = await new PipeDeletionApproval(pipe)
            .RequestAsync(candidate, TestContext.Current.CancellationToken);

        Assert.Equal(answer, outcome);
        Assert.Equal(candidate, person.Asked);
    }

    [Fact]
    public async Task Without_Deckle_listening_the_delete_fails_closed()
    {
        DeletionApprovalOutcome outcome = await new PipeDeletionApproval(UniquePipe(), TimeSpan.FromMilliseconds(200))
            .RequestAsync(new DeletionCandidate("id", "Name", "task", 0), TestContext.Current.CancellationToken);

        Assert.Equal(DeletionApprovalOutcome.Unavailable, outcome);
    }

    [Fact]
    public async Task The_relay_keeps_answering_after_a_request()
    {
        string pipe = UniquePipe();
        await using var server = new DeletionApprovalPipeServer(pipe, new RecordingApproval(DeletionApprovalOutcome.Approved));
        server.Start();
        var client = new PipeDeletionApproval(pipe);

        for (int i = 0; i < 6; i++)
        {
            Assert.Equal(DeletionApprovalOutcome.Approved, await client.RequestAsync(
                new DeletionCandidate($"id{i}", "Name", "task", 0), TestContext.Current.CancellationToken));
        }
    }

    private sealed class RecordingApproval(DeletionApprovalOutcome answer) : IDeletionApproval
    {
        public DeletionCandidate? Asked { get; private set; }

        public Task<DeletionApprovalOutcome> RequestAsync(DeletionCandidate candidate, CancellationToken ct)
        {
            Asked = candidate;
            return Task.FromResult(answer);
        }
    }
}
