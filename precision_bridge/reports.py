"""Parallel Precision Touchpad reports matching driver/report_descriptor.h."""
import struct

REPORT_ID = 1
MAX_CONTACTS = 5
REPORT_SIZE = 35
LOGICAL_MAX = 4095


class ReportEncoder:
    def __init__(self, logical_size, invert_y=False):
        if min(logical_size) <= 0:
            raise ValueError("Invalid sensor dimensions")
        self.width, self.height = logical_size
        self.invert_y = invert_y
        self.previous = {}

    def encode(self, contacts, scan_time):
        current = {}
        for c in contacts:
            if c.active:
                x = min(LOGICAL_MAX, max(0, round(c.x * LOGICAL_MAX / self.width)))
                y = min(LOGICAL_MAX, max(0, round(c.y * LOGICAL_MAX / self.height)))
                if self.invert_y:
                    y = LOGICAL_MAX - y
                current[c.slot] = (x, y)
        if len(current) > MAX_CONTACTS or any(slot not in range(5) for slot in current):
            raise ValueError("Too many contacts or invalid contact ID")
        # Finger-up retains the last valid coordinates and ID for one frame.
        entries = [(slot, xy, True) for slot, xy in sorted(current.items())]
        entries += [(slot, xy, False) for slot, xy in sorted(self.previous.items()) if slot not in current]
        report = bytearray([REPORT_ID])
        for slot, (x, y), active in entries:
            report += struct.pack("<BBHH", 3 if active else 1, slot, x, y)
        report += b"\0" * (6 * (MAX_CONTACTS - len(entries)))
        report += struct.pack("<HBB", scan_time & 0xFFFF, len(entries), 0)
        self.previous = current
        return bytes(report)

    def release(self, scan_time):
        return self.encode([], scan_time)
