#!/usr/bin/env python3
"""hidpp_probe.py - read-only HID++ 2.0 probe of feature 0x2121 (HIRES_WHEEL).

What it does
    Talks HID++ 2.0 to a Logitech device from user mode, over a HID++ long-report
    collection (report id 0x11, 20 bytes), while Logi Options+ keeps running and
    keeps its own handle. Two transports are supported:

      receiver   A Logitech Bolt receiver (VID 046D, PID C548), collection with
                 usage page 0xFF00 (usage 0x0002 preferred). Devices behind the
                 receiver are addressed with device index 1..6. For each index
                 it pings the device, reads its name, looks up feature 0x2121
                 and, when present, reads the wheel capability, the wheel mode
                 and the ratchet switch state, printing raw hex and a decoded
                 line for every reply.

      bluetooth  A device connected directly over Bluetooth LE (HID over GATT),
                 e.g. an MX Master 3S (PID B034). Windows names these HID
                 interfaces `...vid&02046d_pid&b034...` (vid&02 = USB-IF vendor
                 id source), not `vid_046d`. The collection must have a vendor
                 usage page (0xFF43, usage 0x0202 commonly, or 0xFF00) and a
                 20-byte output report. The device is addressed with device
                 index 0xFF (what Solaar does for direct Bluetooth devices).
                 Same probe sequence as above, on that single index.

    Target selection: --target auto (default) tries the Bolt receiver first and
    falls back to Bluetooth when no device behind the receiver exposes 0x2121;
    --target bolt / --target bluetooth force one transport.

Why it is read-only BY CONSTRUCTION
    The wheel must never depend on this tool: Logi Options+ owns the wheel mode
    (diversion, resolution, invert, ratchet). So the script is physically unable
    to change device state:
      * Every byte that reaches the wire goes through GuardedChannel.send().
        It is the only caller of the only sending method (a private, name-mangled
        __raw_write holding the single WriteFile call, and, only when the
        channel was built with --use-set-output-report, the single
        HidD_SetOutputReport call that replaces it; there is never a fallback
        from one to the other).
      * send() checks the exact 20 bytes about to be written against an
        explicit allowlist of (feature id, function number) pairs, and raises
        RefusedMessage BEFORE writing anything if the pair is not listed:
            IRoot       0x0000 : fn 0 getFeature, fn 1 getProtocolVersion (ping)
            DEVICE_NAME 0x0005 : fn 0 getCount,   fn 1 getDeviceName
            HIRES_WHEEL 0x2121 : fn 0 getWheelCapability, fn 1 getWheelMode,
                                 fn 3 getRatchetSwitchState
      * HIRES_WHEEL function 2 (setWheelMode) is not in the list, so it cannot
        be sent. No HID++ 1.0 register command (report ids 0x10 / sub-ids 0x80
        and 0x81, 0x82...) can be sent either: only report id 0x11 is accepted.
      * The device index is bound to the transport the channel was OPENED on, a
        property fixed at construction (not an argument of send()):
            receiver  channel: indexes 1..6 only. 0xFF (and 0) address the
                      receiver itself and are refused.
            bluetooth channel: index 0xFF only (the directly connected device).
                      Indexes 1..6 are refused: there is no receiver behind it.
      * Feature indexes are discovered at runtime (IRoot.getFeature) and mapped
        back to feature ids per device index before the allowlist check, so an
        index can never be used for a feature the allowlist does not name.
      * `--self-test` exercises this judge on dummy channels, without opening
        any device.

Usage
    python hidpp_probe.py                    full probe, --target auto
    python hidpp_probe.py --target bluetooth [--pid B034]
    python hidpp_probe.py --target bolt
    python hidpp_probe.py --enumerate-only   only list the 046D HID interfaces
                                             (with their transport)
    python hidpp_probe.py -v                 also print reports that are not
                                             replies to our requests (Options+
                                             traffic, or a reply carrying a
                                             device index other than the one we
                                             addressed...)
    python hidpp_probe.py --use-set-output-report
                                             send the SAME validated frame with
                                             HidD_SetOutputReport instead of
                                             WriteFile (only if WriteFile is
                                             rejected on Bluetooth)
    python hidpp_probe.py --self-test        pure-Python check of the guard,
                                             touches no hardware

Exit codes: 0 done, 1 no matching collection, 2 collection could not be opened,
3 a message was refused by the allowlist, 4 an I/O error (including a write the
OS rejected), 5 self-test failure.
"""

import argparse
import ctypes
import re
import struct
import sys
import time
from ctypes import wintypes

# --------------------------------------------------------------------------
# Constants
# --------------------------------------------------------------------------

TARGET_VID = 0x046D
TARGET_PID = 0xC548          # Logitech Bolt receiver
HIDPP_USAGE_PAGE = 0xFF00    # Bolt receiver long-report collection
# Bluetooth LE: Logitech devices commonly report usage page 0xFF43 (usage
# 0x0202); 0xFF00 is also accepted. Order = preference. UNCERTAIN, see report.
BLE_VENDOR_USAGE_PAGES = (0xFF43, 0xFF00)
DEFAULT_BLE_PID = 0xB034     # MX Master 3S
HIDPP_LONG_OUT_LEN = 20      # report id 0x11 + 19 bytes
REPORT_LONG = 0x11
SW_ID = 0x0B                 # fixed software id, low nibble of the function byte
PING_DATA = 0xA5             # echoed back by IRoot.getProtocolVersion
REQUEST_TIMEOUT_S = 1.0

TRANSPORT_RECEIVER = "receiver"
TRANSPORT_BLUETOOTH = "bluetooth"
TRANSPORT_OTHER = "usb/other"
# Device indexes a channel may address, fixed by the transport it was opened on.
BLE_DEVICE_INDEX = 0xFF
ALLOWED_DEVICE_INDEXES = {
    TRANSPORT_RECEIVER: frozenset(range(1, 7)),
    TRANSPORT_BLUETOOTH: frozenset({BLE_DEVICE_INDEX}),
}
# HID interface path markers (compared lower-case).
PATH_TAG_USB = "vid_046d"        # USB / receiver paths
PATH_TAG_BLE = "vid&02046d"      # Bluetooth HID paths (vid&02 = USB-IF source)

FEATURE_IROOT = 0x0000
FEATURE_DEVICE_NAME = 0x0005
FEATURE_HIRES_WHEEL = 0x2121

# The allowlist. Nothing else can be written. Keep it this short on purpose.
ALLOWED_CALLS = {
    (FEATURE_IROOT, 0): "IRoot.getFeature",
    (FEATURE_IROOT, 1): "IRoot.getProtocolVersion (ping)",
    (FEATURE_DEVICE_NAME, 0): "DEVICE_NAME.getCount",
    (FEATURE_DEVICE_NAME, 1): "DEVICE_NAME.getDeviceName",
    (FEATURE_HIRES_WHEEL, 0): "HIRES_WHEEL.getWheelCapability",
    (FEATURE_HIRES_WHEEL, 1): "HIRES_WHEEL.getWheelMode",
    (FEATURE_HIRES_WHEEL, 3): "HIRES_WHEEL.getRatchetSwitchState",
}
# Only these feature ids may be looked up through IRoot.getFeature.
LOOKUP_FEATURES = (FEATURE_DEVICE_NAME, FEATURE_HIRES_WHEEL)

