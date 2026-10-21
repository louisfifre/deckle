using System.Net;
using System.Net.Http;

namespace Deckle.Anytype.Tests;

// Stands in for the Anytype backend behind AnytypeV2Client's HttpMessageHandler
// seam: it answers from a queue and records what reached the wire. The request
// is copied while it is alive, because the client disposes it after sending.
internal sealed class FakeV2Handler : HttpMessageHandler
{
    internal sealed record Seen(
        string Method,
        string PathAndQuery,
        IReadOnlyDictionary<string, string> Headers,
        string? Body)
    {
        public bool HasHeader(string name) => Headers.ContainsKey(name);

        public string? Header(string name) => Headers.GetValueOrDefault(name);
    }

    private readonly Queue<Func<HttpResponseMessage>> _responses = new();
    private readonly List<Seen> _seen = [];

    public IReadOnlyList<Seen> Requests => _seen;

    public FakeV2Handler Reply(
        HttpStatusCode status,
        string? json = null,
        params (string Name, string Value)[] headers)
    {
        _responses.Enqueue(() =>
        {
            var response = new HttpResponseMessage(status);
            if (json is not null)
                response.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            foreach ((string name, string value) in headers)
                response.Headers.TryAddWithoutValidation(name, value);
            return response;
        });
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers)
            headers[header.Key] = string.Join(",", header.Value);

        string? body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        _seen.Add(new Seen(request.Method.Method, request.RequestUri!.PathAndQuery, headers, body));

        if (!_responses.TryDequeue(out Func<HttpResponseMessage>? next))
            throw new InvalidOperationException("The fake backend has no reply left for this request.");
        return next();
    }
}
