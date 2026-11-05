#!/usr/bin/env bash
# Restart Fika headless client 2 (Vagabond server raid host, UDP 25566) on taragarmr; the
# servers and headless 1 keep running.
set -euo pipefail

host=${SPT_HOST:-taragarmr}
units=(spt-headless-2)

ssh "$host" systemctl --user restart "${units[@]}"
ssh "$host" systemctl --user --no-pager --lines=0 status "${units[@]}"
