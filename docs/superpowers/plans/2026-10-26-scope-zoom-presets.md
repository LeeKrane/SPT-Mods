# ScopeZoomPresets Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** BepInEx client plugin that makes the scope magnification hotkey on adjustable optics toggle between per-group configured percent points, resets enabled scopes to their min point at raid start, and shows `N% · ~Zx` on the HUD on every zoom change.

**Architecture:** Pure math and group-key logic live in Unity-free files covered by an xunit test project. Harmony patches on `ScopeZoomHandler.ForceChangeScopeState`, `ScopeZoomHandler.method_4`, `GameWorld.OnGameStarted` and `EftBattleUIScreen` apply the behavior to the local player only. Config is BepInEx `ConfigEntry` per group, shown in F12 ConfigurationManager.

**Tech Stack:** C# net472, BepInEx 5, HarmonyX (`0Harmony.dll`), EFT Assembly-CSharp (SPT 4.1.6), Comfort.dll; tests on net8.0 + xunit.

**Spec:** `docs/superpowers/specs/2026-10-26-scope-zoom-presets-design.md`

## Global Constraints

- Compatible with SPT 4.1.6 and Fika 2.4.6. No network changes; local player only.
- Output: `ScopeZoomPresets/out/krane.ScopeZoomPresets.dll`. Build from `ScopeZoomPresets/`: `nice -n 19 nix shell nixpkgs#dotnet-sdk_8 -c dotnet build -c Release -o out`.
- Deploy by symlink only, never copy: `ln -s ~/development/spt-fixes/ScopeZoomPresets/out/krane.ScopeZoomPresets.dll ~/Games/spt/drive_c/SPT/BepInEx/plugins/krane.ScopeZoomPresets.dll`.
- Commit messages: subject only, prefix `[ScopeZoomPresets]`, no attribution lines.
- `ScopeZoomPresets/README.md` covers requirements, what it does, why, settings table.
- Groups (fixed): 1x-4x, 1x-6x, 1x-8x, 2.5x-8x, 3x-9x, 3x-10x, 3x-20x, 3x-24x, 4x-16x, 5x-25x, 6.5x-20x, 7x-35x.
- Per group: `Enabled` (false), `Min %` (0), `Max %` (100), ints 0-100. Section `HUD`: `Show zoom label` (true).
- `MinFov = MinMaxFov.x` is the widest FOV (lowest zoom); `MaxFov = MinMaxFov.y` is the narrowest (highest zoom). 0% = `MinFov`, 100% = `MaxFov`.
- Game code reference (decompiled): `/tmp/claude-1000/-home-krane-development-spt-fixes/d78fe541-6172-4242-b482-4a7f9488b52f/scratchpad/decomp/` (`ScopeZoomHandler.cs`, `EFT.UI/BattleUIScreen.cs`, `EFT/GameWorld.cs`, `EFT/Firearms.cs`).

## Review Focus

