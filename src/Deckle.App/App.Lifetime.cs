using Deckle.App;

namespace Deckle.App;

public partial class App
{
    private void QuitApp()
    {
        DeckleAppSource.Log.ShutdownRequested();
        try { Settings.SettingsService.Instance.Flush(); } catch (Exception ex) { DeckleAppSource.Log.ShutdownWarning(); DeckleAppSource.Log.ShutdownWarningDetail("settings flush: " + ex.Message); }
        try { _hotkeyManager?.Dispose();   } catch (Exception ex) { DeckleAppSource.Log.ShutdownWarning(); DeckleAppSource.Log.ShutdownWarningDetail("hotkeys dispose: " + ex.Message); }
        try { _tray?.Dispose();            } catch (Exception ex) { DeckleAppSource.Log.ShutdownWarning(); DeckleAppSource.Log.ShutdownWarningDetail("tray dispose: " + ex.Message); }
        try { _trayMenu?.Dispose();        } catch (Exception ex) { DeckleAppSource.Log.ShutdownWarning(); DeckleAppSource.Log.ShutdownWarningDetail("tray menu dispose: " + ex.Message); }
        // Before _messageHost: the signal's subclass + Raw Input sink sit on the
        // host's HWND, which _messageHost.Dispose destroys.
        try { _cursorSignal?.Dispose();    } catch (Exception ex) { DeckleAppSource.Log.ShutdownWarning(); DeckleAppSource.Log.ShutdownWarningDetail("cursor signal dispose: " + ex.Message); }
        try { _messageHost?.Dispose();     } catch (Exception ex) { DeckleAppSource.Log.ShutdownWarning(); DeckleAppSource.Log.ShutdownWarningDetail("message host dispose: " + ex.Message); }
        try { _overlayManager?.Dispose();  } catch (Exception ex) { DeckleAppSource.Log.ShutdownWarning(); DeckleAppSource.Log.ShutdownWarningDetail("overlay manager dispose: " + ex.Message); }
        try { _engine?.Dispose();          } catch (Exception ex) { DeckleAppSource.Log.ShutdownWarning(); DeckleAppSource.Log.ShutdownWarningDetail("engine dispose: " + ex.Message); }
        try { _speechEngine?.Dispose();    } catch (Exception ex) { DeckleAppSource.Log.ShutdownWarning(); DeckleAppSource.Log.ShutdownWarningDetail("speech engine dispose: " + ex.Message); }
        try { ShutdownTrackpad();          } catch (Exception ex) { DeckleAppSource.Log.ShutdownWarning(); DeckleAppSource.Log.ShutdownWarningDetail("trackpad shutdown: " + ex.Message); }
        try { ShutdownTaskbarCover();      } catch (Exception ex) { DeckleAppSource.Log.ShutdownWarning(); DeckleAppSource.Log.ShutdownWarningDetail("taskbar cover shutdown: " + ex.Message); }
        try { ShutdownAutocorrect();       } catch (Exception ex) { DeckleAppSource.Log.ShutdownWarning(); DeckleAppSource.Log.ShutdownWarningDetail("autocorrect shutdown: " + ex.Message); }
        try { _ambientEngine?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5)); }
        catch (Exception ex) { DeckleAppSource.Log.ShutdownWarning(); DeckleAppSource.Log.ShutdownWarningDetail("ambient engine dispose: " + ex.Message); }
        Environment.Exit(0);
    }

    public static void RestartApp(string? pageTag = null)
    {
        DeckleAppSource.Log.RestartRequested();
        try { Settings.SettingsService.Instance.Flush(); } catch { }

        var exePath = Environment.ProcessPath;
        if (exePath is not null)
        {
            var args = pageTag is not null
                ? $"--settings \"{pageTag}\""
                : "--settings";
            DeckleAppSource.Log.RestartSpawnNewProcess(exePath, args);
            System.Diagnostics.Process.Start(exePath, args);
        }

        if (Current is App app)
            app.QuitApp();
    }

    public static void RestartViaShellExecute(string args = "")
    {
        DeckleAppSource.Log.PostBuildRestartRequested();
        try { Settings.SettingsService.Instance.Flush(); } catch { }

        var exePath = Environment.ProcessPath;
        if (exePath is not null)
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName        = exePath,
                Arguments       = args,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? "",
            };
            DeckleAppSource.Log.PostBuildShellExecute(exePath);
            try { System.Diagnostics.Process.Start(psi); }
            catch (Exception ex)
            {
                DeckleAppSource.Log.PostBuildRelaunchFailed();
                DeckleAppSource.Log.PostBuildRelaunchFailedDetail(ex.Message);
            }
        }

        if (Current is App app)
            app.QuitApp();
    }

    private void RestartAppFromTray()
    {
        DeckleAppSource.Log.RestartFromTrayRequested();
        try { Settings.SettingsService.Instance.Flush(); } catch { }
        var exePath = Environment.ProcessPath;
        if (exePath is not null)
        {
            DeckleAppSource.Log.RestartSpawnNewProcess(exePath, "");
            System.Diagnostics.Process.Start(exePath);
        }
        QuitApp();
    }
}
