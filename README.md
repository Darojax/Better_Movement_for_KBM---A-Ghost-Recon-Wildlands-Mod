# Better Movement for KBM

<img src="assets/images/better_movement_for_kbm_logo_small_and_trimmed.png" alt="Better Movement for KBM" width="360">

Better Movement for KBM is a Windows mod that gives mouse-and-keyboard players smooth, granular movement-speed control in *Tom Clancy's Ghost Recon Wildlands*.

Scroll the mouse wheel while moving to transition naturally from a very slow walk to a full jog. Aim-Down-Sight and crouched movement speeds are also adjusted to feel more consistent.

> **Release status:** `1.0.0-rc.1` is the initial public release candidate.

## Features

- One continuous mouse-wheel range from slow walking to full jogging.
- Native `X` walk/jog shortcut and normal sprint behavior remain available.
- Adjusted standing, crouched, and Aim-Down-Sight movement.
- Portable launcher supporting Steam and Ubisoft Connect installations.
- Live compatibility, SayNoToEAC, Easy Anti-Cheat, runtime, firewall, and save-backup checks.
- Optional Windows Firewall controls and edition-separated save backups.
- Exact-byte verification before modifying the game and supervised restoration when the mod stops.

## Requirements and installation

- Windows 10 or 11 x64.
- A legitimate Steam or Ubisoft Connect copy of Ghost Recon Wildlands.
- [Microsoft .NET 8 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/8.0).
- SayNoToEAC by SunBeam, installed separately.
- Offline single-player only.

Download the portable release, extract the complete ZIP anywhere, and run `Better Movement for KBM - GRW.exe`. See the [packaged installation and usage guide](release/README.md) for setup, controls, safe shutdown, and removal.

Before playing, open **Settings → Key Mapping → Player Combat** and reassign **Next Weapon** and **Previous Weapon** away from the mouse wheel.

## Important safety warning

This unofficial mod temporarily modifies the running `GRW.exe` process. Ubisoft or its anti-cheat systems may treat process-memory modification as prohibited activity. No launcher check, firewall rule, offline procedure, or anti-cheat replacement can guarantee account safety.

Never use the mod in co-op, Ghost War, or any online session. Back up saves before use and read the full risk notice and disclaimer in the [packaged README](release/README.md).

## Building from source

The launcher and supervised runtime target .NET 8 for 64-bit Windows. Building requires the .NET 8 SDK or newer:

```powershell
dotnet build .\BetterMovementForKBM.slnx -c Release
```

Create the framework-dependent portable and source archives with:

```powershell
.\build-release.ps1
```

Generated output is written beneath `artifacts/` and is not tracked by Git. The portable package contains a single-file launcher, a single-file runtime helper, and one README.

## Repository layout

- `GRWLauncher/` — WPF launcher, checks, storefront launch, backups, firewall controls, and runtime supervision.
- `GRWMovementRuntime/` — verified movement hook and restoration runtime.
- `assets/` — launcher icon and interface artwork.
- `docs/` — architecture and maintainer release checklist.
- `release/` — packaged user guide and Nexus page copy.

See [Architecture](docs/ARCHITECTURE.md) for the component and safety model. The paste-ready BBCode listing is in [Nexus description](release/NEXUS_DESCRIPTION.txt).

## Source, support, and license

The launcher performs no automatic downloads, telemetry, or update checks. Its external SayNoToEAC links open only after a user clicks them.

Report reproducible problems through GitHub Issues or the Nexus Mods page. Include the game edition, launcher status text, and relevant activity-log output, but never upload personal save data.

Released under the [MIT License](LICENSE.txt). Better Movement for KBM is developed by Codex & Darojax. SayNoToEAC is a separate third-party project by SunBeam and is not included.