- Min % set above Max %: points are swapped, toggle still alternates (unit test in Task 1, used in Task 2).
- Min % equal to Max %: hotkey always lands on the same point, no exception (unit test in Task 1).
- Scope index whose `Zooms` entry has one value (ADO's 1x backup sight) or an unlisted range: no group, vanilla toggle (unit test in Task 1).
- Reversed or duplicated labels (`[6, 1]`, `[3, 3, 10, 10]`): resolve to the normal group name (unit test in Task 1).
- Bots and Fika observed players run the same `ScopeZoomHandler` code: patches must not touch their scopes (ownership gate in Task 2, in-game check in Tasks 2 and 4).

---

## File Structure

- `ScopeZoomPresets/ScopeZoomPresets.csproj` - plugin project; excludes `tests/**`.
- `ScopeZoomPresets/ZoomMath.cs` - percent/FOV conversion, approximate zoom, toggle target, label text. Unity-free.
- `ScopeZoomPresets/GroupKeys.cs` - fixed group list, group name from a `Zooms` entry. Unity-free.
- `ScopeZoomPresets/GroupConfig.cs` - BepInEx config entries per group.
- `ScopeZoomPresets/ScopeSight.cs` - helpers on `SightComponent`: adjustable check, local-player check, enabled group lookup.
- `ScopeZoomPresets/Plugin.cs` - entry point, config bind, patch application.
- `ScopeZoomPresets/TogglePatch.cs` - hotkey override.
- `ScopeZoomPresets/RaidStartPatch.cs` - raid-start reset and one-time scope log.
- `ScopeZoomPresets/HudPatch.cs` - HUD label.
- `ScopeZoomPresets/README.md`
- `ScopeZoomPresets/tests/Directory.Build.props` - empty, stops inheritance of the repo props (net472 + game refs).
- `ScopeZoomPresets/tests/ScopeZoomPresets.Tests.csproj`, `ZoomMathTests.cs`, `GroupKeysTests.cs`.

---

### Task 1: Pure math and group keys with tests

**Files:**
- Create: `ScopeZoomPresets/ScopeZoomPresets.csproj`
- Create: `ScopeZoomPresets/ZoomMath.cs`
- Create: `ScopeZoomPresets/GroupKeys.cs`
- Create: `ScopeZoomPresets/tests/Directory.Build.props`
- Create: `ScopeZoomPresets/tests/ScopeZoomPresets.Tests.csproj`
- Test: `ScopeZoomPresets/tests/ZoomMathTests.cs`, `ScopeZoomPresets/tests/GroupKeysTests.cs`

**Interfaces:**
- Produces:
  - `internal static class ZoomMath` in namespace `Krane.ScopeZoomPresets`:
    - `float PercentToFov(float percent, float minFov, float maxFov)`
    - `float FovToPercent(float fov, float minFov, float maxFov)`
    - `float ApproxZoom(float fov, float minFov, float minZoom)`
    - `float ToggleTarget(float current, float lo, float hi)`
    - `void Order(ref float lo, ref float hi)`
    - `string FormatLabel(int percent, float zoom)`
  - `internal static class GroupKeys`:
    - `(float Min, float Max)[] Ranges`
    - `string Name(float min, float max)`
    - `string Find(float[] zooms)` (null when unlisted)

- [ ] **Step 1: Create the projects**

`ScopeZoomPresets/ScopeZoomPresets.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>krane.ScopeZoomPresets</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <Compile Remove="tests/**" />
    <Reference Include="$(SptDir)/EscapeFromTarkov_Data/Managed/Comfort.dll" Private="false" />
  </ItemGroup>
</Project>
```

`ScopeZoomPresets/tests/Directory.Build.props`:
```xml
<Project />
```

`ScopeZoomPresets/tests/ScopeZoomPresets.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../ZoomMath.cs" />
    <Compile Include="../GroupKeys.cs" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
</Project>
```

The test project compiles the two source files directly, so they must not reference Unity, BepInEx or EFT types. `internal` types are visible because they are compiled into the test assembly.

- [ ] **Step 2: Write the failing tests**

`ScopeZoomPresets/tests/ZoomMathTests.cs`:
```csharp
using Krane.ScopeZoomPresets;
using Xunit;

public class ZoomMathTests
{
    // Razor HD Gen 2: MinMaxFov x=26.5 (1x), y=4.4167 (6x).
    private const float Wide = 26.5f;
    private const float Narrow = 4.4167f;

    [Fact]
    public void PercentToFov_Edges()
    {
        Assert.Equal(Wide, ZoomMath.PercentToFov(0, Wide, Narrow), 3);
        Assert.Equal(Narrow, ZoomMath.PercentToFov(100, Wide, Narrow), 3);
        Assert.Equal((Wide + Narrow) / 2, ZoomMath.PercentToFov(50, Wide, Narrow), 3);
    }

    [Fact]
    public void PercentToFov_ClampsOutOfRange()
    {
        Assert.Equal(Narrow, ZoomMath.PercentToFov(150, Wide, Narrow), 3);
        Assert.Equal(Wide, ZoomMath.PercentToFov(-10, Wide, Narrow), 3);
    }

    [Fact]
    public void FovToPercent_InvertsPercentToFov()
    {
        float fov = ZoomMath.PercentToFov(70, Wide, Narrow);
        Assert.Equal(70, ZoomMath.FovToPercent(fov, Wide, Narrow), 3);
    }

    [Fact]
    public void FovToPercent_ZeroRangeIsZero()
    {
        Assert.Equal(0, ZoomMath.FovToPercent(10, 10, 10));
    }

    [Fact]
    public void ApproxZoom_AtWideEdgeIsMinZoom()
    {
        Assert.Equal(1f, ZoomMath.ApproxZoom(Wide, Wide, 1f), 3);
    }

    [Fact]
    public void ApproxZoom_AtNarrowEdgeNearLabel()
    {
        float zoom = ZoomMath.ApproxZoom(Narrow, Wide, 1f);
        Assert.InRange(zoom, 5.5f, 6.5f);
    }

    [Fact]
    public void ToggleTarget_GoesToFartherPoint()
    {
        float lo = ZoomMath.PercentToFov(15, Wide, Narrow);
        float hi = ZoomMath.PercentToFov(70, Wide, Narrow);
        Assert.Equal(hi, ZoomMath.ToggleTarget(lo, lo, hi));
        Assert.Equal(lo, ZoomMath.ToggleTarget(hi, lo, hi));
        // Close to hi: farther is lo.
        Assert.Equal(lo, ZoomMath.ToggleTarget(hi + 0.5f, lo, hi));
    }

    [Fact]
    public void ToggleTarget_TieGoesToHi()
    {
        Assert.Equal(10f, ZoomMath.ToggleTarget(15f, 20f, 10f));
    }

    [Fact]
    public void ToggleTarget_EqualPointsReturnsThatPoint()
    {
        Assert.Equal(12f, ZoomMath.ToggleTarget(20f, 12f, 12f));
    }

    [Fact]
    public void Order_SwapsWhenLoAboveHi()
    {
        float lo = 80, hi = 20;
        ZoomMath.Order(ref lo, ref hi);
        Assert.Equal(20, lo);
        Assert.Equal(80, hi);
    }

    [Fact]
    public void FormatLabel_PercentAndZoom()
    {
        Assert.Equal("70% · ~4.2x", ZoomMath.FormatLabel(70, 4.24f));
    }
}
```

`ScopeZoomPresets/tests/GroupKeysTests.cs`:
```csharp
using Krane.ScopeZoomPresets;
using Xunit;

public class GroupKeysTests
{
    [Fact]
    public void Name_FormatsDecimals()
    {
        Assert.Equal("1x-6x", GroupKeys.Name(1, 6));
        Assert.Equal("2.5x-8x", GroupKeys.Name(2.5f, 8));
        Assert.Equal("6.5x-20x", GroupKeys.Name(6.5f, 20));
    }

    [Fact]
    public void Find_ListedRange()
    {
        Assert.Equal("1x-6x", GroupKeys.Find(new[] { 1f, 6f }));
        Assert.Equal("7x-35x", GroupKeys.Find(new[] { 7f, 35f }));
    }

    [Fact]
    public void Find_ReversedAndDuplicatedLabels()
    {
        Assert.Equal("1x-6x", GroupKeys.Find(new[] { 6f, 1f }));
        Assert.Equal("3x-10x", GroupKeys.Find(new[] { 3f, 3f, 10f, 10f }));
    }

    [Fact]
    public void Find_UnlistedOrSingleValueIsNull()
    {
        Assert.Null(GroupKeys.Find(new[] { 2f, 8f }));
        Assert.Null(GroupKeys.Find(new[] { 1f }));
        Assert.Null(GroupKeys.Find(new float[0]));
        Assert.Null(GroupKeys.Find(null));
    }

    [Fact]
    public void Ranges_HasAllTwelveGroups()
    {
        Assert.Equal(12, GroupKeys.Ranges.Length);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run from `ScopeZoomPresets/tests`: `nice -n 19 nix shell nixpkgs#dotnet-sdk_8 -c dotnet test`
Expected: build error, `ZoomMath` / `GroupKeys` not found.

- [ ] **Step 4: Implement**

`ScopeZoomPresets/ZoomMath.cs`:
```csharp
using System;
using System.Globalization;

namespace Krane.ScopeZoomPresets
{
    // FOV math for adjustable optics. minFov is MinMaxFov.x (widest FOV, lowest zoom), maxFov is
    // MinMaxFov.y (narrowest FOV, highest zoom). Percent is linear in FOV, like the game's ZoomFactor.
    internal static class ZoomMath
    {
        public static float PercentToFov(float percent, float minFov, float maxFov)
        {
            float t = Math.Max(0f, Math.Min(100f, percent)) / 100f;
            return minFov + (maxFov - minFov) * t;
        }

        public static float FovToPercent(float fov, float minFov, float maxFov)
        {
            if (minFov == maxFov)
                return 0f;
            float t = (fov - minFov) / (maxFov - minFov);
            return Math.Max(0f, Math.Min(1f, t)) * 100f;
        }

        // The game has no magnification formula; this scales the low label by the ratio of the
        // half-angle tangents, so it is only an approximation.
        public static float ApproxZoom(float fov, float minFov, float minZoom)
        {
            return (float)(minZoom * Math.Tan(minFov * Math.PI / 360.0) / Math.Tan(fov * Math.PI / 360.0));
        }

        // Same rule as vanilla ForceChangeScopeState: jump to the point farther away, tie goes to hi.
        public static float ToggleTarget(float current, float lo, float hi)
        {
            return Math.Abs(current - lo) > Math.Abs(current - hi) ? lo : hi;
        }

        public static void Order(ref float lo, ref float hi)
        {
            if (lo > hi)
                (lo, hi) = (hi, lo);
        }

        public static string FormatLabel(int percent, float zoom)
        {
            return $"{percent}% · ~{zoom.ToString("0.0", CultureInfo.InvariantCulture)}x";
        }
    }
}
```

`ScopeZoomPresets/GroupKeys.cs`:
```csharp
using System;
using System.Globalization;
using System.Linq;

namespace Krane.ScopeZoomPresets
{
    // Fixed list of adjustable optic groups, keyed by the native label range in the scope's Zooms entry.
    internal static class GroupKeys
    {
        public static readonly (float Min, float Max)[] Ranges =
        {
            (1, 4), (1, 6), (1, 8), (2.5f, 8), (3, 9), (3, 10),
            (3, 20), (3, 24), (4, 16), (5, 25), (6.5f, 20), (7, 35),
        };

        public static string Name(float min, float max)
        {
            return $"{Format(min)}x-{Format(max)}x";
        }

        public static string Find(float[] zooms)
        {
            if (zooms == null || zooms.Length == 0)
                return null;
            float min = (float)Math.Round(zooms.Min(), 1);
            float max = (float)Math.Round(zooms.Max(), 1);
            foreach (var range in Ranges)
            {
                if (Math.Abs(range.Min - min) < 0.01f && Math.Abs(range.Max - max) < 0.01f)
                    return Name(range.Min, range.Max);
            }
            return null;
        }

        private static string Format(float value)
        {
            return value.ToString("0.#", CultureInfo.InvariantCulture);
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run from `ScopeZoomPresets/tests`: `nice -n 19 nix shell nixpkgs#dotnet-sdk_8 -c dotnet test`
Expected: all 16 tests pass.

- [ ] **Step 6: Verify the plugin project still builds**

Run from `ScopeZoomPresets`: `nice -n 19 nix shell nixpkgs#dotnet-sdk_8 -c dotnet build -c Release -o out`
Expected: build succeeds, `out/krane.ScopeZoomPresets.dll` exists, no test sources compiled in.

- [ ] **Step 7: Commit**

```bash
git add ScopeZoomPresets
git commit -m "[ScopeZoomPresets] Add zoom math and group keys"
```

---

### Task 2: Config, ownership gate and hotkey toggle

**Files:**
- Create: `ScopeZoomPresets/GroupConfig.cs`
- Create: `ScopeZoomPresets/ScopeSight.cs`
- Create: `ScopeZoomPresets/TogglePatch.cs`
- Create: `ScopeZoomPresets/Plugin.cs`

**Interfaces:**
- Consumes: `ZoomMath.PercentToFov`, `ZoomMath.ToggleTarget`, `ZoomMath.Order`, `GroupKeys.Ranges`, `GroupKeys.Name`, `GroupKeys.Find`.
- Produces:
  - `internal sealed class GroupSettings` with `ConfigEntry<bool> Enabled`, `ConfigEntry<int> MinPercent`, `ConfigEntry<int> MaxPercent`, `void Percents(out float lo, out float hi)` (ordered).
  - `internal static class GroupConfig`: `void Bind(ConfigFile config)`, `GroupSettings Get(string key)`.
  - `internal static class ScopeSight`: `float[] Zooms(SightComponent sight)`, `bool IsAdjustable(SightComponent sight)`, `bool IsLocal(SightComponent sight)`, `GroupSettings EnabledGroup(SightComponent sight)`.
  - `Plugin.Log` (`ManualLogSource`), `Plugin.ShowLabel` (`ConfigEntry<bool>`), `Plugin.Apply(Harmony harmony, Type patchClass)`.

- [ ] **Step 1: Write `GroupConfig.cs`**

```csharp
using System.Collections.Generic;
using BepInEx.Configuration;

namespace Krane.ScopeZoomPresets
{
    internal sealed class GroupSettings
    {
        public ConfigEntry<bool> Enabled;
        public ConfigEntry<int> MinPercent;
        public ConfigEntry<int> MaxPercent;

        // Min above Max is allowed in the config; the two are swapped here.
        public void Percents(out float lo, out float hi)
        {
            lo = MinPercent.Value;
            hi = MaxPercent.Value;
            ZoomMath.Order(ref lo, ref hi);
        }
    }

    internal static class GroupConfig
    {
        private static readonly Dictionary<string, GroupSettings> Groups = new Dictionary<string, GroupSettings>();

        public static void Bind(ConfigFile config)
        {
            var percent = new AcceptableValueRange<int>(0, 100);
            foreach (var range in GroupKeys.Ranges)
            {
                string section = GroupKeys.Name(range.Min, range.Max);
                Groups[section] = new GroupSettings
                {
                    Enabled = config.Bind(section, "Enabled", false,
                        "Use this group's toggle points and start its scopes at Min % at raid start."),
                    MinPercent = config.Bind(section, "Min %", 0, new ConfigDescription(
                        "Low-zoom toggle point. 0% is the scope's native lowest zoom.", percent)),
                    MaxPercent = config.Bind(section, "Max %", 100, new ConfigDescription(
                        "High-zoom toggle point. 100% is the scope's native highest zoom.", percent)),
                };
            }
        }

        public static GroupSettings Get(string key)
        {
            return key != null && Groups.TryGetValue(key, out var settings) ? settings : null;
        }
    }
}
```

- [ ] **Step 2: Write `ScopeSight.cs`**

```csharp
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;

namespace Krane.ScopeZoomPresets
{
    internal static class ScopeSight
    {
        public static float[] Zooms(SightComponent sight)
        {
            float[][] zooms = sight?._template?.Zooms;
            int index = sight?.SelectedScope ?? -1;
            return zooms != null && index >= 0 && index < zooms.Length ? zooms[index] : null;
        }

        public static bool IsAdjustable(SightComponent sight)
        {
            var data = sight?.AdjustableOpticData;
            return data != null && data.IsAdjustableOptic && data.MinMaxFov.x != data.MinMaxFov.y;
        }

        // Bots and Fika observed players run the same ScopeZoomHandler code. A sight belongs to the
        // local player when its root item is the local player's equipment.
        public static bool IsLocal(SightComponent sight)
        {
            if (sight?.Item == null || !Singleton<GameWorld>.Instantiated)
                return false;
            Player player = Singleton<GameWorld>.Instance.MainPlayer;
            return player != null && sight.Item.GetRootItem() == player.Equipment;
        }

        public static GroupSettings EnabledGroup(SightComponent sight)
        {
            if (!IsAdjustable(sight))
                return null;
            GroupSettings group = GroupConfig.Get(GroupKeys.Find(Zooms(sight)));
            return group != null && group.Enabled.Value ? group : null;
        }
    }
}
```

- [ ] **Step 3: Write `TogglePatch.cs`**

```csharp
using System;
using EFT.InventoryLogic;
using HarmonyLib;

namespace Krane.ScopeZoomPresets
{
    // Vanilla ForceChangeScopeState jumps to whichever native FOV edge is farther away. For the local
    // player's scopes in an enabled group, jump between the configured points instead and repeat the
    // vanilla side effects (target, last edge state, stored zoom, switcher rotation, state event).
    [HarmonyPatch(typeof(ScopeZoomHandler), nameof(ScopeZoomHandler.ForceChangeScopeState))]
    internal static class TogglePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ScopeZoomHandler __instance, SightComponent ____sightComponent,
            ref float ____filedOfView, ref float ___float_2, ref float ___float_4,
            ref ESmoothScopeState ____lastEdgeSmoothScopeState)
        {
            try
            {
                if (!ScopeSight.IsLocal(____sightComponent))
                    return true;
                GroupSettings group = ScopeSight.EnabledGroup(____sightComponent);
                if (group == null)
                    return true;

                group.Percents(out float loPercent, out float hiPercent);
                float lo = ZoomMath.PercentToFov(loPercent, __instance.MinFov, __instance.MaxFov);
                float hi = ZoomMath.PercentToFov(hiPercent, __instance.MinFov, __instance.MaxFov);
                float target = ZoomMath.ToggleTarget(____filedOfView, lo, hi);

                ____filedOfView = target;
                ___float_2 = target;
                ___float_4 = target;
                ____lastEdgeSmoothScopeState = target == lo ? ESmoothScopeState.Max : ESmoothScopeState.Min;
                ____sightComponent.ScopeZoomValue = target;
                __instance.method_11();
                __instance.method_9();
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
                return true;
            }
        }
    }
}
```

- [ ] **Step 4: Write `Plugin.cs`**

```csharp
// Toggle adjustable optics between per-group configured zoom points instead of their native extremes.
//
// The magnification hotkey ends in ScopeZoomHandler.ForceChangeScopeState, which jumps to whichever
// native FOV edge is farther from the current one. TogglePatch keeps that rule but uses the group's
// Min % / Max % points. Percent is linear in FOV between MinMaxFov.x (0%) and MinMaxFov.y (100%).
// Zoom is stored as FOV in SightComponent.ScopeZoomValue and never sent over the network, so all of
// this is local. Only the local player's scopes are touched.
using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace Krane.ScopeZoomPresets
{
    [BepInPlugin("krane.scopezoompresets", "Krane's ScopeZoomPresets", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> ShowLabel;

        private void Awake()
        {
            Log = Logger;
            ShowLabel = Config.Bind("HUD", "Show zoom label", true,
                "Show percent and approximate zoom on every zoom change of an adjustable scope. Off: vanilla label.");
            GroupConfig.Bind(Config);

            var harmony = new Harmony("krane.scopezoompresets");
            Apply(harmony, typeof(TogglePatch));
        }

        // A missing game member makes only that patch class fail; the rest of the game stays vanilla.
        internal static void Apply(Harmony harmony, Type patchClass)
        {
            try
            {
                harmony.PatchAll(patchClass);
            }
            catch (Exception e)
            {
                Log.LogError($"{patchClass.Name} not applied: {e}");
            }
        }
    }
}
```

- [ ] **Step 5: Build**

Run from `ScopeZoomPresets`: `nice -n 19 nix shell nixpkgs#dotnet-sdk_8 -c dotnet build -c Release -o out`
Expected: build succeeds, no warnings about missing references. If `GetRootItem` or `Singleton` does not resolve, add `using EFT.InventoryLogic;` / check Comfort.dll reference.

- [ ] **Step 6: Run unit tests**

Run from `ScopeZoomPresets/tests`: `nice -n 19 nix shell nixpkgs#dotnet-sdk_8 -c dotnet test`
Expected: all pass.

- [ ] **Step 7: Symlink**

```bash
ln -s ~/development/spt-fixes/ScopeZoomPresets/out/krane.ScopeZoomPresets.dll ~/Games/spt/drive_c/SPT/BepInEx/plugins/krane.ScopeZoomPresets.dll
```

- [ ] **Step 8: In-game check (user)**

Ask the user to start a raid with a 1x-6x scope:
- F12: `1x-6x` Enabled on, Min % 15, Max % 70. Hotkey alternates between two zoom points that are not the native edges.
- Scroll to the middle, press the hotkey: jumps to the farther configured point.
- Disable the group: hotkey goes to native 1x/6x again.
- `BepInEx/LogOutput.log` has no `ScopeZoomPresets` errors. With Fika teammates: no errors on their side.

- [ ] **Step 9: Commit**

```bash
git add ScopeZoomPresets
git commit -m "[ScopeZoomPresets] Add configurable magnification toggle"
```

---

### Task 3: Raid-start reset and scope log

**Files:**
- Create: `ScopeZoomPresets/RaidStartPatch.cs`
- Modify: `ScopeZoomPresets/Plugin.cs` (add `Apply(harmony, typeof(RaidStartPatch));` after the TogglePatch line)

**Interfaces:**
- Consumes: `ScopeSight.EnabledGroup`, `GroupSettings.Percents`, `ZoomMath.PercentToFov`, `GroupKeys.Find`, `Plugin.Log`.
- Produces: nothing used by later tasks.

- [ ] **Step 1: Write `RaidStartPatch.cs`**

```csharp
using System;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;

namespace Krane.ScopeZoomPresets
{
    // At raid start, set every adjustable scope of the local player's equipment that is in an enabled
    // group to its Min % point. ScopeZoomHandler.Init reads ScopeZoomValue when the weapon visuals are
    // built; for a weapon already in hands, Init is run again so the live handler picks it up.
    [HarmonyPatch(typeof(GameWorld), nameof(GameWorld.OnGameStarted))]
    internal static class RaidStartPatch
    {
        private static bool _logged;

        [HarmonyPostfix]
        private static void Postfix(GameWorld __instance)
        {
            try
            {
                LogScopesOnce();
                Player player = __instance.MainPlayer;
                if (player == null)
                    return;

                foreach (SightComponent sight in player.Equipment.GetItemComponentsInChildren<SightComponent>())
                {
                    GroupSettings group = ScopeSight.EnabledGroup(sight);
                    if (group == null)
                        continue;
                    group.Percents(out float lo, out _);
                    var fov = sight.AdjustableOpticData.MinMaxFov;
                    sight.ScopeZoomValue = ZoomMath.PercentToFov(lo, fov.x, fov.y);
                }

                if (player.HandsController is Player.FirearmController firearm && firearm.Firearms != null)
                {
                    foreach (SightModVisualControllers controller in firearm.Firearms._sightModVisualControllers)
                    {
                        if (controller != null && ScopeSight.EnabledGroup(controller.SightMod) != null
                            && controller.TryGetZoomHandler(out ScopeZoomHandler handler))
                            handler.Init(controller.SightMod);
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
            }
        }

        // One line per adjustable optic template and scope index, to check where modded scopes land.
        private static void LogScopesOnce()
        {
            if (_logged || !Singleton<ItemFactory>.Instantiated)
                return;
            _logged = true;
            foreach (var template in Singleton<ItemFactory>.Instance.ItemTemplates.Values.OfType<SightModTemplate>())
            {
                if (!template.IsAdjustableOptic || template.Zooms == null)
                    continue;
                for (int i = 0; i < template.Zooms.Length; i++)
                {
                    float[] zooms = template.Zooms[i];
                    string group = GroupKeys.Find(zooms) ?? "unlisted";
                    Plugin.Log.LogInfo($"{template._id} {template._name} scope {i} " +
                        $"Zooms=[{string.Join(", ", zooms)}] MinMaxFov=({template.MinMaxFov.x}, {template.MinMaxFov.y}) group={group}");
                }
            }
        }
    }
}
```

- [ ] **Step 2: Register the patch in `Plugin.Awake`**

```csharp
            Apply(harmony, typeof(TogglePatch));
            Apply(harmony, typeof(RaidStartPatch));
```

- [ ] **Step 3: Build and test**

Run from `ScopeZoomPresets`: `nice -n 19 nix shell nixpkgs#dotnet-sdk_8 -c dotnet build -c Release -o out`
Run from `ScopeZoomPresets/tests`: `nice -n 19 nix shell nixpkgs#dotnet-sdk_8 -c dotnet test`
Expected: both succeed.

- [ ] **Step 4: In-game check (user)**

- With `1x-6x` enabled at Min % 15: set the scope to 6x, end the raid, start a new one. Scope starts at the 15% point, both for the weapon in hands at spawn and a second weapon drawn later.
- With the group disabled: scope keeps its last zoom.
- `BepInEx/LogOutput.log` lists adjustable scopes with groups. Check the ISB PM II 1-8 Black line (`6a34b11b12fc7ef0209ec45d`): report its `Zooms` and `MinMaxFov` to the user.

- [ ] **Step 5: Commit**

```bash
git add ScopeZoomPresets
git commit -m "[ScopeZoomPresets] Reset enabled scopes to min at raid start"
```

---

### Task 4: HUD zoom label

**Files:**
- Create: `ScopeZoomPresets/HudPatch.cs`
- Modify: `ScopeZoomPresets/Plugin.cs` (add `Apply(harmony, typeof(HudPatch));`)

**Interfaces:**
- Consumes: `ScopeSight.IsAdjustable`, `ScopeSight.IsLocal`, `ScopeSight.Zooms`, `ZoomMath.FovToPercent`, `ZoomMath.ApproxZoom`, `ZoomMath.FormatLabel`, `Plugin.ShowLabel`, `Plugin.Log`.

- [ ] **Step 1: Write `HudPatch.cs`**

```csharp
using System;
using System.Linq;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;

namespace Krane.ScopeZoomPresets
{
    // Vanilla shows "xN" on the optic panel only when an adjustable scope sits exactly on a native edge
    // and hides it in between. For adjustable scopes, that handler is skipped and the label is shown on
    // every zoom change instead: once per hotkey press, and on scroll whenever the rounded percent changes.
    internal static class HudPatch
    {
        private static EftBattleUIScreen _screen;
        private static ScopeZoomHandler _lastHandler;
        private static int _lastPercent = -1;

        [HarmonyPatch(typeof(EftBattleUIScreen), "SubscribeOnPlayer")]
        [HarmonyPostfix]
        private static void SubscribePostfix(EftBattleUIScreen __instance)
        {
            _screen = __instance;
        }

        [HarmonyPatch(typeof(EftBattleUIScreen), "SmoothSightChanged")]
        [HarmonyPrefix]
        private static bool SmoothSightChangedPrefix(SightComponent sightComponent)
        {
            return !(Plugin.ShowLabel.Value && ScopeSight.IsAdjustable(sightComponent));
        }

        [HarmonyPatch(typeof(ScopeZoomHandler), nameof(ScopeZoomHandler.method_4))]
        [HarmonyPostfix]
        private static void ScrollPostfix(ScopeZoomHandler __instance, SightComponent ____sightComponent, float ____filedOfView)
        {
            Show(__instance, ____sightComponent, ____filedOfView, false);
        }

        [HarmonyPatch(typeof(ScopeZoomHandler), nameof(ScopeZoomHandler.ForceChangeScopeState))]
        [HarmonyPostfix]
        private static void TogglePostfix(ScopeZoomHandler __instance, SightComponent ____sightComponent, float ____filedOfView)
        {
            Show(__instance, ____sightComponent, ____filedOfView, true);
        }

        private static void Show(ScopeZoomHandler handler, SightComponent sight, float fov, bool force)
        {
            try
            {
                if (!Plugin.ShowLabel.Value || _screen == null || !ScopeSight.IsAdjustable(sight))
                    return;
                int percent = (int)Math.Round(ZoomMath.FovToPercent(fov, handler.MinFov, handler.MaxFov));
                if (!force && handler == _lastHandler && percent == _lastPercent)
                    return;
                if (!ScopeSight.IsLocal(sight))
                    return;

                // A different scope starts updating (aim-in, weapon switch): remember its zoom without
                // showing, so the label appears on the first actual change.
                bool newHandler = handler != _lastHandler;
                _lastHandler = handler;
                _lastPercent = percent;
                if (newHandler && !force)
                    return;

                float[] zooms = ScopeSight.Zooms(sight);
                float minZoom = zooms != null && zooms.Length > 0 ? zooms.Min() : 1f;
                float zoom = ZoomMath.ApproxZoom(fov, handler.MinFov, minZoom);
                _screen._opticCratePanel.Show(ZoomMath.FormatLabel(percent, zoom));
                _screen.HideRaidTime();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
            }
        }
    }
}
```

- [ ] **Step 2: Register the patch in `Plugin.Awake`**

```csharp
            Apply(harmony, typeof(TogglePatch));
            Apply(harmony, typeof(RaidStartPatch));
            Apply(harmony, typeof(HudPatch));
```

- [ ] **Step 3: Build and test**

Run from `ScopeZoomPresets`: `nice -n 19 nix shell nixpkgs#dotnet-sdk_8 -c dotnet build -c Release -o out`
Run from `ScopeZoomPresets/tests`: `nice -n 19 nix shell nixpkgs#dotnet-sdk_8 -c dotnet test`
Expected: both succeed. If Harmony cannot find `SubscribeOnPlayer` / `SmoothSightChanged` on `EftBattleUIScreen` at runtime (log shows `HudPatch not applied`), target the generic base: `typeof(BattleUIScreen<EftBattleUIScreen.EftBattleUIScreenController, EEftScreenType>)`.

- [ ] **Step 4: In-game check (user)**

- Scroll on an adjustable scope: label `N% · ~Zx` appears and updates as the percent changes, including between the native edges.
- Hotkey press: label appears every time, enabled and disabled groups alike.
- Unlisted adjustable scope (if any in the log): label works too.
- Non-adjustable scope (fixed 4x, or switching a dual-mode sight): vanilla `xN` label.
- `HUD / Show zoom label` off: vanilla behavior.
- Bots aiming nearby or Fika teammates scrolling: no label on your screen.

- [ ] **Step 5: Commit**

```bash
git add ScopeZoomPresets
git commit -m "[ScopeZoomPresets] Show percent and zoom on HUD"
```

---

### Task 5: README

**Files:**
- Create: `ScopeZoomPresets/README.md`

- [ ] **Step 1: Write `ScopeZoomPresets/README.md`**

```markdown
# ScopeZoomPresets

## Requirements

- SPT 4.1.6, Fika 2.4.6 (optional, works in single player too)
- No other mods required

## What it does

- The scope magnification hotkey on variable zoom scopes toggles between two points you choose per scope group, e.g. 15% and 70% of a 1x-6x scope instead of 1x and 6x. From an in-between zoom, it jumps to the farther point.
- At raid start, scopes in an enabled group start at the group's Min %.
- The optic HUD label shows `N% · ~Zx` on every zoom change (hotkey and scroll) for all variable zoom scopes.
- Scroll zoom is unchanged and covers the full native range. Only your own scopes are affected; nothing is sent over the network.

Percent is linear in the scope's field of view: 0% is its lowest native zoom, 100% its highest. The `~Zx` value is an approximation; the game has no exact magnification for in-between zooms.

## Why

Vanilla only toggles between the extremes of a variable scope (1x/6x). Often a closer pair like 1.5x/4x is more useful.

## Groups

1x-4x, 1x-6x, 1x-8x, 2.5x-8x, 3x-9x, 3x-10x, 3x-20x, 3x-24x, 4x-16x, 5x-25x, 6.5x-20x, 7x-35x. A scope belongs to the group of the label range in its item data, so modded variants (black/tan Razors) share their base scope's group. Scopes with another range keep the vanilla toggle. The BepInEx log lists every variable scope and its group at the first raid start.

## Settings

| Section / Key | Default | Effect |
|---|---|---|
| `<group>` / Enabled | false | Use this group's toggle points and raid-start reset |
| `<group>` / Min % | 0 | Low-zoom toggle point, 0-100 |
| `<group>` / Max % | 100 | High-zoom toggle point, 0-100. Swapped with Min % if lower |
| HUD / Show zoom label | true | Show `N% · ~Zx` on every zoom change. Off: vanilla label |
```

- [ ] **Step 2: Commit**

```bash
git add ScopeZoomPresets/README.md
git commit -m "[ScopeZoomPresets] Add README"
```
