# UnityProjects - Current Project Map

Unity warehouse simulation project with two overlapping systems:

- A newer AMR station stack driven from `Data/Grid.xlsx` -> `Data/Grid.json` -> `Assets/Scenes/StationScene.unity`
- An older conveyor/diverter sorter stack that is still present and still compiled

`Data/StationDesign.md` is the product/spec document for the AMR station. This file is the codebase map.

## Current Direction

The active build path appears to be the AMR station flow, not the original conveyor demo flow.

Runtime/editor pipeline:

```text
Grid.xlsx
  -> node Data/parse_grid.js
  -> Grid.json
  -> Tools > Station Builder
  -> StationGridData.asset + StationScene.unity
  -> GridMap + ReservationTable + ChargingArea
  -> AMRController robots execute AMRTask jobs
```

The older conveyor code is still useful context because sorter, chute, gaylord, and package assets/scripts were not removed.

## AMR Stack

### Source data

- `Data/Grid.xlsx`: layout source edited outside Unity
- `Data/parse_grid.js`: converts Excel cells into Unity-friendly JSON
- `Data/Grid.json`: generated grid description consumed by the editor builder
- `Data/StationDesign.md`: behavior/specification document for the station

### Edit-time scene generation

- `Assets/Editor/StationGridBuilder.cs`
  - Editor window at `Tools > Station Builder`
  - Reads `Data/Grid.json`
  - Measures the Gaylord prefab footprint
  - Builds or updates `Assets/Scripts/AMR/StationGridData.asset`
  - Rebuilds `Assets/Scenes/StationScene.unity`
  - Creates the `Station` hierarchy with `Grid`, `Gaylords`, `Robots`, and `ChargingArea`
- `Assets/Scripts/AMR/StationBuilderSettings.cs`
  - Persistent builder settings asset
  - Controls Gaylord reference prefab and cell-size overrides
- `Assets/Scripts/AMR/StationBuilderSettings.asset`
  - Default settings instance used by the builder

### AMR runtime

- `Assets/Scripts/AMR/GridCell.cs`
  - `CellType`, `CorridorDirection`, and immutable `CellData`
  - Corridor direction means "forbidden exit is the opposite direction"
- `Assets/Scripts/AMR/StationGridData.cs`
  - ScriptableObject built from `Grid.json`
  - Stores all parsed cells
  - Resolves labels like `S5`, `E3`, `Q10`, `St12`, numeric storage labels
- `Assets/Scripts/AMR/GridMap.cs`
  - Runtime state for each grid cell
  - Tracks occupancy, current reservations, gaylord references
  - Converts between grid coordinates and world positions
  - Traversability rules differ for loaded vs empty robots
- `Assets/Scripts/AMR/Pathfinder.cs`
  - A* over the integer grid
  - Enforces one-way corridor exits
  - Uses `GridMap.IsTraversable(...)` for loaded/empty movement rules
- `Assets/Scripts/AMR/ReservationTable.cs`
  - Time-window reservation model for conflict avoidance
  - Tracks blocked robots and deadlock-triggered backup attempts
- `Assets/Scripts/AMR/AMRTask.cs`
  - `TaskType`, `AMRTask`, `RobotStatus`
  - Tasks use labels rather than direct coordinates
- `Assets/Scripts/AMR/AMRStationConfig.cs`
  - Central simulation tuning: speeds, lift, cooldowns, deadlock, charging bays
- `Assets/Scripts/AMR/AMRController.cs`
  - Robot state machine: `Idle -> NavigateToPickup -> LiftGaylord -> NavigateToDestination -> LowerGaylord`
  - Uses pathfinding + reservations
  - Carries Gaylords by driving position only; leaves world rotation unchanged
- `Assets/Scripts/AMR/ChargingArea.cs`
  - Manages charging bay transforms
  - Uses simple movement outside the grid
- `Assets/Scripts/AMR/IDispatcher.cs`
  - Interface placeholder for a future dispatcher/orchestrator

## Station Scene Layout

`StationGridBuilder` rebuilds `Assets/Scenes/StationScene.unity` around this hierarchy:

