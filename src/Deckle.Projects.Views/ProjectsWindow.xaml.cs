using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Shell;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using WinRT.Interop;

namespace Deckle.Projects.Views;

// ── The Deckle Projects window ───────────────────────────────────────────────
//
// One window through which the person sees the folders they added as projects,
// groups them in categories, and reaches each project's page (spec, Window).
// This file holds the window's lifetime; its chrome, its side pane, its
// navigation, its Refresh and what the self-check drives each have their
// partial file beside it.
//
// The window works on a ProjectsWorkspace: the store, the readings, the script
// menus, the actions that run and the links. Given a module folder it builds
// the workspace itself and owns it; given a workspace (the app keeps one for
// its lifetime, the self-check injects one with a fake terminal and a fixed
// clock) it only uses it, and closing the window leaves it alone.
//
// Closing the window never stops an action: it lives in its terminal (spec,
// Behaviours).
public sealed partial class ProjectsWindow : Window, IProjectsHost
{
    private readonly nint _hwnd;
    private readonly ProjectsWorkspace _workspace;
    private readonly bool _ownsWorkspace;
    private readonly ProjectsContext _context;

    public ProjectsWindow(string moduleDirectory, ProjectsHooks hooks)
        : this(new ProjectsWorkspace(moduleDirectory), hooks, ownsWorkspace: true)
    {
    }

    public ProjectsWindow(ProjectsWorkspace workspace, ProjectsHooks hooks)
        : this(workspace, hooks, ownsWorkspace: false)
    {
    }

    private ProjectsWindow(ProjectsWorkspace workspace, ProjectsHooks hooks, bool ownsWorkspace)
    {
        _workspace = workspace;
        _ownsWorkspace = ownsWorkspace;
        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);
        _context = new ProjectsContext(this, workspace, hooks);

        ApplyChrome();
        ApplyLabels();

        _workspace.Store.Changed += OnStoreChanged;
        Activated += OnActivated;
        Closed += OnClosed;

        AttachPane();
        RebuildPane();
        // The window opens on Home each time (spec, Window).
        NavigateTo(new ProjectsTarget.Home());
    }

    public void ShowAndActivate()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } op) op.Restore();
        AppWindow.Show();
        Activate();
        NativeMethods.SetForegroundWindow(_hwnd);
        WindowingProbe.EmitWindowPositioned(_hwnd, "projects", "Center");
    }

    // The window is gone: nothing it listened to may call back into it, and the
    // page it showed ends what it started.
    private void OnClosed(object sender, WindowEventArgs args)
    {
        _workspace.Store.Changed -= OnStoreChanged;
        Activated -= OnActivated;
        _page?.Dispose();
        _page = null;
        if (_ownsWorkspace) _workspace.Dispose();
    }

    DispatcherQueue IProjectsHost.Dispatcher => DispatcherQueue;

    XamlRoot? IProjectsHost.XamlRoot => Content?.XamlRoot;

    // The system's folder picker, one folder. A picker that cannot open leaves
    // the window as it was and says so in the log.
    async Task<string?> IProjectsHost.PickFolderAsync()
    {
        try
        {
            var picker = new Microsoft.Windows.Storage.Pickers.FolderPicker(AppWindow.Id);
            var picked = await picker.PickSingleFolderAsync();
            return string.IsNullOrEmpty(picked?.Path) ? null : picked.Path;
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            DeckleProjectsSource.Log.FolderPickFailed(ex.GetType().Name);
            return null;
        }
    }

    private void ApplyLabels()
    {
        AutomationProperties.SetName(Nav, ProjectsText.Get("Pane_Label"));
    }
}
