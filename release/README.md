# Better Movement for KBM — A Ghost Recon Wildlands Mod

> **Release candidate.** Use only a package published through the official GitHub release or Nexus page and verify its published SHA-256 hashes.

Granular mouse-wheel movement control for the Windows version of **Tom Clancy's Ghost Recon Wildlands**.

## Important warning

This unofficial mod writes to the running `GRW.exe` process. It is intended exclusively for offline single-player use. Ubisoft may regard process-memory modification or related tools as prohibited. No anti-cheat modification, firewall rule, offline procedure, or disclaimer can guarantee that an account will not be restricted or sanctioned.

Read [DISCLAIMER.txt](DISCLAIMER.txt) before use.

## Movement behavior

- Walk HIP: 16 near-uniform levels from `0.05` to `0.60`, including the exact vanilla-walk anchor at `0.35`.
- Jog HIP: 11 evenly spaced levels from `0.70` to `1.00` in `0.03` steps.
- Mouse wheel traverses one unified ladder from the slowest walk through the fastest jog, regardless of the game's hidden native gait.
- This replaces the vanilla mouse-wheel action that cycles among the three weapon slots. Use `1`, `2`, and `3` for direct weapon-slot selection.
- Standing walk ADS follows a calibrated curve from coefficient `1.81` at vanilla walk to `2.48` at maximum walk, extrapolated through the lower walk levels.
- Standing jog ADS follows the underlying HIP level and is capped at coefficient `3.40` to keep the fastest ADS movement believable and stable.
- Crouching is detected from the game's native stance state. Crouch walk ADS uses a proportional `0.84–1.68` curve; crouch jog ADS uses coefficient `2.70`.
- Scrolling while ADS still adjusts the unified HIP level restored on ADS release.
- From a walking-range speed, `X` jumps to vanilla full jog. From a jogging-range speed, `X` jumps to vanilla walk. No separate custom gait speeds are saved.
- Releasing Sprint/Shift returns movement to vanilla full jog.
- The launcher's adaptive green button disables the runtime and restores the original game instructions.

Small terrain-dependent speed changes on slopes are native Wildlands behavior. Wildlands also applies weapon-dependent movement differences after the mod's shared ADS coefficient; the current tuning has been validated with an LMG, assault rifle, and pistol rather than normalized per weapon class.

## Requirements

- Windows 10 or 11 x64.
- Steam or Ubisoft Connect edition of Ghost Recon Wildlands.
- Offline single-player use.
- SayNoToEAC installed separately from its original source.
- Outbound isolation for Wildlands is strongly recommended but remains optional.

SayNoToEAC is not included and this project does not install or modify it.

## Recommended installation

1. Back up your saves.
2. Install SayNoToEAC using its original author's instructions.
3. Run `Better Movement for KBM - GRW.exe`.
4. Review the live checklist. Red items block only mod attachment; cautions such as firewall isolation and save backup remain optional.
5. Optionally install the separate Wildlands and Ubisoft Connect firewall blocks from the launcher.
6. Click **Launch with Better Movement for KBM** and complete the one-time risk acknowledgement.
7. Load an offline single-player save. The launcher waits for `GRW.exe` and enables the runtime automatically.

Do not use the mod in co-op, Ghost War, or any online session.

## Strict isolation

The launcher can additionally block detected Ubisoft Connect executables. This may prevent login, updates, cloud synchronization, and other Ubisoft games from functioning. It is optional and should only be enabled when cached offline launch works on the user's system.

The standard mode blocks these Wildlands executables when present:

- `GRW.exe`
- `GRW_Upp.exe`
- `rungame.exe`

## Safe shutdown

1. Pause the game.
2. Use the adaptive green button to disable Better Movement for KBM, or close the launcher and approve restoration.
3. Wait until the launcher reports that Better Movement is disabled.
4. Exit Wildlands, then reconnect or remove firewall isolation.

If the runtime is forcibly terminated, exit Wildlands before doing anything online. All memory changes disappear when the game process exits.

## Game updates

The runtime verifies the exact original bytes at every supported instruction site. It refuses to attach when they differ. Do not bypass this protection. A new Wildlands executable must be analyzed and explicitly supported before the mod is updated.

## Save backups

The launcher checks and backs up the save containers belonging to the currently selected edition:

- Steam: `3559`
- Ubisoft Connect: `1771` and legacy container `4740`, when present

The Save backup **Manage** panel detects the default location and allows additional custom or redirected save roots to be registered. Overlapping roots are rejected, including resolvable directory links, so the same container is not backed up twice.

Backups are kept in separate edition and source-identity directories:

`Documents\Better Movement for KBM\Save Backups\<Steam or Ubisoft Connect>\<source name and ID>\<timestamp>`

Steam and Ubisoft Connect installations can use different Ubisoft save containers on the same account. In local testing, the Ubisoft Connect installation used `1771` while the Steam installation used `3559`; progress was therefore not shared automatically between those installations.

The launcher's **Save backup** status follows the selected installation and every registered source independently. It returns to **Caution** if any source has no backup or contains files newer than its latest backup. Removing a custom source from the launcher never deletes its saves or existing backups, and the launcher never restores or copies saves between sources automatically.

## Antivirus notices

Process-memory modification and firewall management can trigger security-product warnings. The project should be downloaded only from its official Nexus/GitHub release, checked against published SHA-256 hashes, and built from source when possible.

## Portable removal

Use **Remove launcher data…** before deleting the portable folder. After one confirmation, it removes backups created by Better Movement for KBM, its local settings and records, and its managed Ghost Recon Wildlands and Ubisoft Connect firewall rules. It leaves normal game saves, SayNoToEAC files, renamed EAC files, and firewall rules created by other tools untouched. The action is unavailable while the movement runtime is running.

SayNoToEAC must be removed separately using its original instructions if the user no longer wants it.

## Support boundaries

Supported: the tested Windows executable and offline single-player. Steam and Ubisoft Connect storefront launch paths are detected independently.

Unsupported: multiplayer/co-op, unknown executable builds, Steam Input/controller emulation combinations, other operating systems, or operation with Easy Anti-Cheat active.
