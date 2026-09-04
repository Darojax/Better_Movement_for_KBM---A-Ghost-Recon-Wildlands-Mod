# Release checklist

1. Confirm the supported Wildlands version and exact instruction signatures.
2. Build `BetterMovementASI` for `Release|x64` with no warnings or errors.
3. Verify that the ASI imports no external-process or networking APIs and writes only its documented local INI.
4. Run fail-closed tests against a non-game host and an unsupported fake `GRW.exe`.
5. Test Steam and Ubisoft Connect startup, drawn and holstered movement, native and automatic Walk/Jog switching, sprint reset, standing/crouched/prone ADS, and several weapon classes.
6. At sensitivity 50 and below, confirm exact v2.1.0 response. Above 50, test smooth acceleration and deceleration, rapid retargeting, range crossing, stationary selection, ADS, and crouching.
7. Verify first-time Walk/Jog binding learning, persistence across sessions, and relearning after a changed binding.
8. Confirm the startup logo waits for the full game window, appears once, remains clipped to that window on multi-monitor systems, and never steals focus or input.
9. Confirm that unsupported builds remain untouched and the game exits cleanly.
10. Verify the pinned Ultimate ASI Loader hash used by `build-asi-release.ps1`.
11. Build the package and inspect that it contains only the ASI, documented INI, `winmm.dll`, and `README.txt`.
12. Record SHA-256 hashes for the ASI, loader, and ZIP.
13. Update `CHANGELOG.md`, compatibility text, Nexus copy, version resources, and the Git tag.
