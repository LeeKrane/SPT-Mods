#!/usr/bin/env bash
# Restart Fika headless client 1 (main server's raid host) on taragarmr; both SPT servers and
# headless 2 keep running.
set -euo pipefail

host=${SPT_HOST:-taragarmr}
unit=spt-headless

ssh "$host" systemctl --user restart "$unit"
ssh "$host" systemctl --user --no-pager --lines=0 status "$unit"
