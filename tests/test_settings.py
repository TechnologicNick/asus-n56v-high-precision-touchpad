import struct
import unittest
from precision_bridge.device import AsusSettings
from precision_bridge.config import defaults, asus_values


class SettingsTests(unittest.TestCase):
    def make_device(self):
        device = AsusSettings.__new__(AsusSettings)
        device.original = None
        source = bytearray(AsusSettings.SIZE)
        struct.pack_into('<4I', source, 0, 0, 0, 1, AsusSettings.SIZE)
        struct.pack_into('<19I', source, 16, *([1] * 17 + [7, 9]))
        device.read = lambda: bytes(source)
        self.requests = []
        device.ioctl = lambda code, data: self.requests.append(data)
        return device, bytes(source)

    def test_live_changes_keep_original_snapshot(self):
        device, original = self.make_device()
        config = defaults()
        device.apply_gestures(asus_values(config))
        first = struct.unpack_from('<19I', self.requests[-1], 16)
        self.assertEqual(first[5:12], (0,) * 7)
        self.assertEqual(first[15:], (1, 1, 7, 9))
        config['windows']['3'] = False
        device.apply_gestures(asus_values(config))
        self.assertEqual(struct.unpack_from('<3I', self.requests[-1], 16 + 9 * 4), (1, 1, 1))
        self.assertEqual(device.original, original)
        device.restore()
        expected = bytearray(original)
        struct.pack_into('<I', expected, 0, 1)
        self.assertEqual(self.requests[-1], bytes(expected))
        self.assertIsNone(device.original)

    def test_reject_unknown_settings(self):
        device, _ = self.make_device()
        with self.assertRaises(ValueError): device.apply_gestures({15: 0})
        with self.assertRaises(ValueError): device.apply_gestures({6: 2})
        self.assertEqual(self.requests, [])


if __name__ == '__main__': unittest.main()