GENERIC_READ = 0x80000000
GENERIC_WRITE = 0x40000000
FILE_SHARE_READ = 0x1
FILE_SHARE_WRITE = 0x2
OPEN_EXISTING = 3
FILE_FLAG_OVERLAPPED = 0x40000000
DIGCF_PRESENT = 0x2
DIGCF_DEVICEINTERFACE = 0x10
ERROR_INVALID_PARAMETER = 87
ERROR_INSUFFICIENT_BUFFER = 122
ERROR_NO_MORE_ITEMS = 259
ERROR_IO_PENDING = 997
ERROR_INVALID_USER_BUFFER = 1784
# Errors that mean "the OS/driver does not accept this report on this collection".
WRITE_REJECTED_ERRORS = (ERROR_INVALID_PARAMETER, ERROR_INVALID_USER_BUFFER)
WAIT_OBJECT_0 = 0
WAIT_TIMEOUT = 258
HIDP_STATUS_SUCCESS = 0x00110000

# --------------------------------------------------------------------------
# ctypes plumbing
# --------------------------------------------------------------------------

kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
hid = ctypes.WinDLL("hid", use_last_error=True)
setupapi = ctypes.WinDLL("setupapi", use_last_error=True)

HANDLE = ctypes.c_void_p
INVALID_HANDLE_VALUE = ctypes.c_void_p(-1).value
DWORD = wintypes.DWORD


class GUID(ctypes.Structure):
    _fields_ = [("Data1", ctypes.c_ulong), ("Data2", ctypes.c_ushort),
                ("Data3", ctypes.c_ushort), ("Data4", ctypes.c_ubyte * 8)]


class SP_DEVICE_INTERFACE_DATA(ctypes.Structure):
    _fields_ = [("cbSize", DWORD), ("InterfaceClassGuid", GUID),
                ("Flags", DWORD), ("Reserved", ctypes.c_size_t)]


class HIDD_ATTRIBUTES(ctypes.Structure):
    _fields_ = [("Size", ctypes.c_ulong), ("VendorID", ctypes.c_ushort),
                ("ProductID", ctypes.c_ushort), ("VersionNumber", ctypes.c_ushort)]


class HIDP_CAPS(ctypes.Structure):
    _fields_ = [("Usage", ctypes.c_ushort), ("UsagePage", ctypes.c_ushort),
                ("InputReportByteLength", ctypes.c_ushort),
                ("OutputReportByteLength", ctypes.c_ushort),
                ("FeatureReportByteLength", ctypes.c_ushort),
                ("Reserved", ctypes.c_ushort * 17),
                ("NumberLinkCollectionNodes", ctypes.c_ushort),
                ("NumberInputButtonCaps", ctypes.c_ushort),
                ("NumberInputValueCaps", ctypes.c_ushort),
                ("NumberInputDataIndices", ctypes.c_ushort),
                ("NumberOutputButtonCaps", ctypes.c_ushort),
                ("NumberOutputValueCaps", ctypes.c_ushort),
                ("NumberOutputDataIndices", ctypes.c_ushort),
                ("NumberFeatureButtonCaps", ctypes.c_ushort),
                ("NumberFeatureValueCaps", ctypes.c_ushort),
                ("NumberFeatureDataIndices", ctypes.c_ushort)]


class OVERLAPPED(ctypes.Structure):
    _fields_ = [("Internal", ctypes.c_size_t), ("InternalHigh", ctypes.c_size_t),
                ("Offset", DWORD), ("OffsetHigh", DWORD), ("hEvent", HANDLE)]


def _sig(func, restype, argtypes):
    func.restype = restype
    func.argtypes = argtypes


_sig(kernel32.CreateFileW, HANDLE,
     [wintypes.LPCWSTR, DWORD, DWORD, ctypes.c_void_p, DWORD, DWORD, HANDLE])
_sig(kernel32.CloseHandle, wintypes.BOOL, [HANDLE])
_sig(kernel32.CreateEventW, HANDLE,
     [ctypes.c_void_p, wintypes.BOOL, wintypes.BOOL, wintypes.LPCWSTR])
_sig(kernel32.WaitForSingleObject, DWORD, [HANDLE, DWORD])
_sig(kernel32.ReadFile, wintypes.BOOL,
     [HANDLE, ctypes.c_void_p, DWORD, ctypes.POINTER(DWORD), ctypes.POINTER(OVERLAPPED)])
_sig(kernel32.WriteFile, wintypes.BOOL,
     [HANDLE, ctypes.c_void_p, DWORD, ctypes.POINTER(DWORD), ctypes.POINTER(OVERLAPPED)])
_sig(kernel32.GetOverlappedResult, wintypes.BOOL,
     [HANDLE, ctypes.POINTER(OVERLAPPED), ctypes.POINTER(DWORD), wintypes.BOOL])
_sig(kernel32.CancelIoEx, wintypes.BOOL, [HANDLE, ctypes.POINTER(OVERLAPPED)])

_sig(setupapi.SetupDiGetClassDevsW, HANDLE,
     [ctypes.POINTER(GUID), wintypes.LPCWSTR, ctypes.c_void_p, DWORD])
_sig(setupapi.SetupDiEnumDeviceInterfaces, wintypes.BOOL,
     [HANDLE, ctypes.c_void_p, ctypes.POINTER(GUID), DWORD,
      ctypes.POINTER(SP_DEVICE_INTERFACE_DATA)])
_sig(setupapi.SetupDiGetDeviceInterfaceDetailW, wintypes.BOOL,
     [HANDLE, ctypes.POINTER(SP_DEVICE_INTERFACE_DATA), ctypes.c_void_p, DWORD,
      ctypes.POINTER(DWORD), ctypes.c_void_p])
_sig(setupapi.SetupDiDestroyDeviceInfoList, wintypes.BOOL, [HANDLE])

_sig(hid.HidD_GetHidGuid, None, [ctypes.POINTER(GUID)])
_sig(hid.HidD_GetAttributes, ctypes.c_ubyte, [HANDLE, ctypes.POINTER(HIDD_ATTRIBUTES)])
_sig(hid.HidD_GetProductString, ctypes.c_ubyte, [HANDLE, ctypes.c_void_p, ctypes.c_ulong])
_sig(hid.HidD_GetPreparsedData, ctypes.c_ubyte, [HANDLE, ctypes.POINTER(ctypes.c_void_p)])
_sig(hid.HidD_FreePreparsedData, ctypes.c_ubyte, [ctypes.c_void_p])
_sig(hid.HidP_GetCaps, ctypes.c_long, [ctypes.c_void_p, ctypes.POINTER(HIDP_CAPS)])
# Only reachable through GuardedChannel.__raw_write, with --use-set-output-report.
_sig(hid.HidD_SetOutputReport, ctypes.c_ubyte, [HANDLE, ctypes.c_void_p, ctypes.c_ulong])


def last_error():
    return ctypes.get_last_error()


def win_error_text(code):
    return f"Win32 error {code} (0x{code:08X}): {ctypes.FormatError(code).strip()}"


def hexs(data):
    return " ".join(f"{b:02X}" for b in data)


