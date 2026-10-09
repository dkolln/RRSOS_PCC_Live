"""
Captures the owner's finished Main Base from a save into src/Dashboard/Assets/main-base-template.json.

Run: python tools/capture-main-base.py <save.json> [out.json]

The template stores every object's position as an offset from the Base beacon's own foundation, in the world axes of the save it was
captured from (the beacon pointed +x, "north"), so the dashboard can turn it to any direction the target beacon points. Inventories are
rebuilt empty by the build; what they held is kept in "held" (copied only when asked) and the drone stations are filled with "fill".
Rules of this capture (the owner's decisions, 2026-10-08):
  - leave out the escape pod and everything far from the base (the heater field: 8 Heater5 and an Optimizer2)
  - the "supply everything" list (211 items) is written as "*" and expanded at build time
  - the disposal-room ore crates (labelled, demanding one ore, supplying nothing) get a "stock": that ore, filling the crate
  - crates labelled Misc* supply nothing
  - butterfly farms and beehives are left out: Base Building > Boosters builds them (owner's decision, 2026-10-09)
  - every DroneStation1 is filled with T3 drones (Drone3), 25 each; an Optimizer keeps its fuses ("keep", always built)
  - a Teleporter1's "set" (its number in the game's list) is dropped: the game gives it one
"""
import json
import os
import re
import sys
from decimal import Decimal

SRC = sys.argv[1]
OUT = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(__file__), '..', 'src', 'Dashboard', 'Assets', 'main-base-template.json')

text = open(SRC, encoding='utf-8-sig').read()
objs, invs = {}, {}
for m in re.finditer(r'\{(?:[^{}"]|"(?:[^"\\]|\\.)*")*\}', text):
    try:
        o = json.loads(m.group(0))
    except Exception:
        continue
    if not isinstance(o, dict) or 'id' not in o:
        continue
    if 'woIds' in o:
        invs[o['id']] = o
    elif 'gId' in o and 'pos' in o:
        objs[o['id']] = o
by_id = {**{i: o for i, o in objs.items()}}
allrec = {}
for m in re.finditer(r'\{(?:[^{}"]|"(?:[^"\\]|\\.)*")*\}', text):
    try:
        o = json.loads(m.group(0))
    except Exception:
        continue
    if isinstance(o, dict) and 'id' in o:
        allrec[o['id']] = o

beacons = [o for o in objs.values() if o['gId'] == 'Beacon' and (o.get('text') or '').strip().lower().startswith('base')]
assert len(beacons) == 1, 'need exactly one Base beacon'
beacon = beacons[0]
bx, by, bz = [Decimal(v) for v in beacon['pos'].split(',')]
under = [o for o in objs.values() if o['gId'] == 'Foundation' and abs(Decimal(o['pos'].split(',')[0]) - bx) < 1 and abs(Decimal(o['pos'].split(',')[2]) - bz) < 1 and 1 < by - Decimal(o['pos'].split(',')[1]) < 4]
assert len(under) == 1
ox, oy, oz = [Decimal(v) for v in under[0]['pos'].split(',')]
print('origin (beacon foundation)', ox, oy, oz)

# the beacon's direction: the opposite of its forward, snapped (same rule as the build code)
import math
qx, qy, qz, qw = [float(v) for v in beacon['rot'].split(',')]
yaw = math.atan2(2 * (qw * qy + qx * qz), 1 - 2 * (qy * qy + qz * qz))
dx, dz = -math.sin(yaw), -math.cos(yaw)
dir_x, dir_z = ((1 if dx > 0 else -1), 0) if abs(dx) > abs(dz) else (0, (1 if dz > 0 else -1))
print('beacon direction', dir_x, dir_z)

# the foundations' extent decides what is "far from the base"
fx = [float(o['pos'].split(',')[0]) for o in objs.values() if o['gId'] == 'Foundation']
fz = [float(o['pos'].split(',')[2]) for o in objs.values() if o['gId'] == 'Foundation']
x0, x1, z0, z1 = min(fx), max(fx), min(fz), max(fz)


def is_far(o):
    x, _, z = [float(v) for v in o['pos'].split(',')]
    return x < x0 - 12 or x > x1 + 12 or z < z0 - 12 or z > z1 + 12


# "supply everything": the save's own 211 list, written as "*"
everything = None
for i in invs.values():
    s = [x for x in (i.get('supplyGrps') or '').split(',') if x]
    if len(s) >= 100:
        everything = s
        break
