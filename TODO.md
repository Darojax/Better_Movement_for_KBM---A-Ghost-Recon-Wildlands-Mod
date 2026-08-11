# GRW Analogue Movement Mod — TODO

## Current status

**Not ready for public release.**

The `0.1.0-beta` artifacts establish the intended packaging, safety checks, firewall workflow, documentation, and installer structure. They are development previews only. Gameplay behavior still requires redesign and extended play-testing before anything is uploaded to Nexus Mods.

The currently proven technical foundation includes:

- Stable RAM-only movement scaling.
- Mouse-wheel input while moving.
- Exact hook-site verification and restoration.
- Walk, jog, and ADS scalar control.
- RMB gating so ADS logic cannot affect HIP movement.
- Sprint reset behavior.
- Offline/EAC/firewall preflight tooling.

## Priority 1 — Combine the vanilla gait toggle with one unified speed ladder

**Implementation status:** Complete in the development runtime; initial in-game validation passed on 2026-08-11. Keep this section as the regression contract until extended play-testing is complete.

### Problem

Separate saved walk and jog profiles make the wheel's available range depend on a hidden gait state. During crouching, tactical movement, ADS, terrain changes, and other transitions, it is easy to lose track of that state and unexpectedly hit the wrong speed limit.

### Intended behavior

- The vanilla `X` walk/jog control remains useful as an instant gait selector.
- The mouse wheel provides one complete granular range from the slowest walk through the fastest jog, regardless of the starting gait.
- Scrolling should traverse one ordered movement ladder:
  - 12 walking levels from `0.05` through `0.60`.
  - Boundary transition from maximum walk to minimum jog.
  - 9 jogging levels from `0.70` through `1.00`.
- Scrolling across the boundary must remain continuous and must not depend on a synthetic `X` keypress.
- Each ladder position is an absolute target speed. The helper derives the required multiplier from Wildlands' current hidden native gait magnitude.
- Pressing physical `X` while the current wheel level is in the walking range jumps to vanilla full jog.
- Pressing physical `X` while the current wheel level is in the jogging range jumps to vanilla walk speed.
- Pressing `X` deliberately discards the current custom level; no separate custom walk or jog speed is remembered.

### Design work

- Replace separate saved walk/jog profiles with one current level index covering the complete ladder.
- Do not synthesize `X` when the wheel crosses the boundary; Wildlands ignores software-generated toggle input and this causes mismatched multipliers, speed caps, and dead wheel positions.
- Track the current unified level independently of Wildlands' hidden native gait.
- Normalize every HIP target against the detected native magnitude (`1.00` jog or approximately `0.35` walk), allowing the full ladder from either native starting state.
- Consume physical `X` while the helper has a known state and emulate its visible behavior by selecting the opposite vanilla anchor.
- Treat physical `X` as a jump based on the current unified speed range rather than a possibly stale internal gait flag.
- Establish a reliable initial gait/level when attaching mid-session.
- Ensure mode synchronization survives:
  - Standing still and moving again.
  - Crouching and standing.
  - Entering and leaving ADS.
  - Sprinting and releasing Shift.
  - Pausing, menus, cutscenes, vehicles, and loading screens.

### ADS behavior to preserve

- Walk ADS remains correlated with the current walking level using the calibrated `1.6875–3.375` curve unless further testing changes it.
- Jog ADS remains fixed at the globally calibrated `4.10`.
- Scrolling during jog ADS may adjust the underlying unified HIP level without changing visible jog-ADS speed.
- Crossing the walk/jog boundary while ADS should change the logical ADS range without synthetic input.
- The overall practical ADS ceiling remains approximately the slowest jogging speed.

### Sprint behavior to revisit

- Shift must still provide immediate sprint/full-jog behavior.
- Releasing sprint returns to vanilla maximum jog, matching the established desired behavior.

### Acceptance criteria