def fmt_index(device_index):
    return str(device_index) if device_index <= 6 else f"0x{device_index:02X}"


def open_path(path, access, flags):
    h = kernel32.CreateFileW(path, access, FILE_SHARE_READ | FILE_SHARE_WRITE,
                             None, OPEN_EXISTING, flags, None)
    if h is None or h == INVALID_HANDLE_VALUE:
        return None, last_error()
    return h, 0


# --------------------------------------------------------------------------
# HID interface enumeration (zero-access opens: works while Options+ holds
# the devices; we only ask the HID class driver for attributes and caps)
# --------------------------------------------------------------------------

class HidInfo:
    def __init__(self, path):
        self.path = path
        self.vid = self.pid = None
        self.product = None
        self.usage_page = self.usage = None
        self.in_len = self.out_len = self.feat_len = None
        self.error = None
        self.transport = TRANSPORT_OTHER


def pid_from_path(path):
    """Fallback PID taken from the interface path (`pid_c548` or `pid&b034`)."""
    m = re.search(r"pid[_&]([0-9a-f]{4})", path.lower())
    return int(m.group(1), 16) if m else None


def transport_of(path, pid):
    """Classify an interface: bluetooth by path, receiver by the Bolt PID."""
    if PATH_TAG_BLE in path.lower():
        return TRANSPORT_BLUETOOTH
    if pid == TARGET_PID:
        return TRANSPORT_RECEIVER
    return TRANSPORT_OTHER


def probe_interface(path):
    info = HidInfo(path)
    h, err = open_path(path, 0, 0)
    if h is None:
        info.error = f"open(0 access) failed: {win_error_text(err)}"
    else:
        try:
            attrs = HIDD_ATTRIBUTES()
            attrs.Size = ctypes.sizeof(attrs)
            if hid.HidD_GetAttributes(h, ctypes.byref(attrs)):
                info.vid, info.pid = attrs.VendorID, attrs.ProductID
            buf = ctypes.create_unicode_buffer(128)
            if hid.HidD_GetProductString(h, buf, ctypes.sizeof(buf)):
                info.product = buf.value
            ppd = ctypes.c_void_p()
            if hid.HidD_GetPreparsedData(h, ctypes.byref(ppd)):
                try:
                    caps = HIDP_CAPS()
                    if hid.HidP_GetCaps(ppd, ctypes.byref(caps)) == HIDP_STATUS_SUCCESS:
                        info.usage_page, info.usage = caps.UsagePage, caps.Usage
                        info.in_len = caps.InputReportByteLength
                        info.out_len = caps.OutputReportByteLength
                        info.feat_len = caps.FeatureReportByteLength
                finally:
                    hid.HidD_FreePreparsedData(ppd)
        finally:
            kernel32.CloseHandle(h)
    # Even when the open failed the path still tells us the PID and the transport.
    if info.pid is None:
        info.pid = pid_from_path(path)
    info.transport = transport_of(path, info.pid)
    return info


def enumerate_logitech():
    guid = GUID()
    hid.HidD_GetHidGuid(ctypes.byref(guid))
    hdev = setupapi.SetupDiGetClassDevsW(ctypes.byref(guid), None, None,
                                         DIGCF_PRESENT | DIGCF_DEVICEINTERFACE)
    if hdev is None or hdev == INVALID_HANDLE_VALUE:
        raise OSError(win_error_text(last_error()))
    infos = []
    try:
        index = 0
        while True:
            ifd = SP_DEVICE_INTERFACE_DATA()
            ifd.cbSize = ctypes.sizeof(ifd)
            if not setupapi.SetupDiEnumDeviceInterfaces(hdev, None, ctypes.byref(guid),
                                                        index, ctypes.byref(ifd)):
                err = last_error()
                if err == ERROR_NO_MORE_ITEMS:
                    break
                raise OSError(win_error_text(err))
            index += 1
            required = DWORD(0)
            # First call only asks for the needed size; it fails by design.
            setupapi.SetupDiGetDeviceInterfaceDetailW(hdev, ctypes.byref(ifd), None, 0,
                                                      ctypes.byref(required), None)
            if required.value == 0:
                continue
            buf = ctypes.create_string_buffer(required.value)
            # SP_DEVICE_INTERFACE_DETAIL_DATA_W: DWORD cbSize, then WCHAR path[].
            # cbSize is the size of the fixed part: 8 on 64-bit, 6 on 32-bit.
            struct.pack_into("<I", buf, 0, 8 if ctypes.sizeof(ctypes.c_void_p) == 8 else 6)
            if not setupapi.SetupDiGetDeviceInterfaceDetailW(
                    hdev, ctypes.byref(ifd), buf, required.value, None, None):
                continue
            path = ctypes.wstring_at(ctypes.addressof(buf) + 4)
            lowered = path.lower()
            # USB / receiver paths carry `vid_046d`; Bluetooth HID paths carry
            # `vid&02046d` (USB-IF vendor id source) instead.
            if PATH_TAG_USB not in lowered and PATH_TAG_BLE not in lowered:
                continue
            infos.append(probe_interface(path))
    finally:
        setupapi.SetupDiDestroyDeviceInfoList(hdev)
    return infos


def fmt_hex(value, width):
    return "?" if value is None else f"0x{value:0{width}X}"


def print_interfaces(infos):
    print(f"=== 046D HID interfaces: {len(infos)} found ===")
    for n, i in enumerate(infos):
        print(f"[{n}] {i.path}")
        print(f"     transport={i.transport}  "
              f"VID:PID={fmt_hex(i.vid, 4)}:{fmt_hex(i.pid, 4)}  "
              f"product={i.product!r}")
        print(f"     usage page={fmt_hex(i.usage_page, 4)} usage={fmt_hex(i.usage, 4)}  "
              f"input report len={i.in_len}  output report len={i.out_len}  "
              f"feature report len={i.feat_len}")
        if i.error:
            print(f"     !! {i.error}")


# --------------------------------------------------------------------------
# The guarded channel: the ONLY place that can write to the device
# --------------------------------------------------------------------------

class RefusedMessage(Exception):
    """Raised by GuardedChannel.send() before anything is written."""


class WriteRejected(OSError):
    """The OS/driver refused the (already validated) report itself."""


class Reply:
    def __init__(self, raw, kind, error_code=None):
        self.raw = bytes(raw)
        self.kind = kind                 # "ok" | "hidpp2-error" | "hidpp1-error"
        self.error_code = error_code

    @property
    def is_error(self):
        return self.kind != "ok"

    @property
    def params(self):
        return self.raw[4:]


HIDPP2_ERRORS = {0x00: "NO_ERROR", 0x01: "UNKNOWN", 0x02: "INVALID_ARGUMENT",
                 0x03: "OUT_OF_RANGE", 0x04: "HW_ERROR", 0x05: "LOGITECH_INTERNAL",
                 0x06: "INVALID_FEATURE_INDEX", 0x07: "INVALID_FUNCTION_ID",
                 0x08: "BUSY", 0x09: "UNSUPPORTED"}
