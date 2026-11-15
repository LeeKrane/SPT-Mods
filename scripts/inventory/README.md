# inventory

Small console tool (net10, `System.Reflection.Metadata`, no assembly loading) behind `scripts/inventory.sh`. It renders `INVENTORY.md`: one matrix with a row per mod and a column per device (Server taragarmr main, Headless 1, Headless 2, tarmantria, tariognatha).

Cell: version if installed, `!` intended but missing, `-` not intended and absent, `1.2.3 ⚠` installed but not intended, `?` no snapshot for that device. Server column = version of the server part; device columns = client plugins/patchers (differing versions of one mod's parts show as `a/b`). Symlinked krane plugins add `@<commit>`.

## Data flow

1. Snapshots: `Inventory bepinex <BepInEx dir> --json` and `Inventory server <mods dir> --json` list every plugin/patcher/server mod (kind, folder key, name, GUID, version, symlink commit). Stored in `inventory/<device>.json` (`server`, `headless1`, `headless2`, `tarmantria`, `tariognatha`).
2. Intent: `scripts/inventory/mods.json` lists each mod with the ids (GUID or folder/file name, GUID matched first) of its server and client parts, and `targets`: `server`, `headless` (both headless clients), `clients` (tarmantria and tariognatha share it, so they stay in sync).
3. `Inventory matrix <mods.json> <snapshot dir> <footer.md>` renders `INVENTORY.md` (attention rows first, then alphabetical, plus an Attention list). `scripts/inventory/footer.md` is appended verbatim (pending archives and backups).

Parts not matched by any manifest entry still get a row flagged _(not in manifest)_ and are listed in a stderr warning; classify them in `mods.json`. Manifest entries that match nothing also warn.

## Commands (`scripts/inventory.sh`)

- `all`: pulls server and both headless installs (read-only rsync of `*.dll`/`package.json`/`mod.json`), snapshots them plus this machine (hostname `tarmantria` or `tariognatha`), regenerates `INVENTORY.md`. On tariognatha only the local snapshot is taken (no rsync).
- `matrix`: re-render from stored snapshots and manifest.
- `local [--json]`, `bepinex <dir> [--json]`, `server <dir> [--json]`: single targets; without `--json` they print the raw markdown tables.

tariognatha: `git pull`, `scripts/inventory.sh all` (or `scripts/inventory.sh local --json > inventory/tariognatha.json` then `scripts/inventory.sh matrix`), commit `inventory/tariognatha.json` and `INVENTORY.md`.

Server mod versions come from the `IModMetadata` ctor IL (`ldstr`, `newobj Version/Range`, `ldc.i4`, `stfld <Prop>k__BackingField`), falling back to assembly version, or package.json/mod.json.

Requirements: `dotnet` 10 on PATH, or `nix` (`nix shell nixpkgs#dotnet-sdk_10`); a runtime-only install is enough once built (`scripts/inventory/out/` is built on first run and git-ignored).
