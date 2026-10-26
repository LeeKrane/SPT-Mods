# DecalLockFix

**Requires:** SPT 4.1.6. No other mods. Fika-compatible (2.4.6).

## What
Lets the main thread dispose decal texture trees without waiting on the decal mip streaming thread. While trees are disposed, the streaming thread sleeps without holding its lock. It resumes on the next rendered frame.

## Why
EFT's `TextureQuadTree.WorkThread` holds `_treesLock` while it sleeps and retakes it immediately. `Monitor` is not fair, so `TextureQuadTree.Dispose` (run after each raid) can wait for minutes. Under Wine the main thread rarely wins, so leaving a raid hangs.

## Settings
None.
