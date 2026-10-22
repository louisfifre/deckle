using System;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Deckle.Audio;
using Deckle.Transcription;

namespace Deckle.Playground;

// ─── HudPage — Microphone source ────────────────────────────────────────────
//
// The Recording preview can follow the real microphone instead of the
// simulation: the level of each 50 ms and the pause gauge a dictation would
// show, computed the way a dictation computes them (MicrophonePreviewFeed).
// The choice is page state, outside TuningModel, so IsDirty never sees it and
// Save never writes it. The Playground reads the capture and segmenter
// settings and writes neither; nothing it records is kept.
//
// When the microphone is open. Only while the preview plays the Recording
// target in Microphone mode: ApplyTarget opens it on Play and closes it on
// Stop, on a switch to another target or to the simulation, and the page
// closes it when it unloads or is disposed. A tab that is merely opened never
// asks for the device. Leaving the page stops a Microphone preview, as Stop
// would, so coming back does not reopen the device without a press on Start.
//
// When it fails. The feed reports an open that did not work, or a capture that
// ended on its own, from its capture thread. The page then moves to the
// simulation, says why in the tab and writes one Warning to the journal. The
// default is Microphone: nothing probes the device before Play, so the
// fallback happens at the first Start that cannot open it.
public sealed partial class HudPage
{
    private enum PreviewSource { Microphone, Simulation }

    private PreviewSource _previewSource = PreviewSource.Microphone;

    // Why the page is on the simulation when the microphone did not work; empty
    // when the person chose the simulation.
    private string _microphoneNote = "";

    // Built on the first Start, on the UI thread, so a tab that never opens the
    // microphone never builds one.
    private MicrophonePreviewFeed? _microphoneFeed;

    // Moves on every Stop. A failure carries the generation it was reported
    // under, so one that reaches the UI thread after the preview moved on is
    // dropped rather than taken for the current run's.
    private int _microphoneGeneration;

    private bool WantsMicrophone() =>
        _previewSource == PreviewSource.Microphone
        && ViewModel.IsPlaying
        && ViewModel.CurrentTarget == HudTarget.Recording;

    // Opens the microphone, or restarts the take on the one already open.
    private void StartMicrophone()
    {
        _microphoneFeed ??= CreateMicrophoneFeed();
        _microphoneFeed.Start();
    }

    // Final once it returns: no level or gauge reaches the preview afterwards,
    // and the gauge has been reset. Safe to call when nothing is open.
    private void StopMicrophone()
    {
        _microphoneFeed?.Stop();
        Interlocked.Increment(ref _microphoneGeneration);
    }

    private MicrophonePreviewFeed CreateMicrophoneFeed()
    {
        // Read here, on the UI thread: the sinks below run on the capture's.
        // HudChrono takes both calls from any thread, as the live HUD's
        // capture does; HudWindow passes them through the same way.
        var chrono = ChronoPreview;
        return new MicrophonePreviewFeed(
            createCapture: () => new MicrophoneCapture(),
            deviceId: () => CaptureSettingsService.Instance.Current.AudioInputDeviceId,
            segmenterSettings: () => TranscriptionSettingsService.Instance.Current.Streaming.Segmenter,
            onLevel: chrono.UpdateAudioLevel,
            onProgress: progress => chrono.UpdateUtteranceProgress(
                progress.IsActive, progress.Fraction, progress.CompletedUtterances),
            onFailed: failure =>
            {
                int generation = Volatile.Read(ref _microphoneGeneration);
                DispatcherQueue.TryEnqueue(() => OnMicrophoneFailed(generation, failure));
            });
    }

    private void OnMicrophoneFailed(int generation, MicrophonePreviewFailure failure)
    {
        if (generation != Volatile.Read(ref _microphoneGeneration) || !WantsMicrophone()) return;

        DecklePlaygroundSource.Log.MicrophonePreviewFellBack(
            failure.Cause, failure.MmsysErr, failure.ExceptionType);
        _previewSource = PreviewSource.Simulation;
        _microphoneNote = failure.Reason + " Showing the simulation.";

        // The panel follows the source; ApplyTarget starts the take over on
        // the simulation, the one path that leaves the preview coherent.
        BuildTuningPanel();
        ApplyTarget();
    }

    // Leaving the page ends a Microphone preview: the device closes now and the
    // preview stops, so the page's next Loaded finds nothing to reopen.
    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        if (!WantsMicrophone()) return;
        StopMicrophone();
        ViewModel.IsPlaying = false;
    }

    // The source row of the Simulation section, and what follows it: a line on
    // what Microphone does, the reason for a fallback, and the simulation's own
    // rows (simulationRows, filled by the caller), shown only in Simulation.
    // A choice changes visibility in place and goes through ApplyTarget: the
    // panel is not rebuilt, so the row keeps its focus.
    private void AddPreviewSourceRows(StackPanel stack, StackPanel simulationRows)
    {
        var microphoneItem = new SelectorBarItem { Text = "Microphone", Tag = PreviewSource.Microphone };
        var simulationItem = new SelectorBarItem { Text = "Simulation", Tag = PreviewSource.Simulation };
        var selector = new SelectorBar { VerticalAlignment = VerticalAlignment.Center };
        selector.Items.Add(microphoneItem);
        selector.Items.Add(simulationItem);
        // Selection follows focus, so nothing is ever left unselected.
        selector.SelectedItem = _previewSource == PreviewSource.Microphone ? microphoneItem : simulationItem;
        AutomationProperties.SetName(selector, "Source");

        var grid = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var label = new TextBlock
        {
            Text = "Source",
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(label, 0);
        Grid.SetColumn(selector, 1);
        grid.Children.Add(label);
        grid.Children.Add(selector);
        stack.Children.Add(grid);

        var microphoneHelp = TuningHelp(
            "Follows your microphone, with the same level and pause gauge as dictation. Nothing is recorded.");
        var note = TuningHelp(_microphoneNote);
        stack.Children.Add(microphoneHelp);
        stack.Children.Add(note);

        void ShowSource()
        {
            bool microphone = _previewSource == PreviewSource.Microphone;
            microphoneHelp.Visibility = microphone ? Visibility.Visible : Visibility.Collapsed;
            note.Text                 = _microphoneNote;
            note.Visibility           = !microphone && _microphoneNote.Length > 0
                                            ? Visibility.Visible : Visibility.Collapsed;
            simulationRows.Visibility = microphone ? Visibility.Collapsed : Visibility.Visible;
        }
        ShowSource();

        // After the first selection, so building the row fires nothing.
        selector.SelectionChanged += (bar, _) =>
        {
            if (bar.SelectedItem?.Tag is not PreviewSource source || source == _previewSource) return;
            _previewSource = source;
            // A choice, not a fallback: the old reason no longer applies.
            _microphoneNote = "";
            ShowSource();
            ApplyTarget();
        };
    }
}
