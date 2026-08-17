# Better Movement for KBM

Better Movement for KBM adds smooth, granular movement-speed control to the Windows version of **Ghost Recon Wildlands**. Scroll the mouse wheel while moving to transition naturally from a very slow walk to a full jog. Aim-Down-Sight and crouched movement are also adjusted to feel more consistent.

The mod uses the mouse wheel for movement-speed control, so first open **Settings → Key Mapping → Player Combat** and reassign **Next Weapon** and **Previous Weapon** to controls other than the mouse wheel. Keys `1`, `2`, and `3` remain available for direct weapon selection.

## Requirements

- Windows 10 or 11 x64.
- Steam or Ubisoft Connect version of Ghost Recon Wildlands.
- [Microsoft .NET 8 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/8.0).
- Current EAC-free game build: SayNoToEAC is not required.
- Legacy builds that still include Easy Anti-Cheat: SayNoToEAC, installed separately using its original author's instructions.
- Offline single-player only.

SayNoToEAC is not included with this mod. When a legacy build requires it, the launcher provides installation guidance and links to its original sources.

## Setup and use

1. Extract the complete ZIP to a folder of your choice.
2. Run `Better Movement for KBM - GRW.exe`.
3. Select your game installation if more than one is detected.
4. Follow the launcher's checklist and create a save backup.
5. Optional but recommended: install the offered Windows Firewall blocks.
6. Click the green **Launch with Better Movement for KBM** button.

Keep the launcher open while playing. Its green button adapts automatically and can launch the game, enable or disable the mod in a running game, or cancel a pending launch.

## Controls

- Mouse wheel while moving: adjust movement speed.
- Configurable walk/jog shortcut (`X` by default): immediately switch to the opposite vanilla walk/jog speed. Use **Controls** in the launcher to change or disable it, including while the mod is running.
- Sprint/Shift: sprint normally; releasing it returns to full jogging speed.

## If the game cannot start

Some systems require one normal online launch before firewall isolation works. Uninstall both firewall blocks in the launcher, start the game normally while online, reach the main menu, then exit and reinstall the blocks.

## Safe shutdown and removal

Use the green launcher button to disable the mod before reconnecting or entering any online mode. If the runtime or launcher is forcibly terminated, exit Ghost Recon Wildlands before doing anything online.

Before deleting the portable folder, use **Remove launcher data…** to remove backups, settings, and firewall rules created by the launcher. Normal game saves and SayNoToEAC files are not removed.

## Important warning

This unofficial mod temporarily modifies the running `GRW.exe` process. Ubisoft or its anti-cheat systems may treat this as prohibited activity. No firewall rule, offline procedure, launcher check, or anti-cheat replacement can guarantee account safety.

Never use the mod in co-op, Ghost War, or any online session. Read the risk notice and disclaimer below before use. If you encounter a problem, report it on the official Nexus Mods page or GitHub repository.

## Risk notice and disclaimer

This is an unofficial community modification and is not affiliated with, endorsed by, or supported by Ubisoft, Easy Anti-Cheat/Epic Games, or Nexus Mods.

The software modifies the memory of the running Ghost Recon Wildlands process. It is intended exclusively for offline single-player use. Using process-memory modification tools may violate Ubisoft's terms or trigger anti-cheat, integrity, telemetry, or security systems. No configuration—including SayNoToEAC, firewall isolation, or disconnecting from the internet—can guarantee that an account will not be detected, restricted, suspended, or terminated.

**DO NOT USE THIS SOFTWARE IN CO-OP, GHOST WAR, OR ANY ONLINE SESSION.**

The software is provided "as is", without warranty of any kind. To the maximum extent permitted by law, the authors and contributors are not responsible for bans, account restrictions, loss of access, corrupted or lost saves, crashes, instability, data loss, security-software alerts, network disruption, or any other direct or indirect damage arising from installation or use.

Back up saves before use. Verify firewall isolation. Confirm Easy Anti-Cheat is not active. Disable the mod through the launcher and exit Wildlands before reconnecting or entering any online mode.

By installing or using this software, you acknowledge these risks and accept sole responsibility for the consequences.

## License

MIT License

Copyright (c) 2026 Better Movement for KBM contributors

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
