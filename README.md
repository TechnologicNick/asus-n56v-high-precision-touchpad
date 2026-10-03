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

## WinUI 3 tray application

Build the unpackaged, self-contained x64 desktop application with the .NET 8
SDK and Windows desktop/WinUI development components installed:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/build-desktop.ps1
```

Launch `artifacts/desktop/N56Precision.exe` and accept the administrator prompt.
Stop any manually running bridge first: the ASUS feed and virtual endpoint are
exclusive. The app starts a hidden Python worker using this project's `.venv`.
Keep the output within this project (or set `N56_BRIDGE_ROOT` to the project).
`--tray` starts with the settings window hidden. Closing the window hides it;
click the notification-area icon to reopen it. Right-click to pause/resume or
exit. Pause and exit stop the worker gracefully and restore ASUS preferences.
The sidebar separates Overview, Windows gestures, ASUS gestures, Buttons &
right-click, and Diagnostics. The Buttons page includes a live contact view;
orange contacts are reserved for buttons and green contacts are in the gesture area.
If the app crashes, its worker notices the parent process exit and restores
settings. No driver replacement, startup task, or login registration is added.

All fourteen ASUS gesture settings have switches. Windows forwarding can be
enabled independently for two, three, four, and five contacts. When Windows
owns a finger count, ASUS gestures with that count are muted to avoid duplicate
actions. Their switch choices are remembered, and the UI shows the override.
The Hybrid preset uses Windows for two/four/five fingers and ASUS for three.
The switches enable the original ASUS implementation, not re-created gestures.
Legacy edge actions and rotation still depend on ASUS/target-application support.
Five-finger forwarding does not create a new Windows five-finger action.

The bottom button zone defaults to 15% of touchpad height and is configurable
from 0 to 100% in the app; 0 disables it. Contacts entering either half of that
strip are excluded from native gesture counting until they lift. One pointing
finger plus a button-zone finger therefore does not become two-finger scrolling.
Once a native scrolling/pinch gesture starts, its participating fingers ignore
the strip until each lifts. Entering the strip no longer cuts off an active
gesture; newly resting button fingers still get excluded, and physical clicks
activate the drag guard unless a separate reserved button finger is present
alongside at least two gesture-area fingers. That combination allows two-hand
scrolling while holding left/right click; a button finger plus only one other
finger still does not scroll.
The original ASUS driver still handles physical left/right clicks; resting a
finger does not synthesize a click. Native CLI users can set
`--button-zone-percent 20` (default 15) without a configuration file. The
configuration key is `buttonZonePercent`; old settings files default to 15.
The zone follows the same `--invert-y` sensor orientation as HID encoding.
The `buttonZoneAtLowY` setting ("Reverse button-zone sensor Y" in the app)
reverses just the zone interpretation if the live view shows the sensor bottom
at the top. Reversed button-zone Y is the default for this ASUS sensor.
Holding either physical mouse button blocks Windows gesture
forwarding unless a separate reserved button contact accompanies at least two
gesture contacts. Without that exception, after release,
forwarding resumes once fewer than two fingers remain, avoiding an accidental
scroll at the end of a drag.

When using original ASUS multi-finger actions, the worker mutes those actions
after observing a button-zone contact and keeps them muted until all fingers
lift. Because ASUS consumes hardware before publishing the diagnostic feed,
that suppression cannot guarantee interception of its first simultaneous
contact frame. The native Windows path filters contacts before submission and
does not have that race. Hardware testing of button-zone behavior is still needed.

Changes are saved atomically to `%LOCALAPPDATA%/N56PrecisionBridge/settings.json`
and applied after all fingers lift. If a disabled count interrupts a Windows
gesture, forwarding resumes only after all contacts lift. Invalid configuration
updates leave the last working configuration active and appear in the UI.
Logs and status files are in the same directory. `worker.log` is size-bounded.
Status updates are best-effort: Windows file-lock/access errors skip a telemetry
update rather than terminating input forwarding. The next update retries.
Native CLI behavior without `--config` still forwards all multi-finger counts,
with the new button-zone and hold + tap filters applied.

Hold + tap right-click is enabled by default: keep one finger down for at least
50 ms, then tap a second finger for at most 250 ms, with no more than 2 mm of
movement. The first finger must remain down when the second lifts. All three
thresholds and the feature switch are configurable in the app. A third finger,
excessive motion, or a long second-finger hold cancels the click. Repeated second
taps work without lifting the first finger. A held physical mouse button also
cancels the custom click to avoid right-clicking while dragging. The second tap may be in the button
zone; the zone still prevents it from becoming a scrolling contact.

Ordinary ASUS two-finger tapping is always suppressed. Starting native mode
also disables Windows' documented **per-user** `TwoFingerTapEnabled` preference,
including for other Precision Touchpads used by that Windows account. This
preference intentionally persists after pausing/exiting, as requested; it can
be re-enabled in Windows touchpad settings. Stationary two-finger pairs are
forwarded immediately to Windows so placing fingers back down can cancel
scroll momentum without movement. The tap tolerance still controls the custom
right-click recognizer and when scrolling fingers gain the bottom-zone exemption;
it no longer withholds native contact reports.
Custom right-click uses a matched `SendInput` mouse-button down/up pair; it does
not simulate keyboard shortcuts. Interactive gesture validation is still needed.

The contact preview receives every sensor frame through the worker's stdout
stream, separate from best-effort status files. The UI draws the latest frame
every 16 ms while the Buttons page is visible; older render frames are not queued.
The hold + tap timeline retains the last attempt's first-finger hold, second-finger
duration, peak movement, active thresholds, and acceptance/rejection reason.

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
