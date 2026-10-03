# Findings from this N56VM

Inspected on Windows 10 Pro 10.0.19045, 2026-10-03.

## Hardware and installed software

| Property | Observed value |
|---|---|
| Computer | ASUSTeK N56VM |
| Touchpad | ASUS PS/2 Port Clickpad |
| PnP ID | `ACPI\ETD0108` |
| Driver service | ATP, running |
| Driver version | AsusTP.sys 1.0.0.148 |
| Companion API | AsusTPApi.dll 1.0.3.82 |
| Reported surface | 3420 × 2052 counts |
| X/Y resolution | 810 counts/inch |

The copied driver INF installs an `ATP` upper filter on the Microsoft PS/2 mouse
stack. It supports a list of ELAN `ACPI\ETD...` devices, including this laptop.
It does not describe a native HID Precision Touchpad.

The installed and copied x64 API DLLs have identical SHA-256 hashes:
`33d2d6b9ca7912d177a0afb49256b967e96a247604ee894179f246369262b6c2`.
The copied Windows 8 x64 driver has SHA-256:
`5e76cde2b3c852755f6e54af774e9becdf472103d83b815899333de268536b98`.

## Companion's input/output design

`AsusTPApi.dll` opens `\\.\AsusTP` and uses `DeviceIoControl`. Its imports include
`keybd_event`, `SendInput`, `mouse_event`, `InitializeTouchInjection`, and
`InjectTouchInput`. The keyboard output worker at RVA `0x10fc0` sends queued key
press/release events using `keybd_event`; it also dispatches wheel/input events.
This confirms the companion contains keyboard and mouse translation paths.
The precise application-specific Ctrl-plus zoom selection was not exhaustively
traced; the user's observation of that behavior is consistent with these paths.

It also exposes an existing raw contact diagnostic interface. No binary patch,
DLL injection, or undocumented function call ABI is needed to access it.

## Named pipes

`CreateTPSrv` (RVA `0x29a0`) calls the pipe initializer at `0x10ba0`.
It creates one instance each of two duplex **byte-mode** pipes:

```text
\\.\pipe\ASUS_Smart_Gesture_Raw_Data_Request_Cmd
\\.\pipe\ASUS_Smart_Gesture_Raw_Data_Request_Report
```

The command worker at RVA `0x10910` reads up to 1024 bytes, adds a terminating NUL,
and compares the entire buffer with command strings using `strcmp`.
Commands must be individual ASCII writes without newline terminators.

| Command | Observed/static behavior |
|---|---|
| `GetLogicalSize` | Writes `LogicalSize=%d,%d` to the report pipe |
| `StartListeningData` | Sets raw reporting enabled; sets user-settings DWORD 0 to 1 |
| `StopListeningData` | Clears raw reporting; sets user-settings DWORD 0 to 0 |
| `EnableHandwrite` | Sends category 6 settings to the driver |
| `DisableHandwrite` | Sends category 6 settings to the driver |

The bridge uses the first three commands only. Start/stop call the companion's
`WriteUserSetting`, which also updates the persisted debug flag. On this machine
that flag was zero before and after capture. Other preferences were preserved.
The stream is diagnostic and has neither terminators nor hardware timestamps.

## Contact serialization

`SendDbginfo` at RVA `0x11750` begins a record with `Data=5` and appends five groups:

```text
Data=5,active,x,y,byteA,byteB,active,x,y,byteA,byteB,...
```

There are always five slots, not necessarily five active fingers. The binary
contact stride is 24 bytes. Relative to the input pointer passed to `SendDbginfo`,
the first slot uses active at `+0x0a`, X at `+0x0c`, Y at `+0x10`, and diagnostic
bytes at `+0x14` and `+0x15`. Each subsequent slot advances by `0x18`.

Example captured from this laptop:

```text
Data=5,1,1580,895,3,33,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0
```

The active flag becomes zero on lift-off, while old coordinates may remain.
The meaning of byteA/byteB is not fully established. They resemble contact size
and pressure, but the implementation uses neither to classify palms.

Because this is a byte stream, `ReadFile` calls can combine or fragment writes.
`StreamDecoder` emits a frame once its 25th comma has arrived. At that point every
field needed for active/X/Y is complete. It ignores the fifth slot's last
diagnostic byte, so no timing guess or delimiter is needed to detect final
finger-up. It consumes that ignored field until the next `Data=` marker.

## Driver control protocol

`AsusTPApi.dll` RVA `0x10860` and `WriteUserSetting` at `0x3700` send
IOCTL `0x221594`, with a `0x314`-byte packet. The driver dispatch reaches the
configuration handler (driver RVA `0x65e4`).

```c
struct Header {
    uint32_t operation;    // 0 = get, 1 = set
    uint32_t category;     // 0 = user settings, 6 = handwriting settings
    uint32_t version;      // 1
    uint32_t packet_size;  // 0x314
};
// category 0: nineteen DWORDs at offset 0x10
```

Getter RVA `0x62a4` and setter `0x6384` copy the category-0 `0x4c`-byte settings
block. The bridge mutes DWORD indices 5–14 only: two-finger tapping, pan, zoom,
rotate, three-finger actions, and edge swipes. It retains the whole original
packet for restoration. Live get/set/get/restore/get verified that the restored
packet exactly equaled the original. These direct calls do not write registry
preferences. The pipe's normal disconnect handler also reapplies the companion's
own settings, providing recovery after a client exits.

Read-only IOCTL `0x221408` returns a `0x420`-byte hardware-properties block.
Relevant offsets, confirmed on this machine:

