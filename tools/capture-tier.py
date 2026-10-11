"""
Captures one of the owner's tier bases from a save into src/Dashboard/Assets/main-base-tier<N>.json.

Run: python tools/capture-tier.py <save.json> <tier number> [--lamp NORTH,WEST] [--out out.json] [--full] [--skip-box X0,X1,Z0,Z1]
  --lamp  the position (any two of the lamp's coordinates, x and z as saved) of the outdoor lamp the tier was built around, when the save has more than one
          (several bases built side by side); without it there must be exactly one lamp standing on a foundation
  --full  the final base instead of a tier (use tier number 6, it only names the file): written to main-base-template.json as "Full base", with the drone network
          kept as a marker (a supply-everything list becomes "*", crates labelled Misc* supply nothing), the drone stations filled with T3 drones, an optimizer's
          fuses always built, and never heaters or drills (Base Building > Boosters builds those)
  --skip-box  leave out everything standing in this box of the save's own world coordinates (x north, z west): the warehouse, which has its own builder.
          Only foundations, chests and the signs hung on them may be in it, or the capture stops
Then add the tier to BaseBuildingService.MainBaseTiers (key, label, file name).

The same format as the full base (tools/capture-main-base.py): every object's position is an offset from the foundation under the anchor, here the one
outdoor lamp standing on a foundation (the lamp points +x, "north"), in the world axes of the save it was captured from, so the dashboard can turn it to
the way any beacon or lamp points. The two bases share one frame (the anchor foundation is the origin of both, and the pods and foundations stand at the
same offsets), so a Tier 1 base built first is found already there when the full base is built over it.
Rules of this capture (the owner's decisions, 2026-10-08):
  - what is in the save is what is captured: no drone settings are added (Tier 1 has no drone network), no machine holds anything
  - loose items lying about (ore, seeds, bottles) are not part of the base: anything the game lists as an item (plugin 0.15's unlocks.json) is left out
  - a labelled crate that holds items, and a food grower holding its seed, are "stocked" with them; the build puts them in when asked to pre-fill the base
  - every vegetube holds its tuska seed (Seed4), whether or not the save had it in
  - then every crate for a real item is filled to its size (tools/fill-crates.py), so a base is built with those crates completely full
  - the escape pod and the anchor lamp itself are left out
  - a vehicle standing in the base (the rover) is part of it, built with the highest tier of every vehicle upgrade (speed 4, lights 2, inventory 3, equipment 2, oxygen, jetpack, beacon, logistics)
"""
import json
import math
import os
import re
import sys
from decimal import Decimal

ARGS = sys.argv[1:]
if len(ARGS) < 2 or not ARGS[1].isdigit():
    sys.exit(__doc__)
SRC, TIER = ARGS[0], int(ARGS[1])
LAMP = None
FULL = '--full' in ARGS
SKIP_BOX = None
OUT = os.path.join(os.path.dirname(__file__), '..', 'src', 'Dashboard', 'Assets', 'main-base-template.json' if FULL else 'main-base-tier%d.json' % TIER)
for i, a in enumerate(ARGS):
    if a == '--lamp':
        LAMP = [float(v) for v in ARGS[i + 1].split(',')]
    elif a == '--out':
        OUT = ARGS[i + 1]
    elif a == '--skip-box':
        SKIP_BOX = [float(v) for v in ARGS[i + 1].split(',')]
UNLOCKS = os.path.expandvars(r'%LOCALAPPDATA%\RRSOS-PCC-Live\unlocks.json')

RECORD = re.compile(r'\{(?:[^{}"]|"(?:[^"\\]|\\.)*")*\}')
text = open(SRC, encoding='utf-8-sig').read()
allrec, objs, invs = {}, {}, {}
for m in RECORD.finditer(text):
    try:
        o = json.loads(m.group(0))
    except Exception:
        continue
    if not isinstance(o, dict) or 'id' not in o:
        continue
    allrec[o['id']] = o
    if 'woIds' in o:
        invs[o['id']] = o
    elif 'gId' in o and 'pos' in o:
        objs[o['id']] = o

if not os.path.exists(UNLOCKS):
    sys.exit('unlocks.json is not there: start the game with plugin 0.15 or newer and load a world once')
kinds = {g['id']: g['kind'] for g in json.load(open(UNLOCKS, encoding='utf-8-sig'))['groups']}


def pos(o):
    return [Decimal(v) for v in o['pos'].split(',')]


