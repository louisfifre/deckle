using System.Runtime.InteropServices;
using System.Threading;
using Deckle.Audio.Internal;
using Deckle.Core;

namespace Deckle.Audio;

// Speaker render primitive — the symmetric counterpart of MicrophoneCapture.
// Plays a finished mono float [-1, 1] clip to the default render device via the
// Win32 winmm waveOut API (the same MME family the capture path uses for
// waveIn).
//
// Single-clip, blocking: Play opens the device, writes one buffer, and blocks
// until the driver signals WHDR_DONE — or the cancellation token fires, in
// which case waveOutReset aborts the in-flight clip. The caller runs Play on a
// background thread and cancels the previous play before starting a new one.
//
// Returns true when a clip was handed to the driver (played to completion or
// cancelled mid-flight), false when it never was: an empty clip, or a device
// that refused to open, prepare or accept the buffer. The caller cannot
// otherwise tell a silent failure from success, since both leave no sound.
//
// Format is carried per call: TTS output is 24 kHz, NOT the 16 kHz the capture
// stack is fixed to.
public static class SpeakerOutput
{
    private const uint WAVE_MAPPER    = 0xFFFFFFFF; // system default render device
    private const uint CALLBACK_EVENT = 0x00050000;
    private const uint WHDR_DONE      = 0x00000001;

    // How long past the clip's own length the wait goes before the log says
    // the driver never flagged it done. Generous on purpose: a Bluetooth sink
    // can add a second or more of latency before the first sample sounds.
    private const long StallGraceMs = 5000;

    public static bool Play(float[] samples, int sampleRate, CancellationToken ct)
    {
        if (samples is null || samples.Length == 0)
        {
            DeckleAudioSource.Log.PlaybackSkipped("empty_clip");
            return false;
        }

        long durationMs = sampleRate > 0 ? samples.Length * 1000L / sampleRate : 0;
        byte[] pcm = PcmConversion.FloatToPcm16(samples);

        var wfx = new WAVEFORMATEX
        {
            wFormatTag      = 1,                       // uncompressed PCM
            nChannels       = 1,                       // mono
            nSamplesPerSec  = (uint)sampleRate,
            nAvgBytesPerSec = (uint)(sampleRate * 2),  // mono × 16-bit
            nBlockAlign     = 2,
            wBitsPerSample  = 16,
            cbSize          = 0,
        };

        // The module-local declaration keeps the last error for the log.
        System.IntPtr hEvent = WaveDeviceInterop.CreateEvent(
            System.IntPtr.Zero, bManualReset: false, bInitialState: false, null);
        if (hEvent == System.IntPtr.Zero)
        {
            DeckleAudioSource.Log.PlaybackFailed(
                "create_event", 0, "none", Marshal.GetLastWin32Error(), ResolveDevice().Name, sampleRate);
            return false;
        }

        uint err = NativeMethods.waveOutOpen(
            out System.IntPtr hWaveOut, WAVE_MAPPER, ref wfx, hEvent,
            System.IntPtr.Zero, CALLBACK_EVENT);
        if (err != 0)
        {
            DeckleAudioSource.Log.PlaybackFailed(
                "open", err, WaveDeviceInterop.MmsysName(err), 0, ResolveDevice().Name, sampleRate);
            NativeMethods.CloseHandle(hEvent);
            return false;
        }

        uint hdrSize = (uint)Marshal.SizeOf<WAVEHDR>();
        System.IntPtr buf    = Marshal.AllocHGlobal(pcm.Length);
        System.IntPtr hdrPtr = Marshal.AllocHGlobal((int)hdrSize);
        bool prepared = false;

        // started pairs PlaybackStarted with exactly one PlaybackCompleted in
        // the finally; outcome stays failed unless the wait loop ends normally.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        bool started = false;
        string outcome = "failed";
        try
        {
            Marshal.Copy(pcm, 0, buf, pcm.Length);
            Marshal.StructureToPtr(new WAVEHDR
            {
                lpData         = buf,
                dwBufferLength = (uint)pcm.Length,
            }, hdrPtr, fDeleteOld: false);

            // A refused prepare or write used to fall into the wait loop below
            // and wait for a WHDR_DONE that never comes, until the caller
            // cancelled; the play now fails at once with its code.
            uint prepareErr = NativeMethods.waveOutPrepareHeader(hWaveOut, hdrPtr, hdrSize);
            if (prepareErr != 0)
            {
                DeckleAudioSource.Log.PlaybackFailed(
                    "prepare_header", prepareErr, WaveDeviceInterop.MmsysName(prepareErr), 0,
                    ResolveDevice().Name, sampleRate);
                return false;
            }
            prepared = true;

            uint writeErr = NativeMethods.waveOutWrite(hWaveOut, hdrPtr, hdrSize);
            if (writeErr != 0)
            {
                DeckleAudioSource.Log.PlaybackFailed(
                    "write", writeErr, WaveDeviceInterop.MmsysName(writeErr), 0,
                    ResolveDevice().Name, sampleRate);
                return false;
            }

            WaveDeviceIdentity device = ResolveDevice();
            DeckleAudioSource.Log.PlaybackStarted(device.Name, device.DeviceId, sampleRate, durationMs);
            started = true;

            // Block until the driver flips WHDR_DONE or the token fires. The
            // event pulses on completion; the 100 ms poll lets cancellation
            // break out promptly without busy-waiting.
            bool done = false;
            bool stallReported = false;
            while (!ct.IsCancellationRequested)
            {
                WAVEHDR hdr = Marshal.PtrToStructure<WAVEHDR>(hdrPtr);
                if ((hdr.dwFlags & WHDR_DONE) != 0)
                {
                    done = true;
                    break;
                }
                if (!stallReported && clock.ElapsedMilliseconds > durationMs + StallGraceMs)
                {
                    stallReported = true;
                    DeckleAudioSource.Log.PlaybackStalled(
                        "done_flag_timeout", clock.ElapsedMilliseconds, durationMs);
                }
                NativeMethods.WaitForSingleObject(hEvent, 100);
            }
            outcome = done ? "completed" : "cancelled";
        }
        finally
        {
            // Order matters: reset flushes any still-playing buffer so the
            // driver no longer owns the header, THEN we unprepare and free it.
            NativeMethods.waveOutReset(hWaveOut);
            if (prepared)
                NativeMethods.waveOutUnprepareHeader(hWaveOut, hdrPtr, hdrSize);
            NativeMethods.waveOutClose(hWaveOut);
            Marshal.FreeHGlobal(buf);
            Marshal.FreeHGlobal(hdrPtr);
            NativeMethods.CloseHandle(hEvent);

            if (started)
                DeckleAudioSource.Log.PlaybackCompleted(outcome, clock.ElapsedMilliseconds);
        }

        return true;
    }

    // The name costs driver calls only the log reads, so it is skipped when no
    // listener is attached.
    private static WaveDeviceIdentity ResolveDevice()
        => DeckleAudioSource.Log.IsEnabled()
            ? WaveDeviceInterop.ResolvePlayback()
            : WaveDeviceIdentity.Unresolved(-1);
}
