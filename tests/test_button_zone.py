import unittest
from types import SimpleNamespace
from precision_bridge.config import ButtonZone, Router, asus_values, defaults, validate
from precision_bridge.reports import ReportEncoder


def finger(slot, x, y, active=True):
    return SimpleNamespace(slot=slot, x=x, y=y, active=active)


class ButtonZoneTests(unittest.TestCase):
    def test_two_hand_scroll_while_button_held_before_or_during_scroll(self):
        for press_first in (False, True):
            zone = ButtonZone((1000, 1000)); router = Router(); config = defaults()
            # Disable three-finger gestures: raw count must not disable the pair.
            config['windows']['3'] = False
            button = finger(0, 100, 950)
            pair = [finger(1, 600, 400), finger(2, 800, 500)]
            if press_first:
                filtered = zone.filter([button], 15)
                self.assertEqual(router.forward(filtered, config, 1, True, len(zone.reserved)), [])
                self.assertTrue(router.drag_blocked)
            else:
                self.assertEqual(router.forward(zone.filter(pair, 15), config, 2), pair)
            filtered = zone.filter([button] + pair, 15)
            self.assertEqual(router.forward(filtered, config, 3, True, len(zone.reserved)), pair)
            self.assertFalse(router.drag_blocked)
            # Removing a scroll finger must not turn button + one into a gesture.
            filtered = zone.filter([button] + pair[:1], 15)
            self.assertEqual(router.forward(filtered, config, 2, True, len(zone.reserved)), [])
            self.assertTrue(router.drag_blocked)
            filtered = zone.filter([button] + pair, 15)
            self.assertEqual(router.forward(filtered, config, 3, True, len(zone.reserved)), pair)

    def test_separate_button_does_not_override_disabled_windows_count(self):
        config = defaults(); config['windows']['2'] = False
        pair = [finger(1, 600, 400), finger(2, 800, 500)]
        self.assertEqual(Router().forward(pair, config, 3, True, 1), [])

    def test_active_scroll_can_cross_zone_until_each_finger_lifts(self):
        zone = ButtonZone((1000, 1000))
        contacts = [finger(0, 300, 400), finger(1, 700, 500)]
        zone.filter(contacts, 15)
        zone.protect_gesture(contacts)
        crossed = [finger(0, 300, 950), finger(1, 700, 950)]
        self.assertEqual(zone.filter(crossed, 15), crossed)
        self.assertFalse(zone.occupied)
        # An additional resting finger is not automatically exempted.
        extra = crossed + [finger(2, 100, 950)]
        self.assertEqual(zone.filter(extra, 15), crossed)
        self.assertEqual(zone.reserved, {2: 'left'})
        zone.filter([finger(0, 300, 950)], 15)
        # A lifted/reused slot no longer belongs to the old scroll.
        self.assertEqual(zone.filter(crossed, 15), crossed[:1])
        zone.filter([], 15)
        self.assertEqual(zone.filter(crossed, 15), [])

    def test_scroll_exemption_does_not_bypass_physical_button_guard(self):
        zone = ButtonZone((1000, 1000)); router = Router()
        contacts = [finger(0, 100, 400), finger(1, 800, 500)]
        zone.protect_gesture(contacts)
        crossed = [finger(0, 100, 950), finger(1, 800, 950)]
        filtered = zone.filter(crossed, 15)
        self.assertEqual(router.forward(filtered, defaults(), 2, buttons_down=True), [])

    def test_physical_button_suppresses_gestures_outside_zone(self):
        config = defaults(); router = Router()
        contacts = [finger(0, 100, 300), finger(1, 700, 400)]
        self.assertEqual(len(router.forward(contacts, config, 2)), 2)
        self.assertEqual(router.forward(contacts, config, 2, buttons_down=True), [])
        self.assertTrue(router.drag_blocked)
        self.assertEqual(router.forward(contacts, config, 2, buttons_down=False), [])
        router.forward(contacts[:1], config, 1, buttons_down=False)
        self.assertEqual(len(router.forward(contacts, config, 2)), 2)

    def test_low_y_bottom_configuration(self):
        zone = ButtonZone((1000, 1000))
        self.assertEqual(zone.filter([finger(0, 200, 100)], 15, bottom_at_low_y=True), [])
        self.assertEqual(len(zone.filter([finger(1, 600, 900)], 15, bottom_at_low_y=True)), 1)

    def test_pointing_and_either_button_do_not_scroll(self):
        for x, side in [(100, 'left'), (900, 'right')]:
            zone, router = ButtonZone((1000, 1000)), Router()
            contacts = [finger(0, 400, 300), finger(1, x, 900)]
            filtered = zone.filter(contacts, 15)
            self.assertEqual([c.slot for c in filtered], [0])
            self.assertEqual(zone.reserved, {1: side})
            self.assertEqual(router.forward(filtered, defaults(), 2), [])

    def test_two_above_zone_still_scroll(self):
        zone = ButtonZone((1000, 1000))
        contacts = [finger(0, 400, 300), finger(1, 600, 800)]
        self.assertEqual(zone.filter(contacts, 15), contacts)
        self.assertFalse(zone.occupied)
        self.assertEqual(len(Router().forward(contacts, defaults())), 2)

    def test_button_finger_latches_until_lift(self):
        zone = ButtonZone((1000, 1000))
        self.assertEqual(zone.filter([finger(1, 100, 950)], 15), [])
        self.assertEqual(zone.filter([finger(1, 800, 500)], 15), [])
        self.assertEqual(zone.reserved, {1: 'left'})
        zone.filter([finger(1, 800, 500, False)], 15)
        self.assertFalse(zone.occupied)
        self.assertEqual(len(zone.filter([finger(1, 800, 500)], 15)), 1)

    def test_threshold_config_and_zero(self):
        contacts = [finger(0, 100, 800)]
        self.assertEqual(len(ButtonZone((1000, 1000)).filter(contacts, 15)), 1)
        self.assertEqual(ButtonZone((1000, 1000)).filter(contacts, 20), [])
        self.assertEqual(len(ButtonZone((1000, 1000)).filter([finger(0, 100, 1000)], 0)), 1)
        self.assertEqual(validate(defaults())['buttonZonePercent'], 15)
        for value in (-1, 101, True, '15', float('nan'), float('inf')):
            data = defaults(); data['buttonZonePercent'] = value
            with self.assertRaises(ValueError): validate(data)

    def test_inverted_sensor_axis(self):
        zone = ButtonZone((3420, 2052), invert_y=True)
        self.assertEqual(zone.filter([finger(0, 1000, 100)], 15), [])
        self.assertEqual(len(zone.filter([finger(1, 2000, 1000)], 15)), 1)

    def test_zone_entry_releases_windows_contact_without_new_ids(self):
        encoder = ReportEncoder((1000, 1000)); zone = ButtonZone((1000, 1000)); router = Router()
        config = defaults()
        initial = [finger(0, 300, 400), finger(1, 700, 500)]
        encoder.encode(router.forward(zone.filter(initial, 15), config), 0)
        moved = [finger(0, 300, 400), finger(1, 700, 900)]
        release = encoder.encode(router.forward(zone.filter(moved, 15), config), 100)
        self.assertEqual(release[33], 2)
        self.assertEqual((release[1], release[7]), (1, 1))
        self.assertEqual(encoder.previous, {})

    def test_asus_multifinger_muted_while_button_zone_occupied(self):
        config = defaults(); config['windows']['2'] = False; config['windows']['3'] = False
        self.assertEqual(asus_values(config)[6], 1)
        muted = asus_values(config, True)
        self.assertEqual([muted[i] for i in range(5, 12)], [0] * 7)
        self.assertEqual(muted[1], 1)

    def test_disabled_gesture_not_reset_by_only_button_remaining(self):
        config = defaults(); config['windows']['3'] = False
        router = Router(); contacts = [finger(i, 100, 200) for i in range(3)]
        router.forward(contacts, config, 3)
        router.forward([], config, 1)
        self.assertEqual(router.forward(contacts[:2], config, 2), [])
        router.forward([], config, 0)
        self.assertEqual(len(router.forward(contacts[:2], config, 2)), 2)


if __name__ == '__main__': unittest.main()
