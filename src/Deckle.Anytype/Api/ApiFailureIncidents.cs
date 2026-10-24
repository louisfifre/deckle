using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Deckle.Anytype;

// Closed vocabulary of the `reason` field of ApiRequestFailed, spelled the same
// way by the v1 and v2 clients and by whoever filters on it.
internal static class ApiFailureReasons
{
    // The backend answered with a status the caller cannot use.
    public const string HttpStatus = "http_status";

    // No answer: the connection failed.
    public const string Transport = "transport";

    // No answer: HttpClient's own timeout.
    public const string Timeout = "timeout";

    // The endpoint's owner is not a provider Deckle trusts, so nothing was sent.
    public const string UntrustedEndpoint = "untrusted_endpoint";

    // Nothing listens on the backend's port: the serve has not bound it yet, or
    // is stuck before it does. Nothing untrusted is there, and nothing was sent.
    public const string BackendNotServing = "backend_not_serving";
}

// The failure incidents of one Anytype API client, shared by the v1 and the v2
// transport. Two things repeat:
//
//   • No answer (transport, timeout): the first failed request writes the Error,
//     the next ones stay Verbose details, the first answer closes the incident.
//   • A refusal (a status the caller cannot use, or an endpoint that is not
//     trusted): the first request of a run writes the Error, the identical ones
//     that follow stay Verbose details, and the run's end writes one log line
//     with its count. A refusal that differs from the run's (another status, or
//     the other reason) opens a run of its own.
//
// A request the caller expects to lose now and then, the stale version of a write
// it re-reads and retries, is no failure: it writes one Verbose line and touches
// neither incident.
//
// Only called inside the client's request gate, so it holds no lock.
internal sealed partial class ApiFailureIncidents
{
    private int _transportFailures;
    private long _transportSince;

    private string _refusalReason = "";
    private int _refusalStatus;
    private int _refusals;
    private long _refusalSince;

    // The request got no answer.
    internal void TransportFailed(string method, string path, Exception ex)
    {
        EndRefusals();
        string reason = ex is TaskCanceledException
            ? ApiFailureReasons.Timeout
            : ApiFailureReasons.Transport;
        if (_transportFailures++ == 0)
        {
            _transportSince = Stopwatch.GetTimestamp();
            DeckleAnytypeSource.Log.ApiRequestFailed(
                method, 0, reason, ex.GetType().Name, ex.Message);
        }
        DeckleAnytypeSource.Log.ApiRequestFailedDetail(
            method, path, 0, $"{ex.GetType().Name}: {ex.Message}");
    }

    // The backend answered, whatever the status.
    internal void Answered()
    {
        if (_transportFailures == 0) return;
        DeckleAnytypeSource.Log.ApiTransportRecovered(
            _transportFailures,
            Stopwatch.GetElapsedTime(_transportSince).TotalSeconds);
        _transportFailures = 0;
    }

    // The request succeeded: whatever was being refused is over.
    internal void Succeeded() => EndRefusals();

    // The request was refused. `code` is the machine code that says why, never the
    // server's free text.
    internal void Refused(string method, string path, int status, string reason, string? code)
    {
        if (_refusals > 0 && reason == _refusalReason && status == _refusalStatus)
        {
            _refusals++;
        }
        else
        {
            EndRefusals();
            _refusalReason = reason;
            _refusalStatus = status;
            _refusals = 1;
            _refusalSince = Stopwatch.GetTimestamp();
            DeckleAnytypeSource.Log.ApiRequestFailed(method, status, reason, "", "");
        }
        DeckleAnytypeSource.Log.ApiRequestFailedDetail(method, path, status, BoundedCode(code));
    }

    // The request was held back because the endpoint is not proven trusted
    // (Inspect is not Trusted); nothing was sent and the bearer stayed home. The
    // two verdicts are two reasons, so a run of one never counts toward the
    // other, and the exception tells the caller which of them it hit: "not
    // trusted" for a stranger on the port, "not serving yet" for an empty port.
    internal InvalidOperationException EndpointRefused(
        string method, string path, BackendEndpointVerdict verdict)
    {
        bool nothingListens = verdict == BackendEndpointVerdict.NotListening;
        Refused(
            method, path, 0,
            nothingListens ? ApiFailureReasons.BackendNotServing : ApiFailureReasons.UntrustedEndpoint,
            nothingListens ? "not_listening" : "owner_untrusted");
        return new InvalidOperationException(nothingListens
            ? "The Anytype backend is not serving yet (nothing listens on its port); the request was not sent."
            : "The Anytype endpoint is not owned by a trusted Deckle provider; the request was not sent.");
    }

    // A write whose version was stale: the caller re-reads and retries it.
    internal void Superseded(string method, string path, int status, string? code)
        => DeckleAnytypeSource.Log.ApiRequestSuperseded(method, path, status, BoundedCode(code));

    private void EndRefusals()
    {
        if (_refusals > 1)
        {
            DeckleAnytypeSource.Log.ApiRefusalsEnded(
                _refusalReason, _refusalStatus, _refusals,
                Stopwatch.GetElapsedTime(_refusalSince).TotalSeconds);
        }
        _refusals = 0;
        _refusalReason = "";
        _refusalStatus = 0;
    }

    // An error code is a short snake_case word. Anything else came from a body that
    // was not an envelope, and its text is not for the log.
    [GeneratedRegex("^[a-z][a-z0-9_]{0,47}$")]
    private static partial Regex CodeShape();

    internal static string BoundedCode(string? code)
        => code is null ? "none" : CodeShape().IsMatch(code) ? code : "unrecognized";
}
