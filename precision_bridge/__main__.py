import argparse
import json
import sys
import time

from .asus_pipe import AsusPipe
from .protocol import StreamDecoder
from .reports import ReportEncoder
from .config import load, defaults, asus_values, Router, ButtonZone, publish_status
from pathlib import Path
from .right_click import ChordRightClick, GestureMotionTracker


def main():
    parser = argparse.ArgumentParser(description="Experimental ASUS raw contacts to Windows Precision gestures")
    parser.add_argument("--native", action="store_true", help="Submit contacts to the installed virtual HID driver (requires administrator)")
    parser.add_argument("--seconds", type=float, default=0, help="Stop after this duration; 0 runs until Ctrl+C")
    parser.add_argument("--invert-y", action="store_true", help="Invert sensor Y before encoding")
    parser.add_argument("--button-zone-percent", type=float, help="Bottom button strip, 0 disables (default: 15%%)")
    parser.add_argument("--json", action="store_true", help="Print each decoded frame as JSON")
    parser.add_argument("--config", type=Path, help="Live desktop settings; applied after all fingers lift")
    parser.add_argument("--stop-file", type=Path, help="Exit gracefully when this file appears")
    parser.add_argument("--status-file", type=Path, help="Publish best-effort desktop status JSON at 10 Hz")
    parser.add_argument("--parent-pid", type=int, help="Gracefully stop if the desktop owner exits")
    parser.add_argument("--preview-stream", action="store_true", help="Stream each sensor frame to the desktop over stdout")
    args = parser.parse_args()
    if sys.platform != "win32":
        parser.error("Live capture requires Windows")
    if args.seconds < 0:
        parser.error("--seconds must be nonnegative")
    if args.config and not args.native:
        parser.error("--config requires --native")
    if args.button_zone_percent is not None and not 0 <= args.button_zone_percent <= 100:
        parser.error("--button-zone-percent must be between 0 and 100")
    if args.config and args.button_zone_percent is not None:
        parser.error("Set buttonZonePercent in --config instead of using the CLI override")
    native = settings = None
    parent = None
    encoder = None
    started = time.monotonic()
    frames = 0
    counts = {}
    config = None
    pending = config
    router = Router()
    last_active = 0
    config_error = None
    zone = None
    zone_muted = False
    effective = 0
    native_defaults = defaults()
    clicks = 0
    try:
        if args.parent_pid:
            from .device import ParentProcess
            parent = ParentProcess(args.parent_pid)
        config = load(args.config) if args.config else None
        pending = config
        if args.native:
            from .device import IoctlDevice, AsusSettings, bridge_status, MouseOutput, suppress_windows_two_finger_tap
            # Check output first, before making changes to ASUS settings.
            native = IoctlDevice(r"\\.\N56PrecisionBridge")
            print("Windows touchpad: " + bridge_status(native), file=sys.stderr if args.json else sys.stdout, flush=True)
            settings = AsusSettings()
            settings.read()  # Validate the supported layout before opening pipes.
            suppress_windows_two_finger_tap()
            mouse = MouseOutput()
        with AsusPipe() as pipe:
            dimensions = pipe.logical_size()
            print(f"ASUS sensor: {dimensions[0]} x {dimensions[1]}; " + ("native multi-finger mode" if native else "capture only"), file=sys.stderr if args.json else sys.stdout, flush=True)
            encoder = ReportEncoder(dimensions, args.invert_y)
            zone = ButtonZone(dimensions, args.invert_y)
            chord = ChordRightClick(dimensions)
            gesture_motion = GestureMotionTracker(dimensions)
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
                if config:
                    settings.apply_gestures(asus_values(config))
                else:
                    settings.mute_multifinger()
                native.ioctl(0x0022A000, encoder.release(int(time.monotonic() * 10000)))
            last_status = 0.0
            last_publish = 0.0
            contact_snapshot = []
            while not args.seconds or time.monotonic() - started < args.seconds:
                if args.stop_file and args.stop_file.exists():
                    break
                if parent and parent.exited():
                    break
                data = pipe.read_available()
                for contacts in decoder.feed(data):
                    frames += 1
                    active = sum(c.active for c in contacts)
                    counts[active] = counts.get(active, 0) + 1
                    last_active = active
                    if config and active == 0 and pending != config:
                        settings.apply_gestures(asus_values(pending))
                        config = pending
                    if args.json:
                        print(json.dumps({"time": time.monotonic(), "contacts": [vars(c) for c in contacts]}), flush=True)
                    if native:
                        frame_time = time.monotonic()
                        profile = config or native_defaults
                        buttons_down = mouse.buttons_down()
                        if chord.update(contacts, frame_time, profile, buttons_down):
                            mouse.right_click()
                            clicks += 1
                        percent = config["buttonZonePercent"] if config else (15.0 if args.button_zone_percent is None else args.button_zone_percent)
                        gesture_contacts = zone.filter(contacts, percent, profile['buttonZoneAtLowY'])
                        contact_snapshot = [{"slot": c.slot, "x": c.x / dimensions[0], "y": c.y / dimensions[1],
                            "button": zone.reserved.get(c.slot)} for c in contacts if c.active]
                        effective = sum(c.active for c in gesture_contacts)
                        # ASUS sees the unfiltered hardware stream. Mute its
                        # multi-finger recognizers while a button finger rests.
                        desired_zone_mute = zone.occupied or buttons_down or (zone_muted and active > 0)
                        if config and desired_zone_mute != zone_muted:
                            settings.apply_gestures(asus_values(config, desired_zone_mute))
                            zone_muted = desired_zone_mute
                        # Keep ASUS's existing one-finger cursor and physical
                        # buttons. A single remaining finger ends the virtual
                        # gesture, avoiding double cursor movement.
                        routed = router.forward(gesture_contacts, profile, active, buttons_down, len(zone.reserved))
                        if gesture_motion.update(routed, profile['rightClickSlopMm']):
                            zone.protect_gesture(routed)
                        report = encoder.encode(routed, int(time.monotonic() * 10000))
                        native.ioctl(0x0022A000, report)
                        if args.preview_stream:
                            print('N56_FRAME ' + json.dumps({'frames': frames, 'contacts': contact_snapshot,
                                'dragSuppressed': router.drag_blocked, 'buttonHeld': buttons_down,
                                'chord': chord.snapshot(frame_time, profile)},
                                separators=(',', ':')), flush=True)
                now = time.monotonic()
                if now - last_status >= 1:
                    if args.config:
                        try:
                            pending = load(args.config)
                            config_error = None
                            if last_active == 0 and pending != config:
                                settings.apply_gestures(asus_values(pending))
                                config = pending
                        except (OSError, ValueError) as error:
                            config_error = str(error)
                    status = bridge_status(native) if native else "capture only"
                    if not args.json:
                        print(f"Frames: {frames}; contact counts: {counts}", flush=True)
                        if native:
                            print("Windows touchpad: " + status, flush=True)
                    last_status = now
                if args.status_file and now - last_publish >= 0.1:
                    publish_status(args.status_file, {"state": "running", "frames": frames,
                        "counts": counts, "driver": status, "configError": config_error,
                        "gestureFingers": effective, "buttonContacts": zone.reserved if zone else {},
                        "rightClicks": clicks, "contacts": contact_snapshot,
                        "dragSuppressed": router.drag_blocked,
                        "pending": pending != config, "time": time.time()})
                    last_publish = now
                time.sleep(0.002)
            # Restore volatile driver settings before StopListeningData restores
            # the companion's own settings. No persisted options are changed.
    except KeyboardInterrupt:
        pass
    except (OSError, TimeoutError, ValueError) as error:
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
        if parent:
            parent.close()
        if args.status_file:
            publish_status(args.status_file, {"state": "stopped", "frames": frames, "time": time.time()})
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
