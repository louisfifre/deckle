using System.Runtime.InteropServices;
using Deckle.Orchestration;
using Xunit;
using static Deckle.Orchestration.DisplayConfigNativeMethods;

namespace Deckle.Orchestration.Tests;

// The CCD calls trust the size written in each packet's header; a wrong
// layout makes Windows reject the packet or read past it. Expected sizes are
// computed from wingdi.h (Windows SDK 10.0.26100): LUID is 8 bytes, every
// enum, UINT32 and BOOL 4, UINT16 2, WCHAR 2.
[Trait("Category", "unit")]
public sealed class DisplayConfigLayoutTests
{
    [Fact]
    public void PathInfoMatchesTheHeader()
    {
        // source: adapterId 8 + id 4 + modeInfoIdx 4 + statusFlags 4 = 20
        Assert.Equal(20, Marshal.SizeOf<DISPLAYCONFIG_PATH_SOURCE_INFO>());
        // target: adapterId 8 + id 4 + modeInfoIdx 4 + outputTechnology 4 + rotation 4
        // + scaling 4 + refreshRate 8 + scanLineOrdering 4 + targetAvailable 4 + statusFlags 4 = 48
        Assert.Equal(48, Marshal.SizeOf<DISPLAYCONFIG_PATH_TARGET_INFO>());
        // source 20 + target 48 + flags 4 = 72
        Assert.Equal(72, Marshal.SizeOf<DISPLAYCONFIG_PATH_INFO>());
    }

    [Fact]
    public void ModeInfoMatchesTheHeader()
    {
        // infoType 4 + id 4 + adapterId 8 + union 48 (DISPLAYCONFIG_VIDEO_SIGNAL_INFO:
        // pixelRate 8 + hSyncFreq 8 + vSyncFreq 8 + activeSize 8 + totalSize 8
        // + videoStandard 4 + scanLineOrdering 4) = 64
        Assert.Equal(64, Marshal.SizeOf<DISPLAYCONFIG_MODE_INFO>());
    }

    [Fact]
    public void DeviceInfoPacketsMatchTheHeader()
    {
        // type 4 + size 4 + adapterId 8 + id 4 = 20
        Assert.Equal(20, Marshal.SizeOf<DISPLAYCONFIG_DEVICE_INFO_HEADER>());
        // header 20 + WCHAR[32] 64 = 84
        Assert.Equal(84, Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>());
        // header 20 + flags 4 + outputTechnology 4 + two UINT16 4 + connectorInstance 4
        // + WCHAR[64] 128 + WCHAR[128] 256 = 420
        Assert.Equal(420, Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>());
    }

    [Fact]
    public void AdvancedColorPacketsMatchTheHeader()
    {
        // header 20 + value 4 + colorEncoding 4 + bitsPerColorChannel 4 = 32
        Assert.Equal(32, Marshal.SizeOf<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>());
        // header 20 + value 4 = 24
        Assert.Equal(24, Marshal.SizeOf<DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE>());
        // header 20 + value 4 + colorEncoding 4 + bitsPerColorChannel 4 + activeColorMode 4 = 36
        Assert.Equal(36, Marshal.SizeOf<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2>());
        // header 20 + value 4 = 24
        Assert.Equal(24, Marshal.SizeOf<DISPLAYCONFIG_SET_HDR_STATE>());
    }

    [Fact]
    public void HeaderCarriesThePacketSize()
    {
        DISPLAYCONFIG_DEVICE_INFO_HEADER header = Header<DISPLAYCONFIG_SET_HDR_STATE>(
            DISPLAYCONFIG_DEVICE_INFO_SET_HDR_STATE, default, 7);

        Assert.Equal(16u, header.type);
        Assert.Equal((uint)Marshal.SizeOf<DISPLAYCONFIG_SET_HDR_STATE>(), header.size);
        Assert.Equal(7u, header.id);
    }
}
