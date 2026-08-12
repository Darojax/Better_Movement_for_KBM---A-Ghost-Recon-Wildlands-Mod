# Better Movement for KBM — TODO

## Current status

**Release candidate — not yet approved for public distribution.**

The core movement model is implemented and passed its initial in-game validation on 2026-08-11. The next phase is extended play-testing, runtime polish, and release validation rather than further redesign.

## Release validation — Save backup panel

The Save backup system is implemented and behaving correctly in current local testing, but it must pass the following release validation before it is relied upon as the only save-protection method.

- Re-run the complete backup workflow from a clean launcher state before approving the first release.
- Verify automatic detection and edition switching for Steam (`3559`) and Ubisoft Connect (`1771` and legacy `4740`).
- Verify legacy-backup recognition, per-source freshness reporting, and persistence across launcher restarts.
- Test adding and removing multiple custom roots, including overlapping folders, junctions, unavailable paths, multiple Ubisoft accounts, and similarly named sources.
- Confirm every source is written to a separate destination and that no save or previous backup can be overwritten, merged, moved, restored, or deleted unintentionally.
- Test panel controls, status messages, live refresh behavior, long paths, failed or interrupted copies, and backup attempts while Ghost Recon Wildlands is running.
- Keep restoration deliberately manual. The launcher may create timestamped backups and open exact live/backup folders, but must not offer an automated restore action that could replace newer progress through a mistaken click.
- Continue making independent manual save backups until this feature has passed end-to-end validation.

## Implemented gameplay contract

Keep these points as the regression baseline:

- One 27-position mouse-wheel ladder is available from either native gait:
  - 16 near-uniform walk positions from `0.05` through `0.60`.
  - Exact vanilla-walk anchor at `0.35`.
  - 11 jog positions from `0.70` through `1.00` in `0.03` steps.
- Wheel traversal is monotonic across the walk/jog boundary and does not synthesize `X`.
- Physical `X` jumps from any walking-range position to vanilla full jog, or from any jogging-range position to vanilla walk.
- Releasing Sprint/Shift returns to vanilla full jog.
- The helper normalizes each HIP target against Wildlands' detected native gait, so the complete range remains available from both walk and jog mode.
- The launcher's adaptive primary action restores every patched instruction and stops the runtime.
- `--verbose` provides live level, target, multiplier, native-gait, and stance diagnostics.

## Implemented ADS and crouch model

- Native stance detection uses the movement owner captured at the established probe hook:
  - movement owner `+0x38` points to the stance-state object;
  - byte `+0xB0`, mirrored at `+0x330`, is `0` standing or `1` crouched;
  - mismatched or out-of-range values are ignored rather than guessed.
- Standing walk ADS is calibrated against the unified HIP ladder:
  - coefficient `1.81` at HIP `0.35`;
  - coefficient `2.48` at HIP `0.60`;
  - linear extrapolation for slower walk positions.
- Standing jog ADS begins at coefficient `3.00` for HIP `0.70`, follows the calibrated curve, and is capped at `3.40` around HIP `0.82` and above.
- Crouch walk ADS uses a proportional coefficient curve from `0.84` to `1.68`.
- Crouch jog ADS uses coefficient `2.70`.
- Scrolling while ADS updates the underlying unified HIP level restored when ADS is released.

## Known limitation — weapon-dependent ADS movement

Wildlands applies weapon-dependent movement differences after every scalar and vector point currently controlled by the mod. Identical captured inputs therefore produce different visible speeds with different weapons.

The shared model has been tested with the Stoner LMG, an assault rifle, and a pistol and was judged acceptable, though not perfectly normalized. The assault rifle was used as the neutral reference; the pistol is the fastest limiting case.

Do not add input-based weapon tracking. Testing showed it can desynchronize through startup state, missed input, remapped controls, scripted swaps, and loadout changes. Weapon-category-specific curves remain deferred until a stable current-equipment class or slot field is discovered.

## Priority 1 — Extended gameplay regression

