using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using Deckle.Diagnostics;

namespace Deckle.Notifications;

// Boot-time index of every NotificationDescriptor declared across modules.
// Owning modules register their descriptors here once; the dispatcher reads
// the catalogue to validate that a prompt's descriptor is known before routing
// it. Registration is fail-fast: a malformed or duplicate Id throws at boot
// rather than surfacing as a silent no-op at show time.
//
// Thread-safe via a single lock. Registration happens at boot and lookups are
// rare relative to that, so a plain lock is simpler than a concurrent
// dictionary and carries no measurable cost.
public sealed class NotificationCatalog
{
    private readonly object _gate = new();
    private readonly Dictionary<string, NotificationDescriptor> _byId = new(StringComparer.Ordinal);

    // Registers a batch of descriptors atomically. Validates each Id is
    // non-empty and unique across ALL previously-registered descriptors;
    // throws InvalidOperationException on the first violation. Emits the
    // Verbose audit event once for the accepted batch.
    public void Register(IReadOnlyList<NotificationDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        if (descriptors.Count == 0) return;

        lock (_gate)
        {
            // Validate the whole batch before mutating, so a partial batch never
            // lands: either all of it registers or none of it does.
            foreach (var descriptor in descriptors)
            {
                if (descriptor is null)
                {
                    throw new InvalidOperationException("A null descriptor cannot be registered.");
                }
                if (string.IsNullOrWhiteSpace(descriptor.Id))
                {
                    throw new InvalidOperationException("A notification descriptor must declare a non-empty Id.");
                }
                if (_byId.ContainsKey(descriptor.Id))
                {
                    throw new InvalidOperationException($"A notification descriptor with Id '{descriptor.Id}' is already registered.");
                }
            }

            // Guard against duplicates within the batch itself.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var descriptor in descriptors)
            {
                if (!seen.Add(descriptor.Id))
                {
                    throw new InvalidOperationException($"The batch declares the notification Id '{descriptor.Id}' more than once.");
                }
            }

            foreach (var descriptor in descriptors)
            {
                _byId.Add(descriptor.Id, descriptor);
            }

            if (DeckleNotificationsSource.Log.IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push))
            {
                DeckleNotificationsSource.Log.CatalogRegistered(
                    string.Join(",", descriptors.Select(d => d.Id)),
                    descriptors.Count);
            }
        }
    }

    public bool IsRegistered(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        lock (_gate)
        {
            return _byId.ContainsKey(id);
        }
    }

    public IReadOnlyCollection<NotificationDescriptor> All
    {
        get
        {
            lock (_gate)
            {
                // Snapshot so callers iterate a stable view free of the lock.
                return _byId.Values.ToArray();
            }
        }
    }
}