| Offset | Value | Interpretation |
|---|---:|---|
| `0x404` | 810 | Y resolution |
| `0x408` | 810 | X resolution |
| `0x40c` | 2052 | Y maximum |
| `0x410` | 3420 | X maximum |

`GetHardwareProperty` at API RVA `0x1840` divides maxima by resolution and
multiplies by 2.54, consistent with resolution in counts/inch and conversion to
centimeters. The descriptor uses rounded physical dimensions 107.2 × 64.3 mm.

## Native Windows path

The installed Windows 10 `user32.dll` exports `CreateSyntheticPointerDevice` and
`InjectSyntheticPointerInput`, but **not** `CreateSyntheticPointerDevice2` or
`InjectTouchpadAction`. Microsoft's newer [touchpad injection example](https://learn.microsoft.com/en-us/windows/win32/input-precisiontouchpad/precision-touchpad-guide)
uses the newer API. Touchscreen injection would have different semantics.

The included KMDF driver uses VHF to expose two mandatory HID collections:
digitizer/touchpad and digitizer/configuration. Feature reports provide maximum
contact count, button type, a 256-byte uncertified status blob, input mode, and
selective reporting. Windows 10 permits an uncertified blob; this does not imply
hardware certification. The driver does not impersonate ASUS's vendor ID.

Input report 1 is 35 bytes including its report ID: five 6-byte contact slots,
scan time, contact count, and one reserved byte. Confidence is set for contacts;
the tip bit is cleared once on lift-off, retaining the previous coordinates and
ID. Contact count includes the lift records present in that frame. Single-finger
frames end virtual gestures while ASUS retains cursor movement and buttons.

## Verification and gaps

Completed: live pipe dimension query; 89,938 bytes of contact data captured in
one 20-second session; typed read-only hardware/settings queries; reversible
driver setting update; 11 decoder/report/descriptor tests; x64 kernel compilation
with warnings treated as errors; Windows INF validation; unsigned package build.

The test-signed driver has subsequently been installed with test mode active.
Windows reports the root device and its HID touchpad child as healthy; `sc query`
reports the driver running. Its administrator control endpoint opens successfully.
Windows assigned instance `ROOT\SYSTEM\0002`, despite hardware ID
`ROOT\N56PrecisionBridge`; installer and rollback discovery use the hardware ID.

Subsequent interactive testing confirmed all five contact counts and native
pinch, scrolling, and multi-finger gestures. The user reported the gestures
working after the descriptor's button usage and configuration switch collection
were aligned with Microsoft's sample. Driver diagnostics show Windows querying
capabilities/certification and selecting mode 3 with surface reporting enabled.
The exact original failure was not isolated to a single descriptor field.

The individual category-0 DWORD names were recovered from the registry-writing
function at API RVA `0x1bb0`: indices 1-4 single tap, double tap, second-tap move,
and tap-again drag release; 5 two-finger tapping; 6 pan; 7 zoom; 8 rotate;
9/10 three-finger up/down; 11 three-finger left/right; 12-14 top/left/right edge
swipes. Indices 15 onward (touchpad enable, mouse detection, tray state) are
preserved. The shared `gesture-catalog.json` records all fourteen gesture fields.

The desktop worker applies the configured ASUS values directly to the volatile
driver block, preserving one original snapshot across live updates. A live
hybrid-profile smoke test verified exact restoration of all non-debug fields.
Button-zone filtering applies before native report encoding; ASUS sees hardware
before diagnostic publishing, so dynamically muting its multi-finger gestures
cannot reliably intercept a first simultaneous frame. This limitation does not
apply to native Windows forwarding.

Unverified: stable slot identities under crossing fingers, power transitions,
cursor drift with hybrid profiles, interactive button-zone/right-click behavior,
and tray interactions. Unit tests cover routing, thresholds, contact releases,
settings validation and restoration; they do not replace hardware validation.

WinUI desktop startup testing exposed a packaging issue: SDK-style Publish
omitted the generated merged `N56Precision.pri`. Native debug output identified
failure to load the InfoBar default style from `generic.xaml`. Explicitly
publishing the generated PRI resolved startup, and UI Automation enumerated the
gesture switches and numeric controls in a running non-elevated UI-only build.
The subsequent UI smoke test passed switch interaction, close-to-tray, tray
message-handler reopen, and graceful UI-test exit. Physical icon clicks and
actual button-zone/hold + tap recognition still need user validation.

### C# runtime and release cleanup (0.2.0-preview.1)

The preceding implementation notes are historical. The Python worker has been
replaced by `desktop/N56Precision.Core` and `N56Precision.Worker`, keeping the
same pipe commands, decoder framing, settings IOCTL and 35-byte HID reports.
The regression suite now runs in C# with no Python requirement, including an
independent descriptor parser and exhaustive single-byte stream splits.

A live non-elevated C# smoke test opened both devices, enabled/stopped the pipe
feed, submitted native reports and verified exact restoration of the eighteen
non-debug ASUS DWORDs. A per-device SetupAPI security descriptor grants the
selected user's SID read/write while preserving SYSTEM/Admin access; the
kernel's default remains restrictive and its image did not need modification.
Microsoft documents administrative per-device overrides for driver-supplied
security descriptors. No ASUS ACL was changed.

Stationary contacts are forwarded immediately for inertia cancellation. Windows
10 was observed retaining its enabled two-finger tap state in memory even when
the HKCU value was zero; toggling the real Settings checkbox refreshed that
state. The setup helper changes only that checkbox. The custom right-click
recognizer remains separate, with current configurable defaults 40/200 ms and
2 mm. User validation confirmed the checkbox fix; broader hardware testing is
still needed before a consumer release.
