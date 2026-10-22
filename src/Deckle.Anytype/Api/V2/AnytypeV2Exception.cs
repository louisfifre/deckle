using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Deckle.Anytype;

// A /v2 request the server refused, with the error envelope kept as data:
//
//   {"status":400,"code":"validation_failed","message":"…",
//    "issues":[{"path":"ops[1].match","message":"…","hint":"…"}],"hint":"…"}
//
// It derives from HttpRequestException so every catch written for the v1 client
// (status-based) keeps working; what it adds is the machine-readable part. A
// bare 409 means two different things (a stale If-Match, or an Idempotency-Key
// reused with another body), so callers branch on the flags below, never on the
// status alone.
public sealed partial class AnytypeV2Exception : HttpRequestException
{
    // Codes the server is documented to use. The survey measured the 409 status
    // of a stale If-Match but not its code: EtagMismatch comes from the upstream
    // agent skill, IdempotencyConflict from the upstream API reference.
    public const string EtagMismatchCode = "etag_mismatch";
    public const string IdempotencyConflictCode = "idempotency_conflict";

    public AnytypeV2Exception(
        string message,
        HttpStatusCode status,
        string? code,
        string? apiMessage,
        string? hint,
        IReadOnlyList<AnytypeV2Issue> issues)
        : base(message, null, status)
    {
        Status = (int)status;
        Code = code;
        ApiMessage = apiMessage;
        Hint = hint;
        Issues = issues;
        OpIndex = FirstOpIndex(issues);
    }

    public int Status { get; }

    // Null when the body is not an envelope (an HTML page from a proxy).
    public string? Code { get; }

    // The machine code in a shape a log line can carry: a short snake_case word,
    // "none" without an envelope, "unrecognized" for anything else. Never the
    // server's text.
    public string LogCode => ApiFailureIncidents.BoundedCode(Code);

    public string? ApiMessage { get; }

    public string? Hint { get; }

    public IReadOnlyList<AnytypeV2Issue> Issues { get; }

    // The index, in the submitted {"ops":[…]} batch, of the op the server
    // blames. Null when the failure is not tied to one op.
    public int? OpIndex { get; }

    // If-Match no longer names the current version: re-read, merge, retry.
    public bool IsStaleEtag => Status == 409 && Code == EtagMismatchCode;

    // The Idempotency-Key was already used for a different request.
    public bool IsIdempotencyConflict => Status == 409 && Code == IdempotencyConflictCode;

    public bool IsRateLimited => Status == 429;

    // Parses the response body of a refused request. A body that is not an
    // envelope still yields an exception carrying the status.
    internal static AnytypeV2Exception From(
        string method, string path, HttpStatusCode status, string body)
    {
        string? code = null, apiMessage = null, hint = null;
        var issues = new List<AnytypeV2Issue>();

        if (TryParseEnvelope(body) is { } envelope)
        {
            code = envelope["code"]?.GetValueKind() == JsonValueKind.String
                ? envelope["code"]!.GetValue<string>() : null;
            apiMessage = ReadString(envelope, "message");
            hint = ReadString(envelope, "hint");
            if (envelope["issues"] is JsonArray array)
            {
                foreach (JsonNode? node in array)
                {
                    if (node is not JsonObject issue) continue;
                    issues.Add(new AnytypeV2Issue(
                        ReadString(issue, "path") ?? "",
                        ReadString(issue, "message") ?? "",
                        ReadString(issue, "hint")));
                }
            }
        }

        string detail = apiMessage ?? Excerpt(body);
        string label = code is null ? $"{(int)status}" : $"{(int)status} {code}";
        // A validation_failed message alone ("invalid request") does not name
        // the refused key; the issues and the hint do, so they ride in Message
        // (what the logs and the tool answers show).
        string message = $"Anytype API {method} {path} failed with {label}: {detail}";
        foreach (AnytypeV2Issue issue in issues.Take(MaxIssuesInMessage))
        {
            message += $"; {issue.Path}: {issue.Message}";
            if (!string.IsNullOrEmpty(issue.Hint)) message += $" ({issue.Hint})";
        }
        if (!string.IsNullOrEmpty(hint)) message += $"; hint: {hint}";

        return new AnytypeV2Exception(message, status, code, apiMessage, hint, issues);
    }

    private const int MaxIssuesInMessage = 3;

    private static JsonObject? TryParseEnvelope(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            return JsonNode.Parse(body) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonObject node, string key) =>
        node[key]?.GetValueKind() == JsonValueKind.String
            ? node[key]!.GetValue<string>()
            : null;

    private static int? FirstOpIndex(IReadOnlyList<AnytypeV2Issue> issues)
    {
        foreach (AnytypeV2Issue issue in issues)
        {
            Match match = OpPathPattern().Match(issue.Path);
            if (match.Success) return int.Parse(match.Groups[1].Value);
        }
        return null;
    }

    // Bounded so a stray HTML error page does not flood a message.
    private static string Excerpt(string text)
    {
        text = text.Replace('\n', ' ').Replace('\r', ' ');
        return text.Length > 200 ? text[..200] + "…" : text;
    }

    [GeneratedRegex(@"^ops\[(\d+)\]")]
    private static partial Regex OpPathPattern();
}
