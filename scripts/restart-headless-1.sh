#!/usr/bin/env bash
# Restart Fika headless client 1 (Vagabond server raid host, UDP 25565) on taragarmr; the
# servers and headless 2 keep running.
set -euo pipefail

host=${SPT_HOST:-taragarmr}
units=(spt-headless)

ssh "$host" systemctl --user restart "${units[@]}"
ssh "$host" systemctl --user --no-pager --lines=0 status "${units[@]}"
