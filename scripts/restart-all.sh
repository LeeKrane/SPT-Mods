#!/usr/bin/env bash
# Restart the SPT server and both Fika headless clients on taragarmr. The headless units
# wait for port 6969 before starting, so the restart returns once all three are up.
set -euo pipefail

host=${SPT_HOST:-taragarmr}
units=(spt-server spt-headless spt-headless-2)

ssh "$host" systemctl --user restart "${units[@]}"
ssh "$host" systemctl --user --no-pager --lines=0 status "${units[@]}"