HIDPP1_ERRORS = {0x00: "SUCCESS", 0x01: "INVALID_SUBID", 0x02: "INVALID_ADDRESS",
                 0x03: "INVALID_VALUE", 0x04: "CONNECT_FAIL", 0x05: "TOO_MANY_DEVICES",
                 0x06: "ALREADY_EXISTS", 0x07: "BUSY", 0x08: "UNKNOWN_DEVICE",
                 0x09: "RESOURCE_ERROR", 0x0A: "REQUEST_UNAVAILABLE",
                 0x0B: "INVALID_PARAM_VALUE", 0x0C: "WRONG_PIN_CODE"}


def build_frame(device_index, feature_index, function, params=b"", sw_id=SW_ID):
    """The 20 bytes of a HID++ 2.0 long request. Pure: sends nothing."""
    frame = bytearray(HIDPP_LONG_OUT_LEN)
    frame[0] = REPORT_LONG
    frame[1] = device_index
    frame[2] = feature_index
    frame[3] = ((function << 4) | sw_id) & 0xFF
    frame[4:4 + len(params)] = params
    return bytes(frame)


class GuardedChannel:
    """Overlapped HID I/O on a HID++ long-report collection.

    `transport` (receiver | bluetooth) is fixed at construction and decides
    which device indexes _validate_frame() accepts; there is no way to change
    it afterwards through the public surface (read-only property).

    Public surface: register_feature(), request(), close(). The raw write is a
    name-mangled private method called from send() and nowhere else.
    """

    def __init__(self, handle, in_len, out_len, transport, verbose=False,
                 use_set_output_report=False):
        if transport not in ALLOWED_DEVICE_INDEXES:
            raise ValueError(f"unknown transport {transport!r}")
        self._h = handle
        self._in_len = in_len
        self._out_len = out_len
        self._transport = transport
        self._allowed_indexes = ALLOWED_DEVICE_INDEXES[transport]
        self._verbose = verbose
        self._use_set_output_report = bool(use_set_output_report)
        self._features = {}              # (device_index, feature_index) -> feature id
        self.ignored_reports = 0
        # Read side: one persistent overlapped read, re-armed only after it
        # completed, so a timeout never loses or double-reads a report.
        self._rbuf = ctypes.create_string_buffer(in_len)
        self._rov = OVERLAPPED()
        self._revt = kernel32.CreateEventW(None, True, False, None)   # manual reset
        self._rov.hEvent = self._revt
        self._read_pending = False
        # Write side.
        self._wevt = kernel32.CreateEventW(None, True, False, None)
        self._closed = False

    @property
    def transport(self):
        return self._transport

    # ---- feature index <-> id mapping (discovered at runtime) -------------

    def register_feature(self, device_index, feature_index, feature_id):
        if (feature_id not in LOOKUP_FEATURES or not (1 <= feature_index <= 0xFE)
                or device_index not in self._allowed_indexes):
            raise RefusedMessage(f"refusing to register feature 0x{feature_id:04X} "
                                 f"at index {feature_index} on device {device_index}")
        key = (device_index, feature_index)
        if self._features.get(key, feature_id) != feature_id:
            raise RefusedMessage(f"feature index {feature_index} on device {device_index} "
                                 f"already mapped to 0x{self._features[key]:04X}")
        self._features[key] = feature_id

    def _feature_id_of(self, device_index, feature_index):
        if feature_index == 0x00:
            return FEATURE_IROOT                       # IRoot is always index 0
        return self._features.get((device_index, feature_index))

    # ---- the choke point --------------------------------------------------

    def _validate_frame(self, frame):
        """Judge the exact bytes about to be written. Raises RefusedMessage."""
        if len(frame) != HIDPP_LONG_OUT_LEN or frame[0] != REPORT_LONG:
            raise RefusedMessage("only 20-byte long reports (id 0x11) are allowed")
        device_index, feature_index, fn_sw = frame[1], frame[2], frame[3]
        params = bytes(frame[4:])
        if device_index not in self._allowed_indexes:
            if self._transport == TRANSPORT_BLUETOOTH:
                raise RefusedMessage(
                    f"device index 0x{device_index:02X} not allowed on a Bluetooth "
                    f"channel (0x{BLE_DEVICE_INDEX:02X} only: the directly connected device)")
            raise RefusedMessage(
                f"device index 0x{device_index:02X} not allowed on a receiver channel "
                "(1..6 only; 0x00 and 0xFF address the receiver itself)")
        if fn_sw & 0x0F != SW_ID:
            raise RefusedMessage(f"software id must be 0x{SW_ID:X}")
        function = fn_sw >> 4
        feature_id = self._feature_id_of(device_index, feature_index)
        if feature_id is None:
            raise RefusedMessage(f"feature index {feature_index} on device {device_index} "
                                 "was not discovered through IRoot.getFeature")
        if (feature_id, function) not in ALLOWED_CALLS:
            raise RefusedMessage(f"(feature 0x{feature_id:04X}, function {function}) is not "
                                 "in the allowlist")
        # Parameter shape per allowed call: nothing exotic can ride along.
        if (feature_id, function) == (FEATURE_IROOT, 0):
            ok = (struct.unpack(">H", params[:2])[0] in LOOKUP_FEATURES
                  and not any(params[2:]))
        elif (feature_id, function) == (FEATURE_IROOT, 1):
            ok = params[0] == 0 and params[1] == 0 and not any(params[3:])
        elif (feature_id, function) == (FEATURE_DEVICE_NAME, 1):
            ok = not any(params[1:])                    # params[0] = name offset
        else:
            ok = not any(params)                        # all remaining calls: no params
        if not ok:
            raise RefusedMessage(f"unexpected parameters for "
                                 f"{ALLOWED_CALLS[(feature_id, function)]}: {hexs(params)}")
        return ALLOWED_CALLS[(feature_id, function)]

    def send(self, device_index, feature_index, function, params=b""):
        """Build, validate, then write one HID++ 2.0 long request."""
        if not (0 <= function <= 0x0F) or len(params) > HIDPP_LONG_OUT_LEN - 4:
            raise RefusedMessage("malformed function number or parameters")
        frame = build_frame(device_index, feature_index, function, params)
        name = self._validate_frame(frame)             # raises BEFORE any write
        print(f"  TX {hexs(frame)}   ; {name}")
        self.__raw_write(frame)                        # the only write in this file
        return name

    def __raw_write(self, frame):
        """The only sender. Byte 0 of `frame` is the report id (0x11), 20 bytes
        in total, exactly what WriteFile / HidD_SetOutputReport expect on a
        HID collection whose output report is numbered."""
        buf = ctypes.create_string_buffer(frame, len(frame))
        if self._use_set_output_report:
            # Same validated frame, other API; chosen once at construction by
            # --use-set-output-report, never as a fallback after a failure.
            if not hid.HidD_SetOutputReport(self._h, buf, len(frame)):
                self.__fail_write("HidD_SetOutputReport", last_error())
            return
        ov = OVERLAPPED()
        ov.hEvent = self._wevt
        written = DWORD(0)
        ok = kernel32.WriteFile(self._h, buf, len(frame), ctypes.byref(written),
                                ctypes.byref(ov))
        if not ok:
            err = last_error()
            if err != ERROR_IO_PENDING:
                self.__fail_write("WriteFile", err)
            if kernel32.WaitForSingleObject(self._wevt, int(REQUEST_TIMEOUT_S * 1000)) \
                    != WAIT_OBJECT_0:
                kernel32.CancelIoEx(self._h, ctypes.byref(ov))
                kernel32.GetOverlappedResult(self._h, ctypes.byref(ov),
                                             ctypes.byref(written), True)
                raise TimeoutError("WriteFile timed out (cancelled)")
        if not kernel32.GetOverlappedResult(self._h, ctypes.byref(ov),
                                            ctypes.byref(written), True):
            self.__fail_write("WriteFile (completion)", last_error())
        if written.value != len(frame):
            raise OSError(f"short write: {written.value}/{len(frame)} bytes")

    def __fail_write(self, api, err):
        """Raise the right exception for a failed send. Never retries."""
        if err in WRITE_REJECTED_ERRORS:
            hint = ("" if self._use_set_output_report else
                    " Re-run with --use-set-output-report to send the same frame "
                    "through HidD_SetOutputReport (nothing is tried automatically).")
            raise WriteRejected(
                f"{api} rejected the 20-byte report (id 0x{REPORT_LONG:02X}) on this "
                f"{self._transport} collection: {win_error_text(err)}. The OS or the "
                f"driver does not accept this report on this collection (wrong "
                f"collection, or the report is not an output report there).{hint}")
        raise OSError(f"{api} failed: {win_error_text(err)}")

    # ---- reading ----------------------------------------------------------

    def _read_report(self, timeout_s):
        """Return one report (bytes) or None on timeout. A timed-out read stays
        pending and is reused by the next call."""
        if not self._read_pending:
            self._rov.Internal = self._rov.InternalHigh = 0
            self._rov.Offset = self._rov.OffsetHigh = 0
            n = DWORD(0)
            ok = kernel32.ReadFile(self._h, self._rbuf, self._in_len, ctypes.byref(n),
                                   ctypes.byref(self._rov))
            if not ok and last_error() != ERROR_IO_PENDING:
                raise OSError(f"ReadFile failed: {win_error_text(last_error())}")
            self._read_pending = True      # also true if it completed synchronously
        rc = kernel32.WaitForSingleObject(self._revt, max(0, int(timeout_s * 1000)))
        if rc == WAIT_TIMEOUT:
            return None
        if rc != WAIT_OBJECT_0:
            raise OSError(f"WaitForSingleObject returned {rc}")
        n = DWORD(0)
        ok = kernel32.GetOverlappedResult(self._h, ctypes.byref(self._rov),
                                          ctypes.byref(n), False)
        self._read_pending = False
        if not ok:
            raise OSError(f"ReadFile completion failed: {win_error_text(last_error())}")
        return bytes(self._rbuf.raw[:n.value])

    @staticmethod
    def _match(report, device_index, feature_index, function):
        """Classify a report as the reply to our request, or None.

        The reply must carry the SAME device index we addressed (the echoed
        index). A reply that arrives with another index is not matched; with -v
        it is printed as ignored so it can be inspected."""
        if len(report) < 7 or report[1] != device_index:
            return None
        fn_sw = (function << 4) | SW_ID
        if report[0] == REPORT_LONG and report[2] == feature_index and report[3] == fn_sw:
            return Reply(report, "ok")
        # Errors: [id, idx, 0xFF (2.0) | 0x8F (1.0), our feature index, our fn|sw, code]
        # (a 1.0 error may arrive as a short 0x10 report, so accept that id too)
        if report[0] in (0x10, REPORT_LONG) and report[2] in (0xFF, 0x8F) \
                and report[3] == feature_index and report[4] == fn_sw:
            return Reply(report, "hidpp2-error" if report[2] == 0xFF else "hidpp1-error",
                         report[5])
        return None

    def request(self, device_index, feature_index, function, params=b""):
        """send() + wait for the matching reply. None means no answer in time."""
        self.send(device_index, feature_index, function, params)
        ignored_before = self.ignored_reports
        deadline = time.monotonic() + REQUEST_TIMEOUT_S
        while True:
            remaining = deadline - time.monotonic()
            report = self._read_report(remaining) if remaining > 0 else None
            if report is None:
                ignored = self.ignored_reports - ignored_before
                note = ""
                if ignored and not self._verbose:
                    note = (f"; {ignored} unrelated report(s) were received and "
                            "ignored - re-run with -v to see them")
                print(f"  RX (timeout, no reply{note})")
                return None
            reply = self._match(report, device_index, feature_index, function)
            if reply is None:
                self.ignored_reports += 1
                if self._verbose:
                    why = ("different device index "
                           f"0x{report[1]:02X} (expected 0x{device_index:02X})"
                           if len(report) >= 2 and report[1] != device_index
                           else "not the reply to this request")
                    print(f"  (ignored: {why}) {hexs(report)}")
                continue
            print(f"  RX {hexs(report)}")
            return reply

    # ---- cleanup ----------------------------------------------------------

    def close(self):
        if self._closed:
            return
        self._closed = True
        if self._read_pending:
            kernel32.CancelIoEx(self._h, ctypes.byref(self._rov))
            n = DWORD(0)
            # Wait until the cancelled read is really finished: the buffer and
            # the OVERLAPPED must stay alive until then.
            kernel32.GetOverlappedResult(self._h, ctypes.byref(self._rov),
                                         ctypes.byref(n), True)
            self._read_pending = False
        kernel32.CloseHandle(self._revt)
        kernel32.CloseHandle(self._wevt)
        if self._h is not None:                       # None = dummy handle (self-test)
            kernel32.CloseHandle(self._h)


