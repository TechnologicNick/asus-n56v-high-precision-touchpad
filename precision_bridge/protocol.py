"""Streaming ASUS CSV decoder. The vendor stream has no line terminators."""
from dataclasses import dataclass


@dataclass(frozen=True)
class Contact:
    slot: int
    active: bool
    x: int
    y: int
    width: int


class StreamDecoder:
    """Emit once the last comma arrives; the final pressure value is unused.

    This avoids guessing byte-pipe message boundaries or waiting for a new
    frame to report finger-up. A chunk may split at ANY byte, including a digit.
    """
    def __init__(self):
        self.buffer = b""
        self.emitted = False

    def feed(self, data):
        self.buffer += data
        frames = []
        while True:
            start = self.buffer.find(b"Data=")
            if start < 0:
                self.buffer = self.buffer[-4:]
                break
            if start:
                self.buffer = self.buffer[start:]
                self.emitted = False
            end = self.buffer.find(b"Data=", 5)
            record = self.buffer if end < 0 else self.buffer[:end]
            if not self.emitted and record.count(b",") >= 25:
                fields = record[5:].split(b",")[:25]
                try:
                    values = [int(field) for field in fields]
                    if values[0] != 5:
                        raise ValueError("Expected five diagnostic slots")
                    contacts = []
                    for slot in range(5):
                        active, x, y, width = values[1 + 5 * slot:5 + 5 * slot]
                        if active not in (0, 1) or not (0 <= x <= 65535 and 0 <= y <= 65535 and 0 <= width <= 255):
                            raise ValueError("Invalid contact")
                        contacts.append(Contact(slot, bool(active), x, y, width))
                    frames.append(tuple(contacts))
                except ValueError:
                    pass
                self.emitted = True
            if end < 0:
                if len(self.buffer) > 2048:
                    self.buffer = self.buffer[-4:]
                    self.emitted = False
                break
            self.buffer = self.buffer[end:]
            self.emitted = False
        return frames
