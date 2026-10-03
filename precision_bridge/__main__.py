import argparse
import json
import sys
import time

from .asus_pipe import AsusPipe
from .protocol import StreamDecoder
from .reports import ReportEncoder


def main():
    parser = argparse.ArgumentParser(description="Experimental ASUS raw contacts to Windows Precision gestures")
    parser.add_argument("--native", action="store_true", help="Submit contacts to the installed virtual HID driver (requires administrator)")
    parser.add_argument("--seconds", type=float, default=0, help="Stop after this duration; 0 runs until Ctrl+C")
    parser.add_argument("--invert-y", action="store_true", help="Invert sensor Y before encoding")
    parser.add_argument("--json", action="store_true", help="Print each decoded frame as JSON")
    args = parser.parse_args()
    if sys.platform != "win32":
        parser.error("Live capture requires Windows")
    if args.seconds < 0:
        parser.error("--seconds must be nonnegative")
    native = settings = None
    encoder = None
    started = time.monotonic()
    frames = 0
    counts = {}
    try:
        if args.native:
            from .device import IoctlDevice, AsusSettings, bridge_status
            # Check output first, before making changes to ASUS settings.
            native = IoctlDevice(r"\\.\N56PrecisionBridge")
            print("Windows touchpad: " + bridge_status(native), file=sys.stderr if args.json else sys.stdout, flush=True)
            settings = AsusSettings()
            settings.read()  # Validate the supported layout before opening pipes.
        with AsusPipe() as pipe:
            dimensions = pipe.logical_size()
            print(f"ASUS sensor: {dimensions[0]} x {dimensions[1]}; " + ("native multi-finger mode" if native else "capture only"), file=sys.stderr if args.json else sys.stdout, flush=True)
            encoder = ReportEncoder(dimensions, args.invert_y)
            decoder = StreamDecoder()
            pipe.start()
            # StartListeningData is processed asynchronously. Wait for its
            # settings update before taking the snapshot used by mute/restore.
            if settings:
                import struct
                deadline = time.monotonic() + 2
                while struct.unpack_from("<I", settings.read(), 16)[0] != 1:
                    if time.monotonic() >= deadline:
                        raise TimeoutError("ASUS did not enable the raw contact feed")
                    time.sleep(0.01)
                pipe.on_stopping = settings.restore
                settings.mute_multifinger()
                native.ioctl(0x0022A000, encoder.release(int(time.monotonic() * 10000)))
            last_status = 0.0
            while not args.seconds or time.monotonic() - started < args.seconds:
                data = pipe.read_available()
                for contacts in decoder.feed(data):
                    frames += 1
                    active = sum(c.active for c in contacts)
                    counts[active] = counts.get(active, 0) + 1
                    if args.json:
                        print(json.dumps({"time": time.monotonic(), "contacts": [vars(c) for c in contacts]}), flush=True)
                    if native:
                        # Keep ASUS's existing one-finger cursor and physical
                        # buttons. A single remaining finger ends the virtual
                        # gesture, avoiding double cursor movement.
                        report = encoder.encode(contacts if active >= 2 else [], int(time.monotonic() * 10000))
                        native.ioctl(0x0022A000, report)
                now = time.monotonic()
                if not args.json and now - last_status >= 1:
                    print(f"Frames: {frames}; contact counts: {counts}", flush=True)
                    if native:
                        print("Windows touchpad: " + bridge_status(native), flush=True)
                    last_status = now
                time.sleep(0.002)
            # Restore volatile driver settings before StopListeningData restores
            # the companion's own settings. No persisted options are changed.
    except KeyboardInterrupt:
        pass
    except (OSError, TimeoutError) as error:
        print(f"Bridge stopped: {error}", file=sys.stderr)
        return 1
    finally:
        if native:
            try:
                if encoder:
                    native.ioctl(0x0022A000, encoder.release(int(time.monotonic() * 10000)))
            except OSError:
                pass
            native.close()
        if settings:
            settings.close()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
