using System;
using System.Threading;
using System.Threading.Tasks;
using Deckle.Core;

namespace Deckle.Setup;

// One thing a selected module needs on disk; the item knows nothing about UI.
// RunAsync returns a failed outcome instead of throwing, so the run continues
// with the next item.
internal sealed record InstallItem
{
    // Stable id — keys the InstallResult, and the SummaryPage's recovery
    // affordances resolve against it.
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    // Download weight, when known up-front (0 = unknown). Estimation display
    // only — progress totals come from the transfer itself.
    public long SizeBytes { get; init; }

    public required Func<bool> IsInstalled { get; init; }

    public required Func<IProgress<Downloader.DownloadProgress>, CancellationToken, Task<ProvisioningResult>> RunAsync { get; init; }
}
