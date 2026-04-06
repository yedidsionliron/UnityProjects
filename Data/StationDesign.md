# Station Design — AMR Simulation Specification
## For: Claude Code (AI Coding Agent)
## Project: Last-Mile Delivery Station — Unity 6.3

---

## Purpose

This document is the single source of truth for building the AMR movement simulation in Unity. It describes the facility layout, the physical rules governing AMR behavior, and the tasks AMRs must perform. There is no code here — only instructions. Implement in whatever Unity patterns are most appropriate for version 6.3.

Do not invent behavior not described here. If something is ambiguous, ask before implementing.

---

## 1. Source of Truth: The Grid File

The facility layout is defined in `/Data/Grid.xlsx`. This file must be read at **editor time** to generate the scene layout. If the file changes, the scene must be regenerable from it without manual adjustments.

### Reading the Grid

- Each Excel cell corresponds to one grid cell in the simulation.
- The cell's fill color determines its type (see Section 2).
- The cell's text value (if any) is its label (e.g. S1, E1, Q1, St1).
- Merged cells represent a single logical zone spanning multiple grid cells.
- Row 1 of the Excel sheet = the top of the facility (storage area top).
- Column 1 = the left edge of the facility.
- Build an editor tool or script that parses the file and instantiates the scene accordingly.
- All layout parameters (number of storage rows, row length, number of crossing corridors, number of staging columns) are derived from the file — do not hardcode them.

### Color-to-Zone Mapping

| Excel fill color | Zone type |
|---|---|
| Blue (RGB 173,214,235) | Storage slot |
| Gray (RGB 220,220,220) | AMR corridor |
| Yellow (RGB 255,245,180) | Induct zone (no robots) |
| Light pink/red | Sort chutes (S1–S29) |
| Violet (RGB 220,200,240) | Empty cart buffer (E1–E14) |
| Orange (RGB 255,220,160) | Feeder queue (Q1–Q10) |
| Green (RGB 180,235,180) | Staging area (St1–St90) |
| Dark gray (sorter system) | Feeder / Singulator / Diverter — no robots |

Merged cells spanning multiple rows/columns define the exact footprint of each zone. Respect the merge boundaries when determining zone extents.

---

## 2. Facility Layout

The facility has two functional areas stacked vertically in the scene:

### 2.1 Storage Area (upper portion of grid)
- Cart slots arranged in paired columns separated by single-cell-wide AMR corridors.
- A horizontal crossing corridor separates two groups of storage rows.
- Every corridor cell is traversable by AMRs.
- Every storage slot cell can hold exactly one Gaylord prefab.
- Slot cells are not traversable by loaded robots.
- Slot cells ARE traversable by empty robots (they pass underneath stored carts).

### 2.2 Operational Area (lower portion of grid)
Contains the following zones, all derived from the Excel file:

- **Induct zone**: large merged yellow region. No robots permitted here under any circumstance.
- **Sorter system**: feeder, singulator, diverter (dark gray merged cells). No robots permitted inside the sorter footprint. Robots may only access the designated handoff cells at the edges of sort chutes.
- **Sort chutes (S1–S28)**: each is a single cell. Robots access from the corridor side only. S29 is the missort chute — treat identically to S1–S28 for robot access.
- **Empty cart buffers (E1–E14)**: E1–E7 on the left flank of the sorter, E8–E14 on the right. Each buffer cell holds one empty Gaylord.
- **Feeder queue (Q1–Q10)**: L-shaped sequence of cells below the feeder. Carts enter at Q10 and advance toward Q1, which feeds the feeder. Robots deposit carts into the queue from the open ends.
- **Staging area (St1–St90)**: three columns of cells to the right of the sorter. St1–St30 is the active front column (driver pickup). St31–St60 and St61–St90 are backup columns replenished by robots.

---

## 3. Grid Model

### 3.1 Cell Size
- Determine the physical cell size in Unity units by reading the Gaylord prefab's bounding box. One grid cell = one Gaylord footprint.
- All positions in the scene are derived from this cell size.

### 3.2 Coordinate System
- The grid is a 2D integer coordinate system on the XZ plane (Y is vertical).
- Row index increases downward (south). Column index increases rightward (east).
- Each cell (row, col) maps to a unique world position.

### 3.3 Cell State
Each cell must track:
- Its type (storage, corridor, induct, sorter, chute, buffer, queue, staging)
- Whether it is occupied (by a Gaylord or by a robot)
- A reference to the Gaylord occupying it (if any)
- Whether it is reserved by a robot in transit

---

## 4. AMR — Physical Model

### 4.1 Asset
- Model: `MovingRobot.fbx` (no prefab yet — create one).
- The robot operates on the grid. It moves one cell at a time in four cardinal directions: North, South, East, West. No diagonal movement.
- One robot occupies one cell at any time.
- Movement is animated as a smooth lerp between cell centers at a configurable speed.

### 4.2 Two-Layer Traversal
This is a critical design rule:

- **Loaded robot** (carrying a Gaylord): may only traverse corridor cells. May not enter storage slot cells, chute cells, buffer cells, or any no-robot zone.
- **Empty robot** (not carrying a Gaylord): may traverse corridor cells AND storage slot cells (it passes underneath stored carts). May not enter the induct zone or the sorter system footprint under any circumstance.

This means empty robots can take shortcuts through the storage area, significantly reducing travel time and congestion.

