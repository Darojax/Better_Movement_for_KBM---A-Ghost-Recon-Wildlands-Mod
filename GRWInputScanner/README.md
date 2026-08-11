# GRW Input Scanner

This is a deliberately read-only research utility for locating candidate movement-input floats in a running
Ghost Recon Wildlands process.

Safety properties:

- Opens `GRW.exe` with only `PROCESS_QUERY_INFORMATION | PROCESS_VM_READ`.
- Imports `ReadProcessMemory` and `VirtualQueryEx`.
- Does not import or call `WriteProcessMemory`, injection APIs, remote-thread APIs, or executable patching APIs.
- Scans only committed, readable, private memory.

Controls while Wildlands is focused:

- Launch with `--load <candidate-file>` to resume from a saved address list.
- Tap `F4` to print current float values for every loaded or remaining candidate.
- In the game's walk mode, hold `W` and tap `F5` to retain fractional magnitudes between zero and one.
- With a loaded/narrowed list, hold `W` and tap `F6` to record each candidate's forward value.
- Then hold `S` and tap `F7` to keep only candidates that changed to the exact opposite sign.
- Hold `W` and tap `F8` to capture values equal to `+1.0` or `-1.0`.
- Release `W` and tap `F9` to retain candidates equal to positive or negative zero.
- Repeat the `F8`/`F9` cycle to narrow candidates.
- Tap `F10` to save remaining addresses.
- Tap `F12` to exit.

Build:

```powershell
dotnet build .\GRWInputScanner\GRWInputScanner.csproj -c Release
```