# the anchor: the outdoor lamp that stands on a foundation
foundations = [o for o in objs.values() if o['gId'] == 'Foundation']
anchors = []
for lamp in (o for o in objs.values() if o['gId'] == 'OutsideLamp1'):
    lx, ly, lz = pos(lamp)
    under = [f for f in foundations if abs(pos(f)[0] - lx) <= 3 and abs(pos(f)[2] - lz) <= 3 and 1 < ly - pos(f)[1] < 4]
    if under:
        anchors.append((lamp, min(under, key=lambda f: abs(pos(f)[0] - lx) + abs(pos(f)[2] - lz))))
if LAMP is not None:
    anchors = [(l, f) for l, f in anchors if abs(float(pos(l)[0]) - LAMP[0]) < 3 and abs(float(pos(l)[2]) - LAMP[1]) < 3]
assert len(anchors) == 1, 'need exactly one outdoor lamp standing on a foundation (found %d); name the one with --lamp NORTH,WEST' % len(anchors)
lamp, origin = anchors[0]
ox, oy, oz = pos(origin)
print('origin (the lamp\'s foundation)', ox, oy, oz)

qx, qy, qz, qw = [float(v) for v in lamp['rot'].split(',')]
yaw = math.atan2(2 * (qw * qy + qx * qz), 1 - 2 * (qy * qy + qz * qz))
dx, dz = -math.sin(yaw), -math.cos(yaw)
dir_x, dir_z = ((1 if dx > 0 else -1), 0) if abs(dx) > abs(dz) else (0, (1 if dz > 0 else -1))
print('lamp direction', dir_x, dir_z)
assert (dir_x, dir_z) == (1, 0), 'the lamp must point north (+x) when the base is captured'


def off(v, origin_v):
    return float(round(v - origin_v, 5))


# the highest tier of each vehicle upgrade family in the game data (VehicleSpeed1..4 -> VehicleSpeed4, ...)
_families = {}
for _id, _kind in kinds.items():
    _m = re.match(r'^(Vehicle.*?)(\d+)$', _id)
    if _kind == 'item' and _m and _id != 'VehicleTruck':
        _families.setdefault(_m.group(1), []).append((int(_m.group(2)), _id))
MAX_VEHICLE_GEAR = {max(v)[1]: 1 for v in _families.values()}


def csv(s):
    return [x for x in str(s or '').split(',') if x]


# the final base only: what Base Building > Boosters builds is never part of it, the drone network stays a marker, a "supply everything" list is the save's own
# (written as "*" and expanded again at build time, from the target save's own list or the dashboard's)
FULL_NEVER = ('Heater1', 'Heater2', 'Heater3', 'Heater4', 'Heater5', 'Drill0', 'Drill1', 'Drill2', 'Drill3', 'Drill4')
everything = None
if FULL:
    for _inv in invs.values():
        _s = csv(_inv.get('supplyGrps'))
        if len(_s) >= 100:
            everything = _s
            break
    assert everything, 'no supply-everything list found in the save'
    _cs = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'src', 'Dashboard', 'Classes', 'DroneNetworkEverything.cs'), encoding='utf-8-sig').read()
    _fallback = [f for f in re.findall(r'"([^"]+)"', _cs[_cs.index('FallbackEverything'):]) if re.match(r'^[A-Za-z0-9_\-]+$', f)]
    print('supply-everything list:', len(everything), 'items; the dashboard fallback list matches it:', _fallback == everything)

