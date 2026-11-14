#!/usr/bin/env bash
# Mod inventory: prints markdown tables of BepInEx plugins/patchers and SPT server mods.
# Reads assembly metadata only (nothing is loaded or run). Needs `dotnet` (SDK or runtime 10) on PATH,
# or nix (falls back to `nix shell nixpkgs#dotnet-sdk_10`). The tool is built on first use into scripts/inventory/out.
#
#   inventory.sh bepinex <BepInEx dir>        plugins + patchers of a client/headless install
#   inventory.sh server  <server mods dir>    SPT 4.x server mods (user/mods)
#   inventory.sh local                        this machine's client (~/Games/spt/drive_c/SPT/BepInEx); run this on tariognatha too
#   inventory.sh all                          local client + taragarmr (server, both headless), via read-only rsync of *.dll/package.json/mod.json
#
# Env: HOST (default taragarmr), TMPDIR_INV (default: fresh mktemp dir, removed on exit)
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
proj="$here/inventory"

run_dotnet() {
  if command -v dotnet >/dev/null 2>&1; then dotnet "$@"
  else nix shell nixpkgs#dotnet-sdk_10 -c dotnet "$@"; fi
}

build() {
  if [ ! -f "$proj/out/Inventory.dll" ] || [ "$proj/Program.cs" -nt "$proj/out/Inventory.dll" ]; then
    run_dotnet build "$proj/Inventory.csproj" -c Release -o "$proj/out" -v q --nologo >&2
  fi
}
tool() { build; run_dotnet "$proj/out/Inventory.dll" "$@"; }

local_client="$HOME/Games/spt/drive_c/SPT/BepInEx"
HOST="${HOST:-taragarmr}"
if [ -n "${TMPDIR_INV:-}" ]; then T=$TMPDIR_INV; else T=$(mktemp -d); trap 'rm -rf "$T"' EXIT; fi

pull() { # remote path -> local dir (dll + small metadata only)
  mkdir -p "$2"
  rsync -a --prune-empty-dirs --exclude='plugins-disabled/' --exclude='cache/' --exclude='core/' \
    --include='*/' --include='*.dll' --include='package.json' --include='mod.json' --exclude='*' \
    "$HOST:$1/" "$2/"
}

case "${1:-}" in
  bepinex) tool bepinex "$2" ;;
  server) tool server "$2" ;;
  local) tool bepinex "$local_client" ;;
  all)
    build
    echo "## Server ($HOST main)"; pull '~/spt-server/SPT_Runtime/user/mods' "$T/server"; tool server "$T/server"
    echo; echo "## Headless 1"; pull '~/spt-headless/game/BepInEx' "$T/h1"; tool bepinex "$T/h1"
    echo; echo "## Headless 2"; pull '~/spt-headless-2/game/BepInEx' "$T/h2"; tool bepinex "$T/h2"
    echo; echo "## Client $(hostname)"; tool bepinex "$local_client"
    ;;
  *) sed -n '2,13p' "$0"; exit 1 ;;
esac
