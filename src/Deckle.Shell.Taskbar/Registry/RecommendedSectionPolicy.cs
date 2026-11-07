using System.Security;
using Microsoft.Win32;

namespace Deckle.Shell.Taskbar;

// The tier 1 engine of "hide Recommended" (brief-csharp-socle A4, B9, T5): the
// documented policy HideRecommendedSection=1 under
// Software\Policies\Microsoft\Windows\Explorer, user scope, which Microsoft
// lists for IoT Enterprise LTSC from 22H2. HKCU rather than HKLM: per user, and
// the same UAC cost.
//
// Windows holds the truth. Nothing about this policy is stored by Deckle: no
// setting, no record of what the key held before, no reapply at startup. The
// page asks IsHidden() when it opens and after each change, and a policy set or
// removed by anyone else shows there as it is.
//
// The policy key is writable by administrators only, even under HKCU, so a
// change takes one of two routes. An elevated Deckle ("Start elevated" on)
// writes the key directly; otherwise reg.exe runs as administrator and Windows
// shows its UAC prompt (PolicyElevation). A declined prompt is
// PermissionDenied. The prompt only ever follows the card's switch, and blocks
// until it is answered, so the page calls HideAsync and ShowAsync, which run it
// off the UI thread.
//
// What is never touched: HKLM. When the machine policy holds 1 (someone else
// set it), Hide has nothing to do, and Show answers HeldByMachine without
// asking for anything, since removing the user value would not give the section
// back. Show deletes the HKCU value only when it holds 1, the value Hide
// writes; a user value the person had set the same way before Deckle goes with
// it, an accepted edge.
//
// When Start notices: probably when StartMenuExperienceHost starts, so at the
// next sign-in. Unverified; the page says nothing about timing until it is
// checked on the machine.
//
// Thread-safe: the page's Hide and Show (thread pool) take one lock, so a
// prompt is never shown twice at once.
public sealed class RecommendedSectionPolicy
{
    public const string ValueName = "HideRecommendedSection";

    /// <summary>The policy key, the same path in both hives.</summary>
    public const string PolicyKeyPath = @"Software\Policies\Microsoft\Windows\Explorer";

    /// <summary>The policy's "hidden" value; 0 or absent shows the section.</summary>
    public const uint Hidden = 1;

    private static readonly Lazy<RecommendedSectionPolicy> _instance = new(ForCurrentProcess);

    /// <summary>The engine on the real policy keys, shared by the page.</summary>
    public static RecommendedSectionPolicy Instance => _instance.Value;

    private readonly object _gate = new();
    private readonly PolicyKey _userKey;
    private readonly PolicyKey _machineKey;
    private readonly PolicyElevation _elevation;
    private readonly bool _isPrivileged;

    /// <param name="userKey">Where the user policy is written: HKCU\<see cref="PolicyKeyPath"/> in the app.</param>
    /// <param name="machineKey">Where the machine policy is only read: HKLM\<see cref="PolicyKeyPath"/> in the app.</param>
    /// <param name="runner">Runs reg.exe as administrator when the process is not elevated.</param>
    /// <param name="isPrivileged">Whether this process holds the admin token, <see cref="Environment.IsPrivilegedProcess"/> in the app.</param>
    public RecommendedSectionPolicy(PolicyKey userKey, PolicyKey machineKey, IElevatedProcessRunner runner, bool isPrivileged)
    {
        _userKey = userKey;
        _machineKey = machineKey;
        _elevation = new PolicyElevation(runner);
        _isPrivileged = isPrivileged;
    }

    private static RecommendedSectionPolicy ForCurrentProcess() => new(
        new PolicyKey(RegistryHive.CurrentUser, PolicyKeyPath),
        new PolicyKey(RegistryHive.LocalMachine, PolicyKeyPath),
        new ElevatedProcessRunner(),
        Environment.IsPrivilegedProcess);

    /// <summary>Whether either hive's policy hides the section.</summary>
    public bool IsHidden()
    {
        lock (_gate) return MachineHides() || UserHides();
    }

    /// <summary>Whether the machine policy hides the section, which Deckle can read and never change.</summary>
    public bool IsHeldByMachine()
    {
        lock (_gate) return MachineHides();
    }

