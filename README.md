# N56 Precision

A deliberately opinionated gesture bridge for the ASUS N56V/N56VM touchpad.
It combines Windows-native scrolling, pinch and swipes with the familiar ASUS
gestures, controlled from a WinUI 3 tray app.

**0.2.0-preview.1 — developer preview, not a production-signed driver release.**
Tested on an N56VM with Windows 10 x64 and the existing ASUS Smart Gesture
package. Other hardware/driver combinations are not certified or claimed to work.

The app and worker are now entirely C#. No Python, PyInstaller, Visual Studio
or .NET installation is needed to run the complete self-contained app package.
Only the virtual-driver installation and per-user permission setup require an
administrator. The tray app itself runs as your ordinary account.

## What it does

- Windows receives real multi-contact HID reports and recognizes native
  scrolling, pinch and swipes. The native path does not synthesize zoom keys.
- ASUS retains one-finger pointing/clicks and optional original gestures.
  Fourteen ASUS switches and separate Windows forwarding switches for 2–5
  fingers let you choose the mix. Windows takes priority for an enabled count.
- Bottom 15% button fingers are excluded from gesture counting, with reversed
  sensor Y by default. Active scrolling/pinch fingers can cross that boundary
  without stopping. Two separate gesture fingers can scroll while another
  finger holds a button in the strip; button + one pointing finger cannot scroll.
- Ordinary two-finger right-click is disabled. Hold one finger for 40 ms, tap
  another for no more than 200 ms, and move neither more than 2 mm to right-click.
  All thresholds are configurable; the first finger must remain down.
- A sensor-frame-speed preview and retained hold/tap timeline explain actual
  timing, peak movement and rejection reasons. Stationary retouch reaches Windows
  immediately, so you can stop scroll momentum without moving your fingers.
- Closing hides to the tray. Startup at sign-in is optional in the Overview tab;
  the installer enables it by default. Pause/exit restores the ASUS preferences.

Defaults match TechnologicNick's preferred hybrid: Windows for two/four fingers,
ASUS for three, five-finger forwarding off, legacy ASUS edge gestures off. Your
existing settings are preserved. Presets and individual switches can change this.
Five-finger forwarding does not guarantee a separate Windows five-finger action.
Original ASUS actions may still use their own keyboard shortcuts or depend on
application support; they are not reimplemented by the bridge.

## Requirements and installation

You need x64 Windows 10 build 19041+ and the compatible ASUS Smart Gesture
driver/companion already installed and running. This is not a replacement for
that package. The bridge depends on its validated raw-data pipe and settings
layout; only one bridge/capture client can own the feed.

For an extracted release package on a machine with the virtual driver installed:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1
```

Setup copies the app to `%LOCALAPPDATA%\Programs\N56Precision`, grants your
specific Windows account access to the virtual driver through a one-time UAC
helper, disables the actual Windows two-finger tap checkbox and starts in the
tray. Accept the driver-permission prompt. Do not run the whole installer under
another administrator account: setup is per-user. `-NoStartup` skips sign-in
registration. Existing settings/logs remain in `%LOCALAPPDATA%\N56PrecisionBridge`.

### Important: the preview driver

The bundled driver is test-signed. A first installation of it requires deliberate
developer setup and Windows Test Mode. The normal installer refuses to silently
trust a test certificate. `Install.ps1 -DeveloperPreview` opts into trusting this
preview signer **only if Test Mode is already active**; it never disables Secure
Boot, HVCI or signature enforcement. See [development setup](docs/development.md)
and the [release/security limitations](docs/release.md) before doing this.

A standard consumer release still needs a Microsoft-signed kernel package.
The current ZIP is an explicitly labeled preview, not a workaround for that
requirement. Signing credentials, private keys and original ASUS binaries are
not distributed.

## Using it

Click the tray icon to open the sidebar. Overview contains presets/startup;
Windows and ASUS pages contain switches; Buttons & right-click contains the
strip, live contacts and timing controls; Diagnostics contains status and logs.
Changes save automatically and apply when all fingers lift. The app retries
missing driver/ASUS startup for roughly one minute, then shows its last error.

Physical button ownership stays with ASUS. Resting in the strip does not create
a synthetic click. The custom hold + tap right-click uses a matched mouse-button
down/up pair, not a keyboard shortcut. Release/disconnect/watchdog handling
prevents the virtual driver retaining stuck contacts.

Windows' two-finger tap preference is intentionally per-user, including other
Precision Touchpads on that account, and stays off after pause/exit/uninstall.
If it remains active after a bridge restart, change the actual Windows Touchpad
checkbox or run `tools/disable-windows-two-finger-tap.ps1`; a registry write alone
did not refresh the Windows 10 cache on the tested machine. This does not affect
the bridge's independent hold + tap recognizer or momentum cancellation.

## Uninstall / rollback

Exit from the tray first, then run the installed app's
`setup/uninstall-app.ps1`. It removes only this app's sign-in entry and offers
driver-permission rollback via UAC. App files, preferences and logs are retained
for recovery; delete that dedicated app directory yourself if desired. No
original ASUS driver is removed and no Windows tap preference is silently restored.

Developer-driver/test-signing rollback is separate and documented in
[development.md](docs/development.md). Do not remove another driver's certificate
or disable Test Mode blindly on a shared development machine.

## Build and test

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/build-desktop.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/build-release.ps1
```

See [development.md](docs/development.md) for toolchain, UI tests, optional live
hardware tests and driver building. [reverse-engineering.md](docs/reverse-engineering.md)
records the vendor protocol/settings evidence and historical development.
The C# worker also supports `--help`, `--version`, `--check-driver`, non-injecting
capture without `--native`, and `--native --seconds 30` for a bounded native run.
Do not run it alongside the tray worker.

The release builder runs tests and creates a curated ZIP with a SHA-256 file
manifest, license and notices. It excludes proprietary ASUS assets, user settings,
logs, certificates/private keys, Python, and development tools. No cloud service
or network connection is used by the runtime.

## License

WTFPL v2 (Do What The Fuck You Want To Public License) — Copyright (c) 2026 TechnologicNick. See [LICENSE](LICENSE) and
[third-party notices](THIRD-PARTY-NOTICES.md). ASUS and Microsoft components retain
their own ownership/licenses. This project is not affiliated with ASUS or Microsoft.
