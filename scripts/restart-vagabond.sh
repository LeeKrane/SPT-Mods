#!/usr/bin/env bash
# Restart the Vagabond SPT server (6970) and both Fika headless clients, its raid hosts, on
# taragarmr. The headless units wait for 6970, so this returns once all three are up.
set -euo pipefail

host=${SPT_HOST:-taragarmr}
units=(spt-server-vagabond spt-headless spt-headless-2)

ssh "$host" systemctl --user restart "${units[@]}"
ssh "$host" systemctl --user --no-pager --lines=0 status "${units[@]}"
