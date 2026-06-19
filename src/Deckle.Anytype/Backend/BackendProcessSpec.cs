namespace Deckle.Anytype;

// ── BackendProcessSpec ───────────────────────────────────────────────────────
//
// Describes the headless anytype-cli process the scheduled task launches: the
// fully-qualified executable path and the command line it is started with (the
// `serve`-class invocation that brings the REST listener up on 127.0.0.1:31012).
//
// This is the seam between the lifecycle mechanism (this module) and the
// provisioning step (later): provisioning downloads the pinned binary into
// %LOCALAPPDATA%\Programs\Deckle and fills this record, then hands it to
// BackendScheduledTask.EnsureRegistered. The supervisor that runs the task on
// demand needs no spec — it only probes health and triggers an already-enrolled
// task.
//
// Arguments is a single pre-composed command-line string, written verbatim into
// the task's <Arguments> element; quoting of any embedded path is the caller's
// responsibility, as it is with ProcessStartInfo.Arguments.
public sealed record BackendProcessSpec(string ExecutablePath, string Arguments);
