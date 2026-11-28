using System.Text.Json;

namespace Deckle.Browser;

public enum PermissionKind { Camera, Microphone, Geolocation, Notifications, ClipboardRead, Other }

public enum PermissionDecision { Allow, Block }

/// <summary>One stored answer, as the site settings list it.</summary>
public sealed record StoredPermission(string Origin, PermissionKind Kind, PermissionDecision Decision);

// What the person answered when a site asked for a permission: per origin and
// per kind, Allow or Block. No stored answer means the browser asks. Kept as
// JSON in a file; a missing or corrupt file is an empty store, never an
// exception.
public sealed class SitePermissions
{
    private readonly Dictionary<(string Origin, PermissionKind Kind), PermissionDecision> _answers = [];

    private sealed record Entry(string? Origin, string? Kind, string? Decision);

    public PermissionDecision? Get(string origin, PermissionKind kind) =>
        _answers.TryGetValue((Key(origin), kind), out PermissionDecision d) ? d : null;

    public void Set(string origin, PermissionKind kind, PermissionDecision decision) =>
        _answers[(Key(origin), kind)] = decision;

    public bool Remove(string origin, PermissionKind kind) => _answers.Remove((Key(origin), kind));

    /// <summary>The stored answers of one origin, in kind order.</summary>
    public IReadOnlyList<StoredPermission> ForOrigin(string origin)
    {
        string key = Key(origin);
        return [.. _answers.Where(a => a.Key.Origin == key)
                           .OrderBy(a => a.Key.Kind)
                           .Select(a => new StoredPermission(a.Key.Origin, a.Key.Kind, a.Value))];
    }

    public int Count => _answers.Count;

    private static string Key(string origin) => origin.Trim().ToLowerInvariant();

    public void Save(string path)
    {
        List<Entry> entries = [.. _answers.Select(a => new Entry(a.Key.Origin, a.Key.Kind.ToString(), a.Value.ToString()))];
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(entries));
        File.Move(temp, path, overwrite: true);
    }

    public static SitePermissions Load(string path)
    {
        var store = new SitePermissions();
        try
        {
            if (!File.Exists(path)) return store;
            List<Entry>? entries = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(path));
            foreach (Entry e in entries ?? [])
            {
                if (e.Origin is { Length: > 0 }
                    && Enum.TryParse(e.Kind, out PermissionKind kind)
                    && Enum.TryParse(e.Decision, out PermissionDecision decision))
                    store.Set(e.Origin, kind, decision);
            }
            return store;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                       or NotSupportedException or ArgumentException)
        {
            return new SitePermissions();
        }
    }
}
