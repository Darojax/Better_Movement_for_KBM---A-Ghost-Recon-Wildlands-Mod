# Better Movement for KBM — Release checklist

Use this checklist for every public build. A successful local play session is necessary, but does not replace recovery and packaging validation.

## Release gate

- [x] Confirm the working tree contains only intentional changes and commit the release candidate.
- [x] Build the launcher and runtime with zero warnings and zero errors.
- [ ] Test both Steam and Ubisoft Connect installations from launcher start through clean shutdown.
- [ ] Test launching with the mod, attaching to an already running game, disabling through the adaptive primary action, normal game exit, and launcher closure.
- [x] Test startup interruptions such as cloud-sync, entitlement, and Ubisoft Connect prompts.
- [x] Confirm an unsupported instruction signature refuses attachment without writing to the game.
- [ ] Confirm active Easy Anti-Cheat blocks attachment and vanilla launch remains available.
- [x] Test installing and uninstalling both firewall-rule groups, including rules for two game installations.
- [ ] Test backup freshness, per-installation separation, game-running suspension, post-exit refresh, custom locations, and manual recovery from a produced backup.
- [ ] Run a multi-hour gameplay regression covering loading, fast travel, death/respawn, ADS, crouch, sprint, slopes, menus, vehicles, and representative weapon classes.
- [x] Run **Remove launcher data** and verify normal saves, SayNoToEAC files, and unrelated firewall rules remain untouched.

## Distribution integrity

- [ ] Build from the final tagged commit with `build-release.ps1`.
- [ ] If available, Authenticode-sign and RFC 3161 timestamp the launcher and runtime PE files before hashes and archives are generated.
- [x] Confirm no `.pdb`, developer path, save data, logs, captures, credentials, or certificate material exists in either archive.
- [x] Extract the source archive elsewhere and confirm it builds independently.
- [x] Verify the published archive hashes in `RELEASE-SHA256SUMS.txt`.
- [x] Scan the exact candidate archives and executables with Microsoft Defender.
- [ ] Optional: scan the public archives with a reputable multi-engine service, respecting its data-retention and privacy terms.
- [ ] Submit any incorrect Microsoft Defender detection through Microsoft Security Intelligence as a software developer and wait for a final determination.
- [ ] Publish binaries only through the official GitHub release and Nexus page, with matching hashes and an exact version/tag.

## Release page

- [ ] State prominently that the mod modifies the running game process and is for offline single-player only.
- [ ] Explain the SayNoToEAC dependency without bundling or claiming ownership of it.
- [ ] Document that mouse-wheel weapon switching is replaced and recommend keys `1`, `2`, and `3`.
- [ ] Include installation, first-launch/firewall recovery, safe shutdown, removal, backup, known-limitations, and support instructions.
- [ ] Include the disclaimer, tested game build/hash, changelog, screenshots/video, and a link to the public source for the exact release.

## Community compatibility follow-up

- The initial release is locally tested on Windows 11 with the Microsoft .NET 8 Desktop Runtime x64. Treat clean Windows 10/11 coverage and unusual system configurations as post-release community validation rather than release blockers.
- Ask users to report reproducible compatibility problems through the official Nexus Mods page or GitHub repository.