template_objects, skipped, warehouse = [], {}, {}
planet = lamp.get('planet')
for o in objs.values():
    g = o['gId']
    # butterfly farms and beehives are built by Base Building > Boosters, and the algae generators, harvesting robot and lake water collectors stand out in the world:
    # none of them is part of a base template (the tiers do keep their own heaters)
    if SKIP_BOX is not None:
        _x, _, _z = pos(o)
        if SKIP_BOX[0] <= _x <= SKIP_BOX[1] and SKIP_BOX[2] <= _z <= SKIP_BOX[3] and o.get('planet') == planet:
            assert g in ('Foundation', 'Sign') or g.startswith('Container'), 'something other than the warehouse stands in the skipped box: %s at %s' % (g, o['pos'])
            warehouse[g] = warehouse.get(g, 0) + 1
            continue
    if FULL and g in FULL_NEVER:
        continue
    if o['id'] == lamp['id'] or g in ('Beacon', 'EscapePod', 'ButterflyFarm1', 'ButterflyFarm2', 'ButterflyFarm3', 'Beehive1', 'Beehive2', 'AlgaeGenerator1', 'AlgaeGenerator2', 'HarvestingRobot1', 'WaterCollector2') or o.get('planet') != planet:
        continue
    x, y, z = pos(o)
    # only the base itself: the save may hold other things elsewhere
    if not (ox - 30 <= x <= ox + 200 and oz - 60 <= z <= oz + 200):
        continue
    # a vehicle standing in the base (the rover) is part of it, with the upgrades fitted to it; every other item lying about is not
    if kinds.get(g) == 'item' and not g.startswith('Vehicle'):
        skipped[g] = skipped.get(g, 0) + 1
        continue
    t = {'g': g, 'dx': off(x, ox), 'dy': off(y, oy), 'dz': off(z, oz), 'rot': o['rot']}
    for key in ('pnls', 'color', 'text', 'liGrps'):
        if key in o:
            t[key] = o[key]
    if 'liId' in o:
        inv = invs[o['liId']]
        spec = {'size': inv['size']}
        if 'demandGrps' in inv:
            spec['demand'] = inv['demandGrps']
            spec['supply'] = inv['supplyGrps']
            spec['priority'] = inv['priority']
            if FULL:
                if csv(spec['supply']) == everything:
                    spec['supply'] = '*'
                if g in ('Container2', 'Container3') and (o.get('text') or '').lower().startswith('misc'):
                    spec['supply'] = ''
        held = {}
        for w in csv(inv['woIds']):
            r = allrec.get(int(w))
            if r is not None:
                held[r['gId']] = held.get(r['gId'], 0) + 1
        if held:
            if (g.startswith('Container') and o.get('text')) or g.startswith('VegetableGrower'):
                spec['stock'] = held
            else:
                spec['held'] = held
        if FULL and g == 'DroneStation1':
            spec['fill'] = {'Drone3': inv['size']}
            spec.pop('held', None)
        if FULL and g.startswith('Optimizer') and held:
            spec['keep'] = dict(held)   # an optimizer's fuses are its setup: always built
            spec.pop('held', None)
        if g.startswith('Vegetube'):
            spec['stock'] = {'Seed4': inv['size']}   # every vegetube is built with its tuska seed
            spec.pop('held', None)
        t['inv'] = spec
    if 'siIds' in o:
        t['sec'] = [invs[int(s)]['size'] for s in csv(o['siIds'])]
        if g.startswith('Vehicle'):
            # a vehicle is built with the highest tier of every upgrade the game has for it (owner's rule), whatever was fitted when it was captured
            t['secGear'] = [dict(MAX_VEHICLE_GEAR) for s in csv(o['siIds'])]
    template_objects.append(t)

template_objects.sort(key=lambda t: (t['g'], t['dx'], t['dz'], t['dy']))
counts = {}
for t in template_objects:
    counts[t['g']] = counts.get(t['g'], 0) + 1

doc = {
    'schema': 1,
    'name': 'Full base' if FULL else 'Tier %d base' % TIER,
    'capturedFrom': '%s, %s, the outdoor lamp on Prime' % (os.path.basename(SRC), __import__('datetime').date.today().isoformat()),
    'beacon': {'text': 'Base', 'dirX': dir_x, 'dirZ': dir_z},
    'excluded': ('loose items on the ground (%d) and the escape pod' % sum(skipped.values()))
                + ('; never heaters, drills, butterfly farms, beehives, algae generators, the harvesting robot or lake water collectors' if FULL else '')
                + ('; not the warehouse (%d objects)' % sum(warehouse.values()) if SKIP_BOX is not None else ''),
    'counts': counts,
    'objects': template_objects,
}
os.makedirs(os.path.dirname(os.path.abspath(OUT)), exist_ok=True)
with open(OUT, 'w', encoding='utf-8', newline='\n') as f:
    json.dump(doc, f, separators=(',', ':'))
print('objects in the template:', len(template_objects), '| left out as items:', skipped, '| warehouse left out:', warehouse)
print('written', os.path.abspath(OUT), os.path.getsize(OUT), 'bytes')

# every crate for a real item is built completely full (see tools/fill-crates.py)
import subprocess
subprocess.run([sys.executable, os.path.join(os.path.dirname(os.path.abspath(__file__)), 'fill-crates.py'), os.path.abspath(OUT)], check=True)
print(dict(sorted(counts.items(), key=lambda kv: -kv[1])))
