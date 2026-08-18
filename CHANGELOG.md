# Changelog

All notable public changes to Better Movement for KBM are documented here.

## 2.1.1 - 2026-08-18

- Added brief time-based acceleration and deceleration smoothing for wheel adjustments above sensitivity 50.
- Preserved every calibrated movement destination and left sensitivity 50 and below unchanged.
- Made repeated high-sensitivity wheel input retarget smoothly from the currently applied speed.
- Prevented lagging game movement samples from being mistaken for native Walk/Jog changes during smoothed transitions.
- Kept Walk/Jog switching, sprint restoration, and stationary speed selection immediate.

## 2.1.0 — 2026-08-18

- Added live mouse-wheel movement-sensitivity adjustment from 0 to 100.
- Added plain `F6` decrease, `F7` display, and `F8` increase shortcuts with hold-to-repeat adjustment.
- Added a fullscreen-compatible, non-activating sensitivity slider with a timed fade-out.
- Added persistent sensitivity and configurable sensitivity shortcuts through `BetterMovementForKBM.ini`.
- Preserved the v2.0.1 movement ladder exactly at the default sensitivity of 50.
- Expanded high sensitivity so 100 traverses the complete movement range in six wheel detents.
- Kept sensitivity changes independent from the currently selected movement speed.

## 2.0.1 — 2026-08-17

- Fixed a low-speed gait transition that could make one or two upward wheel steps jump to vanilla walking speed.
- Restored immediate native Walk/Jog toggle response while retaining protection against wheel-induced gait changes.
- Added stationary speed selection: wheel adjustments made while standing still now apply when movement begins.

## 2.0.0 — 2026-08-17

- Replaced the external launcher/runtime architecture with a native in-process ASI.
- Reduced installation to `BetterMovementForKBM.asi` and `winmm.dll` beside `GRW.exe`.
- Removed the launcher, .NET dependency, configuration file, routine logging, firewall management, save-backup interface, and SayNoToEAC requirement.
- Restricted compatibility to the current EAC-free game version `133.1.0.9840374` / Steam build `24669148`.
- Preserved the complete movement model: unified mouse-wheel control, native in-game Walk/Jog binding, sprint reset, and calibrated standing/crouched ADS behavior.
- Added atomic exact-instruction validation and rollback for all three in-process redirects.
- Removed all external-process memory APIs from the active runtime.
- Added Ultimate ASI Loader v9.7.4 x64 with its upstream MIT license notice.
- Passed live Steam gameplay regression and fail-closed host testing.
- Accepted by Nexus Mods without the suspicious-file warning shown on the legacy launcher package.

## 1.2.0 — 2026-08-17

- Added a configurable walk/jog shortcut in the launcher, with `X` retained as the default.
- Added an option to disable the walk/jog shortcut completely.
- Shortcut changes now apply to an active gameplay session without restarting or reattaching the mod.

## 1.1.0 — 2026-08-14

- Added verified compatibility with game version `133.1.0.9840374` / Steam build `24669148` for both Steam and Ubisoft Connect.
- Added runtime layouts for both the latest and previous verified game builds, retaining exact-byte checks before every memory write.
- Adapted the launcher to the latest build's native removal of Easy Anti-Cheat: SayNoToEAC is reported as unnecessary and its management action is omitted.
- Preserved the mandatory SayNoToEAC and active Easy Anti-Cheat safeguards for legacy game builds.
- Added the current mod version to the launcher header and executable metadata.

## 1.0.0-rc.1 — 2026-08-13

- Added a unified 27-position mouse-wheel movement ladder spanning slow walk through full jog.
- Preserved the native `X` walk/jog shortcut and sprint-release return to full jog.
- Added calibrated standing, crouched, and Aim-Down-Sight movement behavior.
- Added exact-byte verification and clean restoration of every modified instruction.
- Added a portable launcher for Steam and Ubisoft Connect installation selection, supervised attachment, optional Windows Firewall blocks, SayNoToEAC/EAC checks, save backups, and launcher-data cleanup.
- Added edition-separated save discovery and timestamped manual-recovery backups.
- Added release hashes, source packaging, and optional Authenticode signing support.
- Added a cancellable persistent-startup state so interrupted storefront launches never leave the launcher controls unavailable until timeout.
- Consolidated launch, enable, disable, and startup cancellation into one state-aware primary action instead of duplicating enable/disable controls.
- Made the main Save backup check refresh immediately after a successful backup instead of waiting for the cached live-check interval.
- Tightened the main window after removing redundant active-runtime guidance from the status banner.
- Reduced excess vertical space in the SayNoToEAC management panel.
- Removed the legacy F5 runtime-shutdown hotkey; the launcher's adaptive primary action is now the sole in-session disable control.
- Validated that unsupported live instruction signatures refuse attachment without writing, and clarified the resulting error message.
- Reduced the portable package from roughly 220 MB extracted to about 2 MB by using the Microsoft .NET 8 Desktop Runtime already installed on the user's system.
- Replaced the bundled technical README with a concise setup and usage guide and removed the redundant internal file-hash manifest.
- Consolidated the portable package into a single-file launcher, a single-file runtime helper, and one README containing the disclaimer and license.
- Prepared the public source tree by removing obsolete research utilities, moving the runtime out of its prototype project, and consolidating maintainer documentation.
