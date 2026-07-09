using System.Runtime.InteropServices;

namespace Deckle.Audio.Internal;

// Media Foundation interop for AudioFileDecoder — the synchronous
// IMFSourceReader route that decodes an arbitrary audio file to 16 kHz mono
// 32-bit float PCM.
//
// Style mirrors Deckle.Vision/ScreenCaptureInterop*: NO [ComImport] managed
// interfaces (a documented CsWinRT pitfall — mixing a classic RCW cast with the
// WinUI projection throws "element not found" at runtime), so every COM call
// goes through the object's vtable via a delegate* unmanaged function pointer
// indexed by the interface's method slot; IIDs and attribute GUIDs are held as
// static readonly Guid; every returned COM pointer is Marshal.Release()d by the
// caller in a finally.
//
// Deviation from the Vision file's PreserveSig=false convention: the flat
// P/Invokes and the vtable helpers here all return the raw HRESULT (int),
// because AudioFileDecoder must MAP specific HRESULTs to AudioFileDecodeStatus
// rather than throw — a bad file is a status, not an exception. Only genuine
// programming errors (an STA calling thread) throw, on the decoder side.
//
// Vtable slot indices count from IUnknown::QueryInterface = 0 and are stable per
// COM contract (an interface never changes shape once shipped). Each interface's
// full method order is enumerated beside its slots so the indices can be audited
// against mfreadwrite.h / mfobjects.h.
internal static unsafe class MediaFoundationInterop
{
    // ── Constants ────────────────────────────────────────────────────────────

    // MFStartup version token (mfapi.h MF_VERSION) + full-platform flag.
    public const uint MF_VERSION     = 0x00020070;
    public const uint MFSTARTUP_FULL = 0;

    // CoInitializeEx apartment — Media Foundation requires MTA on the decode
    // thread (COINIT_MULTITHREADED = 0x0).
    public const uint COINIT_MULTITHREADED = 0x0;

    // Source-reader stream selectors (mfreadwrite.h).
    public const uint MF_SOURCE_READER_ALL_STREAMS        = 0xFFFFFFFE;
    public const uint MF_SOURCE_READER_FIRST_AUDIO_STREAM = 0xFFFFFFFD;

    // MF_SOURCE_READER_FLAG bits reported in ReadSample's stream-flags out param.
    public const uint MF_SOURCE_READERF_ERROR                   = 0x00000001;
    public const uint MF_SOURCE_READERF_ENDOFSTREAM             = 0x00000002;
    public const uint MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED = 0x00000020;
    public const uint MF_SOURCE_READERF_STREAMTICK              = 0x00000100;

    // HRESULTs (winerror.h + mferror.h) that AudioFileDecoder maps to a status.
    public const int S_OK               = 0;
    public const int S_FALSE            = 1;
    public const int E_FAIL             = unchecked((int)0x80004005);
    public const int RPC_E_CHANGED_MODE = unchecked((int)0x80010106);
    public const int MF_E_INVALIDSTREAMNUMBER         = unchecked((int)0xC00D36B3);
    public const int MF_E_INVALIDMEDIATYPE            = unchecked((int)0xC00D36B4);
    public const int MF_E_UNSUPPORTED_SCHEME          = unchecked((int)0xC00D3E9C);
    public const int MF_E_UNSUPPORTED_BYTESTREAM_TYPE = unchecked((int)0xC00D3E9D);
    public const int MF_E_TOPO_CODEC_NOT_FOUND        = unchecked((int)0xC00D5212);

    // ── Media type attribute + subtype GUIDs (mfapi.h, exported from mfuuid.lib) ─

