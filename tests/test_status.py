import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
from precision_bridge.config import publish_status, atomic_json


class StatusTests(unittest.TestCase):
    def test_locked_status_is_nonfatal_and_next_update_recovers(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'status.json'
            atomic_json(path, {'frames': 1})
            with patch('precision_bridge.config.os.replace', side_effect=PermissionError('reader denied replacement')):
                self.assertFalse(publish_status(path, {'frames': 2}))
            self.assertTrue(publish_status(path, {'frames': 3}))
            self.assertIn('3', path.read_text())
            self.assertEqual(list(path.parent.glob('*.tmp')), [])

    def test_unavailable_status_location_is_nonfatal(self):
        with patch('precision_bridge.config.atomic_json', side_effect=OSError('disk unavailable')):
            self.assertFalse(publish_status('unused', {}))


if __name__ == '__main__': unittest.main()
