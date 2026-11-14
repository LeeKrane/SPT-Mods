# inventory

Small console tool (net10, `System.Reflection.Metadata`, no assembly loading) behind `scripts/inventory.sh`.

- `Inventory bepinex <BepInEx dir>`: plugins (`[BepInPlugin]` name/GUID/version) and patchers (assembly/informational version). Marks symlinked krane plugins with the repo commit.
- `Inventory server <mods dir>`: SPT 4.x server mods; finds the type implementing `IModMetadata` (or deriving `AbstractModMetadata`) and reads Name/Author/ModGuid/Version/SptVersion from the ctor IL (`ldstr`, `newobj Version/Range`, `ldc.i4`, `stfld <Prop>k__BackingField`). Falls back to assembly version, or package.json/mod.json.

Requirements: `dotnet` 10 on PATH, or `nix` (`nix shell nixpkgs#dotnet-sdk_10`); a runtime-only install is enough once built (`scripts/inventory/out/` is built on first run and git-ignored). On tariognatha: `scripts/inventory.sh local`.
