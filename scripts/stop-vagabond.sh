#!/usr/bin/env bash
# Stop the Vagabond SPT server (6970) and both Fika headless clients, which need it.
set -euo pipefail

host=${SPT_HOST:-taragarmr}
units=(spt-headless spt-headless-2 spt-server-vagabond)

ssh "$host" systemctl --user stop "${units[@]}"
ssh "$host" systemctl --user --no-pager --lines=0 status "${units[@]}" || true
