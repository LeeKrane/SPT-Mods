#!/usr/bin/env bash
# Restart the main SPT server (6969) on taragarmr, plus the Fika headless clients that host its
# raids (their ~/spt-headless*/.server names spt-server). Those units require spt-server, so
# systemd restarts them with it anyway; they wait for 6969, so this returns once all are up.
set -euo pipefail

host=${SPT_HOST:-taragarmr}
units=(spt-server)
# Each headless unit is named after its instance directory.
for u in spt-headless spt-headless-2; do
  if [ "$(ssh "$host" cat "$u/.server" 2>/dev/null)" = spt-server ]; then units+=("$u"); fi
done

ssh "$host" systemctl --user restart "${units[@]}"
ssh "$host" systemctl --user --no-pager --lines=0 status "${units[@]}"