    public Task<PolicyOutcome> HideAsync() => Task.Run(Hide);

    public Task<PolicyOutcome> ShowAsync() => Task.Run(Show);

    /// <summary>
    /// Puts the policy in place, prompting for UAC when the process is not
    /// elevated. Blocks on the prompt.
    /// </summary>
    public PolicyOutcome Hide()
    {
        lock (_gate)
        {
            if (MachineHides()) return Report("hide", "machine", PolicyOutcome.Applied, "none");
            if (UserHides()) return Report("hide", "in_place", PolicyOutcome.Applied, "none");

            (string route, PolicyWriteResult result) = _isPrivileged
                ? ("direct", WriteDirect())
                : ("elevated", _elevation.SetDword(_userKey, ValueName, Hidden));
            result = Verified(result, expectHidden: true);
            return Report("hide", route, result.Outcome, result.Error);
        }
    }

    /// <summary>
    /// Removes the user policy, prompting for UAC when the process is not
    /// elevated. Blocks on the prompt, except when the machine policy holds the
    /// section hidden: then it answers <see cref="PolicyOutcome.HeldByMachine"/>
    /// at once.
    /// </summary>
    public PolicyOutcome Show()
    {
        lock (_gate)
        {
            if (MachineHides()) return Report("show", "machine", PolicyOutcome.HeldByMachine, "none");
            if (!UserHides()) return Report("show", "in_place", PolicyOutcome.Applied, "none");

            (string route, PolicyWriteResult result) = _isPrivileged
                ? ("direct", DeleteDirect())
                : ("elevated", _elevation.DeleteValue(_userKey, ValueName));
            result = Verified(result, expectHidden: false);
            return Report("show", route, result.Outcome, result.Error);
        }
    }

    // An exit code 0 that left the user's hive unchanged means the elevated
    // reg.exe wrote another account's hive (PolicyElevation explains when).
    private PolicyWriteResult Verified(PolicyWriteResult result, bool expectHidden)
    {
        if (result.Outcome != PolicyOutcome.Applied) return result;
        return UserHides() == expectHidden ? result : new PolicyWriteResult(PolicyOutcome.Failed, "other_hive");
    }

    private PolicyWriteResult WriteDirect() => Direct(() => _userKey.WriteDword(ValueName, Hidden));

    private PolicyWriteResult DeleteDirect() => Direct(() => _userKey.DeleteValue(ValueName));

    private static PolicyWriteResult Direct(Action write)
    {
        try
        {
            write();
            return new PolicyWriteResult(PolicyOutcome.Applied, "none");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            return new PolicyWriteResult(PolicyOutcome.PermissionDenied, ex.GetType().Name);
        }
        catch (IOException ex)
        {
            return new PolicyWriteResult(PolicyOutcome.Failed, ex.GetType().Name);
        }
    }

    private bool MachineHides() => Holds(_machineKey);

    private bool UserHides() => Holds(_userKey);

    // A key the process may not read counts as no policy: the section shows,
    // and a write attempt says whether Deckle may change it.
    private static bool Holds(PolicyKey key)
    {
        try
        {
            return key.ReadDword(ValueName) == Hidden;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            return false;
        }
    }

    // Every attempt gets its detail line; one that wrote also gets its
    // milestone, and one that did not do what was asked gets the Warning with
    // its cause. A call that found the state already in place (route machine or
    // in_place) writes nothing, so it has no milestone.
    private static PolicyOutcome Report(string action, string route, PolicyOutcome outcome, string error)
    {
        if (outcome != PolicyOutcome.Applied)
            DeckleShellTaskbarSource.Log.RecommendedPolicyFailed(action, route, outcome.ToString(), error);
        else if (route is "direct" or "elevated")
        {
            if (action == "show") DeckleShellTaskbarSource.Log.RecommendedSectionShown();
            else DeckleShellTaskbarSource.Log.RecommendedSectionHidden();
        }
        DeckleShellTaskbarSource.Log.RecommendedPolicyDetail(action, route, outcome.ToString(), error);
        return outcome;
    }
}
