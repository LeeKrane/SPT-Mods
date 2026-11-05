#!/usr/bin/env bash
# Stop Fika headless client 2 on taragarmr; it stays down until started or restarted.
set -euo pipefail

host=${SPT_HOST:-taragarmr}
units=(spt-headless-2)

ssh "$host" systemctl --user stop "${units[@]}"
ssh "$host" systemctl --user --no-pager --lines=0 status "${units[@]}" || true
