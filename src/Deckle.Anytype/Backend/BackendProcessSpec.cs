namespace Deckle.Anytype;

// ── BackendProcessSpec ───────────────────────────────────────────────────────
//
// Describes the headless anytype-cli process the supervisor spawns: the
// fully-qualified executable path and the command line it is started with (the
// `serve`-class invocation that brings the REST listener up on 127.0.0.1:31012).
//
// This is the seam between the lifecycle mechanism (BackendSupervisor /
// BackendProcess) and the provisioning step: provisioning downloads the pinned
// binary into %LOCALAPPDATA%\Programs\Deckle and BackendInstallation fills this
// record from that layout.
//
// Arguments is a single pre-composed command-line string, handed verbatim to
// ProcessStartInfo.Arguments; quoting of any embedded path is the caller's
// responsibility.
public sealed record BackendProcessSpec(string ExecutablePath, string Arguments);
