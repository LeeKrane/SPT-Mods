#!/usr/bin/env bash
# Push the local KraneSPT SVM preset (written by Greed) to taragarmr and back up the
# server's copy. Restarts nothing; SVM loads the preset on the next server start.
set -euo pipefail

host=${SPT_HOST:-taragarmr}
preset=${SPT_PRESET:-KraneSPT}
svm='SPT_Runtime/user/mods/[SVM] Server Value Modifier/Presets'
local_file="$HOME/Games/spt/drive_c/SPT/$svm/$preset.json"
remote_file="spt-server/$svm/$preset.json"

[ -f "$local_file" ] || { echo "No local preset: $local_file" >&2; exit 1; }
jq empty "$local_file" || { echo "Local preset is not valid JSON: $local_file" >&2; exit 1; }

stamp=$(date +%Y%m%d-%H%M%S)
# %q quotes the path for the remote shell; it has spaces and brackets.
remote_q=$(printf '%q' "$remote_file")

ssh "$host" "[ -f $remote_q ] && cp -p $remote_q $remote_q.bak-$stamp || true"
ssh "$host" "cat > $remote_q.tmp && mv $remote_q.tmp $remote_q" <"$local_file"
echo "Pushed $preset.json to $host (old copy: $preset.json.bak-$stamp)"
