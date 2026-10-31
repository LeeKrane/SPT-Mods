#!/usr/bin/env bash
# Restart both Fika headless clients on taragarmr; the SPT server keeps running.
set -euo pipefail

host=${SPT_HOST:-taragarmr}
units=(spt-headless spt-headless-2)

ssh "$host" systemctl --user restart "${units[@]}"
ssh "$host" systemctl --user --no-pager --lines=0 status "${units[@]}"