- Full wheel-down to wheel-up traversal is monotonic and repeatable.
- The complete ladder exposes all intended levels.
- The full ladder is available from both vanilla walk and vanilla jog starting states.
- `X` from any walking-range level selects vanilla full jog.
- `X` from any jogging-range level selects vanilla walk speed.
- Repeated `X` presses at the vanilla anchors reproduce the familiar vanilla toggle behavior.
- Walk-to-jog and jog-to-walk boundaries never become inverted and contain no capped or ignored wheel positions.
- The player can always reach both the slowest walk and fastest jog regardless of prior state.
- No mid-range speed dips, reversals, or unexpected snaps.
- HIP and ADS release into the correct unified level.

### Initial validation result

- Full jog scrolled continuously down through the jogging and walking ranges to the slowest walk.
- No snap back to full jog, dead wheel positions, or boundary delay remained after changing to absolute target-speed normalization.
- Physical `X`, sprint reset, HIP movement, and ADS behavior all matched the intended controls in the initial combined test.
- The earlier synthetic-`X` boundary implementation was rejected because Wildlands ignored the injected toggle and produced capped multipliers and speed snapping.

## Priority 2 — Investigate and redesign crouching movement

### Problem

Crouching is currently much too slow across most practical situations. The existing granular system exposes this limitation more clearly, and crouch movement needs its own measurement and calibration.

### Investigation

- Determine whether crouch uses:
  - The existing HIP magnitude scalar.
  - The ADS scalar path.
  - A separate crouch multiplier or speed cap.
  - Different values for crouch-walk and crouch-jog states.
- Capture baseline values for:
  - Crouch HIP at slow, middle, and maximum input.
  - Crouch ADS at slow, middle, and maximum input.
  - Forward, backward, and strafing movement.
  - Flat terrain, uphill, and downhill movement.
- Determine whether automatic walk/jog boundary changes remain valid while crouched.
- Check animation quality and foot sliding at increased crouch speeds.

### Intended behavior

- Crouch speed should remain granular and respond predictably to the same wheel ladder.
- Practical crouch movement should be substantially faster than current vanilla behavior where appropriate.
- Maximum crouch speed must remain visually believable and must not force standing/jogging animations.
- Crouch ADS should correlate sensibly with crouch HIP movement.
- Standing and crouching must preserve or intentionally map the current unified speed level without unexpected jumps.

### Acceptance criteria

- Crouch speed is monotonic across the wheel range.
- No animation corruption, foot sliding, or repeated state snapping.
- Entering/exiting crouch does not lose gait synchronization.
- Crouch ADS and HIP speeds feel deliberately related.
- Terrain variations remain small and non-disruptive.

## Priority 3 — Investigate weapon-dependent ADS movement

### Observed behavior

- ADS movement with a pistol is substantially faster than ADS movement with the Stoner MG.
- It is not yet known whether this difference is entirely vanilla behavior, is amplified by the current ADS patch, or varies by individual weapon or weapon class.

### Investigation findings — 2026-08-11

- Matched full-jog ADS captures produced effectively identical values for the Stoner MG and pistol at every currently controlled stage:
  - Original ADS scalar: `2.5` for both.
  - Mod-selected ADS scalar: `4.6` for both.
  - Pre-multiplication movement-vector magnitude: `1.0` for both.
  - Deeper ADS output multiplier: `0.3` for both.
- The weapon-specific speed difference is therefore introduced after the mod's existing scalar and vector control points, probably in Wildlands' locomotion or animation layer.
- The diagnostic output-writer hook was restored successfully and should not be part of the normal runtime.
- Current recommendation: preserve the vanilla weapon-weight distinction unless broader weapon-class testing finds unreasonable outliers. Normalization would require a deeper and more fragile weapon-state or locomotion hook.
- Global jog-ADS calibration result:
  - The pistol was used as the fastest/limiting weapon case.
  - `4.10` was captured as the maximum acceptable pistol jog-ADS coefficient.
  - The same `4.10` value was judged acceptable with an assault rifle and an LMG.
  - Adopt `4.10` as the global ceiling while allowing Wildlands to preserve its downstream weapon-dependent differences.
