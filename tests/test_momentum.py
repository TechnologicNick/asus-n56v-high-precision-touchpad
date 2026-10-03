import unittest
from types import SimpleNamespace
from precision_bridge.config import defaults, ButtonZone, Router
from precision_bridge.reports import ReportEncoder
from precision_bridge.right_click import ChordRightClick, GestureMotionTracker


def finger(slot, x, y):
    return SimpleNamespace(slot=slot, x=x, y=y, active=True)


class MomentumTests(unittest.TestCase):
    def test_stationary_retouch_is_reported_immediately_without_right_click(self):
        config = defaults(); zone = ButtonZone((1000, 1000)); router = Router()
        motion = GestureMotionTracker((1000, 1000)); encoder = ReportEncoder((1000, 1000))
        chord = ChordRightClick((1000, 1000))
        def frame(contacts, now):
            self.assertFalse(chord.update(contacts, now, config))
            routed = router.forward(zone.filter(contacts, 15, True), config, len(contacts))
            if motion.update(routed, 2): zone.protect_gesture(routed)
            return encoder.encode(routed, int(now * 10000))
        pair = [finger(0, 400, 400), finger(1, 600, 500)]
        frame(pair, 0)
        frame([finger(0, 400, 500), finger(1, 600, 600)], .1)
        frame([], .2)  # Windows may continue scrolling with inertia after lift.
        retouch = frame(pair, .3)
        self.assertEqual(retouch[33], 2)
        self.assertEqual((retouch[1], retouch[7]), (3, 3))
        self.assertFalse(motion.open)
        self.assertEqual(zone.gesture_slots, set())
        # Stationary holding still sends contacts, but doesn't earn zone immunity.
        stationary = frame(pair, .4)
        self.assertEqual((stationary[1], stationary[7]), (3, 3))
        frame([], .5)

