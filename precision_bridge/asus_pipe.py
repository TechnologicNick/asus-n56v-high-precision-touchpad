"""Client for ASUS TP API 1.0.3.82's reverse-engineered byte-stream pipes."""
import ctypes
from ctypes import wintypes as w
import re
import time

PREFIX = r"\\.\pipe\ASUS_Smart_Gesture_Raw_Data_Request_"


class AsusPipe:
    def __init__(self):
        self.kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        signatures = {
            "CreateFileW": ([w.LPCWSTR, w.DWORD, w.DWORD, w.LPVOID, w.DWORD, w.DWORD, w.HANDLE], w.HANDLE),
            "CloseHandle": ([w.HANDLE], w.BOOL),
            "ReadFile": ([w.HANDLE, w.LPVOID, w.DWORD, ctypes.POINTER(w.DWORD), w.LPVOID], w.BOOL),
            "WriteFile": ([w.HANDLE, w.LPCVOID, w.DWORD, ctypes.POINTER(w.DWORD), w.LPVOID], w.BOOL),
            "PeekNamedPipe": ([w.HANDLE, w.LPVOID, w.DWORD, ctypes.POINTER(w.DWORD), ctypes.POINTER(w.DWORD), ctypes.POINTER(w.DWORD)], w.BOOL),
            "WaitNamedPipeW": ([w.LPCWSTR, w.DWORD], w.BOOL),
        }
        for name, (args, result) in signatures.items():
            fn = getattr(self.kernel, name)
            fn.argtypes, fn.restype = args, result
        self.cmd = self.report = None
        self.listening = False
        self.on_stopping = None

    def _open(self, suffix):
        path = PREFIX + suffix
        if not self.kernel.WaitNamedPipeW(path, 2000):
            raise ctypes.WinError(ctypes.get_last_error())
        handle = self.kernel.CreateFileW(path, 0xC0000000, 0, None, 3, 0, None)
        if handle == ctypes.c_void_p(-1).value:
            raise ctypes.WinError(ctypes.get_last_error())
        return handle

    def __enter__(self):
        try:
            self.cmd = self._open("Cmd")
            self.report = self._open("Report")
            return self
        except BaseException:
            self.close()
            raise

    def command(self, value):
        # The server compares the entire ReadFile payload with strcmp. One write,
        # no newline, and no concurrent writers; the server adds its own NUL.
        payload = value.encode("ascii")
        size = w.DWORD()
        if not self.kernel.WriteFile(self.cmd, payload, len(payload), ctypes.byref(size), None):
            raise ctypes.WinError(ctypes.get_last_error())
        if size.value != len(payload):
            raise OSError("Short command write")

    def read_available(self):
        available = w.DWORD()
        if not self.kernel.PeekNamedPipe(self.report, None, 0, None, ctypes.byref(available), None):
            raise ctypes.WinError(ctypes.get_last_error())
        if not available.value:
            return b""
        data = ctypes.create_string_buffer(min(available.value, 65536))
        size = w.DWORD()
        if not self.kernel.ReadFile(self.report, data, len(data), ctypes.byref(size), None):
            raise ctypes.WinError(ctypes.get_last_error())
        return data.raw[:size.value]

    def logical_size(self, timeout=2):
        self.command("GetLogicalSize")
        deadline = time.monotonic() + timeout
        data = b""
        while time.monotonic() < deadline:
            data += self.read_available()
            match = re.fullmatch(rb"LogicalSize=(\d+),(\d+)", data)
            if match and int(match[1]) > 0 and int(match[2]) > 0:
                # Byte pipes have no framing; wait for the complete response.
                time.sleep(0.02)
                extra = self.read_available()
                if extra:
                    data += extra
                    continue
                return int(match[1]), int(match[2])
            time.sleep(0.005)
        raise TimeoutError(f"No valid logical size response: {data!r}")

    def start(self):
        self.command("StartListeningData")
        self.listening = True

    def close(self):
        restore_error = None
        if self.on_stopping is not None:
            try:
                self.on_stopping()
            except OSError as error:
                restore_error = error
            self.on_stopping = None
        if self.listening and self.cmd:
            try:
                self.command("StopListeningData")
                # Allow command consumption before disconnecting the byte pipe.
                time.sleep(0.03)
            except OSError:
                pass
        self.listening = False
        for name in ("report", "cmd"):
            handle = getattr(self, name)
            if handle:
                self.kernel.CloseHandle(handle)
                setattr(self, name, None)
        if restore_error:
            raise restore_error

    def __exit__(self, *exc):
        self.close()
