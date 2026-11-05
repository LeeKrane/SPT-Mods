#!/usr/bin/env bash
# Stop the main SPT server (6969). It no longer starts on boot (disabled while both headless
# clients host Vagabond raids); `systemctl --user enable --now spt-server` brings it back.
set -euo pipefail

host=${SPT_HOST:-taragarmr}
units=(spt-server)

ssh "$host" systemctl --user stop "${units[@]}"
ssh "$host" systemctl --user --no-pager --lines=0 status "${units[@]}" || true
