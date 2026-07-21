using Deckle.Anytype;

namespace Deckle.Home;

internal sealed class HomeRuntimeProvider(AnytypeApiClient api, string spaceId)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HomeSchemaRuntime? _runtime;

    public async Task<HomeSchemaRuntime> GetAsync(CancellationToken ct)
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
                    HomeSchema.ClosedVocabularies.Keys.ToArray(),
                    ct).ConfigureAwait(false);
                _runtime = HomeSchema.Validate(snapshot);
                return _runtime;
            }
            catch (HomeSchemaException ex)
            {
                DeckleHomeSource.Log.SchemaRejected();
                DeckleHomeSource.Log.SchemaRejectedDetail(ex.Message);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
