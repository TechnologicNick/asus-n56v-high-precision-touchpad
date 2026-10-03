# Release status and safety

## Current scope

0.2.0-preview.1 is a developer preview for the ASUS N56V family, validated on
one N56VM with Windows 10 x64. Native gestures have been interactively tested;
the C# migration has automated behavioral checks and a live non-elevated
interface/restoration smoke test. Broader hardware and Windows-version claims
are not made. Windows 10 19041+ is the build minimum, not a promise that every
supported build or ASUS driver revision has been tested.

## Signing boundary

The bundled virtual driver is test-signed. The default installer refuses to
trust/install it silently. A new machine needs deliberate developer setup and
Test Mode; the preview flag is not a way to bypass Code Integrity. Setup never
automatically disables Secure Boot, HVCI or signature enforcement. If Test Mode
is already active, `Install.ps1 -DeveloperPreview` explicitly trusts the
development certificate embedded in this driver's catalog.

A normal consumer release requires a Microsoft-signed kernel package through
the Hardware Developer Center and applicable testing/signing requirements.
The build refuses to promote a test-signed package to a non-preview version.
See Microsoft's current [driver signing policy](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/kernel-mode-code-signing-policy--windows-vista-and-later-)
and [signing options](https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/driver-signing-offerings).
An account/certificate/submission owned by the maintainer is required; this
repository does not include signing credentials or claim certification.

## Runtime permissions

The kernel driver starts with SYSTEM/Administrator access only. During explicit
setup, an elevated helper assigns a per-device security descriptor granting the
chosen user's SID read/write access. The app/worker are asInvoker and need no
UAC prompt at sign-in. No SYSTEM broker, service, elevated scheduled task,
Everyone/Users grant, or vendor-driver ACL modification is introduced.

That SID can submit bounded virtual touchpad reports. Any program running as
that user could do so too; virtual input is a security-sensitive capability.
Only grant users you trust. Exclusive opens, IOCTL/report validation, contact
limits, and disconnect/stall/power releases remain enforced by the driver.
The administrative setup saves the previous descriptor in ProgramData and
`set-driver-user-access.ps1 -Remove` restores it (or the restrictive default).
Driver updates/reinstallation can require rerunning the permission setup.

## Public-release checklist

- Microsoft-sign the final kernel package and verify the complete catalog on a
  clean system with normal boot security enabled.
- Test initial root installation, upgrade, failure rollback, revoking access,
  multi-user behavior and uninstall on clean machines. Current setup scripts
  have been exercised on the existing development installation, not all paths.
- Test real sign-in startup/retry, suspend/resume, shutdown, fast startup, vendor
  restarts and contact-slot stability under crossing fingers.
- Validate HVCI/Driver Verifier and Windows-supported signing/toolchain versions.
- Upgrade the Windows App SDK 1.6/toolchain to a supported release and revalidate
  on the target Windows builds. This preview retains the UI version proven on
  the original laptop; the bundled .NET 8 runtime uses servicing patch 8.0.31.
- Decide on Authenticode signing for the desktop/worker to improve publisher
  identity; no claim of signed desktop binaries is made.
- Publish ZIP hash, changelog, tested hardware/driver versions and WTFPL v2 license.
- Run the self-contained extracted app from a location outside the source tree.

## Data and uninstall

No network, telemetry service or cloud account is used at runtime. Settings,
bounded logs and latest status snapshots live in LocalAppData/N56PrecisionBridge.
The live preview is an in-process stdout stream, not a retained contact history.

Ordinary Windows two-finger tapping is disabled as an intentional per-user
preference, also affecting other Precision Touchpads on that account. It stays
off after exit/uninstall; re-enable it in Windows Touchpad settings if desired.
The physical ASUS preferences are otherwise restored on graceful exit.

Uninstall removes the sign-in entry and can revoke the driver grant. It retains
app files/preferences/logs for recovery and does not automatically alter boot
security or uninstall the original ASUS driver. Removing a release-preview
certificate or leaving Test Mode is a separate explicit administrator decision.
