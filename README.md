# Better Movement for KBM — A Ghost Recon Wildlands Mod

> **Release candidate:** gameplay is working in extended local testing; clean-machine, recovery, packaging, and final compatibility validation remain before the first public release.

<img src="assets/images/nomad_1.png" alt="Nomad in Ghost Recon Wildlands" align="left" width="280" hspace="16">

A Windows mod that gives keyboard-and-mouse players granular movement-speed control in *Tom Clancy's Ghost Recon Wildlands*.

The mouse wheel traverses one continuous range from a very slow walk to full jogging speed, while retaining the familiar `X` walk/jog shortcut. Before using the mod, open **Settings → Key Mapping → Player Combat** and reassign **Next Weapon** and **Previous Weapon** to controls other than the mouse wheel. The keyboard's `1`, `2`, and `3` keys remain available for direct weapon selection.

### Current behavior

- 16 near-uniform walking levels from `0.05` to `0.60`, including the exact vanilla-walk anchor at `0.35`.
- 11 evenly spaced jogging levels from `0.70` to `1.00`.
- One unified mouse-wheel ladder, independent of the game's hidden native gait.
- Mouse-wheel weapon cycling is replaced by movement-speed control; weapon-slot keys `1`, `2`, and `3` remain available.
- `X` jumps from a walking-range speed to vanilla full jog, or from a jogging-range speed to vanilla walk.
- Releasing sprint returns movement to vanilla full jog.
- Standing ADS follows the underlying HIP level, with a deliberately limited maximum speed.
- Crouching is detected automatically and uses separately calibrated walk-ADS and jog-ADS behavior.
- Wildlands' downstream weapon-dependent speed differences remain; the shared tuning has been validated with an LMG, assault rifle, and pistol.
- The launcher's adaptive green button disables the runtime and restores the original game instructions.

<br clear="left">

## Safety warning

This unofficial mod writes to the running `GRW.exe` process and is intended exclusively for offline single-player use. Memory modification may violate Ubisoft's terms or trigger anti-cheat, integrity, telemetry, or security systems. No anti-cheat modification, firewall rule, or offline procedure can guarantee account safety.

Do not use it in co-op, Ghost War, or any online session. Read the full [risk notice](release/DISCLAIMER.txt) before testing.

## Project status and documentation

- [Development TODO and regression contract](TODO.md)
- [Release checklist](RELEASE_CHECKLIST.md)
- [Changelog](CHANGELOG.md)
- [Runtime, installation, and shutdown notes](release/README.md)
- [License](LICENSE.txt)

The repository includes the production launcher, supervised runtime, portable release-building script, and the focused research utilities used to locate and validate Wildlands' movement values. Generated binaries, memory captures, debugger packages, screenshots, and test logs are intentionally excluded.

## Building from source

The launcher and runtime target .NET 8 for 64-bit Windows:

```powershell
dotnet build .\GRWLauncher\GRWLauncher.csproj -c Release
dotnet build .\GRWMovementRuntime\GRWMovementRuntime.csproj -c Release
```

The [launcher](GRWLauncher/README.md) performs live installation and safety checks, launches either the Steam or Ubisoft Connect edition, and supervises clean runtime restoration. Merely opening it is read-only; firewall changes, backups, launch, and attachment require explicit actions.

To assemble local development packages:

```powershell
.\build-release.ps1
```

The build script writes generated packages beneath `artifacts/`, which is not tracked by Git.

The small portable package is framework-dependent and requires the Microsoft .NET 8 Desktop Runtime x64. End users do not need the .NET SDK.
