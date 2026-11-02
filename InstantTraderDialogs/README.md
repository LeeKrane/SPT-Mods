# InstantTraderDialogs

**Requires:** SPT 4.1.6, Fika 2.4.6 compatible. Hard dependency: VisitAPI 1.3.7 (`com.sora.visitapi`). Built for the WTT content backport storyline traders whose VisitAPI rooms it replaces; the 3D rooms keep working when the toggle is off.

## What
With `SkipTraderRooms` on, the VISIT button opens the trader dialog directly (no room bundle, no loading screen, no 3D scene, no animations); the dialog keeps its own small trader icon. `.dlg` dialogs skip their 3D scene the same way. Raids are untouched. `TraderVoiceVolume` scales trader voice lines in the 3D room and in direct mode.

## Why
VisitAPI rooms load 300 MB to 1 GB of assets per visit. Direct mode opens instantly.

## Voice clips
Retail voice lines exist only inside the room bundles. Direct mode plays them from VisitAPI's voice cache (`BepInEx/cache/VisitAPI/voice/<traderId>/<key>.ogg`), so extract them once (about 81 MB, 20 s; re-runs skip existing files):

```
cd tools
nix shell nixpkgs#python3 -c python3 -m venv venv
venv/bin/pip install -r requirements.txt
venv/bin/python -I extract_voices.py
```

Needs ffmpeg with libvorbis (or `--format wav`). Without the clips, dialogs show subtitles only. Jaeger's `Labyrinth_1..4` lines have no clip in the bundle.

## Settings
| Section / key | Default | Effect |
|---|---|---|
| General / SkipTraderRooms | false | Skip 3D trader rooms and open the dialog directly. Read on each click. |
| General / TraderVoiceVolume | 1 (0 to 1) | Volume of trader voice lines in trader dialogs (room and direct mode). Music is not affected. |
