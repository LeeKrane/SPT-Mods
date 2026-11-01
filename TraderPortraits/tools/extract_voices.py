#!/usr/bin/env python3
"""Extract trader voice clips from VisitAPI room bundles into VisitAPI's voice cache.

Reads every room bundle (<traderId>_<name>), finds the LipSyncDictionary entries
(key -> LipSyncElement.bakedData -> uLipSync BakedData.audioClip) and writes
<out>/<traderId>/<key>.ogg (Vorbis, what Unity decodes) or .wav, which
VisitAPI.Native.VoiceCache loads. Keys come from the dictionary, never the clip name.
Existing files are skipped. Bundles are untrusted: run with `python -I`.

Requirements: Python 3, `pip install -r requirements.txt` (UnityPy), ffmpeg with
libvorbis on PATH (otherwise use --format wav).

Setup + run (NixOS; UnityPy's bundled FMOD needs libstdc++, which the tool
locates through nix and re-execs itself with LD_LIBRARY_PATH):
    nix shell nixpkgs#python3 -c python3 -m venv venv
    venv/bin/pip install -r requirements.txt
    venv/bin/python -I extract_voices.py [--trader <24-hex id>] [--format ogg|wav]
Defaults: --rooms ~/Games/spt/drive_c/SPT/BepInEx/plugins/VisitAPI/rooms
          --out   ~/Games/spt/drive_c/SPT/BepInEx/cache/VisitAPI/voice
"""
import argparse
import glob
import json
import os
import re
import subprocess
import sys
from concurrent.futures import ThreadPoolExecutor

SPT = os.path.expanduser("~/Games/spt/drive_c/SPT/BepInEx")
EXTS = (".ogg", ".wav", ".mp3")
KEY_RE = re.compile(r"^[A-Za-z0-9_.-]{1,128}$")
TRADER_RE = re.compile(r"^[0-9a-f]{24}$")


def ensure_libstdcxx():
    """UnityPy's FMOD needs libstdc++; on NixOS it is not on the loader path."""
    if os.environ.get("EXTRACT_VOICES_REEXEC"):
        return
    import ctypes
    try:
        ctypes.CDLL("libstdc++.so.6")
        return
    except OSError:
        pass
    out = subprocess.run(["nix", "build", "--no-link", "--print-out-paths", "nixpkgs#stdenv.cc.cc.lib"],
                         capture_output=True, text=True).stdout.split()
    if not out:
        sys.exit("libstdc++.so.6 not found and nix lookup failed; set LD_LIBRARY_PATH")
    env = dict(os.environ, EXTRACT_VOICES_REEXEC="1",
               LD_LIBRARY_PATH=out[0] + "/lib:" + os.environ.get("LD_LIBRARY_PATH", ""))
    os.execve(sys.executable, [sys.executable, "-I"] + sys.argv, env)


def collect_files(env):
    files = {}
    def walk(f):
        for name, sub in f.files.items():
            if hasattr(sub, "objects"):
                files[name] = sub
            elif hasattr(sub, "files"):
                walk(sub)
    walk(env)
    return files


def resolve(files, cur, pptr):
    """Resolve a raw {m_FileID, m_PathID} dict to an ObjectReader."""
    fid, pid = pptr["m_FileID"], pptr["m_PathID"]
    if pid == 0:
        return None
    if fid == 0:
        return cur.objects.get(pid)
    path = cur.externals[fid - 1].path
    name = path.split("/")[-1]
    f = files.get(name) or files.get(name.replace(".sharedAssets", "") + ".sharedAssets")
    return f.objects.get(pid) if f else None


def find_keys(env):
    """Return {key: ObjectReader(AudioClip)} plus warnings."""
    files = collect_files(env)
    keys, warns = {}, []
    for f in files.values():
        for o in f.objects.values():
            if o.type.name != "MonoBehaviour":
                continue
            try:
                t = o.read_typetree()
            except Exception:
                continue
            ents = t.get("entries")
            if not isinstance(ents, list) or not ents:
                continue
            for e in ents:
                v = e.get("value") if isinstance(e, dict) else None
                if not isinstance(v, dict) or "bakedData" not in v:
                    continue
                key = e.get("key")
                if not key:
                    continue
                if not isinstance(key, str) or not KEY_RE.match(key) or key in (".", ".."):
                    warns.append(f"{key!r}: unsafe key, skipped")
                    continue
                try:
                    bd = resolve(files, f, v["bakedData"])
                    if bd is None:
                        continue
                    bf = bd.assets_file
                    clip = resolve(files, bf, bd.read_typetree().get("audioClip") or {"m_FileID": 0, "m_PathID": 0})
                except Exception as ex:
                    warns.append(f"{key}: unreadable entry ({ex!r})")
                    continue
                if clip is None or clip.type.name != "AudioClip":
                    warns.append(f"{key}: no AudioClip")
                    continue
                ident = (clip.assets_file.name, clip.path_id)
                if key in keys and keys[key][0] != ident:
                    warns.append(f"{key}: conflicting clips, keeping first")
                    continue
                keys.setdefault(key, (ident, clip))
    return {k: v[1] for k, v in keys.items()}, warns


