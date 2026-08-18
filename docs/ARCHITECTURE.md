# Architecture

Better Movement for KBM v2 is a native x64 ASI loaded into `GRW.exe` by Ultimate ASI Loader. It has no companion launcher, background service, telemetry, networking, or external process-memory component. A local INI beside the ASI stores wheel sensitivity and its three shortcuts.

## Startup and compatibility

The ASI confirms that its host is `GRW.exe`, validates the executable image boundaries, and compares every required instruction with the expected bytes for the supported build. Only after all checks pass does it allocate a local code cave and install the movement, gait-probe, and ADS redirects as one unit. Any failed write or verification restores the original instructions and releases the allocation.

Supported release target:

- Ghost Recon Wildlands `133.1.0.9840374`
- Steam build `24669148`
- Current Steam and Ubisoft Connect executables sharing that verified layout

Unsupported or modified executables are left untouched.

## Runtime

The runtime observes the game's native gait state, so the Walk/Jog binding configured in Wildlands remains authoritative. Mouse-wheel input selects the calibrated movement ladder, sprint restores full jogging speed, and the ADS redirect applies the standing and crouched calibration. Sensitivity scales wheel-step size without changing the selected speed or calibrated endpoints. A no-activate, click-through Windows overlay displays live sensitivity changes without hooking the renderer.

The worker waits until Wildlands has remained in the foreground before installing its low-level mouse hook. All memory access is confined to the current `GRW.exe` process.

## Shutdown

On normal runtime shutdown, the mouse hook and timer are removed, original instructions are restored, and the code cave is released. Closing the game naturally releases the entire process image.

## Distribution

The release package contains:

- `BetterMovementForKBM.asi`
- `BetterMovementForKBM.ini`, containing documented defaults
- `winmm.dll` from Ultimate ASI Loader by ThirteenAG
- `README.txt`, including the required third-party MIT license notice

The v1.x launcher architecture is preserved in Git history and the `v1.2.0` tag; it is not part of the v2 main branch.
