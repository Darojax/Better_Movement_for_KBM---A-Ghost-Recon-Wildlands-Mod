# Architecture

Better Movement for KBM v2 is a native x64 ASI loaded into `GRW.exe` by Ultimate ASI Loader. It has no companion launcher, background service, telemetry, networking, or external process-memory component. A local INI beside the ASI stores wheel sensitivity, its three shortcuts, and the automatically learned Walk/Jog binding.

## Startup and compatibility

The ASI confirms that its host is `GRW.exe`, validates the executable image boundaries, and compares every required instruction with the expected bytes for the supported build. Only after all checks pass does it allocate a local code cave and install the movement, gait-probe, and ADS redirects as one unit. Any failed write or verification restores the original instructions and releases the allocation.

Supported release target:

- Ghost Recon Wildlands `133.1.0.9840374`
- Steam build `24669148`
- Current Steam and Ubisoft Connect executables sharing that verified layout

Unsupported or modified executables are left untouched.

## Runtime

The runtime observes the game's native gait state, so the Walk/Jog binding configured in Wildlands remains authoritative. Mouse-wheel input selects the calibrated drawn-weapon movement ladder or a separate holstered curve, and sprint restores full jogging speed. After one ordinary Walk/Jog press while moving, the runtime stores that physical binding and uses it to cross the holstered gait boundary automatically; a later physical press updates the stored binding.

The ADS redirect uses both stance and native gait to keep aiming movement below the corresponding non-ADS target while retaining a useful improvement over vanilla. Sensitivity scales wheel-step size without changing the selected speed or calibrated endpoints. Above sensitivity 50, the selected destination updates immediately while a separate applied target follows it through a short time-based transition capped at roughly 160 ms across the full range; repeated wheel events redirect that transition from its current position. Gait inference is suspended while the game settles so delayed movement samples cannot cause false Walk/Jog rebases. Walk/Jog switching, sprint restoration, stationary selection, and sensitivity 50 or below bypass smoothing.

A no-activate, click-through Windows overlay displays live sensitivity changes without hooking the renderer. A separate embedded-image overlay appears once after the full-size GRW window is foreground and stable, then slides into and out of the lower-left corner. Each frame is clipped to the game window so the notification cannot spill onto an adjacent monitor.

The worker waits for the full-size Wildlands game window, rather than its startup splash, before installing its low-level input hooks. All memory access is confined to the current `GRW.exe` process.

## Shutdown

On normal runtime shutdown, the mouse hook and timer are removed, original instructions are restored, and the code cave is released. Closing the game naturally releases the entire process image.

## Distribution

The release package contains:

- `BetterMovementForKBM.asi`
- `BetterMovementForKBM.ini`, containing documented defaults
- `winmm.dll` from Ultimate ASI Loader by ThirteenAG
- `README.txt`, including the required third-party MIT license notice

The v1.x launcher architecture is preserved in Git history and the `v1.2.0` tag; it is not part of the v2 main branch.
