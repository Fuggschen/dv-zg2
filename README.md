# ZG2 — Zugschlusstafeln (Train End Boards)

A [Derail Valley](https://store.steampowered.com/app/588030/Derail_Valley/) mod that adds **Zugschlusstafeln** — the red-and-white reflective train-end marker boards used on real German railways — as physical in-game items. Players must attach a board to the last car of their train or face a configurable percentage-based fine on job completion.

## Overview

In real-life German railway operations, every train must carry a *Zugschlusstafel* (train-end board) on its rearmost vehicle. ZG2 brings that regulation into Derail Valley:

- **Four board variants** — small and large, each in new and worn condition. New boards can be bought from shops; worn boards spawn at stations.
- Boards spawn at every station on the jobs table and can be purchased from station shops for **$500**.
- Players attach boards via the game's **gadget customization** system.
- Boards feature a **retroreflective shader** that realistically glows when illuminated by nearby lights.
- Mounted boards show as **map markers** (red dots) so you can see at a glance where the end of your train is.
- When a **haul job** (Transport or Empty Haul) is completed **without** a board ever having been present on the train, a **percentage-based fine** is registered as a debt in the career terminal.
- **Multiplayer-aware** — fine tracking and station spawning only run on the host; clients are unaffected.

## Dependencies

| Dependency | Type | Required |
|---|---|---|
| [Unity Mod Manager](https://www.nexusmods.com/site/mods/21) ≥ 0.27.2 | Mod loader | Yes |
| [custom_item_mod](https://www.nexusmods.com/derailvalley/mods/891) | UMM mod | Yes — handles item prefab setup, shop integration, and save/load |

## Installation

1. Install **Unity Mod Manager** and configure it for Derail Valley.
2. Install **custom_item_mod** via UMM.
3. Install **ZG2** via UMM.
4. Launch the game — boards will appear at all stations.

## Configuration

All settings are accessible via the **Unity Mod Manager** settings panel in-game (Ctrl+F10 by default).

| Setting | Default | Range | Description |
|---|---|---|---|
| Penalty Percent | 30% | 0–100 | Percentage of job wage deducted as a fine when no board was present |
| Boards Per Station | 3 | 1–10 | Total number of boards (random variant) spawned at every station on world load |
| Check Interval (seconds) | 30 | 10–120 | How often the mod scans for board presence on tracked job trains |
| Show Map Markers | true | on/off | Display red map markers on cars with mounted ZG2 boards |

## Architecture

### Lifecycle

The mod's entry point is `ZG2.Main.Load`, registered with UMM via `Info.json`.

1. **`Load()`** — loads settings, creates Harmony instance, registers UMM callbacks.
2. **`OnToggle(true)`** — applies all Harmony patches, subscribes to `WorldStreamingInit.LoadingFinished` and `UnloadWatcher.UnloadRequested`.
3. **`ShopInitPatch`** (Harmony prefix on `GlobalShopController.Awake`, priority 200, after `custom_item_mod`) — calls `ZG2BoardFactory.Initialise()`. This runs **before** `OnWorldLoaded`.
4. **`OnWorldLoaded()`** — creates a persistent `[ZG2 Mod Manager]` GameObject, then initialises subsystems in strict order: `ZG2SaveManager` → `ZG2StationSpawner` → `ZG2JobTracker` → `ZG2PenaltyHandler` → `ZG2MapMarkerManager`.
5. **Teardown** (`OnWorldUnload` / `OnToggle(false)`) — symmetric shutdown of all systems (including map markers), Harmony unpatch.

### Subsystems

| Class | Role |
|---|---|
| `ZG2BoardFactory` | Loads the `zg2boards` AssetBundle, creates `CustomItem` instances for all four variants (with icons from the bundle), adds `ZG2Board` + gadget + retroreflective components, injects into `ItemModsFinder.CustomItems`. |
| `ZG2StationSpawner` | Iterates all stations on world load and spawns N boards (random variant) at each station's `jobBookletSpawnSurface` position. Skipped on MP clients. |
| `ZG2JobTracker` | MonoBehaviour coroutine that maintains a dictionary of tracked jobs. Periodically checks whether the first or last car of each job's trainsets has a ZG2 gadget attached. Only tracks haul jobs (Transport / Empty Haul). |
| `ZG2GadgetDetector` | Static utility that inspects `TrainCarCustomization.Customizers` for `GadgetBase` instances with a `ZG2Board` component (with a fallback interior hierarchy search). |
| `ZG2PenaltyHandler` | Two-phase fine evaluation: Phase 1 (prefix) calculates the fine and stores it as pending; Phase 2 (postfix) registers a `ZG2FineDebt` with `CareerManagerDebtController` and shows a notification. Skipped on MP clients. |
| `ZG2FineDebt` | Extends `DisplayableDebt`. Flat regulatory fine (`DebtType.StagedJob`, non-taxable). Registered with the career debt controller. |
| `ZG2SaveManager` | Subscribes to `SaveGameManager.OnInternalDataUpdate`. Persists job tracking state under key `ZG2Mod_Data` (version 1). Skipped on MP clients. |
| `ZG2Board` | MonoBehaviour on each board item. Stores `BoardVariant` (Old/Modern/OldWorn/ModernWorn) and persists it via `ItemSaveData` events. |
| `ZG2MapMarkerManager` | Manages red-dot map markers (caboose-style `DynamicFastTravelDestination`) on cars with mounted ZG2 boards. Toggled via the `ShowMapMarkers` setting. |
| `ZG2MarkerAnchor` | MonoBehaviour that keeps a detached map-marker anchor tracking a `TrainCar`'s world position. |
| `ZG2Retroreflective` | Passes nearby light data to a custom retroreflective shader so boards glow realistically when illuminated. |
| `ZG2MPUtil` | Soft-dependency multiplayer detection via reflection on `MPAPI.MultiplayerAPI`. No compile-time dependency. |
| `EventHelper` | Reflection-based event subscribe/unsubscribe to work around Krafs.Publicizer ambiguity. |

### Harmony Patches

All patches live in `ZG2.Game/Patches/` and are guarded with `if (!Main.Enabled) return`.

| Patch | Target | Purpose |
|---|---|---|
| `ShopInitPatch` | `GlobalShopController.Awake` | Initialises `ZG2BoardFactory` (priority 200, after custom_item_mod) |
| `JobTakenPatch` | `JobsManager.TakeJob` | Starts tracking a newly taken haul job (skips save-restored jobs and non-haul types) |
| `JobAbandonedPatch` | `JobsManager.AbandonJob` | Stops tracking an abandoned job |
| `JobValidatorPatch` | `JobValidator.ValidateJob` | Two-phase fine: prefix pre-evaluates, postfix commits (haul jobs only) |
| `GadgetWhitelistPatch` | `GadgetSystemUtility.AllowedGadgetNames` setter | Appends ZG2 gadget names (all 4 variants) to the whitelist |
| `GadgetPlacedPatch` | `GadgetItem.Place` | Triggers map marker addition when a ZG2 board is mounted on a car |
| `GadgetRemovedPatch` | `GadgetBase.Remove` | Triggers map marker removal when a ZG2 board is unmounted |
| `BookletPaymentPatch` | *(disabled)* | Previously injected fines into booklet payment page; currently commented out |


### Save System

- **Key**: `ZG2Mod_Data` in the game's `SaveGameData`
- **Version**: 1 (includes a version field for future migrations)
- **Persisted**: Job tracking state — array of `{ jobId, everHadBoard, currentlyHasBoard }` for each active tracked job
- **Not persisted**: Station spawns (re-created on every world load); per-item board variant (handled by custom_item_mod's own save/load intercept via `ItemSaveData`)

## Building from Source

### Prerequisites

- .NET Framework 4.8 (`net48`)
- Derail Valley installed locally
- Unity 2019.4.40f1 (for asset bundle rebuilds only)
- `custom_item_mod` installed in the game's `Mods/` folder (provides `custom_item_mod.dll` + `custom_item_components.dll`)

### Local Paths

Set `DvInstallDir` and `UnityInstallDir` in `Directory.Build.targets` to match your local environment.

### Build

```
dotnet build .\ZG2.sln -c Debug
```

Output: `ZG2.Game/bin/Debug/` → `ZG2.dll`

### Project Structure

```
ZG2.sln
├─ ZG2.Game/                    # Runtime mod code
│  ├─ Main.cs                   # UMM entry point and lifecycle
│  ├─ ZG2BoardFactory.cs        # Asset bundle loading + item registration
│  ├─ ZG2StationSpawner.cs      # Per-station board spawning
│  ├─ ZG2JobTracker.cs          # Job tracking coroutine
│  ├─ ZG2GadgetDetector.cs      # Board detection on train cars
│  ├─ ZG2PenaltyHandler.cs      # Fine calculation + debt registration
│  ├─ ZG2FineDebt.cs            # DisplayableDebt subclass
│  ├─ ZG2SaveManager.cs         # Save/load integration
│  ├─ ZG2Board.cs               # Board MonoBehaviour
│  ├─ ZG2MapMarkerManager.cs    # Map marker management for mounted boards
│  ├─ ZG2MarkerAnchor.cs        # Detached anchor tracking car position
│  ├─ ZG2Retroreflective.cs     # Retroreflective shader light data
│  ├─ ZG2MPUtil.cs              # Multiplayer detection (soft dependency)
│  ├─ ZG2Settings.cs            # UMM settings
│  ├─ EventHelper.cs            # Reflection event helper
│  ├─ Patches/                  # Harmony patches
├─ ZG2.UnityProject/            # Unity project for board prefabs and asset bundles
├─ Directory.Build.targets      # Local path configuration
└─ Info.json                    # UMM mod metadata
```

### Credits

3D Models and Textures: Adrii_95

Code: Fuggschen

### Licence

This project is licenced under the MIT Licence