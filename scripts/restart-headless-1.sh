#!/usr/bin/env bash
# Restart Fika headless client 1 (raid host, UDP 25565) on taragarmr; the server and
# headless 2 keep running.
set -euo pipefail

host=${SPT_HOST:-taragarmr}
units=(spt-headless)

ssh "$host" systemctl --user restart "${units[@]}"
ssh "$host" systemctl --user --no-pager --lines=0 status "${units[@]}"
