# Development and testing

The runtime is C#/.NET 8 + WinUI 3; the kernel component is C/KMDF + VHF.
Builds target x64 Windows 10 build 19041 or later. Hardware validation to date is
on the N56VM and its ASUS Smart Gesture 1.0.3.82 API / AsusTP.sys 1.0.0.148.

## Build the desktop and worker

Install Visual Studio 2022, the C++ desktop toolchain, the .NET 8 SDK, WinUI/
Windows desktop application tools and a Windows SDK. Packaging uses the Visual
Studio x64 MSBuild, because a plain `dotnet publish` of WinUI omits required PRI
packaging tasks on this SDK/toolchain combination.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/build-desktop.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test.ps1
```

The result is `artifacts/desktop/N56Precision.exe` with a self-contained worker
in its `worker` subdirectory. Keep the whole output directory together.

`tools/test-desktop-ui.ps1` runs an isolated, non-hardware UI-only build. Build
that variant using Configuration=UiTest, Platform=x64,
ApplicationManifest=ui-test.manifest and PublishDir=artifacts/ui-test/. The tests
exercise sidebar tabs, switch interaction, timeline accessibility, close-to-tray
and the tray-reopen handler. UI-test mode never starts the hardware worker.

Self-contained publishes pin the .NET 8.0.31 servicing runtime, independent of
the older locally installed SDK/runtime used by the build tools. Keep this pin
updated from Microsoft's .NET 8 release metadata. Framework-dependent unit
tests can use the installed developer runtime.

After exiting the tray app, hardware smoke testing is optional:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test.ps1 -HardwareSmoke
```

It runs the C# worker for three seconds as the ordinary user, checks the live
ASUS/native interfaces, and compares all eighteen non-debug ASUS DWORDs before
and after. Run it only on supported hardware with the virtual driver installed.

## Build and install a development driver

The original reproducible driver scripts use Visual Studio C++, a local
hash-verified WDK package and SDK 10.0.22621.0. Defaults were tested with KMDF
1.15 and WDK 10.0.26100.0; use matching SDK/WDK versions for your supported
development toolchain.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/fetch-wdk.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/build-driver.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/install-development-driver.ps1
```

The last command is explicitly for development. It creates/trusts a local test
certificate, may enable Test Mode for the next boot, and records rollback state
under artifacts/installation/. Restart yourself if instructed, then rerun it.
It never replaces the physical ASUS driver. Exit the tray app before updating.

After installation, run `tools/set-driver-user-access.ps1` as your intended
ordinary account and accept its one-time UAC prompt. It grants only that SID
read/write access to the bridge device. Do not add Everyone/Users to the ACL.

Rollback for that development installation:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/remove-development-driver.ps1
```

It discovers the virtual root by hardware ID, removes only the project's
certificate, and disables Test Mode only if the recorded installation enabled
it. Check the log; a reboot may be required. The original ASUS package is kept.

## Release build

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/build-release.ps1
```

This runs C# tests, builds both self-contained programs, copies an allow-listed
driver/setup/documentation set, and emits a ZIP and per-file SHA-256 manifest.
An existing release directory is not overwritten. No user files, logs, signing
keys, original ASUS binaries, WDK or Visual Studio files are included.

Source layout:

- desktop/N56Precision: WinUI tray/settings application.
- desktop/N56Precision.Core: decoder, report encoder, gesture filters, IOCTL/
  pipe clients, settings, startup registration and worker runtime.
- desktop/N56Precision.Worker: console entry point for the native C# worker.
- desktop/N56Precision.Tests: dependency-free regression/descriptor/hardware tests.
- driver: KMDF/VHF source, descriptor and INF.
- tools: build/setup/verification helpers; inspect_pe.py is optional static analysis.

The historical Python runtime is superseded and removed. Its behavior was
ported into the C# tests; git history retains the original implementation.
