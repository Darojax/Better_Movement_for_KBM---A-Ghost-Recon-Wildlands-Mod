# Changelog

All notable public changes to Better Movement for KBM are documented here.

## 1.0.0-rc.1 — Unreleased

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
