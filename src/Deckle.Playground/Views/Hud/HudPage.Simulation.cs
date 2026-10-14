using Microsoft.UI.Xaml.Controls;

namespace Deckle.Playground;

// ─── HudPage — Simulation section ───────────────────────────────────────────
//
// What the preview plays, never what the HUD is: these values live in page
// fields, outside TuningModel and the countdown config, so IsDirty never sees
// them, Save never writes them and the bar never shows for them.
public sealed partial class HudPage
{
    // Simulated conversational speech for the Recording preview, fed to the
    // stroke by the RMS pump. One class so a reset is one `new()` and the
    // defaults are written once.
    private sealed class SpeechSimulation
    {
        // 0.013 ≈ -38 dBFS, 0.100 ≈ -20 dBFS.
        public float RmsMin           = 0.013f;
        public float RmsMax           = 0.100f;
        public float RmsPeriodSeconds = 2.0f;
        public bool  ManualOverride   = false;
        public float ManualValue      = 0.012f;
    }

    private SpeechSimulation _speechSimulation = new();

    private void AddSimulationExpander()
    {
        var stack = NewTuningSection("Simulation", ResetSimulation);
        stack.Children.Add(TuningHelp("Simulation changes the preview only. Nothing here is saved."));
        switch (ViewModel.CurrentTarget)
        {
            case HudTarget.Recording: AddSpeechSimulationRows(stack); break;
            case HudTarget.Gauge:     AddGaugeSimulationRows(stack);  break;
        }
    }

    // Only the shown target's simulation: the other target's state is not on
    // screen, so resetting it would be a surprise on return.
    private void ResetSimulation()
    {
        switch (ViewModel.CurrentTarget)
        {
            case HudTarget.Recording: _speechSimulation = new(); break;
            case HudTarget.Gauge:     ResetGaugeSimulation();    break;
        }
        RebuildTuningPanel();
    }

    // The preview maps the simulated RMS through the live AudioLevelMapper
    // calibration, which Settings > Recording and auto-calibration own. The
    // Playground reads it and never writes it. No row rebuilds the preview:
    // the pump reads the fields on every tick.
    private void AddSpeechSimulationRows(StackPanel stack)
    {
        // Step 0.001: the defaults (0.013, 0.100) need three decimals.
        AddFloatRow(stack, "SimRmsMin", 0, 0.3, 0.001, _speechSimulation.RmsMin,
            v => _speechSimulation.RmsMin = (float)v);
        AddFloatRow(stack, "SimRmsMax", 0, 0.3, 0.001, _speechSimulation.RmsMax,
            v => _speechSimulation.RmsMax = (float)v);
        AddFloatRow(stack, "SimRmsPeriodSeconds", 0.2, 10, 0.1, _speechSimulation.RmsPeriodSeconds,
            v => _speechSimulation.RmsPeriodSeconds = (float)v);
        AddToggleRow(stack, "Manual override", _speechSimulation.ManualOverride,
            v => _speechSimulation.ManualOverride = v);
        AddFloatRow(stack, "SimRmsManualValue", 0, 0.3, 0.001, _speechSimulation.ManualValue,
            v => _speechSimulation.ManualValue = (float)v);
    }
}
