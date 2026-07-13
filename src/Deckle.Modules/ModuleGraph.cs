namespace Deckle.Modules;

// ── ModuleGraph ───────────────────────────────────────────────────────────────
//
// The cascade rules of the module selector, as pure set arithmetic over the
// catalogue's DependsOn edges: checking a module pulls in everything it needs,
// unchecking one expels everything that needs it. Both walks are transitive and
// cycle-safe (a visited set bounds them), and both leave the rest of the
// selection untouched — the UI hands in the current selection and renders the
// returned one, holding no cascade logic of its own.
public static class ModuleGraph
{
    // The selection with `id` checked, plus every module it transitively
    // depends on. Dependency ids the catalogue does not know are skipped — a
    // stale edge must not block the check.
    public static IReadOnlySet<string> WithDependencies(
        IReadOnlyList<ModuleDescriptor> catalog, IEnumerable<string> selection, string id)
    {
        var result = new HashSet<string>(selection, StringComparer.Ordinal);
        var byId = catalog.ToDictionary(m => m.Id, StringComparer.Ordinal);

        var pending = new Stack<string>();
        pending.Push(id);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            if (!byId.ContainsKey(current) || !result.Add(current)) continue;
            foreach (string dep in byId[current].DependsOn)
                pending.Push(dep);
        }

        return result;
    }

    // The selection with `id` unchecked, minus every module that transitively
    // depends on it.
    public static IReadOnlySet<string> WithoutDependents(
        IReadOnlyList<ModuleDescriptor> catalog, IEnumerable<string> selection, string id)
    {
        var result = new HashSet<string>(selection, StringComparer.Ordinal);

        // Visited guards the walk, not the Remove: an intermediate that is
        // already unchecked must still relay the cascade to its own dependents.
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(id);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            if (!visited.Add(current)) continue;
            result.Remove(current);
            foreach (ModuleDescriptor m in catalog)
            {
                if (m.DependsOn.Contains(current, StringComparer.Ordinal))
                    pending.Push(m.Id);
            }
        }

        return result;
    }
}
