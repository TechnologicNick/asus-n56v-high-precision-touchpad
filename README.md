# ASUS N56V Precision Gesture Bridge

An experimental bridge for the N56VM's ELAN PS/2 touchpad. It reads actual
contacts from ASUS Smart Gesture and sends multi-finger contacts to a virtual
Windows Precision Touchpad HID device. Windows performs gesture recognition;
this code never synthesizes Ctrl, plus, minus, keyboard shortcuts, or wheel zoom.

**Current status:** the test-signed x64 virtual driver is installed and running.
Windows recognizes its HID touchpad, and its administrator control interface
opens successfully. The root device instance is `ROOT\SYSTEM\0002`; its hardware
ID is `ROOT\N56PrecisionBridge`. Native pinch/scroll behavior still needs an
interactive test with the Python bridge running. This remains a development
prototype.

## Run the working contact monitor

From this directory in PowerShell:

```powershell
python -m precision_bridge --seconds 30
```

Move one, two, and three fingers, then pinch. The monitor prints frame totals
grouped by the number of active contacts. Use `--json` to inspect coordinates.
It depends on the installed ASUS driver and running `AsusTPCenter.exe`.
No extra Python packages are required for capture or the bridge.

Only one raw-data pipe client can connect at a time. If opening the pipe fails,
close another contact monitor first. A connection error stops the program.
Capture temporarily enables the companion's diagnostic feed; a normal exit or
pipe disconnect sends the companion back to its normal settings. Keep ASUS
Smart Gesture running: it provides both the feed and the existing cursor/clicks.

## Native gesture implementation

```text
ELAN PS/2 sensor → ASUS driver → ASUS raw-data pipes → Python bridge
                                                     ↓
                                         virtual HID driver → Windows gestures
```

The existing ASUS mouse supplies one-finger movement and physical clicks. The
virtual device receives only interactions with two or more fingers. During native
mode, the bridge temporarily disables the original ASUS multi-finger actions in
the driver, then restores them on exit. The changes are volatile; the ASUS
gesture preferences in the registry remain in place.

After installing a signed development driver, run from an elevated terminal:

```powershell
python -m precision_bridge --native
```

Ctrl+C stops the bridge and releases virtual contacts. The driver also releases
contacts on client disconnect, power transitions, and a 500 ms input stall.
The driver endpoint is restricted to Administrators and SYSTEM.

## Build

The current workspace already has a built test-signed package under
`artifacts/driver/`: `N56PrecisionBridge.sys`, `.inf`, and `.cat`.

To reproduce it, use Visual Studio 2022's x64 C++ tools, a Windows SDK, and the
WDK. These commands download a hash-verified WDK NuGet package into the workspace
and build without installing the kit into Windows:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/fetch-wdk.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/build-driver.ps1
python -m unittest discover -s tests -v
```

The build script defaults to this computer's SDK 10.0.22621.0 and the local WDK
10.0.26100.0, using KMDF 1.15 for compatibility. This combination compiled here;
Microsoft recommends matching SDK/WDK build versions for a supported build
environment. Pass `-SdkRoot`, `-SdkVersion`, and `-WdkRoot` for another installation.

Installation is deliberately a separate step. An unsigned `.sys` cannot normally
load on this Windows x64 installation. Development signing may require changing
Windows boot security settings, installing a test certificate, and restarting.
After approval, the development certificate was installed into Local Machine
My, Root, and TrustedPublisher, and test signing was enabled for the next boot.
Its thumbprint and installation stage are recorded in
`artifacts/installation/state.json`; the certificate expires on January 3, 2027.
Rebuilding produces an unsigned image again; rerun the installer to sign it.

For installation or verification, run this command from the project directory. It prompts
for administrator access, checks the running Code Integrity mode, verifies the
package signatures, and installs the root device only when test mode is active:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/install-development-driver.ps1
```

The installation log and state are in `artifacts/installation/`. After installation,
run `python -m precision_bridge --native` from an administrator terminal.

The installer also updates an existing bridge device after a rebuild. Native
mode prints Windows negotiation and submission counters. `mode=3`,
`surface=True`, increasing `submitted`, and `last-status=0x00000000` mean that
the virtual driver is forwarding reports successfully; these counters do not
by themselves prove that Windows recognized a gesture. Lift all fingers after
starting, then test two-finger scrolling on a long page and pinch in a browser.
If neither works, include the `Windows touchpad:` lines in the next report.

For rollback, stop the bridge and run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/remove-development-driver.ps1
```

This removes the independent virtual device and only this project's certificate.
It disables test signing if this project enabled it, taking effect after another
restart. Driver Store files may remain. The original ASUS touchpad driver is not
replaced by this package. If a forced termination interrupts restoration, restart
ASUS Smart Gesture or reboot.

## Remaining hardware validation

- Interactive capture on this laptop has confirmed contact counts from zero
  through five. Coordinate accuracy for each simultaneous contact still needs
  checking if gesture recognition fails.
- Contact IDs currently follow ASUS's slots. Stable slot identity through
  crossing fingers and partial lifts still needs verification.
- Native OS recognition, smooth pinch, scrolling, and three-finger gestures need
  testing with the signed driver loaded. Disabling ASUS gestures may affect its
  handling of pointer movement during multi-finger interactions; verify that
  there is no cursor drift or duplicate action.
- Surface size is derived from this laptop's reported geometry: 3420 × 2052
  counts, 810 counts/inch, approximately 107.2 × 64.3 mm. Other hardware needs
  descriptor calibration. `--invert-y` supports checking the axis orientation.
- Diagnostic contact bytes beyond active/X/Y remain incompletely interpreted.
  Palm rejection is not implemented in this bridge. Timing comes from arrival
  time because the text feed does not include hardware timestamps.
- This does not certify the old physical hardware or make it pass Microsoft's
  Precision Touchpad hardware tests.

## Reverse engineering

See [docs/reverse-engineering.md](docs/reverse-engineering.md) for binary hashes,
addresses, packet layouts, evidence, and unresolved questions.

For static inspection only:

```powershell
python -m pip install -r requirements-analysis.txt
python tools/inspect_pe.py 'ASUS Smart Gesture/AsTPCenter/x64/AsusTPApi.dll' --brief
```

The supplied proprietary binaries are inspected, not patched or redistributed.
The bridge uses its own Win32 calls and does not load the ASUS API DLL.

References: Microsoft's [Precision Touchpad collections](https://learn.microsoft.com/en-us/windows-hardware/design/component-guidelines/touchpad-required-hid-top-level-collections),
[contact/feature report requirements](https://learn.microsoft.com/en-us/windows-hardware/design/component-guidelines/touchpad-windows-precision-touchpad-collection),
[VHF input reporting](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/vhf/nf-vhf-vhfreadreportsubmit),
and [WDK setup](https://learn.microsoft.com/en-us/windows-hardware/drivers/download-the-wdk).
