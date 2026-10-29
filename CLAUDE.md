# spt-fixes

BepInEx client plugins and SPT server mods. One project per directory; each builds to `<Name>/out/krane.<Name>.dll`.

All created plugins must be compatible with Fika 2.4.6 and SPT 4.1.6.

SPT plugin path: `~/Games/spt/drive_c/SPT/BepInEx/plugins`

Server mods (own `Directory.Build.props`, net10) are named with a `4Server` suffix (e.g. `FleaOfferGuard4Server`) and run on taragarmr, not locally. Deploy with `scripts/deploy-server-mod.sh <Name>` (keeps the remote `config.json`). Restart with `scripts/restart-all.sh` only when explicitly told to.

NEVER restart anything yourself (server, headless clients, services, game). Only restart when the user explicitly tells you to.

Never copy a built DLL into the plugin path. Symlink it, so a rebuild goes live without a copy step:

```
ln -s ~/development/spt-fixes/<Name>/out/krane.<Name>.dll ~/Games/spt/drive_c/SPT/BepInEx/plugins/krane.<Name>.dll
```

Every plugin has a concise `<Name>/README.md` covering: requirements (SPT/Fika versions, other mods, hard or soft dependency), what it does, why it exists, and a settings table (section / key, default, effect). Write it when creating a plugin. Update it in the same change whenever behavior, dependencies or config entries change.

Commit messages: subject only, super concise, prefixed with the plugin in brackets, e.g. `[DecalLockFix] Fix decal lock on reload`. Changes spanning several plugins list each: `[MagTopUp][UseItemsAnywhereTweaks] ...`. Non-plugin changes use `[scripts]` for `scripts/` and `[repo]` for repo-wide files (CLAUDE.md, .gitignore, Directory.Build.props).

## Communication

- Always answer super concisely.
- Never assume anything. When instructions on implementing, updating or fixing a plugin are unclear, ask questions before acting.