    public static readonly Guid MF_MT_MAJOR_TYPE =
        new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    public static readonly Guid MF_MT_SUBTYPE =
        new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    public static readonly Guid MF_MT_AUDIO_BITS_PER_SAMPLE =
        new("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
    public static readonly Guid MF_MT_AUDIO_SAMPLES_PER_SECOND =
        new("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
    public static readonly Guid MF_MT_AUDIO_NUM_CHANNELS =
        new("37e48bf5-645e-4c5b-89de-ada9e29b696a");

    // Major type MFMediaType_Audio; subtype MFAudioFormat_Float. Audio subtype
    // GUIDs derive from a WAVE format tag as {tag}-0000-0010-8000-00AA00389B71 —
    // Float = WAVE_FORMAT_IEEE_FLOAT (0x0003).
    public static readonly Guid MFMediaType_Audio =
        new("73647561-0000-0010-8000-00aa00389b71");
    public static readonly Guid MFAudioFormat_Float =
        new("00000003-0000-0010-8000-00aa00389b71");

    // ── Vtable slots ─────────────────────────────────────────────────────────
    //
    // IMFSourceReader : IUnknown (mfreadwrite.h)
    //   0 QueryInterface  1 AddRef  2 Release
    //   3 GetStreamSelection      4 SetStreamSelection
    //   5 GetNativeMediaType      6 GetCurrentMediaType
    //   7 SetCurrentMediaType     8 SetCurrentPosition
    //   9 ReadSample             10 Flush
    //  11 GetServiceForStream    12 GetPresentationAttribute
    private const int SourceReader_SetStreamSelection  = 4;
    private const int SourceReader_GetCurrentMediaType = 6;
    private const int SourceReader_SetCurrentMediaType = 7;
    private const int SourceReader_ReadSample          = 9;

    // IMFMediaType : IMFAttributes : IUnknown (mfobjects.h)
    //   IUnknown 0-2, then IMFAttributes:
    //   3 GetItem   4 GetItemType  5 CompareItem  6 Compare
    //   7 GetUINT32 8 GetUINT64    9 GetDouble   10 GetGUID
    //  11 GetStringLength 12 GetString 13 GetAllocatedString
    //  14 GetBlobSize 15 GetBlob 16 GetAllocatedBlob 17 GetUnknown
    //  18 SetItem 19 DeleteItem 20 DeleteAllItems
    //  21 SetUINT32 22 SetUINT64 23 SetDouble 24 SetGUID
    //  25 SetString 26 SetBlob 27 SetUnknown 28 LockStore 29 UnlockStore
    //  30 GetCount 31 GetItemByIndex 32 CopyAllItems  (then IMFMediaType's own)
    private const int Attributes_SetUINT32 = 21;
    private const int Attributes_SetGUID   = 24;

    // IMFSample : IMFAttributes : IUnknown (mfobjects.h)
    //   IMFAttributes 0-32, then:
    //  33 GetSampleFlags 34 SetSampleFlags 35 GetSampleTime 36 SetSampleTime
    //  37 GetSampleDuration 38 SetSampleDuration 39 GetBufferCount
    //  40 GetBufferByIndex 41 ConvertToContiguousBuffer 42 AddBuffer …
    private const int Sample_ConvertToContiguousBuffer = 41;

    // IMFMediaBuffer : IUnknown (mfobjects.h)
    //   0-2 IUnknown, 3 Lock, 4 Unlock, 5 GetCurrentLength,
    //   6 SetCurrentLength, 7 GetMaxLength
    private const int Buffer_Lock   = 3;
    private const int Buffer_Unlock = 4;

    // ── Flat P/Invokes ───────────────────────────────────────────────────────
    //
    // int returns keep PreserveSig (the default), so the HRESULT reaches the
    // caller instead of being converted into a thrown COMException.

    [DllImport("ole32.dll", ExactSpelling = true)]
    public static extern int CoInitializeEx(nint pvReserved, uint dwCoInit);

    [DllImport("ole32.dll", ExactSpelling = true)]
    public static extern void CoUninitialize();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFStartup(uint version, uint dwFlags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFShutdown();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateMediaType(out nint ppMFType);

    [DllImport("mfreadwrite.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    public static extern int MFCreateSourceReaderFromURL(
        string pwszURL, nint pAttributes, out nint ppSourceReader);

    // ── IMFSourceReader vtable helpers ───────────────────────────────────────

    // SetStreamSelection(dwStreamIndex, fSelected). fSelected is a Win32 BOOL.
    public static int SourceReaderSetStreamSelection(nint reader, uint streamIndex, bool selected)
    {
        var vtbl = *(nint**)reader;
        var fn = (delegate* unmanaged<nint, uint, int, int>)vtbl[SourceReader_SetStreamSelection];
        return fn(reader, streamIndex, selected ? 1 : 0);
    }

    // SetCurrentMediaType(dwStreamIndex, pdwReserved=NULL, pMediaType).
    public static int SourceReaderSetCurrentMediaType(nint reader, uint streamIndex, nint mediaType)
    {
        var vtbl = *(nint**)reader;
        var fn = (delegate* unmanaged<nint, uint, nint, nint, int>)vtbl[SourceReader_SetCurrentMediaType];
        return fn(reader, streamIndex, 0, mediaType);
    }

    // GetCurrentMediaType(dwStreamIndex, ppMediaType). Caller releases the type.
    public static int SourceReaderGetCurrentMediaType(nint reader, uint streamIndex, out nint mediaType)
    {
        var vtbl = *(nint**)reader;
        var fn = (delegate* unmanaged<nint, uint, nint*, int>)vtbl[SourceReader_GetCurrentMediaType];
        nint typePtr;
        int hr = fn(reader, streamIndex, &typePtr);
        mediaType = typePtr;
        return hr;
    }

    // ReadSample(dwStreamIndex, dwControlFlags=0, pdwActualStreamIndex,
    //            pdwStreamFlags, pllTimestamp, ppSample). Synchronous read: the
    //            actual-index and timestamp out params are unused here. ppSample
    //            can come back NULL on S_OK (stream tick / no data). Caller
    //            releases a non-null sample.
    public static int SourceReaderReadSample(nint reader, uint streamIndex, out uint streamFlags, out nint sample)
    {
        var vtbl = *(nint**)reader;
        var fn = (delegate* unmanaged<nint, uint, uint, uint*, uint*, long*, nint*, int>)vtbl[SourceReader_ReadSample];
        uint actualIndex;
        uint flags;
        long timestamp;
        nint samplePtr;
        int hr = fn(reader, streamIndex, 0, &actualIndex, &flags, &timestamp, &samplePtr);
        streamFlags = flags;
        sample = samplePtr;
        return hr;
    }

    // ── IMFMediaType (IMFAttributes) vtable helpers ──────────────────────────

    public static int MediaTypeSetGuid(nint mediaType, Guid key, Guid value)
    {
        var vtbl = *(nint**)mediaType;
        var fn = (delegate* unmanaged<nint, Guid*, Guid*, int>)vtbl[Attributes_SetGUID];
        return fn(mediaType, &key, &value);
    }

    public static int MediaTypeSetUInt32(nint mediaType, Guid key, uint value)
    {
        var vtbl = *(nint**)mediaType;
        var fn = (delegate* unmanaged<nint, Guid*, uint, int>)vtbl[Attributes_SetUINT32];
        return fn(mediaType, &key, value);
    }

    // ── IMFSample / IMFMediaBuffer vtable helpers ────────────────────────────

    // ConvertToContiguousBuffer(ppBuffer). Collapses a multi-buffer sample into
    // one buffer (a no-op copy for the single-buffer common case). Caller
    // releases the returned buffer.
    public static int SampleConvertToContiguousBuffer(nint sample, out nint buffer)
    {
        var vtbl = *(nint**)sample;
        var fn = (delegate* unmanaged<nint, nint*, int>)vtbl[Sample_ConvertToContiguousBuffer];
        nint bufferPtr;
        int hr = fn(sample, &bufferPtr);
        buffer = bufferPtr;
        return hr;
    }

    // Lock(ppbBuffer, pcbMaxLength, pcbCurrentLength). Returns a raw pointer to
    // the buffer memory plus the valid byte count. Must be paired 1:1 with
    // BufferUnlock. The max-length out is unused here.
    public static int BufferLock(nint buffer, out nint data, out uint currentLength)
    {
        var vtbl = *(nint**)buffer;
        var fn = (delegate* unmanaged<nint, nint*, uint*, uint*, int>)vtbl[Buffer_Lock];
        nint dataPtr;
        uint maxLength;
        uint curLength;
        int hr = fn(buffer, &dataPtr, &maxLength, &curLength);
        data = dataPtr;
        currentLength = curLength;
        return hr;
    }

    public static int BufferUnlock(nint buffer)
    {
        var vtbl = *(nint**)buffer;
        var fn = (delegate* unmanaged<nint, int>)vtbl[Buffer_Unlock];
        return fn(buffer);
    }
}
