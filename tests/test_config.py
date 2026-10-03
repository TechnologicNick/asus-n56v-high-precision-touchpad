import json
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from precision_bridge.config import defaults, validate, asus_values, Router, load, atomic_json, GESTURES


class ConfigTests(unittest.TestCase):
    def test_catalog_covers_every_gesture(self):
        self.assertEqual(sorted(g['index'] for g in GESTURES), list(range(1, 15)))
        self.assertEqual(len({g['key'] for g in GESTURES}), 14)

    def test_windows_priority(self):
        config = defaults()
        values = asus_values(config)
        self.assertEqual([values[i] for i in range(5, 12)], [0] * 7)
        config['windows']['3'] = False
        values = asus_values(config)
        self.assertEqual([values[i] for i in range(9, 12)], [1] * 3)
        config['asus']['three_down'] = False
        self.assertEqual(asus_values(config)[10], 0)
        self.assertEqual(asus_values(config)[6], 0)

    def test_strict_validation(self):
        for change in (lambda d: d.update(version=2),
                       lambda d: d['windows'].update({'2': 1}),
                       lambda d: d['asus'].pop('zoom')):
            data = defaults()
            change(data)
            with self.assertRaises(ValueError): validate(data)

    def test_atomic_roundtrip(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'settings.json'
            self.assertEqual(load(path), defaults())
            atomic_json(path, defaults())
            self.assertEqual(load(path), defaults())
            self.assertEqual(list(path.parent.glob('*.tmp')), [])
            path.write_text(json.dumps(defaults()), encoding='utf-8-sig')
            self.assertEqual(load(path), defaults())

    def test_route_transition_requires_all_up(self):
        config = defaults()
        config['windows']['3'] = False
        router = Router()
        contacts = lambda n: [SimpleNamespace(active=True, slot=i) for i in range(n)]
        self.assertEqual(len(router.forward(contacts(2), config)), 2)
        self.assertEqual(router.forward(contacts(3), config), [])
        self.assertEqual(router.forward(contacts(2), config), [])
        router.forward([], config)
        self.assertEqual(len(router.forward(contacts(4), config)), 4)


if __name__ == '__main__': unittest.main()
