using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Llm.Tests;

// The per-call usage line (tokens, cache, timings) is the LLM activity's
// detail: off by default, admitted by Detailed logs > Language model calls,
// so a person compares calls and cost only when they asked to. It is the
// only test of this suite that sets the process-wide admission.
[Trait("Category", "observability")]
public sealed class LlmUsageLogTests : IDisposable
{
    public void Dispose() => OperationalLogAdmission.Configure(static _ => false);

    [Fact]
    public void TheUsageLineIsWrittenOnlyWhenLanguageModelCallsAreDetailed()
    {
        using var listener = new TestEventListener("Deckle-Llm");
        string profile = "usage-" + Guid.NewGuid().ToString("N");

        OperationalLogAdmission.Configure(static _ => OperationalLogVerbosity.Summary);
        DeckleLlmSource.Log.RewriteResponseDetail(10, 5, 5, profile, 0, 0, 10, 120, 30, "stop");
        Assert.DoesNotContain(listener.Events, e => Of(e, profile));

        OperationalLogAdmission.Configure(static activity => activity == OperationalLogActivity.Llm
            ? OperationalLogVerbosity.Details
            : OperationalLogVerbosity.Summary);
        DeckleLlmSource.Log.RewriteResponseDetail(10, 5, 5, profile, 0, 0, 10, 120, 30, "stop", 7, 900);

        EventWrittenEventArgs line = Assert.Single(listener.Events, e => Of(e, profile));
        Assert.Equal(120, Field<int>(line, "prompt_tokens"));
        Assert.Equal(30, Field<int>(line, "eval_tokens"));
        Assert.Equal(900, Field<int>(line, "cache_read_tokens"));
    }

    // The template reads from the general to the particular: the profile,
    // then the tokens, before the timings and the sizes.
    [Fact]
    public void TheUsageLineNamesTheProfileThenTheTokens()
    {
        string message = ProviderManifest.EventsOf(typeof(DeckleLlmSource))
            .Single(d => d.Id == DeckleLlmSource.EvtRewriteResponseDetail).Name;
        Assert.Equal(nameof(DeckleLlmSource.RewriteResponseDetail), message);

        var attribute = (EventAttribute)typeof(DeckleLlmSource)
            .GetMethod(nameof(DeckleLlmSource.RewriteResponseDetail))!
            .GetCustomAttributes(typeof(EventAttribute), false)[0];
        Assert.StartsWith("profile={3} | prompt_tokens={7} | eval_tokens={8}", attribute.Message);
    }

    private static bool Of(EventWrittenEventArgs e, string profile)
        => e.EventId == DeckleLlmSource.EvtRewriteResponseDetail
        && Equals(e.Payload![e.PayloadNames!.IndexOf("profile")], profile);

    private static T Field<T>(EventWrittenEventArgs e, string name)
        => (T)e.Payload![e.PayloadNames!.IndexOf(name)]!;
}
