using Deckle.Anytype;

namespace Deckle.Travel;

internal sealed class TravelRuntimeProvider(AnytypeApiClient api, string spaceId)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TravelSchemaRuntime? _runtime;

    public async Task<TravelSchemaRuntime> GetAsync(CancellationToken ct)
    {
        if (_runtime is not null) return _runtime;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_runtime is not null) return _runtime;

            try
            {
                var reader = new SchemaSnapshotReader(api);
                SchemaSnapshot snapshot = await reader.ReadAsync(
                    spaceId,
                    TravelSchema.ClosedVocabularies.Keys.ToArray(),
                    ct).ConfigureAwait(false);
                _runtime = TravelSchema.Validate(snapshot);
                return _runtime;
            }
            catch (TravelSchemaException ex)
            {
                DeckleTravelSource.Log.SchemaRejected();
                DeckleTravelSource.Log.SchemaRejectedDetail(ex.Message);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
