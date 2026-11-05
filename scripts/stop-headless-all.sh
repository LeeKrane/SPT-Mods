#!/usr/bin/env bash
# Stop both Fika headless clients on taragarmr; the servers keep running.
set -euo pipefail

host=${SPT_HOST:-taragarmr}
units=(spt-headless spt-headless-2)

ssh "$host" systemctl --user stop "${units[@]}"
ssh "$host" systemctl --user --no-pager --lines=0 status "${units[@]}" || true
