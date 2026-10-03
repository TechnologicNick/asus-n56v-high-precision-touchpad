"""Read-only check of the installed bridge's administrator control endpoint."""
from pathlib import Path
import sys
import struct

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from precision_bridge.device import IoctlDevice

with IoctlDevice(r"\\.\N56PrecisionBridge") as device:
    print("Driver control interface opened successfully.")
    print("Driver diagnostics:", struct.unpack("<24I", device.ioctl(0x00226004, b"", 96)))
