using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Deckle.Catalog;

// ── DiagnosticsContribution ───────────────────────────────────────────────────
//
// A module's dataset opt-ins, handed to the shared Diagnostics page. Settings >
// Diagnostics is the one editor of everything Deckle records about itself (the
// 2026-07-16 doctrine); a module keeps the model and the consent of its own
// datasets, and only lends the page the descriptors to compose. Open builds the
// model when the page composes, never at registration: a view model may read
// its store on construction, and the registry runs at boot.
//
// The composer resolves each card's header and description from the model's
// own assembly, so the strings stay in the module's .resw beside its consents.
public sealed record DiagnosticsContribution(string Id, int Order, Func<DiagnosticsSource> Open);

// The model behind a contribution's cards: the object whose PropertyChanged
// the composer follows, the descriptors, the reload the page runs on each
// visit so a consent flipped elsewhere shows up, and TurnOff, which stops every
// dataset of the contribution. TurnOff exists because the composer's ResetAll
// skips a consent (it carries no default), while the page's Reset all promises
// that every opt-in on the page goes off, as the page's own application-log
// consent does. Turning a consent off needs no dialog; only turning one on asks.
public sealed record DiagnosticsSource(
    INotifyPropertyChanged Model,
    IReadOnlyList<SettingDescriptor> Settings,
    Action Load,
    Action TurnOff);

// ── DiagnosticsRegistry ───────────────────────────────────────────────────────
//
// Lives on the Catalog floor like SettingsModuleDescriptor, so a module
// contributes without a reference to the Diagnostics module and the Diagnostics
// page composes without a reference to any module. The composition root
// registers one contribution per present module, beside its settings page.
public static class DiagnosticsRegistry
{
    private static readonly object _gate = new();
    private static readonly List<DiagnosticsContribution> _contributions = new();

    public static IReadOnlyList<DiagnosticsContribution> Contributions
    {
        get { lock (_gate) return _contributions.OrderBy(c => c.Order).ToList(); }
    }

    public static void Register(DiagnosticsContribution contribution)
    {
        lock (_gate)
        {
            int existing = _contributions.FindIndex(c => c.Id == contribution.Id);
            if (existing >= 0) _contributions[existing] = contribution;
            else _contributions.Add(contribution);
        }
    }
}
