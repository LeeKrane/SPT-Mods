#!/usr/bin/env bash
# Restart Fika headless client 2 on taragarmr; the SPT server and headless 1 keep running.
set -euo pipefail

host=${SPT_HOST:-taragarmr}
unit=spt-headless-2

ssh "$host" systemctl --user restart "$unit"
ssh "$host" systemctl --user --no-pager --lines=0 status "$unit"
