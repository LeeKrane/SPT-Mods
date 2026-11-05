#!/usr/bin/env bash
# Restart both SPT servers and both Fika headless clients on taragarmr: spt-server (6969) with
# headless 1, spt-server-vagabond (6970) with headless 2. The headless units wait for their
# server's port before starting, so the restart returns once all four are up.
set -euo pipefail

host=${SPT_HOST:-taragarmr}
units=(spt-server spt-server-vagabond spt-headless spt-headless-2)

ssh "$host" systemctl --user restart "${units[@]}"
ssh "$host" systemctl --user --no-pager --lines=0 status "${units[@]}"