- Pistol-detection investigation:
  - A stable player ADS owner was captured directly from the established ADS hook.
  - Repeated Stoner/pistol snapshots were compared through two pointer depths plus one targeted third-level object.
  - A candidate boolean failed broader validation: the second LMG reported the pistol-like value while the assault rifle reported the non-pistol value.
  - A candidate structured fingerprint also failed: the original Stoner and assault rifle shared one fingerprint, while the other LMG and both pistols shared another.
  - No tested field consistently separated three pistol captures from four non-pistol captures.
  - The downstream diagnostic writer also observes unrelated movement objects and cannot identify the player's weapon without an additional stable owner relationship.
  - Input-based sidearm tracking was rejected as insufficiently stable because it can desynchronize through startup state, remapped bindings, missed events, scripted swaps, and loadout changes.
  - Conclusion: do not implement pistol-specific behavior unless a future investigation finds a direct, stable current-equipment class or slot field.

### Investigation

- Measure and compare ADS movement with:
  - Pistols and other sidearms.
  - Assault rifles and submachine guns.
  - Light machine guns, including the Stoner MG.
  - Sniper rifles and other scoped weapons.
- Test both walk-ADS and jog-ADS at equivalent underlying wheel levels.
- Determine whether the game applies a weapon-specific movement multiplier, animation-state cap, or a different movement path.
- Check whether changing weapons while already ADS or moving produces a stale or incorrect speed state.
- Decide whether the final design should preserve a modest weapon-weight distinction or normalize ADS movement across weapon classes. Do not tune globally around a single weapon.

### Acceptance criteria

- ADS speed remains predictable after switching between a primary weapon and pistol.
- No weapon class is unintentionally extremely slow or unrealistically fast.
- Any intentional weapon-dependent difference is documented and feels proportionate.
- The calibrated walk-ADS curve and fixed jog-ADS ceiling behave consistently for every tested weapon class.

## Priority 4 — Extended gameplay regression testing

- Test multiple weapons, including scoped weapons and sidearms.
- Test forward, backward, diagonal, and strafe movement.
- Test standing, crouching, and eventually prone movement.
- Test slopes, stairs, interiors, water edges, and uneven terrain.
- Test combat, detection states, cover, reloads, item use, and camera transitions.
- Test long sessions and repeated loading/fast travel.
- Test death, respawn, mission restart, and character transitions.
- Test vehicles and confirm the wheel does not alter movement profiles while driving.
- Reconfirm the TAB quick-select menu remains conflict-free.
- Reconfirm controller input is not synthesized and mouse aiming remains unaffected.
- Record any startup stutter or temporarily missed wheel input after attachment.

## Priority 5 — Runtime polish after gameplay stabilizes

- Replace floating-point accumulation with explicit indexed level tables.
- Add automatic gait-transition state logging available only under `--verbose`.
- Improve startup synchronization and avoid accepting wheel input until the initial state is settled.
- Add a small status UI or tray indicator showing:
  - Runtime attached/restored.
  - Current unified level.
  - Internal walk/jog gait.
  - Standing/crouching/ADS state where detectable.
- Add a clean stop command from the launcher instead of relying only on `F5`.
- Add crash recovery/watchdog behavior for restoring hooks when practical.
- Remove all remaining calibration-only command-line paths from the public runtime.

## Priority 6 — Release preparation

- Do not upload the current `0.1.0-beta` artifacts.
- Increment the version only after the unified ladder and crouch behavior pass regression testing.
- Perform a clean-machine installer test.
- Test both standard and strict firewall modes.
- Confirm uninstall removes only rules prefixed `GRW Movement Mod - `.
- Rebuild portable, installer, and source packages from the final source.
- Regenerate and verify all SHA-256 manifests.
- Consider Authenticode signing before public distribution.
- Prepare Nexus description, installation guide, known issues, changelog, screenshots/video, and support policy.
- Recheck current Ubisoft terms and Nexus submission rules immediately before publication.

## Deferred ideas

- User-configurable level counts and curves.
- Optional restoration of pre-sprint speed.
- On-screen level indicator.
- Per-stance presets.
- Configurable wheel direction and sensitivity.
- Support for additional executable builds only after exact signature analysis.
