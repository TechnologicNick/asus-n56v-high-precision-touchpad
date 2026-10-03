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
