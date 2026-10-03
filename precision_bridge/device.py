"""Small, typed Win32 IOCTL client. No vendor DLL is loaded."""
import ctypes
from ctypes import wintypes as w
import struct


def bridge_status(device):
    """Read counters without synthesizing input or changing device settings."""
    values = struct.unpack("<24I", device.ioctl(0x00226004, b"", 96))
    return (f"mode={values[1]} surface={bool(values[2] & 1)} "
            f"waiting-for-lift={bool(values[3])} "
            f"submitted={values[5]} blocked={values[6]} "
            f"last-status=0x{values[7]:08X} "
            f"capabilities={values[10]} certification={values[11]} "
            f"mode-requests={values[18]} switch-requests={values[19]}")


class IoctlDevice:
    def __init__(self, path):
        self.kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        self.kernel.CreateFileW.argtypes = [w.LPCWSTR, w.DWORD, w.DWORD, w.LPVOID, w.DWORD, w.DWORD, w.HANDLE]
        self.kernel.CreateFileW.restype = w.HANDLE
        self.kernel.DeviceIoControl.argtypes = [w.HANDLE, w.DWORD, w.LPVOID, w.DWORD, w.LPVOID, w.DWORD, ctypes.POINTER(w.DWORD), w.LPVOID]
        self.kernel.DeviceIoControl.restype = w.BOOL
        self.kernel.CloseHandle.argtypes = [w.HANDLE]
        self.kernel.CloseHandle.restype = w.BOOL
        self.handle = self.kernel.CreateFileW(path, 0xC0000000, 3, None, 3, 0, None)
        if self.handle == ctypes.c_void_p(-1).value:
            self.handle = None
            raise ctypes.WinError(ctypes.get_last_error())

    def ioctl(self, code, data, output_size=0):
        source = ctypes.create_string_buffer(data, len(data))
        target = ctypes.create_string_buffer(output_size) if output_size else None
        returned = w.DWORD()
        if not self.kernel.DeviceIoControl(self.handle, code, source, len(data), target, output_size, ctypes.byref(returned), None):
            raise ctypes.WinError(ctypes.get_last_error())
        return target.raw[:returned.value] if target else b""

    def close(self):
        if self.handle:
            self.kernel.CloseHandle(self.handle)
            self.handle = None

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        self.close()


class ParentProcess:
    """Gracefully stop the worker if its desktop owner exits unexpectedly."""
    def __init__(self, pid):
        self.kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        self.kernel.OpenProcess.argtypes = [w.DWORD, w.BOOL, w.DWORD]
        self.kernel.OpenProcess.restype = w.HANDLE
        self.kernel.WaitForSingleObject.argtypes = [w.HANDLE, w.DWORD]
        self.kernel.WaitForSingleObject.restype = w.DWORD
        self.kernel.CloseHandle.argtypes = [w.HANDLE]
        self.kernel.CloseHandle.restype = w.BOOL
        self.handle = self.kernel.OpenProcess(0x100000, False, pid)
        if not self.handle:
            raise ctypes.WinError(ctypes.get_last_error())

    def exited(self):
        status = self.kernel.WaitForSingleObject(self.handle, 0)
        if status == 0xffffffff:
            raise ctypes.WinError(ctypes.get_last_error())
        return status == 0

    def close(self):
        if self.handle:
            self.kernel.CloseHandle(self.handle)
            self.handle = None


def suppress_windows_two_finger_tap():
    """Explicit user preference: disable Windows' ordinary two-finger tap.

    This documented setting is per Windows user, not per virtual device.
    It is intentionally persistent, like changing the checkbox in Settings.
    """
    import winreg
    key_name = r"Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad"
    with winreg.CreateKeyEx(winreg.HKEY_CURRENT_USER, key_name, 0, winreg.KEY_SET_VALUE) as key:
        winreg.SetValueEx(key, "TwoFingerTapEnabled", 0, winreg.REG_DWORD, 0)
    user = ctypes.WinDLL("user32", use_last_error=True)
    user.SendMessageTimeoutW.argtypes = [w.HWND, w.UINT, ctypes.c_size_t, w.LPCWSTR, w.UINT, w.UINT, ctypes.POINTER(ctypes.c_size_t)]
    user.SendMessageTimeoutW.restype = ctypes.c_size_t
    result = ctypes.c_size_t()
    user.SendMessageTimeoutW(0xffff, 0x1a, 0, "PrecisionTouchPad", 2, 1000, ctypes.byref(result))


