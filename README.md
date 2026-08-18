# Better Movement for KBM

<img src="assets/images/better_movement_for_kbm_logo_small_and_trimmed.png" alt="Better Movement for KBM" width="360">

Better Movement for KBM gives mouse-and-keyboard players smooth, granular movement-speed control in *Tom Clancy's Ghost Recon Wildlands*.

Scroll the mouse wheel to select movement speed, whether moving or standing still, and transition naturally from a very slow walk to a full jog. Standing, crouched, and Aim-Down-Sight movement speeds are adjusted to feel more consistent.

> **Current release:** `2.1.1`, for game version `133.1.0.9840374` / Steam build `24669148`.

## Features

- One continuous mouse-wheel range from slow walking to full jogging.
- Uses the Walk/Jog key configured inside Ghost Recon Wildlands.
- Sprinting restores full jogging speed.
- Calibrated standing, crouched, and Aim-Down-Sight movement.
- Live, persistent mouse-wheel sensitivity adjustment with an on-screen slider.
- Brief acceleration and deceleration smoothing above sensitivity 50, without changing calibrated destination speeds.
- Configurable sensitivity shortcuts through a documented INI file.
- Exact instruction verification before any game code is changed.
- Native in-process ASI runtime with no launcher, configuration program, or external process-memory access.

## Installation

1. Exit Ghost Recon Wildlands.
2. Download and extract the current v2.1 ASI package.
3. Copy `BetterMovementForKBM.asi`, `BetterMovementForKBM.ini`, and `winmm.dll` beside `GRW.exe`.
4. Launch the game normally through Steam or Ubisoft Connect.

No launcher, .NET runtime, firewall rule, or SayNoToEAC installation is required.

Use `F6` to decrease wheel sensitivity, `F7` to display its current value, and `F8` to increase it. The setting is shown in-game and saved automatically. These shortcuts can be changed in `BetterMovementForKBM.ini`.

Before playing, open **Settings → Key Mapping → Player Combat** and reassign **Next Weapon** and **Previous Weapon** away from the mouse wheel.

To uninstall, exit the game and delete `BetterMovementForKBM.asi`, `BetterMovementForKBM.ini`, and `winmm.dll`. If another mod uses the same ASI loader, keep `winmm.dll` and remove only this mod's ASI and INI files.

## Safety

Use this unofficial mod only in single-player. Do not use it in co-op or Ghost War. Back up important saves before using any game modification.

The ASI modifies movement instructions inside the running game process. This is normal for this type of mod, but unofficial modifications are always used at the player's own risk.

## Building

Build the native x64 release with Visual Studio's C++ toolchain:

```powershell
& 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe' `
    .\BetterMovementASI\BetterMovementASI.vcxproj /p:Configuration=Release /p:Platform=x64
```

Create the Nexus package with:

```powershell
.\build-asi-release.ps1
```

Generated output is written beneath `artifacts/` and is not tracked by Git.

## Project layout

- `BetterMovementASI/` — native x64 movement runtime.
- `docs/ARCHITECTURE.md` — runtime architecture and compatibility model.
- `docs/RELEASE_CHECKLIST.md` — maintainer release procedure.
- `release/README.txt` — packaged installation, usage, safety, and third-party license text.
- `release/NEXUS_DESCRIPTION.txt` — paste-ready Nexus Mods page copy.
- `build-asi-release.ps1` — reproducible minimal-package build.

The former v1.x launcher and external runtime remain available in the `v1.2.0` Git tag.

## Credits and license

Better Movement for KBM is developed by Codex & Darojax and released under the [MIT License](LICENSE.txt).

The package includes [Ultimate ASI Loader](https://github.com/ThirteenAG/Ultimate-ASI-Loader) by ThirteenAG, distributed under its MIT License. Its full license notice is included in the packaged README.