# --------------------------------------------------------------------------
# Decoding
# --------------------------------------------------------------------------

def describe_error(reply):
    table = HIDPP2_ERRORS if reply.kind == "hidpp2-error" else HIDPP1_ERRORS
    style = "HID++ 2.0 error (0xFF)" if reply.kind == "hidpp2-error" else \
        "HID++ 1.0 error (0x8F)"
    code = reply.error_code
    return f"{style}, code 0x{code:02X} = {table.get(code, 'unknown')}  -> no answer from this index"


def decode_feature_type(flags):
    names = [(0x80, "obsolete"), (0x40, "hidden"), (0x20, "engineering"),
             (0x10, "manuf-deactivatable"), (0x08, "compliance-deactivatable")]
    on = [n for bit, n in names if flags & bit]
    return f"0x{flags:02X}" + (f" [{', '.join(on)}]" if on else " [none]")


def decode_capability(p):
    multiplier, flags, ratchets, diameter = p[0], p[1], p[2], p[3]
    set_bits = [str(b) for b in range(8) if flags & (1 << b)] or ["none"]
    return (f"multiplier={multiplier} high-res ticks per ratchet; "
            f"flags=0b{flags:08b} (set bits: {', '.join(set_bits)}; expected meanings "
            f"'has invert' / 'has ratchet switch', bit positions UNVERIFIED); "
            f"ratchets/rotation={ratchets} (uncertain); diameter={diameter} (uncertain, "
            f"unit unknown)")


def decode_mode(p):
    b = p[0]
    return (f"mode=0b{b:08b}: bit0 target={'HID++ (DIVERTED)' if b & 1 else 'HID (native)'}, "
            f"bit1 resolution={'HIGH-RES' if b & 2 else 'low-res'}, "
            f"bit2 invert={'ON' if b & 4 else 'off'}"
            + (f", other bits=0b{b & ~0x07:08b} (unknown)" if b & ~0x07 else ""))


