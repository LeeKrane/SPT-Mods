#!/usr/bin/env bash
# Mod inventory: one device matrix (INVENTORY.md) of BepInEx plugins/patchers and SPT server mods.
# Reads assembly metadata only (nothing is loaded or run). Needs `dotnet` (SDK or runtime 10) on PATH,
# or nix (falls back to `nix shell nixpkgs#dotnet-sdk_10`). The tool is built on first use into scripts/inventory/out.
#
#   inventory.sh all                          refresh snapshots (server + both headless via read-only rsync, this machine if tarmantria/tariognatha), then INVENTORY.md
#   inventory.sh matrix                       re-render INVENTORY.md from inventory/*.json + scripts/inventory/mods.json
#   inventory.sh local [--json]               this machine's client (~/Games/spt/drive_c/SPT/BepInEx); tariognatha: `local --json > inventory/tariognatha.json`
#   inventory.sh bepinex <BepInEx dir> [--json]   plugins + patchers of a client/headless install
#   inventory.sh server  <server mods dir> [--json]   SPT 4.x server mods (user/mods)
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

snapdir="$here/../inventory"
mkdir -p "$snapdir"
snapshot() { # device, then tool args (e.g. bepinex <dir>): writes inventory/<device>.json atomically
  local dev=$1; shift
  tool "$@" --json > "$T/$dev.json"
  mv "$T/$dev.json" "$snapdir/$dev.json"
}
matrix() { tool matrix "$proj/mods.json" "$snapdir" "$proj/footer.md" > "$T/INVENTORY.md"; mv "$T/INVENTORY.md" "$here/../INVENTORY.md"; }
local_device() { # this machine's device name, empty if unknown
  case "$(hostname)" in tarmantria|tariognatha) hostname ;; *) echo "" ;; esac
}

case "${1:-}" in
  bepinex) tool bepinex "$2" "${@:3}" ;;
  server) tool server "$2" "${@:3}" ;;
  local) tool bepinex "$local_client" "${@:2}" ;;
  matrix) matrix ;;
  all)
    build
    dev=$(local_device)
    if [ "$dev" != tariognatha ]; then
      pull '~/spt-server/SPT_Runtime/user/mods' "$T/server"; snapshot server server "$T/server"
      pull '~/spt-headless/game/BepInEx' "$T/h1"; snapshot headless1 bepinex "$T/h1"
      pull '~/spt-headless-2/game/BepInEx' "$T/h2"; snapshot headless2 bepinex "$T/h2"
    fi
    if [ -n "$dev" ]; then snapshot "$dev" bepinex "$local_client"
    else echo "warning: unknown host $(hostname), local snapshot skipped" >&2; fi
    matrix
    ;;
  *) sed -n '2,15p' "$0"; exit 1 ;;
esac
