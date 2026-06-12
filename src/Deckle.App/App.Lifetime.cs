using Deckle.App.Diagnostics;

namespace Deckle.App;

public partial class App
{
    private void QuitApp()
    {
        DeckleAppSource.Log.ShutdownRequested();
        try { Settings.SettingsService.Instance.Flush(); } catch (Exception ex) { DeckleAppSource.Log.ShutdownWarning("settings flush: " + ex.Message); }
        try { _hotkeyManager?.Dispose();   } catch (Exception ex) { DeckleAppSource.Log.ShutdownWarning("hotkeys dispose: " + ex.Message); }
        try { _tray?.Dispose();            } catch (Exception ex) { DeckleAppSource.Log.ShutdownWarning("tray dispose: " + ex.Message); }
        try { _trayMenu?.Dispose();        } catch (Exception ex) { DeckleAppSource.Log.ShutdownWarning("tray menu dispose: " + ex.Message); }
        try { _messageHost?.Dispose();     } catch (Exception ex) { DeckleAppSource.Log.ShutdownWarning("message host dispose: " + ex.Message); }
        try { _overlayManager?.Dispose();  } catch (Exception ex) { DeckleAppSource.Log.ShutdownWarning("overlay manager dispose: " + ex.Message); }
        try { _engine?.Dispose();          } catch (Exception ex) { DeckleAppSource.Log.ShutdownWarning("engine dispose: " + ex.Message); }
        try { ShutdownTrackpad();          } catch (Exception ex) { DeckleAppSource.Log.ShutdownWarning("trackpad shutdown: " + ex.Message); }
        try { ShutdownTaskbarCover();      } catch (Exception ex) { DeckleAppSource.Log.ShutdownWarning("taskbar cover shutdown: " + ex.Message); }
        try { _ambientEngine?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5)); }
        catch (Exception ex) { DeckleAppSource.Log.ShutdownWarning("ambient engine dispose: " + ex.Message); }
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
                DeckleAppSource.Log.PostBuildRelaunchFailed(ex.Message);
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
