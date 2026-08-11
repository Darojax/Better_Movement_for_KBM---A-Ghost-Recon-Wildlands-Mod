# GRW Motion Scanner

Read-only differential scanner for locating downstream motion values using a walk / idle / walk / jog sequence.
It imports no process-memory write, allocation, injection, or remote-thread API.

Use `--load <motion-result-file>` to resume from a saved shortlist.
Tap `F4` to print the float neighborhood around every remaining candidate.

Use a long, open, level stretch and preserve the same heading:

1. Walk freely with `W` and tap `F5`; keep moving until the scan finishes.
2. Stand idle and tap `F6`; remain idle until it finishes.
3. Walk freely again and tap `F7`; keep moving until it finishes.
4. Jog freely and tap `F8`; keep moving until it finishes.
5. Tap `F10` to save the remaining addresses and measured ratios.
