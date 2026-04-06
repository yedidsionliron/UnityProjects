# ConveyorEditor — Project Map

Unity 2022+ warehouse conveyor simulation. Branch: `multi-diverters`.

## Architecture

### Routing pipeline (runtime)
```
BoxSpawner → OnBoxSpawned → PackageRouter.OnBoxSpawned → pkg.address = Random(1, totalLanes)
                                                    ↓
                                             AddressInit.TotalAddressSpace (static)

Package (on belt) → Diverter.BuildZones() → DivertZone (trigger) → checks pkg.address
                                                                  → SortPoint.Contains(address)
                                                                  → applies lateral force → GaylordContainer
```

### Edit-time setup (Inspector)
```
DiverterConfig (MonoBehaviour) → DiverterConfigEditor (Editor) → sizes belt via PCSConfig
                              → Build() → creates SortPoint[]
                              → AddressInit.Assign(sortPoints) → bakes addressMin/addressMax
                              → Diverter.sortPoints = sortPoints
```

## Key files

| File | Role |
|------|------|
| `Assets/Scripts/Package.cs` | Data tag on each box: `int address`, zero-friction physics material |
| `Assets/Scripts/BoxSpawner.cs` | Spawns boxes on interval, fires `OnBoxSpawned` event |
| `Assets/Scripts/PackageRouter.cs` | Sets `pkg.address`, spawns Gaylords along diverters |
| `Assets/Scripts/Diverter.cs` | Creates `DivertZone` triggers at runtime, computes `ExitOffset()` |
| `Assets/Scripts/DivertZone.cs` | `OnTriggerStay` → applies lateral friction force to route package |
| `Assets/Scripts/DiverterConfig1.cs` | Edit-time: creates SortPoints, places gaylords. Class name: `DiverterConfig` |
| `Assets/Scripts/SortPoint.cs` | Destination marker: `id`, `side` (Left/Right), `addressMin/Max`, `Contains()` |
| `Assets/Scripts/AddressInit.cs` | Owns address space; `Assign()` distributes ranges across SortPoints |
| `Assets/Scripts/GaylordContainer.cs` | Runtime interior colliders for gaylord boxes (floor + 4 walls) |
| `Assets/Editor/DiverterConfigEditor.cs` | Custom inspector for DiverterConfig; calls `BuildSortPoints()`; computes `groundWorldY` from `PCSConfig.conveyorSupportHeight` |
| `Assets/PCS/Scripts/PCSConfig.cs` | Third-party conveyor asset config: length, width, speed, supports |
| `Assets/PCS/Scripts/PCSConveyor.cs` | Force-based belt driving (FixedUpdate OverlapBox) |
| `Assets/PCS/Scripts/PCSsingulator.cs` | Singulator conveyor variant |
| `Assets/PCS/Scripts/BeltCapacitySensor.cs` | Detects belt jam/capacity |
| `Assets/PCS/Editor/PCSInspector.cs` | Custom inspector for PCSConfig |

## Address scheme
- Addresses are 1-indexed integers in `[1, totalAddressSpace]`
- `AddressInit` divides `totalAddressSpace` evenly across all SortPoints
- Each `Diverter` has `numDivertPoints` zones → `numDivertPoints * 2` lanes (L + R per zone)
- `DivertZone` routes left if `leftPoint.Contains(address)`, right if `rightPoint.Contains(address)`
- Packages with no matching SortPoint pass through

## Physics model
- Belt: `PCSConveyor` uses `OverlapBox` + friction force (`F = μmg`) in `transform.forward`
- Divert: `DivertZone` uses same friction model in `transform.right` (lateral)
- Packages: zero-friction `PhysicsMaterial` (set in `Package.Awake`) to prevent sticking to belt edges
- Gaylords: runtime `BoxCollider` walls via `GaylordContainer.RebuildColliders()`
- Gaylord placement: `Build()` computes `groundWorldY = pcs.transform.TransformPoint(0, -(2 * conveyorSupportHeight), 0).y`; `PlaceGaylord` lifts each gaylord by `bounds.min` offset so the mesh bottom sits at ground level regardless of belt elevation or prefab pivot position

## Scenes
- `Assets/Scenes/Scene0.unity` — main test scene
- `Assets/Scenes/SceneSingulator.unity` — singulator variant

## Assembly structure
- `Assets/PCS/Scripts/PCS.asmdef` — PCS namespace (separate assembly)
- Default assembly — everything in `Assets/Scripts/` and `Assets/Editor/`
