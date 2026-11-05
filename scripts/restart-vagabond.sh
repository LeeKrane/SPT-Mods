#!/usr/bin/env bash
# Restart the Vagabond SPT server (6970) and its raid host, headless 2, on taragarmr. The main
# server and headless 1 keep running. Headless 2 waits for 6970, so this returns once both are up.
set -euo pipefail

host=${SPT_HOST:-taragarmr}
units=(spt-server-vagabond spt-headless-2)

ssh "$host" systemctl --user restart "${units[@]}"
ssh "$host" systemctl --user --no-pager --lines=0 status "${units[@]}"
