namespace Deckle.Anytype;

internal sealed class SchemaPreviewStore
{
    private const int MaxPreviews = 32;
    private static readonly TimeSpan PreviewLifetime = TimeSpan.FromMinutes(15);
    private readonly object _gate = new();
    private readonly Dictionary<string, StoredPreview> _previews = new(StringComparer.Ordinal);

    internal void Store(SchemaPreview preview)
    {
        lock (_gate)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            RemoveExpiredPreviews(now);
            if (_previews.Count >= MaxPreviews)
            {
                string oldest = _previews.MinBy(pair => pair.Value.CreatedAt).Key;
                _previews.Remove(oldest);
            }
            _previews[preview.Id] = new StoredPreview(preview, now);
        }
    }

    internal bool TryGet(string id, out SchemaPreview preview)
    {
        lock (_gate)
        {
            RemoveExpiredPreviews(DateTimeOffset.UtcNow);
            if (_previews.TryGetValue(id, out StoredPreview? stored))
            {
                preview = stored.Preview;
                return true;
            }
            preview = null!;
            return false;
        }
    }

    internal void Remove(string id)
    {
        lock (_gate)
            _previews.Remove(id);
    }

    private void RemoveExpiredPreviews(DateTimeOffset now)
    {
        foreach (string id in _previews
            .Where(pair => now - pair.Value.CreatedAt >= PreviewLifetime)
            .Select(pair => pair.Key)
            .ToArray())
        {
            _previews.Remove(id);
        }
    }

    private sealed record StoredPreview(SchemaPreview Preview, DateTimeOffset CreatedAt);


}