def decode_ratchet(p):
    b = p[0]
    state = "RATCHET" if b & 1 else "free-spin (value 0 or 2; polarity from memory, uncertain)"
    return f"state byte=0x{b:02X}: bit0 -> {state}"


# --------------------------------------------------------------------------
# Probe
# --------------------------------------------------------------------------

def call(chan, dev, feature_index, function, params=b""):
    """request(); on error reply print the code and return None."""
    reply = chan.request(dev, feature_index, function, params)
    if reply is None:
        return None
    if reply.is_error:
        print(f"  => {describe_error(reply)}")
        return None
    return reply


def discover(chan, dev, feature_id):
    reply = call(chan, dev, 0x00, 0, struct.pack(">H", feature_id))
    if reply is None:
        return None
    p = reply.params
    index, ftype, version = p[0], p[1], p[2]
    if index == 0:
        print(f"  => feature 0x{feature_id:04X}: not supported by this device")
        return None
    print(f"  => feature 0x{feature_id:04X}: index={index} type={decode_feature_type(ftype)} "
          f"version={version}")
    chan.register_feature(dev, index, feature_id)
    return index


def read_name(chan, dev, index):
    reply = call(chan, dev, index, 0)
    if reply is None:
        return None
    count = reply.params[0]
    print(f"  => name length = {count}")
    name = bytearray()
    while len(name) < count:
        reply = call(chan, dev, index, 1, bytes([len(name)]))
        if reply is None:
            return None
        chunk = reply.params[:16].split(b"\x00")[0][:count - len(name)]
        if not chunk:
            break
        name += chunk
    return name.decode("utf-8", errors="replace")


def probe_device(chan, dev):
    """Probe one device index. Returns True when the device exposes 0x2121."""
    print(f"\n--- device_index {fmt_index(dev)} ({chan.transport}) ---")
    reply = call(chan, dev, 0x00, 1, bytes([0, 0, PING_DATA]))
    if reply is None:
        return False
    p = reply.params
    echo = "echo OK" if p[2] == PING_DATA else f"echo MISMATCH (got 0x{p[2]:02X})"
    print(f"  => protocol {p[0]}.{p[1]}, {echo}")

    name_index = discover(chan, dev, FEATURE_DEVICE_NAME)
    if name_index is not None:
        name = read_name(chan, dev, name_index)
        print(f"  => device name: {name!r}")

    wheel_index = discover(chan, dev, FEATURE_HIRES_WHEEL)
    if wheel_index is None:
        return False
    for fn, label, decoder in ((0, "getWheelCapability", decode_capability),
                               (1, "getWheelMode", decode_mode),
                               (3, "getRatchetSwitchState", decode_ratchet)):
        print(f"  [{label}]")
        reply = call(chan, dev, wheel_index, fn)
        if reply is not None:
            print(f"  => {decoder(reply.params)}")
    return True


# --------------------------------------------------------------------------
# Target selection
# --------------------------------------------------------------------------

def select_long_collection(infos):
    """Bolt receiver: usage page 0xFF00, 20-byte output report."""
    cands = [i for i in infos
             if i.vid == TARGET_VID and i.pid == TARGET_PID
             and i.usage_page == HIDPP_USAGE_PAGE and i.out_len == HIDPP_LONG_OUT_LEN]
    cands.sort(key=lambda i: i.usage != 0x0002)       # prefer usage 0x0002 if several
    return cands


def select_bluetooth_collection(infos, wanted_pid=None):
    """Bluetooth: a Logitech BLE HID collection with a vendor usage page and a
    20-byte output report. PID B034 (MX Master 3S) first, 0xFF43 before 0xFF00;
    `wanted_pid` restricts the choice to one PID."""
    cands = [i for i in infos
             if i.transport == TRANSPORT_BLUETOOTH and i.vid in (None, TARGET_VID)
             and i.usage_page in BLE_VENDOR_USAGE_PAGES
             and i.out_len == HIDPP_LONG_OUT_LEN]
    if wanted_pid is not None:
        cands = [i for i in cands if i.pid == wanted_pid]
    cands.sort(key=lambda i: (i.pid != DEFAULT_BLE_PID,
                              BLE_VENDOR_USAGE_PAGES.index(i.usage_page), i.path))
    return cands


def end_session(chan, devices):
    """Probe `devices` on `chan`, always close it. Returns (exit code, found)."""
    found = False
    rc = 0
    try:
        for dev in devices:
            if probe_device(chan, dev):
                found = True
    except RefusedMessage as exc:
        print(f"\nREFUSED by the allowlist (nothing was written): {exc}")
        rc = 3
    except WriteRejected as exc:
        print(f"\nWRITE REJECTED BY THE OS (stopping, no alternative API was tried): {exc}")
        rc = 4
    except OSError as exc:
        print(f"\nI/O error (stopping): {exc}")
        rc = 4
    finally:
        chan.close()
    print(f"\nDone ({chan.transport}). Reports ignored (not replies to us): "
          f"{chan.ignored_reports}")
    return rc, found


def run_bolt(infos, args):
    """Returns (exit code, wheel found). Codes 1/2: transport unavailable."""
    cands = select_long_collection(infos)
    if not cands:
        print(f"\nNo interface matches VID {TARGET_VID:04X} PID {TARGET_PID:04X} "
              f"usage page 0x{HIDPP_USAGE_PAGE:04X} output length {HIDPP_LONG_OUT_LEN}. "
              "(Bolt receiver not available.)")
        return 1, False
    if len(cands) > 1:
        print(f"\nWarning: {len(cands)} interfaces match; using the first (usage 0x0002 preferred).")
    chosen = cands[0]
    print(f"\nSelected HID++ long collection (receiver): {chosen.path}")
    print(f"Software id (sw_id) used in every request: 0x{SW_ID:02X}")

    handle, err = open_path(chosen.path, GENERIC_READ | GENERIC_WRITE, FILE_FLAG_OVERLAPPED)
    if handle is None:
        print(f"\nCannot open the collection shared read/write: {win_error_text(err)}")
        print("Stopping: this is itself a result (the handle is not shareable while "
              "Options+ holds it).")
        return 2, False

    chan = GuardedChannel(handle, chosen.in_len, chosen.out_len, TRANSPORT_RECEIVER,
                          args.verbose, args.use_set_output_report)
    return end_session(chan, range(1, 7))


