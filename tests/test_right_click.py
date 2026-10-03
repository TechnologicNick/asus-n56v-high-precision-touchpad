import unittest
from types import SimpleNamespace
from precision_bridge.config import defaults, validate, asus_values
from precision_bridge.right_click import ChordRightClick, GestureMotionTracker


def f(slot, x=1000, y=600):
    return SimpleNamespace(slot=slot, x=x, y=y, active=True)


class RightClickTests(unittest.TestCase):
    def test_staggered_79ms_hold_46ms_tap_is_right_click(self):
        recognizer, config = self.create()
        recognizer.update([f(0)], 0, config)
        recognizer.update([f(0), f(1, 2000)], .079, config)
        self.assertTrue(recognizer.update([f(0)], .125, config))

    def test_near_simultaneous_staggered_tap_still_rejected(self):
        recognizer, config = self.create()
        recognizer.update([f(0)], 0, config)
        recognizer.update([f(0), f(1)], .01, config)
        self.assertFalse(recognizer.update([f(0)], .08, config))

    def create(self):
        return ChordRightClick((3420, 2052)), defaults()

    def test_held_finger_then_tap_clicks_on_second_lift(self):
        recognizer, config = self.create()
        self.assertFalse(recognizer.update([f(0)], 0, config))
        self.assertFalse(recognizer.update([f(0), f(1, 2000)], 0.4, config))
        self.assertFalse(recognizer.update([f(0), f(1, 2001)], 0.45, config))
        self.assertTrue(recognizer.update([f(0)], 0.5, config))
        self.assertFalse(recognizer.update([f(0)], 0.6, config))
        recognizer.update([f(0), f(1)], 0.7, config)
        self.assertTrue(recognizer.update([f(0)], 0.8, config))

    def test_simultaneous_tap_suppressed(self):
        recognizer, config = self.create()
        recognizer.update([f(0), f(1)], 0, config)
        self.assertFalse(recognizer.update([f(0)], 0.1, config))
        self.assertFalse(recognizer.update([], 0.11, config))

    def test_physical_click_cancels_custom_tap(self):
        recognizer, config = self.create()
        recognizer.update([f(0)], 0, config)
        recognizer.update([f(0), f(1)], 0.4, config)
        recognizer.update([f(0), f(1)], 0.45, config, buttons_down=True)
        self.assertFalse(recognizer.update([f(0)], 0.5, config))

    def test_minimum_duration_configurable(self):
        for minimum, expected in [(300, True), (600, False)]:
            recognizer, config = self.create(); config['rightClickHoldMs'] = minimum
            recognizer.update([f(0)], 0, config)
            recognizer.update([f(0), f(1)], 0.4, config)
            self.assertEqual(recognizer.update([f(0)], 0.5, config), expected)

    def test_second_motion_and_return_still_rejected(self):
        recognizer, config = self.create()
        recognizer.update([f(0)], 0, config)
        recognizer.update([f(0), f(1)], 0.4, config)
        recognizer.update([f(0), f(1, 1500)], 0.45, config)
        recognizer.update([f(0), f(1)], 0.48, config)
        self.assertFalse(recognizer.update([f(0)], 0.5, config))

    def test_long_hold_third_finger_or_anchor_lift_cancel(self):
        for event, event_time, release in [([f(0), f(1)], 0.8, [f(0)]),
                ([f(0), f(1), f(2)], 0.45, [f(0)]), ([f(1)], 0.45, [])]:
            recognizer, config = self.create()
            recognizer.update([f(0)], 0, config)
            recognizer.update([f(0), f(1)], 0.4, config)
            self.assertFalse(recognizer.update(event, event_time, config))
            self.assertFalse(recognizer.update(release, event_time + 0.05, config))

    def test_disabled_and_both_lift(self):
        recognizer, config = self.create(); config['chordRightClickEnabled'] = False
        recognizer.update([f(0)], 0, config)
        recognizer.update([f(0), f(1)], 0.4, config)
        self.assertFalse(recognizer.update([f(0)], 0.5, config))
        config['chordRightClickEnabled'] = True
        recognizer.update([f(0), f(1)], 0.6, config)
        self.assertFalse(recognizer.update([], 0.7, config))

    def test_motion_tracker_marks_scroll_without_filtering_contacts(self):
        tracker = GestureMotionTracker((3420, 2052))
        self.assertFalse(tracker.update([f(0), f(1)], 2))
        self.assertFalse(tracker.update([f(0), f(1, 1002)], 2))
        moved = [f(0, 1200), f(1, 1200)]
        self.assertTrue(tracker.update(moved, 2))
        self.assertTrue(tracker.update([f(0), f(1)], 2))
        self.assertFalse(tracker.update([], 2))
        self.assertFalse(tracker.update([f(0), f(1)], 2))

    def test_higher_counts_not_gated(self):
        tracker = GestureMotionTracker((3420, 2052))
        for n in (3, 4, 5): self.assertTrue(tracker.update([f(i) for i in range(n)], 2))

    def test_validation_and_asus_ordinary_tap_override(self):
        config = defaults(); config['windows']['2'] = False
        self.assertEqual(asus_values(config)[5], 0)
        for key, bad in [('chordRightClickEnabled', 1), ('rightClickHoldMs', -1), ('rightClickTapMaxMs', 0), ('rightClickSlopMm', float('nan'))]:
            config = defaults(); config[key] = bad
            with self.assertRaises(ValueError): validate(config)


if __name__ == '__main__': unittest.main()
