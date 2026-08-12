# Better Movement for KBM launcher

The WPF launcher is the user-facing entry point for Better Movement for KBM. It uses the production backend and supervises the separate movement runtime.

## Implemented functionality

- Detects every Ubisoft Connect and Steam installation and presents them in a persistent installation picker, with a custom-folder option for any copy not found automatically.
- Hashes an executable only when its path, size, or modification time changes; slower installation and firewall checks are cached while volatile process/runtime state refreshes every second. Executable analysis displays a gently animated amber progress ring and keeps its **Details** action visible but disabled until analysis completes.
- Recognizes the currently tested Ghost Recon Wildlands executable and maps its SHA-256 to Steam public build `24446260`. The tested Steam and Ubisoft executables are byte-identical even though Ubisoft supplies no version label. The runtime still requires exact original in-memory instructions before it writes anything.
- Detects the SayNoToEAC stub/backup layout and active Easy Anti-Cheat processes. Its square-cornered in-launcher modal panel quickly fades in and out, dims and blocks the launcher beneath it, and provides sequential installation instructions, original and mirror links, and direct access to the selected installation's `EasyAntiCheat` folder. Either missing SayNoToEAC or active EAC blocks mod attachment but never blocks vanilla launch.
- Detects compatible outbound firewall blocks whether they were created by this launcher or another utility. Firewall isolation is always optional and never a red blocking condition.
- Provides a fixed, dimming in-launcher **Safety risks** panel explaining how the supervised runtime hooks and consumes mouse-wheel input, verifies and temporarily changes `GRW.exe` memory, restores original instructions, and keeps explicit backup/firewall actions separate. It also explains potential anti-cheat detection, account suspension or banning, crashes and save loss; why non-blocking cautions should still be reviewed; how they differ from blocked and ready checks; and an explicit at-your-own-risk disclaimer.
- Manages Ghost Recon Wildlands and Ubisoft Connect isolation independently. Only the requested firewall action elevates. Ubisoft Connect's **Uninstall** action removes launcher-managed rules and the recognized legacy `GRW Isolation - Ubisoft` rules; unrelated external firewall rules are never removed.
- Opens a fixed Save backup management panel for the selected edition. It detects the default Ubisoft save root, accepts multiple custom or redirected roots, rejects overlapping sources after canonical path/junction resolution, and lets users remove a custom source without deleting saves or backups.
- Checks and creates timestamped backups for the selected edition's save containers (`3559` for Steam; `1771` and legacy `4740` for Ubisoft Connect). Every storefront, root, Ubisoft account, and container remains separated, with independent source identities and freshness records. Compatible backups made by older launcher builds are still recognized for the edition whose containers they contain.
- Launches Ghost Recon Wildlands through its detected storefront, waits for `GRW.exe`, and starts the runtime automatically; it can also attach to an already running game.
- Supervises the runtime through a private named event. **Disable mod**, launcher closure, `F5`, or normal game exit all use the runtime's restoration path.
- Records the offline single-player risk acknowledgement once in local application data.
- Runs as a normal fixed-size taskbar window with a custom title bar and no tray icon. Its last normally closed position is restored on the next launch and clamped to the current virtual desktop so it cannot be stranded completely off-screen.
- Keeps checklist rows and their controls alive across unchanged live refreshes, preventing buttons from being replaced during a click. Rows remain at a stable height; long status details are ellipsized and available through a tooltip.
- Presents meaningful actions, results, and errors in a chronological, non-selectable activity log that follows new entries at the bottom. Routine live-check refreshes, dismissed or cancelled dialogs, and duplicate launch confirmations are intentionally omitted; the local scrollbar uses a 6-pixel visible thumb.
- Uses slim dark-themed scrollbars throughout the launcher instead of the native light Windows controls.
- Places primary affirmative actions consistently at the bottom right of launcher dialogs and the main window.
- Supports dragging from any non-interactive surface—including the active SayNoToEAC overlay—while preserving buttons, links, lists, scrolling controls, and folder selection.

## Development build

```powershell
dotnet build .\GRWLauncher\GRWLauncher.csproj -c Release
```

The movement runtime is built and copied into the launcher's `Runtime` output directory automatically.

Opening the launcher performs read-only inspection. Firewall changes, save backups, game launch, and runtime attachment occur only after their corresponding user actions.
