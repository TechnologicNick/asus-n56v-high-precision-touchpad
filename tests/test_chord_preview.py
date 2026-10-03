import unittest
from types import SimpleNamespace
from precision_bridge.config import defaults
from precision_bridge.right_click import ChordRightClick


def finger(slot, x=300, y=500):
    return SimpleNamespace(slot=slot, x=x, y=y, active=True)


class ChordPreviewTests(unittest.TestCase):
    def test_accepted_attempt_retains_measurements(self):
        chord = ChordRightClick((1000, 1000)); config = defaults()
        chord.update([finger(0)], 0, config)
        chord.update([finger(0), finger(1)], .4, config)
        self.assertTrue(chord.update([finger(0)], .5, config))
        chord.update([], .6, config)
        attempt = chord.snapshot(.6, config)['attempt']
        self.assertTrue(attempt['accepted'])
        self.assertAlmostEqual(attempt['holdMs'], 400)
        self.assertAlmostEqual(attempt['tapMs'], 100)
        self.assertEqual(attempt['reason'], 'Right-click accepted')

    def test_early_second_finger_explained(self):
        chord = ChordRightClick((1000, 1000)); config = defaults()
        chord.update([finger(0)], 0, config)
        chord.update([finger(0), finger(1)], .02, config)
        self.assertFalse(chord.update([finger(0)], .2, config))
        self.assertEqual(chord.attempt['reason'], 'First finger was not held long enough')

    def test_movement_peak_and_timeout_explained(self):
        for moving, reason in [(True, 'Too much movement'), (False, 'Second finger held too long')]:
            chord = ChordRightClick((1000, 1000)); config = defaults()
            chord.update([finger(0)], 0, config)
            chord.update([finger(0), finger(1)], .4, config)
            chord.update([finger(0), finger(1, 350 if moving else 300)], .5 if moving else .7, config)
            self.assertFalse(chord.update([finger(0)], .6 if moving else .8, config))
            self.assertEqual(chord.attempt['reason'], reason)
            if moving: self.assertGreater(chord.attempt['motionMm'], config['rightClickSlopMm'])

    def test_default_orientation_and_ready_state(self):
        config = defaults(); self.assertTrue(config['buttonZoneAtLowY'])
        chord = ChordRightClick((1000, 1000)); chord.update([finger(0)], 0, config)
        self.assertTrue(chord.snapshot(.4, config)['ready'])
