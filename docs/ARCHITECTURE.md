# Architecture

Better Movement for KBM is split into two processes so the user interface can supervise a narrow movement runtime and request clean restoration independently.

## Launcher

`GRWLauncher` is the WPF user interface. It:

- discovers Steam, Ubisoft Connect, and manually selected installations;
- evaluates game compatibility and local safety state;
- manages explicit save-backup and Windows Firewall actions;
- launches the selected storefront and waits for the matching `GRW.exe` process;
- starts, verifies, monitors, and stops the movement runtime; and
- stores launcher settings and backups outside the portable application folder.

Opening the launcher performs read-only inspection. Game launch, attachment, backup creation, firewall changes, and launcher-data cleanup require an explicit user action. Firewall changes are the only actions that request administrator approval.

## Movement runtime

`GRWMovementRuntime` is a separate console process started by the launcher for one specific `GRW.exe` process ID. It implements the mouse-wheel movement ladder and the standing, crouched, and Aim-Down-Sight movement model.

Before writing, the runtime verifies every required instruction against the exact expected original bytes. If any site differs, attachment is refused. During normal shutdown it restores the original instructions and releases its remote allocation. The launcher supervises shutdown through a private named event and reports verification or restoration failures.

The runtime does not patch files on disk. Its changes exist only in the selected running game process.

## Network behavior

Neither executable performs automatic downloads, telemetry, analytics, or update checks. The launcher contains user-activated links to the original SayNoToEAC sources, which are opened in the default browser only when clicked.

Compatibility profiles are selected by the exact `GRW.exe` SHA-256. Legacy profiles retain the SayNoToEAC requirement; verified builds that no longer include Easy Anti-Cheat omit that obsolete requirement. Every profile still requires exact live instruction verification before any memory write.

Optional Windows Firewall rules are local outbound block rules for detected Ghost Recon Wildlands and Ubisoft Connect executables. They are created or removed only when requested by the user.

## Local data

The launcher stores its settings, risk acknowledgement, save-source identities, and backup metadata in the user's local application-data area. Save backups are kept separately by game installation and source identity. Automatic save restoration is intentionally not provided.

The **Remove launcher data** workflow removes launcher-created settings, backups, and project-managed firewall rules. It does not remove normal game saves or SayNoToEAC/Easy Anti-Cheat files.

## Packaging

The public package is framework-dependent and requires the Microsoft .NET 8 Desktop Runtime x64. Each application is published as a single executable, producing this portable layout:

```text
Better Movement for KBM - GRW.exe
README.md
Runtime/
  GRWAnalogueMovement.exe
```
