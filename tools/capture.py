"""Capture ASUS diagnostic contacts without injecting any input."""
import argparse
from pathlib import Path
import sys
import time

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from precision_bridge.asus_pipe import AsusPipe
from precision_bridge.protocol import StreamDecoder

parser = argparse.ArgumentParser()
parser.add_argument("--seconds", type=float, default=15)
parser.add_argument("--raw", action="store_true")
args = parser.parse_args()
with AsusPipe() as pipe:
    print("Logical size:", pipe.logical_size(), flush=True)
    pipe.start()
    deadline = time.monotonic() + args.seconds
    count = 0
    frames = 0
    counts = {}
    examples = {}
    decoder = StreamDecoder()
    while time.monotonic() < deadline:
        data = pipe.read_available()
        if data:
            count += len(data)
            if args.raw:
                print(repr(data), flush=True)
            for contacts in decoder.feed(data):
                frames += 1
                active = sum(c.active for c in contacts)
                counts[active] = counts.get(active, 0) + 1
                if active not in examples:
                    examples[active] = contacts
                    print("First", active, "contact frame:", contacts, flush=True)
        time.sleep(0.005)
    print("Bytes captured:", count, flush=True)
    print("Frames:", frames, "Contact counts:", counts, flush=True)
