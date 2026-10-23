namespace Deckle.Catalog;

// ─── Glyphs ───────────────────────────────────────────────────────
//
// C# mirror of Themes/Icons.xaml, for code that sets FontIcon.Glyph.
// Any key added here must be added to Icons.xaml too, and vice versa:
// the two are kept in sync by hand.
//
// Nested static classes mirror the dotted XAML keys
// (Icon.Badge.Success → Glyphs.Badge.Success).
//
// Constants hold the Fluent Icons character itself, which renders as a
// box in editors without the font; Icons.xaml shows the hex code.

public static class Glyphs
{
    // Navigation / Section headers
    // Segoe Fluent "Setting" (E713) — the General node of the rail.
    public const string Settings = "";
    public const string Home = "";
    public const string Microphone = "";
    public const string Speech = "";
    public const string Sparkle = "";
    public const string Lightbulb = "";
    public const string Trackpad = "";
    public const string Mouse = "";
    public const string Diagnostics = "";
    public const string Logs = "";
    // Segoe Fluent "Puzzle" (EA86) — the Playground.
    public const string Playground = "";

    // Segoe Fluent "Spelling" (F87B) — proofing / autocorrect.
    public const string Autocorrect = "";

    // Segoe Fluent "DockBottom" (E90E) — the taskbar cover.
    public const string Taskbar = "";

    // Segoe Fluent "Game" (E7FC) — the apps and games registry.
    public const string Games = "";

    // Common semantic concepts
    public const string Shortcut = "";
    public const string Theme = "";
    public const string Overlay = "";
    public const string Model = "";
    public const string Paste = "";
    public const string Launch = "";
    public const string Lightning = "";
    public const string Setup = "";
    public const string Folder = "";
    // Segoe Fluent "OpenLocal" (E8DA): the icon-only button that opens the folder picker.
    public const string Speaker = "";
    public const string Latency = "";

    // Common actions
    public const string Reset = "";
    public const string Search = "";
    public const string Copy = "";
    public const string Save = "";
    public const string Delete = "";
    public const string Export = "";
    public const string Close = "";
    public const string Download = "";
    public const string Refresh = "";
    public const string OpenExternal = "";
    public const string Cancel = "";

    // Whisper transcription specifics
    public const string Language = "";
    public const string Prompt = "";
    public const string Gpu = "";
    public const string Filter = "";
    public const string Pattern = "";
    public const string Context = "";
    public const string Tokens = "";
    public const string Tuning = "";

    // Diagnostics specifics
    public const string AppLog = "";
    public const string AudioRecording = "";
    public const string VoiceLevel = "";

    public const string Window = "";
    // Segoe Fluent "View" (E890), the eye: the tile that opens a live preview.
    public const string Preview = "";

    // Ambient / Hue specifics
    public const string Bridge = "";
    public const string Endpoint = "";
    public const string Link = "";
    public const string List = "";

    // Same code point as Launch, named for sites where the lightbulb
    // itself is the intent.
    public const string LightbulbFilled = "";

    public static class Transport
    {
        public const string Play = "";
        public const string Pause = "";
        public const string Stop = "";
    }

    public static class Badge
    {
        public const string Success = "";
        public const string Critical = "";
        public const string Warning = "";
        public const string Info = "";
    }
}
