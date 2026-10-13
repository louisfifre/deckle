namespace Deckle.Anytype;

// The seam between the lifecycle mechanism (BackendSupervisor / BackendProcess)
// and provisioning: BackendInstallation fills this record from the provider
// store's active version.
//
// Arguments is a single pre-composed command-line string, handed verbatim to
// ProcessStartInfo.Arguments; quoting of any embedded path is the caller's
// responsibility.
public sealed record BackendProcessSpec(string ExecutablePath, string Arguments);