### 4.3 Gaylord Carry Mechanics
- The robot slides underneath the Gaylord and lifts it 3cm off the ground.
- The Gaylord's world-space rotation never changes during transport. The robot rotates independently beneath it.
- Do not parent the Gaylord to the robot in a way that inherits rotation. Drive Gaylord position from the robot's carry point each frame; leave rotation untouched.
- When the robot lowers the Gaylord at the destination, it sets the Gaylord down and detaches.

---

## 5. AMR — Pathfinding

### 5.1 Algorithm
Use A* pathfinding on the integer grid. The traversable cell set differs by robot state:
- Loaded: corridor cells only.
- Empty: corridor cells + storage slot cells.

In both cases, the no-robot zones (induct zone, sorter footprint) are permanently impassable.

### 5.2 Path Representation
A path is an ordered list of grid coordinates from current cell to destination cell.

### 5.3 Conflict Avoidance
- Use a time-windowed reservation table. Before a robot begins moving, it reserves each cell in its path for the expected time window of occupancy.
- If a cell in the planned path is already reserved by another robot, the robot replans from its current position.
- If two robots are mutually blocked for more than a configurable timeout, one robot backs up one cell and replans. The number of backup attempts before escalating is a configurable parameter.

### 5.4 One-Way Corridors
Corridors in the storage area are one-way. The direction of each corridor is encoded in the grid layout. Enforce one-way constraints during pathfinding — do not allow robots to traverse a corridor against its designated direction.

---

## 6. AMR — Tasks

AMRs perform the following task types. Each task has an origin cell and a destination cell.

| Task | Description |
|---|---|
| `PickupFromChute` | Go to a sort chute cell, lift the full Gaylord, carry it to a storage slot. |
| `DeliverToStorage` | Carry a Gaylord from its current location to an assigned storage slot. |
| `DeliverEmptyToChute` | Pick up an empty Gaylord from a buffer cell (E1–E14) or storage slot and deliver it to a sort chute. |
| `DeliverToQueue` | Carry a Gaylord from storage to a feeder queue cell (Q10 entry point). |
| `DeliverToStaging` | Carry a Gaylord from storage to a staging cell (St1–St30 front row). |
| `ReplenishStaging` | Move a Gaylord from a backup staging column (St31–90) to the front column (St1–30). |
| `ReturnEmpty` | Collect an empty Gaylord from staging (after driver pickup) and return it to storage. |
| `Charge` | Navigate to the charging area and wait. |

Tasks are assigned by a dispatcher (not yet implemented — leave a clear interface for it). The robot executes one task at a time.

---

## 7. AMR — State Machine

Each robot runs the following states:

```
Idle
 └─► NavigateToPickup
      └─► LiftGaylord
           └─► NavigateToDestination
                └─► LowerGaylord
                     └─► Idle
```

- `Idle`: robot waits at its current cell for a task assignment.
- `NavigateToPickup`: robot (empty) navigates to the origin cell of its task.
- `LiftGaylord`: robot raises the Gaylord 3cm over a short animation. Gaylord follows robot position but not rotation.
- `NavigateToDestination`: robot (loaded) navigates to the destination cell.
- `LowerGaylord`: robot lowers the Gaylord to ground level and detaches.
- Back to `Idle`.

Transitions are event-driven. Each state completes before the next begins.

---

## 8. Charging Area

- A charging area must be included in the scene.
- Its location is not yet defined in the Excel file — place it at a reasonable position adjacent to the operational area, accessible from the main corridors.
- Idle robots that have no pending tasks navigate to the charging area.
- The charging area has a configurable number of charging bays. One robot per bay.

---

## 9. Dispatcher Interface

Do not implement the dispatcher logic yet. However, build a clean interface that the dispatcher can call to:
- Assign a task to a specific robot.
- Query the status of any robot (position, state, carried Gaylord).
- Query the occupancy of any grid cell.
- Get a list of idle robots.
- Get a list of occupied storage slots with their Gaylord references.

This interface will be consumed by the sort sequencer and placement optimizer in a future implementation phase.

---

## 10. Scene Structure

Organize the scene hierarchy as follows:

```
Station (root)
├── Grid
│   ├── StorageSlots
│   ├── Corridors
│   ├── SortChutes
│   ├── EmptyCartBuffers
│   ├── FeederQueue
│   ├── StagingArea
│   └── NoRobotZones
├── Gaylords (runtime pool)
├── Robots
└── ChargingArea
```

The Grid is generated from the Excel file at editor time. All other objects are instantiated at runtime.

---

## 11. Parameters (Configurable)

Expose the following as configurable fields (ScriptableObject or Inspector):

- Robot movement speed (loaded and empty, separately)
- Lift height (default 3cm)
- Lift animation duration
- Reservation timeout (seconds before replanning)
- Deadlock backup timeout
- Number of standby sort chutes
- Number of charging bays
- Path replanning cooldown

---

## 12. Out of Scope (This Phase)

The following are explicitly out of scope for this implementation phase:

- Sort sequencer logic
- Placement optimizer logic
- Package-level simulation (individual package tracking)
- Sorter machine behavior (feeder, singulator, diverter already implemented)
- Driver behavior at staging
- Multi-floor configuration
- Performance metrics and analytics

These will be specified in separate documents and added to this file as future phases.