def to_ogg(wav):
    r = subprocess.run(["ffmpeg", "-nostdin", "-v", "error", "-f", "wav", "-i", "pipe:0",
                        "-c:a", "libvorbis", "-q:a", "4", "-f", "ogg", "pipe:1"],
                       input=wav, capture_output=True)
    if r.returncode or not r.stdout.startswith(b"OggS"):
        raise RuntimeError(r.stderr.decode(errors="replace")[:200])
    return r.stdout


def write(path, data):
    tmp = path + ".part"
    with open(tmp, "wb") as fh:
        fh.write(data)
    os.replace(tmp, path)


def extract_bundle(UnityPy, bundle, outdir, fmt):
    env = UnityPy.load(bundle)
    keys, warns = find_keys(env)
    os.makedirs(outdir, exist_ok=True)
    todo = {k: c for k, c in keys.items()
            if not any(os.path.exists(os.path.join(outdir, k + e)) for e in EXTS)}
    wavs = {}  # clip ident -> wav bytes, so shared clips are exported once
    jobs = []
    for k, c in todo.items():
        ident = (c.assets_file.name, c.path_id)
        if ident not in wavs:
            try:
                samples = c.read().samples
                wavs[ident] = next(iter(samples.values()))
            except Exception as e:
                wavs[ident] = None
                warns.append(f"{k}: export failed ({e!r})")
        if wavs[ident] is not None:
            jobs.append((k, ident))

    def one(job):
        k, ident = job
        try:
            dest = os.path.realpath(os.path.join(outdir, k + "." + fmt))
            if os.path.commonpath([os.path.realpath(outdir), dest]) != os.path.realpath(outdir):
                return f"{k}: path escapes output dir, skipped"
            data = to_ogg(wavs[ident]) if fmt == "ogg" else wavs[ident]
            write(dest, data)
            return None
        except Exception as e:
            return f"{k}: encode failed ({e})"
    with ThreadPoolExecutor(max_workers=os.cpu_count() or 4) as ex:
        warns += [w for w in ex.map(one, jobs) if w]
    return keys, len(jobs), len(keys) - len(todo), warns


def dialogue_ids(path):
    ids = {}
    def walk(n, trader):
        if isinstance(n, dict):
            for k, v in n.items():
                if k == "lipSyncId" and isinstance(v, str):
                    ids.setdefault(trader, set()).add(v)
                else:
                    walk(v, trader)
        elif isinstance(n, list):
            for x in n:
                walk(x, trader)
    for el in json.load(open(path)).get("elements", []):
        walk(el, el.get("Trader"))
    return ids


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--rooms", default=os.path.join(SPT, "plugins/VisitAPI/rooms"))
    ap.add_argument("--out", default=os.path.join(SPT, "cache/VisitAPI/voice"))
    ap.add_argument("--trader", help="only this 24-hex trader id")
    ap.add_argument("--format", choices=["ogg", "wav"], default="ogg")
    a = ap.parse_args()
    ensure_libstdcxx()
    import UnityPy

    dlg = os.path.join(a.rooms, "dialogue.json")
    ids = dialogue_ids(dlg) if os.path.exists(dlg) else {}
    bundles = sorted(p for p in glob.glob(os.path.join(a.rooms, "*_*"))
                     if "." not in os.path.basename(p) and os.path.basename(p) != "vendors_scripts")
    for b in bundles:
        tid = os.path.basename(b).split("_")[0]
        if not TRADER_RE.match(tid) or (a.trader and a.trader != tid):
            continue
        try:
            keys, wrote, skipped, warns = extract_bundle(UnityPy, b, os.path.join(a.out, tid), a.format)
        except Exception as e:
            print(f"{os.path.basename(b)}: failed ({e!r}), skipped", file=sys.stderr)
            continue
        have = {k for k in keys if any(os.path.exists(os.path.join(a.out, tid, k + e)) for e in EXTS)}
        want = ids.get(tid, set())
        print(f"{os.path.basename(b)}: keys={len(keys)} wrote={wrote} skipped={skipped} "
              f"dialogue ids {len(want & have)}/{len(want)}")
        miss = sorted(want - have)
        if miss:
            print(f"  missing dialogue ids: {', '.join(miss[:40])}{' ...' if len(miss) > 40 else ''}")
        for w in warns:
            print("  warn:", w)


if __name__ == "__main__":
    main()
