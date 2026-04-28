"""
Conveyor Bend – Single Solid Piece
====================================
Paste into Blender > Scripting editor and click Run Script.

Creates ONE object called "ConveyorBend" that looks like a real
conveyor: a curved trough with raised side rails and support legs,
all in one mesh. Tweak the parameters at the top, then re-run.

After running:
  1. Alt+N → Recalculate Outside  (fix normals if needed)
  2. Export: File → Export → FBX
             ✓ Apply Transforms | Forward: -Z | Up: Y
"""

import bpy, bmesh, math

# ── Parameters ────────────────────────────────────────────────────────
INNER_R   = 0.5     # inner turn radius (m) – how tight the inside curve is
BELT_W    = 2.0     # belt width (m) – match your straight conveyor
OUTER_R   = INNER_R + BELT_W   # = 2.5

ANGLE_DEG = 90.0    # how many degrees the conveyor turns
SEGS      = 32      # arc smoothness (more = rounder)

RAIL_W    = 0.08    # width of each side rail wall
RAIL_H    = 0.28    # height of side rails above the belt surface
BODY_H    = 0.15    # depth of the body/frame below the belt
LEG_H     = 0.75    # leg height below the body (floor to body)
LEG_INSET = 0.05    # how far legs are inset from inner/outer edge
# ─────────────────────────────────────────────────────────────────────

#
#  Cross-section profile (r, z) viewed from the end:
#
#                     INNER          OUTER
#                      rail           rail
#    z=RAIL_H   *----*              *----*
#               |    |              |    |
#    z=0.0      |    *--------------*    |   <- belt surface (recessed)
#               |                        |
#    z=-BODY_H  *------------------------*   <- bottom of frame
#               |    |              |    |
#               |    |    legs      |    |
#   z=-(BODY+   *----*--------------*----*   <- floor (leg bottoms)
#     LEG_H)
#
#  The profile is swept along a circular arc (ANGLE_DEG degrees).
#

FLOOR_Z = -(BODY_H + LEG_H)

# Profile = list of (r, z) going CCW around the perimeter
# (inner side first, going up and across, then outer side down)
PROFILE = [
    # inner rail – outer face going up
    (INNER_R - RAIL_W,  FLOOR_Z),          # 0  inner-bottom-outer
    (INNER_R - RAIL_W,  -BODY_H),          # 1  inner-body-outer
    (INNER_R - RAIL_W,  RAIL_H),           # 2  inner-rail-top-outer
    # inner rail – inner face (toward belt)
    (INNER_R,           RAIL_H),           # 3  inner-rail-top-inner
    (INNER_R,           0.0),              # 4  belt-inner-top
    # belt surface (recessed between the two rails)
    (OUTER_R,           0.0),              # 5  belt-outer-top
    # outer rail – inner face
    (OUTER_R,           RAIL_H),           # 6  outer-rail-top-inner
    (OUTER_R,           -BODY_H),          # 7  outer-body-inner  (not needed visually but closes frame)
    # outer rail – outer face going down
    (OUTER_R + RAIL_W,  RAIL_H),           # 8  outer-rail-top-outer
    (OUTER_R + RAIL_W,  -BODY_H),          # 9  outer-body-outer
    (OUTER_R + RAIL_W,  FLOOR_Z),          # 10 outer-bottom-outer
    # floor / leg base – going back inward
    (OUTER_R - LEG_INSET, FLOOR_Z),        # 11 outer-leg-inner
    (INNER_R + LEG_INSET, FLOOR_Z),        # 12 inner-leg-outer  (gap = open space between legs)
]
# Note: profile is NOT closed here – we close it with start/end caps


def remove_obj(name):
    if name in bpy.data.objects:
        old = bpy.data.objects[name]
        me  = old.data
        bpy.data.objects.remove(old, do_unlink=True)
        if isinstance(me, bpy.types.Mesh) and me.users == 0:
            bpy.data.meshes.remove(me)


remove_obj("ConveyorBend")

me = bpy.data.meshes.new("ConveyorBend")
ob = bpy.data.objects.new("ConveyorBend", me)
bpy.context.collection.objects.link(ob)
ob.location = (0, 0, 0)

bm = bmesh.new()
N = len(PROFILE)

# ── Build the swept profile grid ──────────────────────────────
# grid[angle_index][profile_point_index] = BMVert
grid = []
for i in range(SEGS + 1):
    a = math.radians(ANGLE_DEG * i / SEGS)
    c, s = math.cos(a), math.sin(a)
    row = []
    for r, z in PROFILE:
        row.append(bm.verts.new((r * c, r * s, z)))
    grid.append(row)

# ── Swept side faces ──────────────────────────────────────────
for i in range(SEGS):
    for j in range(N - 1):
        # skip the floor gap between inner and outer legs (points 12→0 would close wrongly)
        bm.faces.new([
            grid[i  ][j],
            grid[i+1][j],
            grid[i+1][j+1],
            grid[i  ][j+1],
        ])

# ── End caps (the two flat faces at 0° and ANGLE_DEG) ─────────
bm.faces.new([grid[0][j]     for j in range(N - 1, -1, -1)])   # start face (0°)
bm.faces.new([grid[SEGS][j]  for j in range(N)])                # end face (ANGLE_DEG)

# ── Floor face under the legs ──────────────────────────────────
# At each angle step, close the floor rectangle (points 0, 10, 11, 12)
# (skipped in the loop above because it's the "underside" open gap)
for i in range(SEGS):
    # floor: from outer-bottom (10) around to inner-bottom (0), across gap (12 to 0 direction)
    bm.faces.new([
        grid[i  ][0],
        grid[i  ][12],
        grid[i+1][12],
        grid[i+1][0],
    ])
    bm.faces.new([
        grid[i  ][11],
        grid[i  ][10],
        grid[i+1][10],
        grid[i+1][11],
    ])

# ── Belt top surface ──────────────────────────────────────────
# Points 4 (belt-inner-top) to 5 (belt-outer-top) – the flat top surface packages slide on
for i in range(SEGS):
    bm.faces.new([
        grid[i  ][4],
        grid[i  ][5],
        grid[i+1][5],
        grid[i+1][4],
    ])

# ── Rail tops ─────────────────────────────────────────────────
# Inner rail top: from point 2 to point 3
for i in range(SEGS):
    bm.faces.new([
        grid[i  ][2],
        grid[i  ][3],
        grid[i+1][3],
        grid[i+1][2],
    ])
# Outer rail top: from point 6 to point 8
for i in range(SEGS):
    bm.faces.new([
        grid[i  ][6],
        grid[i  ][8],
        grid[i+1][8],
        grid[i+1][6],
    ])

# ── Frame bottom ──────────────────────────────────────────────
# Points 1 to 7 (the underside of the main body frame, open in the middle)
for i in range(SEGS):
    bm.faces.new([
        grid[i  ][1],
        grid[i  ][7],
        grid[i+1][7],
        grid[i+1][1],
    ])

# ── Clean up and apply ────────────────────────────────────────
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0001)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
bm.to_mesh(me)
bm.free()
me.update()

print("=" * 50)
print("ConveyorBend created!")
print(f"  inner r : {INNER_R} m")
print(f"  outer r : {OUTER_R} m  (belt width = {BELT_W} m)")
print(f"  angle   : {ANGLE_DEG}°")
print(f"  segs    : {SEGS}")
print()
print("Origin is at the arc center (0, 0, 0).")
print("In Unity, rotate around Y to aim the output end.")
print("=" * 50)