```text
Station
|- Grid
|  |- StorageSlots
|  |- Corridors
|  |- SortChutes
|  |- EmptyCartBuffers
|  |- FeederQueue
|  |- StagingArea
|  `- NoRobotZones
|- Gaylords
|- Robots
`- ChargingArea
```

Grid cells are currently visualized as generated `Quad` tiles with text labels where applicable.

## Legacy Conveyor/Sorter Stack

The older sorter simulation is still in the repo and still matters when touching shared assets or scene objects.

Main files:

- `Assets/Scripts/BoxSpawner.cs`: spawns boxes and raises `OnBoxSpawned`
- `Assets/Scripts/PackageRouter.cs`: assigns package addresses and spawns Gaylords along diverters
- `Assets/Scripts/Diverter.cs`: creates divert trigger zones and computes exit offsets
- `Assets/Scripts/DivertZone.cs`: trigger-based lateral routing force
- `Assets/Scripts/SortPoint.cs`: lane/address target definition
- `Assets/Scripts/AddressInit.cs`: assigns address ranges across sort points
- `Assets/Scripts/GaylordContainer.cs`: builds collider walls/floor for Gaylords
- `Assets/Scripts/ConveyorBelt.cs`, `Assets/Scripts/ConveyorBeltVisual.cs`: custom conveyor behavior/visuals
- `Assets/Scripts/Singulator.cs`: package separation behavior
- `Assets/Scripts/Package.cs`, `Assets/Scripts/PackageRecord.cs`, `Assets/Scripts/PackageTracker.cs`: package identity/tracking
- `Assets/Scripts/DiverterConfig1.cs`: edit-time diverter builder; class name is `DiverterConfig`
- `Assets/Editor/DiverterConfigEditor.cs`: custom inspector for `DiverterConfig`

Third-party / imported conveyor support:

- `Assets/PCS/...`: conveyor package and editor tooling
- `Assets/EKstudio/...`, `Assets/UnityWarehouseSceneHDRP/...`, `Assets/NAACo/...`: environment and art assets

## Scenes

- `Assets/Scenes/StationScene.unity`: AMR station scene generated by `StationGridBuilder`
- `Assets/Scenes/SceneSingulator.unity`: older singulator scene
- `Assets/Scenes/SceneTemp.unity`: temporary scene
- `Assets/_Recovery/*.unity`: recovery scenes, not part of the main flow

## Important Assets

- `Assets/LastMileAssets/Prefabs/Gaylord.prefab`: used by `StationGridBuilder` to measure cell footprint
- `Assets/Scripts/AMR/StationGridData.asset`: generated grid data asset
- `Assets/Scripts/AMR/StationBuilderSettings.asset`: builder settings asset

## Known Realities / Constraints

- The AMR system is partially implemented, not finished end-to-end.
- The dispatcher exists only as an interface; there is no scheduling/orchestration layer yet.
- `StationGridBuilder` consumes `Grid.json`, not `Grid.xlsx` directly.
- `Data/parse_grid.js` currently depends on a hardcoded global `xlsx` install path:
  - `C:/Users/Liron/AppData/Roaming/npm/node_modules/xlsx`
- Charging is handled outside the grid with a direct move-to-bay lerp.
- The repo still contains legacy conveyor scripts and scenes, so code changes need to avoid breaking both stacks unless one is intentionally being removed.

## Practical Workflow

When changing the station layout:

1. Edit `Data/Grid.xlsx`
2. Run `node Data/parse_grid.js`
3. In Unity, open `Tools > Station Builder`
4. Rebuild the station scene
5. Verify `Assets/Scenes/StationScene.unity` and `Assets/Scripts/AMR/StationGridData.asset` changed as expected

When changing AMR behavior:

1. Check `Data/StationDesign.md` first
2. Update the runtime code in `Assets/Scripts/AMR/`
3. Keep the builder/runtime contract intact:
   - labels resolve through `StationGridData`
   - world placement resolves through `GridMap`
   - movement legality resolves through `GridMap` + `Pathfinder`
   - robot conflict handling resolves through `ReservationTable`
