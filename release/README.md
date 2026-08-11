# GRW Analogue Movement Mod

> **Development preview — not ready for distribution.** Gameplay behavior is still being redesigned and play-tested. See [`TODO.md`](../TODO.md) in the source project.

Granular mouse-wheel movement control for the Windows version of **Tom Clancy's Ghost Recon Wildlands**.

## Important warning

This unofficial mod writes to the running `GRW.exe` process. It is intended exclusively for offline single-player use. Ubisoft may regard process-memory modification or related tools as prohibited. No anti-cheat modification, firewall rule, offline procedure, or disclaimer can guarantee that an account will not be restricted or sanctioned.

Read [DISCLAIMER.txt](DISCLAIMER.txt) before use.

## Movement behavior

- Walk HIP: 12 levels from `0.05` to `0.60`.
- Jog HIP: 9 levels from `0.70` to `1.00`.
- Walk ADS: 12 correlated levels using the calibrated `1.6875–3.375` curve.
- Jog ADS: fixed at the globally calibrated `4.10`.
- Mouse wheel traverses one unified ladder from the slowest walk through the fastest jog, regardless of the game's hidden native gait.
- While in jog ADS, the visible speed remains fixed while the wheel adjusts the underlying unified HIP level restored on ADS release.
- From a walking-range speed, `X` jumps to vanilla full jog. From a jogging-range speed, `X` jumps to vanilla walk. No separate custom gait speeds are saved.
- Releasing Sprint/Shift returns movement to vanilla full jog.
- `F5` restores original game instructions and exits the runtime.

Small terrain-dependent speed changes on slopes are native Wildlands behavior.

## Requirements

- Windows 10 or 11 x64.
- Ubisoft Connect edition of Ghost Recon Wildlands matching the supported executable instructions.
- Offline single-player use.
- SayNoToEAC installed separately from its original source.
- An enabled outbound Windows Firewall block for `GRW.exe`.

SayNoToEAC is not included and this project does not install or modify it.

## Recommended installation

1. Back up your saves.
2. Install SayNoToEAC using its original author's instructions.
3. Run `GRWMovementSafety.exe`.
4. Choose **Install recommended GRW-only firewall rules**.
5. Confirm all required safety checks show `[OK]`.
6. Start Wildlands and load an offline single-player save.
7. Run `GRWAnalogueMovement.exe`.
8. Complete the one-time risk acknowledgement.

Do not use the mod in co-op, Ghost War, or any online session.

## Strict isolation

The safety utility can additionally block detected Ubisoft Connect executables. This may prevent login, updates, cloud synchronization, and other Ubisoft games from functioning. It is optional and should only be enabled when cached offline launch works on the user's system.

The standard mode blocks these Wildlands executables when present:

- `GRW.exe`
- `GRW_Upp.exe`
- `rungame.exe`

## Safe shutdown

1. Pause the game.
2. Press `F5` and wait for the runtime to report that the original instructions were restored.
3. Exit Wildlands.
4. Only then reconnect or remove firewall isolation.

If the runtime is forcibly terminated, exit Wildlands before doing anything online. All memory changes disappear when the game process exits.

## Game updates

The runtime verifies the exact original bytes at every supported instruction site. It refuses to attach when they differ. Do not bypass this protection. A new Wildlands executable must be analyzed and explicitly supported before the mod is updated.

## Save backups

The safety utility backs up detected Ubisoft save folders `1771` and `4740` to:

`Documents\GRW Movement Mod\Save Backups\<timestamp>`

## Antivirus notices

Process-memory modification and firewall management can trigger security-product warnings. The project should be downloaded only from its official Nexus/GitHub release, checked against published SHA-256 hashes, and built from source when possible.

## Uninstallation

- Installer: uninstall normally; its uninstaller removes only firewall rules prefixed `GRW Movement Mod - `.
- Portable: run the safety utility and choose **Remove rules created by this utility**, then delete the extracted folder.
- SayNoToEAC must be removed separately using its original instructions.

## Support boundaries

Supported: the tested Ubisoft Connect Windows build and offline single-player.

Unsupported: multiplayer/co-op, unknown executable builds, Steam Input/controller emulation combinations, other operating systems, or operation with Easy Anti-Cheat active.
