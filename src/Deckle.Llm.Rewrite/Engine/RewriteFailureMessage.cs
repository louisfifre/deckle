using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using Deckle.Llm;

namespace Deckle.Llm.Rewrite;

// ─── Rewrite failure message ─────────────────────────────────────────────────
//
// Turns a failed transcript rewrite into the overlay card the user reads: one
// sentence that names where it failed and why, then the outcome. Built only
// from what the service holds at that moment (the profile, the provider it
// sends to, the exception), so it stays pure and testable without a network.
//
// The card is a fixed 272x78 dip HUD overlay whose body wraps with no line
// cap: past two lines at 14 px it overflows the card and gets clipped. Two
// lines hold about 65 characters, so the cause sentence aims at 45 or fewer
// before the fixed " Raw transcript copied." outcome.
//
// The provider's Name is what the user sees in Settings, so it is the subject
// of every sentence: it already points at the entry to fix, and repeating it
// or adding the profile would push the outcome off the card.
//
// Only the seam's shape is read, never an SDK's: a refusal is an
// HttpRequestException with a StatusCode, a failed connection a
// SocketException somewhere inside, whichever engine threw.

public readonly record struct RewriteFailureMessage(string Title, string Body)
{
    const string UnavailableTitle   = "Rewriter unavailable";
    const string NotConfiguredTitle = "Rewriter not configured";
    const string Outcome            = "Raw transcript copied.";

    // A raw exception message is the fallback only: kept short so the
    // outcome still lands inside the card's two lines more often than not.
    const int MaxDetailLength = 60;

    /// <summary>The message for a profile whose provider resolved to no
    /// engine: no provider set, a provider id no longer in the list, or a
    /// provider switched off in Settings.</summary>
    public static RewriteFailureMessage ForUnresolvedProvider(RewriteProfile profile, LlmProvider? provider)
    {
        string cause;
        if (string.IsNullOrWhiteSpace(profile.ProviderId))
            cause = $"Profile \"{profile.Name}\" has no provider.";
        else if (provider is null)
            cause = $"Profile \"{profile.Name}\" points at a provider that is missing.";
        else
            cause = $"Provider \"{NameOf(provider)}\" is disabled.";

        return new RewriteFailureMessage(NotConfiguredTitle, $"{cause} {Outcome}");
    }

    /// <summary>The message for an engine that threw. <paramref name="provider"/>
    /// is null when the caller has no way to look it up; the sentence then
    /// falls back to "The provider".</summary>
    public static RewriteFailureMessage ForException(RewriteProfile profile, LlmProvider? provider, Exception exception) =>
        new(UnavailableTitle, $"{Cause(profile, provider, exception)} {Outcome}");

    static string Cause(RewriteProfile profile, LlmProvider? provider, Exception exception)
    {
        string subject = provider is null ? "The provider" : NameOf(provider);
        string? where  = HostOf(provider);

        // Exact match on the engine's own constant: the missing key is a
        // configuration gap the engine detects before any request is sent.
        if (exception is InvalidOperationException && exception.Message == ClaudeEngine.MissingApiKeyMessage)
            return provider is null ? "The provider has no API key." : $"Provider \"{subject}\" has no API key.";

        // A status code means the server answered: the request reached it and
        // was refused, so the reason is about the key, the model or the load.
        if (Find<HttpRequestException>(exception)?.StatusCode is HttpStatusCode code)
        {
            switch ((int)code)
            {
                case 401:
                case 403:
                    return $"{subject} rejected the API key.";
                case 404:
                    return $"{subject} does not know model {profile.Model}.";
                case 429:
                    return $"{subject} is rate limiting requests.";
                case >= 500:
                    return $"{subject} answered a server error (HTTP {(int)code}).";
            }
        }

        // No answer at all: the socket error tells "nothing listening there"
        // (a local server not started) from "that address leads nowhere".
        switch (ConnectionFailureOf(exception))
        {
            case ConnectionFailure.NotAnswering:
                return where is null ? $"{subject} is not answering." : $"{subject} is not answering at {where}.";
            case ConnectionFailure.Unreachable:
                return where is null ? $"{subject} cannot be reached." : $"{subject} cannot be reached at {where}.";
        }

        return $"{subject} failed: {Detail(exception)}";
    }

    // The failure class the log records, read from the same seam shape as
    // the card above so the log and the overlay agree on why. A short
    // fixed word a filter can match, never free text.
    public static string ReasonOf(Exception exception)
    {
        if (exception is InvalidOperationException && exception.Message == ClaudeEngine.MissingApiKeyMessage)
            return "missing_api_key";

        if (Find<HttpRequestException>(exception)?.StatusCode is HttpStatusCode code)
        {
            return (int)code switch
            {
                401 or 403 => "auth_rejected",
                404        => "unknown_model",
                429        => "rate_limited",
                >= 500     => "server_error",
                _          => "http_status",
            };
        }

        switch (ConnectionFailureOf(exception))
        {
            case ConnectionFailure.NotAnswering: return "not_answering";
            case ConnectionFailure.Unreachable:  return "unreachable";
        }

        if (exception is InvalidOperationException && exception.Message == ClaudeEngine.RefusalMessage)
            return "refused";

        // A server that answered 200 with a body the engine could not read
        // (an HTML error page from a proxy, a missing "response" field).
        if (Find<JsonException>(exception) is not null || Find<KeyNotFoundException>(exception) is not null)
            return "invalid_response";

        if (Find<OperationCanceledException>(exception) is not null)
            return "cancelled";

        return "error";
    }

    // The HTTP status when the server answered, 0 when it never did: an
    // EventSource payload takes no nullable.
    public static int StatusOf(Exception exception) =>
        Find<HttpRequestException>(exception)?.StatusCode is HttpStatusCode code ? (int)code : 0;

    enum ConnectionFailure { None, NotAnswering, Unreachable }

    static ConnectionFailure ConnectionFailureOf(Exception exception)
    {
        // An engine's SDK may wrap the transport's exception (an I/O
        // exception over HttpRequestException over SocketException), HttpClient
        // wraps the socket's; the chain is walked rather than matched at one
        // depth.
        if (Find<SocketException>(exception) is SocketException socket)
        {
            switch (socket.SocketErrorCode)
            {
                case SocketError.ConnectionRefused:
                    return ConnectionFailure.NotAnswering;
                case SocketError.HostNotFound:
                case SocketError.NoData:
                case SocketError.TryAgain:
                case SocketError.HostUnreachable:
                case SocketError.HostDown:
                case SocketError.NetworkUnreachable:
                case SocketError.NetworkDown:
                case SocketError.TimedOut:
                    return ConnectionFailure.Unreachable;
            }
        }

        return Find<HttpRequestException>(exception)?.HttpRequestError switch
        {
            HttpRequestError.NameResolutionError => ConnectionFailure.Unreachable,
            HttpRequestError.ConnectionError     => ConnectionFailure.Unreachable,
            _                                    => ConnectionFailure.None,
        };
    }

    static T? Find<T>(Exception exception) where T : Exception
    {
        for (Exception? e = exception; e is not null; e = e.InnerException)
        {
            if (e is T match) return match;
        }
        return null;
    }

    /// <summary>The endpoint's host and port, never the full URL: the path
    /// (/api/generate, /v1) says nothing the user acts on. Null for a
    /// provider with no endpoint, such as Claude.</summary>
    static string? HostOf(LlmProvider? provider)
    {
        string endpoint = provider?.Endpoint?.Trim() ?? "";
        if (endpoint.Length == 0) return null;

        // "localhost:11434" without a scheme parses as scheme "localhost",
        // so a non-http result is retried with one.
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            if (!Uri.TryCreate("http://" + endpoint, UriKind.Absolute, out uri))
                return endpoint;
        }
        return uri.Authority;
    }

    // A provider added in Settings starts with an empty Name; its Id is the
    // only other handle the user can match.
    static string NameOf(LlmProvider provider) =>
        string.IsNullOrWhiteSpace(provider.Name) ? provider.Id : provider.Name.Trim();

    static string Detail(Exception exception)
    {
        string message = string.Join(' ', exception.Message.Split(
            (char[])['\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries));
        if (message.Length == 0) return exception.GetType().Name + ".";
        if (message.Length > MaxDetailLength)
            return message[..(MaxDetailLength - 1)].TrimEnd() + "…";
        return message.EndsWith('.') ? message : message + ".";
    }
}
