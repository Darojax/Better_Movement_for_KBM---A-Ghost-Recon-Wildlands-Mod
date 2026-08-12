# Better Movement for KBM launcher

The WPF launcher is the user-facing entry point for Better Movement for KBM. It uses the production backend and supervises the separate movement runtime.

## Implemented functionality

- Detects every Ubisoft Connect and Steam installation and presents them in a persistent installation picker, with a custom-folder option for any copy not found automatically.
- Hashes an executable only when its path, size, or modification time changes; slower installation and firewall checks are cached while volatile process/runtime state refreshes every second. Executable analysis displays a gently animated amber progress ring and keeps its **Details** action visible but disabled until analysis completes.
- Recognizes the currently tested Ghost Recon Wildlands executable and maps its SHA-256 to Steam public build `24446260`. The tested Steam and Ubisoft executables are byte-identical even though Ubisoft supplies no version label. The runtime still requires exact original in-memory instructions before it writes anything.
- Detects the SayNoToEAC replacement DLLs and active Easy Anti-Cheat processes. The management panel reports replacement-DLL readiness and optional original `.BAK` recovery files in separate status boxes; missing backups never block the mod. Its square-cornered modal quickly fades in and out, dims and blocks the launcher beneath it, and provides sequential installation instructions, original and mirror links, and direct access to the selected installation's `EasyAntiCheat` folder. Either missing SayNoToEAC or active EAC blocks mod attachment but never blocks vanilla launch.
- Detects compatible outbound Windows Firewall blocks whether they were created by this launcher or another utility. Firewall blocking is always optional and never a red blocking condition.
- Provides a fixed, dimming in-launcher **Usage & Risks** panel explaining operation, how the supervised runtime hooks and consumes mouse-wheel input, verifies and temporarily changes `GRW.exe` memory, restores original instructions, and keeps explicit backup/firewall actions separate. It also explains potential anti-cheat detection, account suspension or banning, crashes and save loss; why non-blocking cautions should still be reviewed; how they differ from blocked and ready checks; and an explicit at-your-own-risk disclaimer.
- Manages Ghost Recon Wildlands and Ubisoft Connect Windows Firewall blocks independently. Only the requested firewall action elevates. **Uninstall** removes launcher-managed or compatible external outbound block rules targeting the detected executables; rules for unrelated applications are never removed. General launcher-data cleanup still removes only project-managed rules.
- Opens a fixed Save backup management panel showing save sources for every detected Steam and Ubisoft Connect installation together. It detects the default Ubisoft save root, accepts multiple custom or redirected roots, rejects overlapping sources after canonical path/junction resolution, and provides per-source controls to create an edition-separated timestamped backup or open the live and backed-up folders. Removing a custom source does not delete saves or backups.
- Does not automate save restoration. Recovery remains an explicit manual file operation so the launcher cannot replace newer progress through a mistaken restore click.
- Automatically shows one active save source per detected installation: Steam prefers container `3559`, while Ubisoft Connect prefers `1771` and falls back to legacy `4740` only when `1771` is absent. If several accounts contain the preferred container, the most recently modified source is selected. Explicitly registered custom locations remain additional independent rows. New timestamped folders contain the save files directly; compatible backups made by older launcher builds are still recognized.
- Launches Ghost Recon Wildlands through its detected storefront, waits for `GRW.exe`, and starts the runtime automatically. One adaptive primary action launches with the mod, enables it in an already running game, disables an active runtime, or cancels a pending startup according to the current state.
- During a launcher-initiated start, requires `GRW.exe` to remain continuously present before attachment and retries a clean early runtime exit for up to four minutes. This allows Steam/Ubisoft Connect synchronization or confirmation prompts to complete without silently abandoning the requested mod startup; nonzero runtime failures are still reported immediately.
- While startup is waiting, the primary action becomes **Cancel launch attempt**. Cancelling immediately stops the wait and restores every launcher control; if runtime attachment has already begun, cancellation uses the normal instruction-restoration path before returning control.
- Supervises the runtime through a private named event. The adaptive primary action, launcher closure, or normal game exit all use the runtime's restoration path.
- Leaves the launcher at its current taskbar/window position after successful attachment and never activates or minimizes it automatically.
- Records the offline single-player risk acknowledgement once in local application data.
- Runs as a normal fixed-size taskbar window with a custom title bar and no tray icon. Its last normally closed position is restored on the next launch and clamped to the current virtual desktop so it cannot be stranded completely off-screen.
- Supports portable removal through one confirmation panel. It deletes launcher-created save backups and local state, removes only project-managed Ghost Recon Wildlands and Ubisoft Connect firewall rules, leaves normal saves and SayNoToEAC/EAC files untouched, and closes without recreating window-position state. The action is disabled with an explanatory tooltip whenever any movement runtime is detected.
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
