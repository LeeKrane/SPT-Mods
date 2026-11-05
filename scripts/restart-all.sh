#!/usr/bin/env bash
# Restart every SPT unit on taragarmr that is enabled (starts on boot): the main server (6969)
# while it is active, the Vagabond server (6970) and both Fika headless clients, which host the
# Vagabond server's raids. A disabled server (systemctl --user disable) is left alone. The
# headless units wait for their server's port, so this returns once all are up.
set -euo pipefail

host=${SPT_HOST:-taragarmr}
all=(spt-server spt-server-vagabond spt-headless spt-headless-2)

units=()
for u in "${all[@]}"; do
  if [ "$(ssh "$host" systemctl --user is-enabled "$u" 2>/dev/null)" = enabled ]; then units+=("$u"); fi
done
[ "${#units[@]}" -gt 0 ] || { echo "No enabled SPT units on $host" >&2; exit 1; }
ssh "$host" systemctl --user restart "${units[@]}"
ssh "$host" systemctl --user --no-pager --lines=0 status "${units[@]}"
