using Windows.UI.ViewManagement;

namespace Deckle.Hud;

// Process-wide projection of the Windows UI animation preference. UISettings
// owns the system integration; this wrapper adds one cached value and one
// change signal for all Deckle-authored simple animations.
internal sealed class SystemAnimationPreference
{
    private static readonly Lazy<SystemAnimationPreference> _instance =
        new(() => new SystemAnimationPreference());

    private readonly UISettings _settings = new();
    private readonly object _sync = new();
    private bool _animationsEnabled;

    public static SystemAnimationPreference Instance => _instance.Value;

    private SystemAnimationPreference()
    {
        _animationsEnabled = _settings.AnimationsEnabled;
        // The property exists on Deckle's minimum OS; the hot-change event was
        // added in Windows 10 2004. Older supported builds keep the launch-time
        // value because there is no event surface to subscribe to.
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            _settings.AnimationsEnabledChanged += OnAnimationsEnabledChanged;
    }

    public bool AnimationsEnabled
    {
        get
        {
            lock (_sync) return _animationsEnabled;
        }
    }

    public event Action<bool>? Changed;

    private void OnAnimationsEnabledChanged(UISettings sender, object args)
    {
        bool current = sender.AnimationsEnabled;
        lock (_sync)
        {
            if (_animationsEnabled == current) return;
            _animationsEnabled = current;
        }

        // UISettings does not promise the UI thread. Each window marshals to
        // the DispatcherQueue that owns its animated resource.
        Changed?.Invoke(current);
    }
}
