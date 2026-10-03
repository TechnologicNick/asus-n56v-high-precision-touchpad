"""Elevated, brief worker smoke test; restores existing ASUS settings on exit."""
from pathlib import Path
import struct
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from precision_bridge.config import defaults, atomic_json
from precision_bridge.device import AsusSettings

folder = ROOT / 'artifacts' / 'desktop-smoke'
folder.mkdir(parents=True, exist_ok=True)
config_path = folder / 'settings.json'
config = defaults()
config['windows']['3'] = False
atomic_json(config_path, config)
with AsusSettings() as settings:
    before = settings.read()
result = subprocess.run([sys.executable, '-m', 'precision_bridge', '--native', '--seconds', '3',
    '--config', str(config_path), '--status-file', str(folder / 'status.json'),
    '--parent-pid', str(__import__('os').getpid())], cwd=ROOT)
with AsusSettings() as settings:
    after = settings.read()
# Pipe start/stop normalizes debug bit to zero. Other 18 DWORDs must survive.
assert before[20:] == after[20:], 'ASUS preferences were not restored'
assert struct.unpack_from('<I', after, 16)[0] == 0, 'Raw feed left enabled'
assert result.returncode == 0, f'Worker failed: {result.returncode}'
print('PASS: hybrid worker started, stopped, and restored ASUS preferences.')
