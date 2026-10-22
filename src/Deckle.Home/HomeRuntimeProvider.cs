using System.Diagnostics;
using System.Net.Http;
using Deckle.Anytype;

namespace Deckle.Home;

internal sealed class HomeRuntimeProvider(AnytypeApiClient api, string spaceId)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HomeSchemaRuntime? _runtime;

    // The failure streak since the last success; only touched inside _gate.
    private int _failures;
    private long _failingSince;

    public async Task<HomeSchemaRuntime> GetAsync(CancellationToken ct)
    {
        if (_runtime is not null) return _runtime;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_runtime is not null) return _runtime;

            long started = Stopwatch.GetTimestamp();
            try
            {
                var reader = new SchemaSnapshotReader(api);
                SchemaSnapshot snapshot = await reader.ReadAsync(
                    spaceId,
                    HomeSchema.ClosedVocabularies.Keys.ToArray(),
                    ct).ConfigureAwait(false);
                _runtime = HomeSchema.Validate(snapshot);

                long since = _failures > 0 ? _failingSince : started;
                DeckleHomeSource.Log.SchemaReady(
                    _failures, Stopwatch.GetElapsedTime(since).TotalMilliseconds);
                _failures = 0;
                return _runtime;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                ReportFailure(ex);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void ReportFailure(Exception ex)
    {
        (string reason, int status) = ex switch
        {
            HomeSchemaException schema => (schema.Reason, 0),
            HttpRequestException { StatusCode: { } code } => ("http_status", (int)code),
            HttpRequestException => ("transport", 0),
            _ => ("exception", 0),
        };
        string exType = ex.GetType().Name;

        if (_failures++ == 0)
        {
            _failingSince = Stopwatch.GetTimestamp();
            DeckleHomeSource.Log.SchemaUnavailable(reason, exType, status);
        }
        else
        {
            DeckleHomeSource.Log.SchemaAttemptFailed(reason, exType, status, _failures);
        }

        if (ex is HomeSchemaException)
            DeckleHomeSource.Log.SchemaRejectedDetail(ex.Message);
    }
}
