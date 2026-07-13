using System;
using System.Threading;
using System.Threading.Tasks;
using Deckle.Core;

namespace Deckle.Setup;

// ── InstallItem ───────────────────────────────────────────────────────────────
//
// One unit of the install step: something a selected module needs on disk, with
// its two verbs — is it already there, and put it there. The wizard's install
// page runs a list of these sequentially and renders one row per item; the item
// itself knows nothing about UI. RunAsync reports download progress through the
// standard Downloader progress shape and returns an outcome instead of throwing
// — a failed item lands in the results and the run continues with the next one.
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

    public required Func<IProgress<Downloader.DownloadProgress>, CancellationToken, Task<InstallItemOutcome>> RunAsync { get; init; }
}

internal sealed record InstallItemOutcome(bool Success, string? ErrorMessage, long? Bytes, string? Sha256 = null)
{
    public static InstallItemOutcome Ok(long? bytes, string? sha256 = null) => new(true, null, bytes, sha256);
    public static InstallItemOutcome Fail(string message) => new(false, message, null);
}
