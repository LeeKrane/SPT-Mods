#!/usr/bin/env bash
# Stop every SPT unit on taragarmr: both servers and both Fika headless clients. Boot still
# starts the enabled ones.
set -euo pipefail

host=${SPT_HOST:-taragarmr}
units=(spt-headless spt-headless-2 spt-server-vagabond spt-server)

ssh "$host" systemctl --user stop "${units[@]}"
ssh "$host" systemctl --user --no-pager --lines=0 status "${units[@]}" || true
