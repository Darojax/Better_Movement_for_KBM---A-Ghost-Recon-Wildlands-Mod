# Better Movement for KBM — A Ghost Recon Wildlands Mod

> **Development preview:** this project is under active play-testing and is not ready for public distribution.

<img src="assets/images/nomad_1.png" alt="Nomad in Ghost Recon Wildlands" align="left" width="280" hspace="16">

A Windows mod that gives keyboard-and-mouse players granular movement-speed control in *Tom Clancy's Ghost Recon Wildlands*.

The mouse wheel traverses one continuous range from a very slow walk to full jogging speed, while retaining the familiar `X` walk/jog shortcut.

### Current behavior

- 16 near-uniform walking levels from `0.05` to `0.60`, including the exact vanilla-walk anchor at `0.35`.
- 11 evenly spaced jogging levels from `0.70` to `1.00`.
- One unified mouse-wheel ladder, independent of the game's hidden native gait.
- `X` jumps from a walking-range speed to vanilla full jog, or from a jogging-range speed to vanilla walk.
- Releasing sprint returns movement to vanilla full jog.
- Standing ADS follows the underlying HIP level, with a deliberately limited maximum speed.
- Crouching is detected automatically and uses separately calibrated walk-ADS and jog-ADS behavior.
- Wildlands' downstream weapon-dependent speed differences remain; the shared tuning has been validated with an LMG, assault rifle, and pistol.
- `F5` restores the original game instructions and exits the runtime.

<br clear="left">

## Safety warning

This unofficial mod writes to the running `GRW.exe` process and is intended exclusively for offline single-player use. Memory modification may violate Ubisoft's terms or trigger anti-cheat, integrity, telemetry, or security systems. No anti-cheat modification, firewall rule, or offline procedure can guarantee account safety.

Do not use it in co-op, Ghost War, or any online session. Read the full [risk notice](release/DISCLAIMER.txt) before testing.

## Project status and documentation

- [Development TODO and regression contract](TODO.md)
- [Runtime, installation, and shutdown notes](release/README.md)
- [License](LICENSE.txt)

The repository includes the runtime, safety utility, installer definition, release-building script, and the focused research utilities used to locate and validate Wildlands' movement values. Generated binaries, memory captures, debugger packages, screenshots, and test logs are intentionally excluded.

## Building from source

The runtime and safety utility target .NET 8 for 64-bit Windows:

```powershell
dotnet build .\GRWMovementRuntime\GRWMovementRuntime.csproj -c Release
dotnet build .\GRWSafetySetup\GRWSafetySetup.csproj -c Release
```

To assemble local development packages:

```powershell
.\build-release.ps1
```

The build script writes generated packages beneath `artifacts/`, which is not tracked by Git.
