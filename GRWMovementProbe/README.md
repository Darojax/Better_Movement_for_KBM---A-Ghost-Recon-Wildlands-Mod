een re

# GRW Movement Probe

This is a deliberately narrow, session-local validation utility. It writes up to eight supplied four-byte floats only
while the user holds `F4` together with exactly one of `W` or `S`.

Safety limits:

- Refuses targets outside committed, writable, private memory.
- Refuses activation unless the target initially contains exactly `+1.0` or `-1.0`.
- Accepts magnitudes only from `0.05` through `0.95`.
- Writes only the supplied addresses, with a hard maximum of eight four-byte targets.
- Stops on hotkey release, movement-key release, failure, or the 10-second activation cap.
- Does not patch files, allocate remote memory, inject code, or create remote threads.

This probe is for the isolated, offline single-player research session only.
