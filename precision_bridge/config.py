"""Shared desktop/worker configuration and deterministic gesture ownership."""
import json
import os
from pathlib import Path

CATALOG_PATH = Path(__file__).resolve().parents[1] / "gesture-catalog.json"
GESTURES = json.loads(CATALOG_PATH.read_text(encoding="utf-8"))


def default_path():
    return Path(os.environ.get("LOCALAPPDATA", str(Path.home()))) / "N56PrecisionBridge" / "settings.json"


def defaults():
    return {"version": 1, "buttonZonePercent": 15.0, "buttonZoneAtLowY": True, "chordRightClickEnabled": True,
            "rightClickHoldMs": 50.0, "rightClickTapMaxMs": 250.0, "rightClickSlopMm": 2.0,
            "windows": {str(n): True for n in range(2, 6)},
            "asus": {g["key"]: g["default"] for g in GESTURES}}


def validate(data):
    if not isinstance(data, dict) or data.get("version") != 1:
        raise ValueError("Unsupported configuration version")
    result = defaults()
    percent = data.get("buttonZonePercent", 15.0)
    if type(percent) not in (int, float) or not 0 <= percent <= 100:
        raise ValueError("Button zone percentage must be between 0 and 100")
    result["buttonZonePercent"] = float(percent)
    low_y = data.get("buttonZoneAtLowY", True)
    if type(low_y) is not bool:
        raise ValueError("buttonZoneAtLowY must be a boolean")
    result["buttonZoneAtLowY"] = low_y
    enabled = data.get("chordRightClickEnabled", True)
    if type(enabled) is not bool:
        raise ValueError("chordRightClickEnabled must be a boolean")
    result["chordRightClickEnabled"] = enabled
    for key, low, high in (("rightClickHoldMs", 0, 5000), ("rightClickTapMaxMs", 50, 2000), ("rightClickSlopMm", 0.1, 10)):
        value = data.get(key, result[key])
        if type(value) not in (int, float) or not low <= value <= high:
            raise ValueError(f"{key} must be between {low} and {high}")
        result[key] = float(value)
    for section in ("windows", "asus"):
        values = data.get(section)
        if not isinstance(values, dict) or set(values) != set(result[section]):
            raise ValueError(f"Invalid {section} switches")
        if any(type(value) is not bool for value in values.values()):
            raise ValueError(f"{section} switches must be booleans")
        result[section] = dict(values)
    return result


def load(path):
    try:
        return validate(json.loads(Path(path).read_text(encoding="utf-8-sig")))
    except FileNotFoundError:
        return defaults()


def atomic_json(path, data):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + "." + str(os.getpid()) + ".tmp")
    try:
        temporary.write_text(json.dumps(data, indent=2), encoding="utf-8")
        os.replace(temporary, path)
    finally:
        try:
            temporary.unlink(missing_ok=True)
        except OSError:
            pass


def publish_status(path, data):
    """Telemetry must never stop input forwarding (AV/readers may lock files)."""
    try:
        atomic_json(path, data)
        return True
    except OSError:
        return False


def asus_values(config, button_zone_active=False):
    return {g["index"]: int(g["index"] != 5 and config["asus"][g["key"]] and
            not config["windows"].get(str(g["fingers"]), False) and
            not (button_zone_active and g["fingers"] >= 2)) for g in GESTURES}


class ButtonZone:
    """Reserve contacts that enter the bottom strip until their physical lift.

    Latching prevents small movements across the boundary from unexpectedly
    promoting a resting button finger into a gesture. Physical clicks remain
    entirely owned by the original ASUS driver.
    """
    def __init__(self, logical_size, invert_y=False):
        self.width, self.height = logical_size
        if min(logical_size) <= 0:
            raise ValueError("Invalid sensor dimensions")
        self.invert_y = invert_y
        self.reserved = {}
        self.gesture_slots = set()

    def protect_gesture(self, contacts):
        """Once forwarded, gesture participants ignore the strip until lift."""
        if sum(c.active for c in contacts) >= 2:
            self.gesture_slots.update(c.slot for c in contacts if c.active and c.slot not in self.reserved)

    def filter(self, contacts, percent, bottom_at_low_y=False):
        active = {c.slot for c in contacts if c.active}
        self.gesture_slots.intersection_update(active)
        self.reserved = {slot: side for slot, side in self.reserved.items() if slot in active}
        for contact in contacts:
            if not contact.active or percent == 0 or contact.slot in self.gesture_slots:
                continue
            y = self.height - contact.y if self.invert_y != bottom_at_low_y else contact.y
            if y >= self.height * (1 - percent / 100):
                self.reserved.setdefault(contact.slot, "left" if contact.x < self.width / 2 else "right")
        return [contact for contact in contacts if contact.slot not in self.reserved]

    @property
    def occupied(self):
        return bool(self.reserved)


class Router:
    """Do not restart a Windows gesture mid-contact after a disabled count.

    A transition to a disabled multi-finger count ends native reporting until
    all fingers lift. This prevents 3->2 from masquerading as a fresh gesture.
    """
    def __init__(self):
        self.blocked = False
        self.drag_blocked = False

    def forward(self, contacts, config, physical_count=None, buttons_down=False, button_contact_count=0):
        count = sum(c.active for c in contacts)
        physical = count if physical_count is None else physical_count
        if physical == 0:
            self.blocked = False
        if physical < 2 and not buttons_down:
            self.drag_blocked = False
        # A separate reserved button finger is not one of the scrolling pair.
        # Clear the drag latch even if the button was pressed before the pair landed.
        independent_pair = button_contact_count > 0 and count >= 2
        if independent_pair:
            self.drag_blocked = False
        elif buttons_down:
            self.drag_blocked = True
        if count >= 2 and not config["windows"][str(count)]:
            self.blocked = True
        return contacts if count >= 2 and not self.blocked and not self.drag_blocked else []
