#!/usr/bin/env bash
# Push a built server mod (<Name>/out/) to taragarmr's SPT mods dir as krane-<Name>. Builds and
# restarts nothing (run scripts/restart-all.sh after). The remote config.json is never overwritten;
# the built one is copied only if absent.
set -euo pipefail

host=${SPT_HOST:-taragarmr}
name=${1:?usage: deploy-server-mod.sh <Name>}
out="$(cd "$(dirname "$0")/.." && pwd)/$name/out"
dest="spt-server/SPT_Runtime/user/mods/krane-$name"

[ -d "$out" ] || { echo "No build output: $out" >&2; exit 1; }
ls "$out"/krane.*.dll >/dev/null 2>&1 || { echo "No krane.*.dll in $out" >&2; exit 1; }

ssh "$host" mkdir -p "$dest"
if command -v rsync >/dev/null; then
  rsync -rt --delete --exclude=config.json --exclude='*.bak*' "$out"/ "$host:$dest/"
  [ ! -f "$out/config.json" ] || rsync -t --ignore-existing "$out/config.json" "$host:$dest/config.json"
else
  tar -C "$out" --exclude=config.json -cf - . | ssh "$host" tar -C "$dest" -xf -
  [ ! -f "$out/config.json" ] || ssh "$host" "[ -f $dest/config.json ] || cat > $dest/config.json" <"$out/config.json"
fi
echo "Deployed $name to $host:$dest (remote config.json kept if present)"