assert everything, 'no supply-everything list found'
print('supply-everything list:', len(everything), 'items')

# check against the dashboard's own fallback list
cs = open(os.path.join(os.path.dirname(__file__), '..', 'src', 'Dashboard', 'Classes', 'DroneNetworkEverything.cs'), encoding='utf-8-sig').read()
fallback = re.findall(r'"([^"]+)"', cs[cs.index('FallbackEverything'):])
fallback = [f for f in fallback if re.match(r'^[A-Za-z0-9_\-]+$', f)]
print('dashboard fallback list matches the save:', fallback == everything, '(%d vs %d)' % (len(fallback), len(everything)))


def off(v, origin):
    return float(round(Decimal(v) - origin, 5))


def csv_count(csv):
    return [x for x in (csv or '').split(',') if x]


# the items the disposal-room crates are for (the game's own ids: Uranim, Aluminium), so other labelled crates (quartz, fuses...) are not stocked
ORES = {'Iron', 'Silicon', 'Titanium', 'Magnesium', 'Cobalt', 'Alloy', 'Uranim', 'Iridium', 'Aluminium', 'Sulfur', 'Osmium', 'Obsidian', 'Aluminum'}

# built by the Boosters page instead (a ring around an optimizer), never part of a base template
BOOSTER_MACHINES = ('ButterflyFarm1', 'ButterflyFarm2', 'ButterflyFarm3', 'Beehive1', 'Beehive2')

excluded = []
template_objects = []
li_owner = {}
for o in objs.values():
    g = o['gId']
    if g in ('Beacon', 'EscapePod') or g in BOOSTER_MACHINES:
        continue
    if is_far(o):
        excluded.append((g, o['pos']))
        continue
    p = o['pos'].split(',')
    t = {'g': g, 'dx': off(p[0], ox), 'dy': off(p[1], oy), 'dz': off(p[2], oz), 'rot': o['rot']}
    for key in ('pnls', 'color', 'text', 'liGrps'):
        if key in o:
            t[key] = o[key]
    label = (o.get('text') or '')
    if 'liId' in o:
        inv = invs[o['liId']]
        spec = {'size': inv['size']}
        if 'demandGrps' in inv:
            demand = inv['demandGrps']
            supply = inv['supplyGrps']
            if csv_count(supply) == everything:
                supply = '*'
            # the owner's rules
            if g in ('Container2', 'Container3') and label.lower().startswith('misc'):
                supply = ''
            spec['demand'] = demand
            spec['supply'] = supply
            spec['priority'] = inv['priority']
        held = {}
        for wid in csv_count(inv['woIds']):
            r = allrec.get(int(wid))
            if r is not None:
                held[r['gId']] = held.get(r['gId'], 0) + 1
        if held:
            spec['held'] = held
        if g == 'DroneStation1':
            spec['fill'] = {'Drone3': inv['size']}
        # the disposal-room ore crates: labelled, set to demand one item and supply nothing; a build can stock them full of that item
        if g.startswith('Container') and label and spec.get('supply') == '' and spec.get('demand') in ORES:
            spec['stock'] = {spec['demand']: inv['size']}
        # an optimizer's fuses are its setup, not a product: always built
        if g.startswith('Optimizer') and held:
            spec['keep'] = dict(held)
        t['inv'] = spec
    if 'siIds' in o:
        sizes = []
        for sid in csv_count(str(o['siIds'])):
            sizes.append(invs[int(sid)]['size'])
        t['sec'] = sizes
    template_objects.append(t)

template_objects.sort(key=lambda t: (t['g'], t['dx'], t['dz'], t['dy']))
counts = {}
for t in template_objects:
    counts[t['g']] = counts.get(t['g'], 0) + 1

doc = {
    'schema': 1,
    'name': 'Full base',
    'capturedFrom': 'Creative-1.json, 2026-10-08, beacon "Base" on Prime',
    'beacon': {'text': 'Base', 'dirX': dir_x, 'dirZ': dir_z},
    'excluded': 'EscapePod, and %d objects far from the base (the heater field)' % len(excluded),
    'counts': counts,
    'objects': template_objects,
}
os.makedirs(os.path.dirname(os.path.abspath(OUT)), exist_ok=True)
with open(OUT, 'w', encoding='utf-8', newline='\n') as f:
    json.dump(doc, f, separators=(',', ':'))
print('objects in template:', len(template_objects), '| excluded:', len(excluded), set(g for g, _ in excluded))
print('written', OUT, os.path.getsize(OUT), 'bytes')
print(counts)
