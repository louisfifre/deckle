using System.Runtime.InteropServices;
using System.Threading;
using Deckle.Core;

namespace Deckle.Audio;

// Speaker render primitive — the symmetric counterpart of MicrophoneCapture.
// Plays a finished mono float [-1, 1] clip to the default render device via the
// Win32 winmm waveOut API (the same MME family the capture path uses for
// waveIn). No managed audio library is pulled in; the WAVEFORMATEX / WAVEHDR
// structs and the waveOut* P/Invokes are shared from Deckle.Core.
//
// Single-clip, blocking: Play opens the device, writes one buffer, and blocks
// until the driver signals WHDR_DONE — or the cancellation token fires, in
// which case waveOutReset aborts the in-flight clip. The caller runs Play on a
// background thread and serializes calls (SpeechEngine cancels the previous
// play before starting a new one).
//
// Returns true when a clip was handed to the driver (played to completion or
// cancelled mid-flight), false when the render device could not be opened —
// the caller cannot otherwise tell a silent device-open failure from success,
// since both leave no sound. The open failure is also logged here under
// [AUDIO]; the bool lets the speech narrative surface it under [SPEECH].
//
// Format is carried per call: TTS output is 24 kHz, NOT the 16 kHz the capture
// stack is fixed to.
public static class SpeakerOutput
{
    private const uint WAVE_MAPPER    = 0xFFFFFFFF; // system default render device
    private const uint CALLBACK_EVENT = 0x00050000;
    private const uint WHDR_DONE      = 0x00000001;

    public static bool Play(float[] samples, int sampleRate, CancellationToken ct)
    {
        if (samples is null || samples.Length == 0) return false;

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

        System.IntPtr hEvent = NativeMethods.CreateEvent(
            System.IntPtr.Zero, bManualReset: false, bInitialState: false, null);

        uint err = NativeMethods.waveOutOpen(
            out System.IntPtr hWaveOut, WAVE_MAPPER, ref wfx, hEvent,
            System.IntPtr.Zero, CALLBACK_EVENT);
        if (err != 0)
        {
            DeckleAudioSource.Log.SpeakerOpenFailed();
            DeckleAudioSource.Log.SpeakerOpenFailedDetail(err);
            NativeMethods.CloseHandle(hEvent);
            return false;
        }

        uint hdrSize = (uint)Marshal.SizeOf<WAVEHDR>();
        System.IntPtr buf    = Marshal.AllocHGlobal(pcm.Length);
        System.IntPtr hdrPtr = Marshal.AllocHGlobal((int)hdrSize);
        bool prepared = false;
        try
        {
            Marshal.Copy(pcm, 0, buf, pcm.Length);
            Marshal.StructureToPtr(new WAVEHDR
            {
                lpData         = buf,
                dwBufferLength = (uint)pcm.Length,
            }, hdrPtr, fDeleteOld: false);

            NativeMethods.waveOutPrepareHeader(hWaveOut, hdrPtr, hdrSize);
            prepared = true;
            NativeMethods.waveOutWrite(hWaveOut, hdrPtr, hdrSize);

            // Block until the driver flips WHDR_DONE or the token fires. The
            // event pulses on completion; the 100 ms poll lets cancellation
            // break out promptly without busy-waiting.
            while (!ct.IsCancellationRequested)
            {
                WAVEHDR hdr = Marshal.PtrToStructure<WAVEHDR>(hdrPtr);
                if ((hdr.dwFlags & WHDR_DONE) != 0) break;
                NativeMethods.WaitForSingleObject(hEvent, 100);
            }
        }
        finally
        {
            // Order matters: reset flushes any still-playing buffer so the
            // driver no longer owns the header, THEN we unprepare and free it.
            // reset → unprepare → close device → free memory → close event.
            NativeMethods.waveOutReset(hWaveOut);
            if (prepared)
                NativeMethods.waveOutUnprepareHeader(hWaveOut, hdrPtr, hdrSize);
            NativeMethods.waveOutClose(hWaveOut);
            Marshal.FreeHGlobal(buf);
            Marshal.FreeHGlobal(hdrPtr);
            NativeMethods.CloseHandle(hEvent);
        }

        return true;
    }
}
