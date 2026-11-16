#!/usr/bin/env bash
# Stop the main SPT server (6969) on taragarmr. The headless clients require it, so systemd
# stops them too. Boot still starts it.
set -euo pipefail

host=${SPT_HOST:-taragarmr}
units=(spt-server)

ssh "$host" systemctl --user stop "${units[@]}"
ssh "$host" systemctl --user --no-pager --lines=0 status "${units[@]}" || true