def run_bluetooth(infos, args):
    """Returns (exit code, wheel found). Codes 1/2: transport unavailable."""
    cands = select_bluetooth_collection(infos, args.pid)
    if not cands:
        wanted = f" PID {args.pid:04X}" if args.pid is not None else ""
        print(f"\nNo Bluetooth interface matches VID 046D{wanted}, vendor usage page "
              f"({' or '.join(f'0x{p:04X}' for p in BLE_VENDOR_USAGE_PAGES)}) and output "
              f"length {HIDPP_LONG_OUT_LEN}. (Bluetooth device not available.)")
        ble = [i for i in infos if i.transport == TRANSPORT_BLUETOOTH]
        if ble:
            print("Bluetooth interfaces seen (see --enumerate-only for details):")
            for i in ble:
                print(f"  PID={fmt_hex(i.pid, 4)} usage page={fmt_hex(i.usage_page, 4)} "
                      f"usage={fmt_hex(i.usage, 4)} output len={i.out_len}")
        return 1, False
    chosen = cands[0]
    if len(cands) > 1:
        print(f"\nWarning: {len(cands)} Bluetooth collections match; using the first "
              f"(PID {DEFAULT_BLE_PID:04X} preferred, then usage page 0xFF43, or --pid):")
        for i in cands:
            print(f"  PID={fmt_hex(i.pid, 4)} usage page={fmt_hex(i.usage_page, 4)} "
                  f"usage={fmt_hex(i.usage, 4)} {i.path}")
    print(f"\nSelected HID++ long collection (bluetooth): {chosen.path}")
    print(f"  PID={fmt_hex(chosen.pid, 4)} product={chosen.product!r} "
          f"usage page={fmt_hex(chosen.usage_page, 4)} usage={fmt_hex(chosen.usage, 4)} "
          f"input len={chosen.in_len} output len={chosen.out_len}")
    print("  (vendor usage page 0xFF43 / usage 0x0202 is the usual Logitech BLE layout; "
          "this is UNCERTAIN - judge by whether the device answers)")
    print(f"Device index used: 0x{BLE_DEVICE_INDEX:02X} (direct device). "
          f"Software id (sw_id) used in every request: 0x{SW_ID:02X}")
    if not chosen.in_len or chosen.in_len < 7:
        print(f"Input report length is {chosen.in_len}: no reply could be read. Stopping.")
        return 1, False

    handle, err = open_path(chosen.path, GENERIC_READ | GENERIC_WRITE, FILE_FLAG_OVERLAPPED)
    if handle is None:
        print(f"\nCannot open the collection shared read/write: {win_error_text(err)}")
        print("Stopping: this is itself a result (the OS or Options+ may own this handle).")
        return 2, False

    chan = GuardedChannel(handle, chosen.in_len, chosen.out_len, TRANSPORT_BLUETOOTH,
                          args.verbose, args.use_set_output_report)
    return end_session(chan, (BLE_DEVICE_INDEX,))


# --------------------------------------------------------------------------
# Self-test: exercises the guard on dummy channels. Opens NO device.
# --------------------------------------------------------------------------

def run_self_test():
    passed = failed = 0

    def check(label, ok):
        nonlocal passed, failed
        if ok:
            passed += 1
        else:
            failed += 1
        print(f"  {'PASS' if ok else 'FAIL'}  {label}")

    def tripwire(frame):
        raise AssertionError("the raw write was reached")

    def make(transport):
        # Dummy handle (None). The raw write is replaced by a tripwire on this
        # instance: if anything in the test ever reached it, the test fails.
        chan = GuardedChannel(None, HIDPP_LONG_OUT_LEN, HIDPP_LONG_OUT_LEN, transport)
        chan._GuardedChannel__raw_write = tripwire
        return chan

    def refused(chan, frame):
        try:
            chan._validate_frame(frame)
        except RefusedMessage:
            return True
        return False

    def refused_by_send(chan, dev, fidx, fn, params=b""):
        """Through the public send(): must raise RefusedMessage, not reach the write."""
        try:
            chan.send(dev, fidx, fn, params)
        except RefusedMessage:
            return True
        except AssertionError:
            return False
        return False

    NAME_IDX, WHEEL_IDX = 4, 7
    ok_params = {(FEATURE_IROOT, 0): struct.pack(">H", FEATURE_DEVICE_NAME),
                 (FEATURE_IROOT, 1): bytes([0, 0, PING_DATA]),
                 (FEATURE_DEVICE_NAME, 1): bytes([0])}
    feature_index = {FEATURE_IROOT: 0, FEATURE_DEVICE_NAME: NAME_IDX,
                     FEATURE_HIRES_WHEEL: WHEEL_IDX}

    for transport, dev, other_devs in (
            (TRANSPORT_RECEIVER, 1, (0, 7, 0xFF, 0xFE)),
            (TRANSPORT_BLUETOOTH, BLE_DEVICE_INDEX, (0, 1, 2, 6, 7, 0xFE))):
        print(f"\n[{transport} channel, device index {fmt_index(dev)}]")
        chan = make(transport)
        chan.register_feature(dev, NAME_IDX, FEATURE_DEVICE_NAME)
        chan.register_feature(dev, WHEEL_IDX, FEATURE_HIRES_WHEEL)
        check("transport is exposed read-only",
              chan.transport == transport and _assign_fails(chan))

        # The 7 allowed calls pass (and nothing else is reported as allowed).
        names = []
        for (fid, fn) in ALLOWED_CALLS:
            frame = build_frame(dev, feature_index[fid], fn, ok_params.get((fid, fn), b""))
            try:
                names.append(chan._validate_frame(frame))
            except RefusedMessage as exc:
                names.append(f"REFUSED: {exc}")
        check("the 7 allowed calls pass", names == list(ALLOWED_CALLS.values()))
        # getFeature for the second lookup id also passes.
        check("IRoot.getFeature(0x2121) passes",
              not refused(chan, build_frame(dev, 0, 0, struct.pack(">H", FEATURE_HIRES_WHEEL))))

        # Exhaustive over function numbers: only the allowlisted ones pass.
        for fid in (FEATURE_IROOT, FEATURE_DEVICE_NAME, FEATURE_HIRES_WHEEL):
            passing = {fn for fn in range(16)
                       if not refused(chan, build_frame(dev, feature_index[fid], fn,
                                                        ok_params.get((fid, fn), b"")))}
            expected = {fn for (f, fn) in ALLOWED_CALLS if f == fid}
            check(f"feature 0x{fid:04X}: functions that pass == {sorted(expected)}",
                  passing == expected)
        check("HIRES_WHEEL fn 2 (setWheelMode) refused",
              refused(chan, build_frame(dev, WHEEL_IDX, 2))
              and refused(chan, build_frame(dev, WHEEL_IDX, 2, bytes([0x07])))
              and refused_by_send(chan, dev, WHEEL_IDX, 2))

        # Device index: only this transport's index passes, over all 256 values.
        passing = {d for d in range(256) if not refused(chan, build_frame(d, 0, 1, ok_params[(0, 1)]))}
        check(f"device indexes accepted on this channel == {sorted(ALLOWED_DEVICE_INDEXES[transport])}",
              passing == set(ALLOWED_DEVICE_INDEXES[transport]))
        for d in other_devs:
            check(f"device index 0x{d:02X} refused (also via send())",
                  refused(chan, build_frame(d, 0, 1, ok_params[(0, 1)]))
                  and refused_by_send(chan, d, 0, 1, ok_params[(0, 1)]))

        # Feature index never discovered.
        check("undiscovered feature index refused",
              refused(chan, build_frame(dev, 9, 0)) and refused(chan, build_frame(dev, 0xFF, 1))
              and refused_by_send(chan, dev, 9, 0))
        check("register_feature refuses unknown ids / indexes / device indexes",
              _register_refused(chan, dev, 3, 0x2122) and _register_refused(chan, dev, 0, FEATURE_HIRES_WHEEL)
              and _register_refused(chan, dev, 0xFF, FEATURE_HIRES_WHEEL)
              and all(_register_refused(chan, d, 3, FEATURE_HIRES_WHEEL) for d in other_devs))

        # Software id.
        check("wrong sw_id refused",
              all(refused(chan, build_frame(dev, WHEEL_IDX, 1, sw_id=sw)) for sw in (0x0, 0x1, 0xA, 0xC, 0xF)))

        # Non-zero parameters.
        wheel_bad = [bytes([1]), bytes([0] * 15 + [1]), bytes([0, 0, 0xFF])]
        check("non-zero params on HIRES_WHEEL fn 0/1/3 refused",
              all(refused(chan, build_frame(dev, WHEEL_IDX, fn, p)) for fn in (0, 1, 3) for p in wheel_bad))
        check("non-zero params on DEVICE_NAME fn 0 / fn 1 (beyond offset) refused",
              refused(chan, build_frame(dev, NAME_IDX, 0, bytes([1])))
              and refused(chan, build_frame(dev, NAME_IDX, 1, bytes([0, 1])))
              and not refused(chan, build_frame(dev, NAME_IDX, 1, bytes([5]))))
        check("IRoot ping with params outside the data byte refused",
              refused(chan, build_frame(dev, 0, 1, bytes([1, 0, PING_DATA])))
              and refused(chan, build_frame(dev, 0, 1, bytes([0, 1, PING_DATA])))
              and refused(chan, build_frame(dev, 0, 1, bytes([0, 0, PING_DATA, 1]))))
        check("IRoot.getFeature of an id outside LOOKUP_FEATURES or with extra bytes refused",
              all(refused(chan, build_frame(dev, 0, 0, p)) for p in
                  (struct.pack(">H", 0x0000), struct.pack(">H", 0x1000), struct.pack(">H", 0x2120),
                   struct.pack(">H", FEATURE_HIRES_WHEEL) + bytes([1]))))

        # Report shape.
        good = build_frame(dev, 0, 1, ok_params[(0, 1)])
        check("short report id 0x10, wrong lengths refused",
              refused(chan, bytes([0x10]) + good[1:7]) and refused(chan, good[:19])
              and refused(chan, good + b"\x00") and refused(chan, bytes([0x12]) + good[1:]))

    # Index 0xFF specifically, both ways, stated explicitly.
    print("\n[cross-transport]")
    rec, ble = make(TRANSPORT_RECEIVER), make(TRANSPORT_BLUETOOTH)
    check("index 0xFF refused on a receiver channel", refused(rec, build_frame(0xFF, 0, 1, bytes([0, 0, PING_DATA]))))
    check("index 1 refused on a Bluetooth channel", refused(ble, build_frame(1, 0, 1, bytes([0, 0, PING_DATA]))))
    check("a feature discovered on device 1 is not usable on device 2",
          _cross_device_refused(rec))
    check("unknown transport rejected at construction", _bad_transport_rejected())

    # Reply matcher: the echoed index only.
    print("\n[reply matcher]")
    rep = bytes([0x11, 0xFF, 0x00, (1 << 4) | SW_ID, 4, 2, PING_DATA]) + bytes(13)
    check("reply with the echoed index 0xFF matches",
          GuardedChannel._match(rep, 0xFF, 0x00, 1) is not None)
    check("reply with another index is not matched",
          GuardedChannel._match(rep, 0x01, 0x00, 1) is None
          and GuardedChannel._match(rep[:1] + bytes([0x01]) + rep[2:], 0xFF, 0x00, 1) is None)
    err = bytes([0x11, 0xFF, 0xFF, 0x00, (1 << 4) | SW_ID, 0x09]) + bytes(14)
    m = GuardedChannel._match(err, 0xFF, 0x00, 1)
    check("HID++ 2.0 error with the echoed index matches as an error",
          m is not None and m.kind == "hidpp2-error" and m.error_code == 0x09)

    print(f"\nSelf-test: {passed} passed, {failed} failed")
    return 0 if failed == 0 else 5