- Test long sessions, repeated attachment/restoration, loading, fast travel, death, respawn, and mission restart.
- Test all 27 positions in forward, backward, diagonal, and strafe movement.
- Recheck the walk/jog boundary, repeated `X` presses, sprint release, standing/crouching transitions, and ADS transitions.
- Test representative pistols, assault rifles, SMGs, LMGs, sniper rifles, and scoped weapons.
- Test slopes, stairs, interiors, uneven ground, water edges, combat, cover, reloads, item use, and camera transitions.
- Confirm menus, cutscenes, vehicles, and the `TAB` quick-select menu do not cause unintended wheel changes.
- Confirm mouse aiming remains unaffected and no controller input is synthesized.
- Record any startup stutter or temporarily inaccurate wheel state while the hooks settle.
- Re-run `--verify` after every forced helper termination or game crash.

### Acceptance criteria

- Full wheel traversal remains monotonic, repeatable, and free of snaps, reversals, dead positions, or hidden caps.
- Entering or leaving crouch and ADS preserves the intended logical level.
- No tested weapon becomes faster in ADS than its corresponding HIP movement in an obviously disruptive way.
- Animation quality remains acceptable at every stance and speed.
- Terrain variations remain small and recognizably native.
- The game remains stable during a representative multi-hour session.

## Priority 2 — Runtime and launcher polish

- Production launcher backend implemented; harden it through regression testing while preserving this contract:
  - Detect Steam and Ubisoft Connect installations.
  - Classify exact supported builds as green, structurally compatible unknown builds as amber, and incompatible signatures as red.
  - Treat firewall isolation as optional: active is green and inactive is amber, never launch-blocking.
  - Install and remove only firewall rules managed by this project, using elevation only for the requested action.
  - Detect SayNoToEAC, link to the original source and instructions, and independently block attachment whenever EAC is active or loaded.
  - Launch through the detected storefront, wait for the selected installation's `GRW.exe`, and attach automatically without activating, minimizing, or covering the game window.
  - Keep vanilla launch available even when a red condition blocks mod attachment.
  - Restore hooks when the adaptive primary action disables the mod, the launcher closes, or the game exits.
  - Poll volatile state such as GRW/EAC processes and runtime health every second; cache executable hashing, installation discovery, firewall enumeration, and other comparatively expensive checks on a slower cadence.
- Implement launcher/runtime supervision through a local named pipe or similarly narrow IPC boundary.
- Improve startup synchronization and ignore wheel input until initial native gait and stance are trustworthy.
- Decide which developer-only calibration and probe modes should remain in source but be excluded from public instructions.
- Decide whether the production window should expose the current movement level and stance or keep diagnostics behind an expandable panel.
- Investigate practical watchdog/crash-recovery behavior without weakening exact-byte safety checks.
- Keep `--verify`, `--restore`, and `--verbose` available for diagnosis.

## Priority 3 — Release preparation

- Choose a release version only after extended regression passes.
- Collect post-release community reports for clean Windows 10/11 systems and unusual configurations; these are not first-release blockers.
- Test standard and strict firewall modes and verify **Remove launcher data…** removes only rules managed by Better Movement for KBM.
- Verify the exact-instruction refusal path against an unsupported or deliberately mismatched build.
- Rebuild portable and source packages from the final commit.
- Regenerate and independently verify SHA-256 manifests.
- Review antivirus behavior and consider Authenticode signing.
- Prepare the Nexus description, installation guide, safety warning, known issues, changelog, screenshots/video, and support policy.
- Recheck current Ubisoft terms and Nexus submission rules immediately before publication.

## Deferred ideas

- Direct, reliable weapon-category detection and category-specific ADS curves.
- User-configurable level counts or response curves.
- Configurable wheel direction and sensitivity.
- Optional restoration of the pre-sprint level.
- On-screen movement-level indicator.
- Per-stance presets and prone-specific tuning.
- Additional executable builds only after exact signature analysis and validation.