class MouseOutput:
    def __init__(self):
        class Mouse(ctypes.Structure):
            _fields_ = [("dx", w.LONG), ("dy", w.LONG), ("data", w.DWORD), ("flags", w.DWORD), ("time", w.DWORD), ("extra", ctypes.c_size_t)]
        class Payload(ctypes.Union):
            _fields_ = [("mouse", Mouse), ("padding", ctypes.c_byte * 32)]
        class Input(ctypes.Structure):
            _fields_ = [("type", w.DWORD), ("payload", Payload)]
        self.Input = Input
        self.user = ctypes.WinDLL("user32", use_last_error=True)
        self.user.SendInput.argtypes = [w.UINT, ctypes.POINTER(Input), ctypes.c_int]
        self.user.SendInput.restype = w.UINT
        self.user.GetAsyncKeyState.argtypes = [ctypes.c_int]
        self.user.GetAsyncKeyState.restype = ctypes.c_short

    def buttons_down(self):
        return bool(self.user.GetAsyncKeyState(1) & 0x8000 or self.user.GetAsyncKeyState(2) & 0x8000)

    def right_click(self):
        inputs = (self.Input * 2)()
        inputs[0].payload.mouse.flags = 0x0008
        inputs[1].payload.mouse.flags = 0x0010
        sent = self.user.SendInput(2, inputs, ctypes.sizeof(self.Input))
        if sent != 2:
            error = ctypes.get_last_error()
            if sent == 1:
                self.user.SendInput(1, ctypes.byref(inputs[1]), ctypes.sizeof(self.Input))
            raise ctypes.WinError(error)


class AsusSettings(IoctlDevice):
    """Volatile driver settings, recovered from AsusTP.sys 1.0.0.148.

    Category 0 is the 19-DWORD user-settings block. No registry writes here.
    Used only for native mode, where ASUS's multi-finger actions must be muted.
    """
    CODE = 0x221594
    SIZE = 0x314

    def __init__(self):
        super().__init__(r"\\.\AsusTP")
        self.original = None

    def read(self):
        request = struct.pack("<4I", 0, 0, 1, self.SIZE) + bytes(self.SIZE - 16)
        result = self.ioctl(self.CODE, request, self.SIZE)
        if len(result) != self.SIZE:
            raise OSError(f"Unexpected ASUS settings size: {len(result)}")
        settings = struct.unpack_from("<19I", result, 16)
        if any(value not in (0, 1) for value in settings[:17]):
            raise OSError("Unsupported ASUS driver settings layout")
        return result

    def mute_multifinger(self):
        self.original = self.read()
        request = bytearray(self.original)
        struct.pack_into("<I", request, 0, 1)
        for index in range(5, 15):
            struct.pack_into("<I", request, 16 + 4 * index, 0)
        self.ioctl(self.CODE, bytes(request))

    def apply_gestures(self, values):
        if self.original is None:
            self.original = self.read()
        request = bytearray(self.original)
        struct.pack_into("<I", request, 0, 1)
        for index, value in values.items():
            if index not in range(1, 15) or value not in (0, 1):
                raise ValueError("Unsupported ASUS gesture setting")
            struct.pack_into("<I", request, 16 + 4 * index, value)
        self.ioctl(self.CODE, bytes(request))

    def restore(self):
        if self.original is not None:
            request = bytearray(self.original)
            struct.pack_into("<I", request, 0, 1)
            self.ioctl(self.CODE, bytes(request))
            self.original = None

    def close(self):
        try:
            self.restore()
        finally:
            super().close()
