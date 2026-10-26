# spt-fixes

BepInEx client plugins for SPT. One project per directory; each builds to `<Name>/out/krane.<Name>.dll`.

SPT plugin path: `~/Games/spt/drive_c/SPT/BepInEx/plugins`

Never copy a built DLL into the plugin path. Symlink it, so a rebuild goes live without a copy step:

```
ln -s ~/development/spt-fixes/<Name>/out/krane.<Name>.dll ~/Games/spt/drive_c/SPT/BepInEx/plugins/krane.<Name>.dll
```
