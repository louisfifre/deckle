using Deckle.Settings;
using Windows.UI.ViewManagement;

namespace Deckle.Hud;

// Process-wide answer to "may Deckle animate now?" for every animation Deckle
// draws itself. It combines two inputs (ADR-0009):
//
//   - Deckle's own Appearance.Animations setting: "System" | "On" | "Reduced";
//   - the Windows "Animation effects" preference (UISettings.AnimationsEnabled),
//     which only decides when the setting says "System".
//
// The Windows switch alone cannot tell "motion bothers me" from "Windows's own
// animations are slow": following it to the letter took the HUD's activity
// signal away from a person who had only turned Windows's animations off. The
// setting lets that person keep Deckle's animations without forcing them on
// anyone; "System" keeps the accessible default.
public sealed class AnimationPreference
{
    public const string ModeSystem  = "System";
    public const string ModeOn      = "On";
    public const string ModeReduced = "Reduced";

    private static readonly Lazy<AnimationPreference> _instance =
        new(() => new AnimationPreference());

    private readonly UISettings _settings = new();
    private readonly object _sync = new();
    private bool _windowsEnabled;
    private string _mode;
    private bool _animationsEnabled;

    public static AnimationPreference Instance => _instance.Value;

    private AnimationPreference()
    {
        _windowsEnabled = _settings.AnimationsEnabled;
        _mode = SettingsService.Instance.Current.Appearance.Animations;
        _animationsEnabled = Resolve(_mode, _windowsEnabled);

        // The property exists on Deckle's minimum OS; the hot-change event was
        // added in Windows 10 2004. Older supported builds keep the launch-time
        // value because there is no event surface to subscribe to.
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            _settings.AnimationsEnabledChanged += OnWindowsPreferenceChanged;

        // Changed fires after the settings file is written, on the store's
        // writer thread: a change lands within the save debounce.
        SettingsService.Instance.Changed += OnSettingsChanged;
    }

    public bool AnimationsEnabled
    {
        get
        {
            lock (_sync) return _animationsEnabled;
        }
    }

    // The Windows "Animation effects" preference alone, for the one-time offer
    // of Deckle's own animations (ShouldOfferOwnAnimations).
    public bool WindowsAnimationsEnabled
    {
        get
        {
            lock (_sync) return _windowsEnabled;
        }
    }

    // Raised with the new effective value, only when it changes.
    public event Action<bool>? Changed;

    // Whether to offer, once, Deckle's own animations: only to a person whose
    // Windows animations are off and who never chose for Deckle, since that
    // person gets a HUD with no processing outline without having asked for
    // it (ADR-0009). An unknown mode counts as "System", as in Resolve.
    public static bool ShouldOfferOwnAnimations(
        string? mode, bool windowsAnimationsEnabled, bool alreadyOffered)
        => !alreadyOffered
           && !windowsAnimationsEnabled
           && mode is not (ModeOn or ModeReduced);

    // The one rule, kept pure so a test holds it. An unknown mode (a value
    // typed by hand in settings.json) falls back to the Windows preference,
    // the same answer as "System".
    public static bool Resolve(string? mode, bool windowsAnimationsEnabled) => mode switch
    {
        ModeOn      => true,
        ModeReduced => false,
        _           => windowsAnimationsEnabled,
    };

    private void OnWindowsPreferenceChanged(UISettings sender, object args)
    {
        bool windows = sender.AnimationsEnabled;
        Update(() => _windowsEnabled = windows);
    }

    private void OnSettingsChanged()
    {
        string mode = SettingsService.Instance.Current.Appearance.Animations;
        Update(() => _mode = mode);
    }

    private void Update(Action write)
    {
        bool current;
        lock (_sync)
        {
            write();
            current = Resolve(_mode, _windowsEnabled);
            if (_animationsEnabled == current) return;
            _animationsEnabled = current;
        }

        // Neither source promises the UI thread. Each window marshals to the
        // DispatcherQueue that owns its animated resource.
        Changed?.Invoke(current);
    }
}
