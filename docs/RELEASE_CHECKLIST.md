# Release checklist

1. Confirm the supported Wildlands version and exact instruction signatures.
2. Build `BetterMovementASI` for `Release|x64` with no warnings or errors.
3. Verify that the ASI imports no external-process, networking, or persistence APIs.
4. Run fail-closed tests against a non-game host and an unsupported fake `GRW.exe`.
5. Test Steam and Ubisoft Connect startup, HIP movement, native Walk/Jog switching, sprint reset, standing ADS, crouched ADS, and several weapon classes.
6. Confirm that unsupported builds remain untouched and the game exits cleanly.
7. Verify the pinned Ultimate ASI Loader hash used by `build-asi-release.ps1`.
8. Build the package and inspect that it contains only the ASI, `winmm.dll`, and `README.txt`.
9. Record SHA-256 hashes for the ASI, loader, and ZIP.
10. Update `CHANGELOG.md`, compatibility text, Nexus copy, version resources, and the Git tag.