def _assign_fails(chan):
    try:
        chan.transport = TRANSPORT_RECEIVER
    except AttributeError:
        return True
    return False


def _register_refused(chan, device_index, feature_index, feature_id):
    try:
        chan.register_feature(device_index, feature_index, feature_id)
    except RefusedMessage:
        return True
    return False


def _cross_device_refused(rec):
    rec.register_feature(1, 5, FEATURE_HIRES_WHEEL)
    try:
        rec._validate_frame(build_frame(2, 5, 1))
    except RefusedMessage:
        return True
    return False


def _bad_transport_rejected():
    try:
        GuardedChannel(None, HIDPP_LONG_OUT_LEN, HIDPP_LONG_OUT_LEN, "usb")
    except ValueError:
        return True
    return False


# --------------------------------------------------------------------------
# main
# --------------------------------------------------------------------------

def hex_int(text):
    return int(text, 16)


def main():
    ap = argparse.ArgumentParser(description="Read-only HID++ 2.0 HIRES_WHEEL probe.")
    ap.add_argument("--enumerate-only", action="store_true",
                    help="list 046D HID interfaces (with transport) and stop")
    ap.add_argument("--target", choices=("auto", "bolt", "bluetooth"), default="auto",
                    help="auto: Bolt first (indexes 1..6), then Bluetooth if no device "
                         "there exposes 0x2121 (default)")
    ap.add_argument("--pid", type=hex_int, default=None, metavar="XXXX",
                    help="hex PID of the Bluetooth device to use (default: prefer B034)")
    ap.add_argument("--use-set-output-report", action="store_true",
                    help="send the same validated frame with HidD_SetOutputReport "
                         "instead of WriteFile")
    ap.add_argument("-v", "--verbose", action="store_true",
                    help="print reports that are not replies to our requests")
    ap.add_argument("--self-test", action="store_true",
                    help="check the frame validation on dummy channels; opens no device")
    args = ap.parse_args()
    try:
        sys.stdout.reconfigure(errors="replace")
    except Exception:
        pass

    if args.self_test:
        return run_self_test()

    infos = enumerate_logitech()
    print_interfaces(infos)
    if args.enumerate_only:
        return 0

    if args.pid is not None and args.target == "bolt":
        print("\nNote: --pid only applies to the Bluetooth target; ignored for bolt.")

    if args.target == "bolt":
        rc, _ = run_bolt(infos, args)
        return rc
    if args.target == "bluetooth":
        rc, _ = run_bluetooth(infos, args)
        return rc

    # auto: Bolt first; Bluetooth only if the receiver did not give us a wheel.
    bolt_rc, bolt_found = run_bolt(infos, args)
    if bolt_rc in (3, 4):
        return bolt_rc                       # refused / I/O error: stop here
    if bolt_found:
        return 0
    print("\n=== auto: no device behind the Bolt receiver exposes 0x2121 "
          "-> trying Bluetooth ===")
    ble_rc, ble_found = run_bluetooth(infos, args)
    if ble_rc in (3, 4):
        return ble_rc
    if ble_found:
        return 0
    print("\nNo device exposed feature 0x2121 on either transport.")
    if bolt_rc == 0 or ble_rc == 0:
        return 0                             # at least one transport answered the probe
    return ble_rc                            # both unavailable: report the Bluetooth reason


if __name__ == "__main__":
    sys.exit(main())
