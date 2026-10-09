"""
Captures one of the owner's tier bases from a save into src/Dashboard/Assets/main-base-tier<N>.json.

Run: python tools/capture-tier.py <save.json> <tier number> [--lamp NORTH,WEST] [--out out.json]
  --lamp  the position (any two of the lamp's coordinates, x and z as saved) of the outdoor lamp the tier was built around, when the save has more than one
          (several bases built side by side); without it there must be exactly one lamp standing on a foundation
Then add the tier to BaseBuildingService.MainBaseTiers (key, label, file name).

The same format as the full base (tools/capture-main-base.py): every object's position is an offset from the foundation under the anchor, here the one
outdoor lamp standing on a foundation (the lamp points +x, "north"), in the world axes of the save it was captured from, so the dashboard can turn it to
the way any beacon or lamp points. The two bases share one frame (the anchor foundation is the origin of both, and the pods and foundations stand at the
same offsets), so a Tier 1 base built first is found already there when the full base is built over it.
Rules of this capture (the owner's decisions, 2026-10-08):
  - what is in the save is what is captured: no drone settings are added (Tier 1 has no drone network), no machine holds anything
  - loose items lying about (ore, seeds, bottles) are not part of the base: anything the game lists as an item (plugin 0.15's unlocks.json) is left out
  - a labelled crate that holds items is "stocked" with them (the disposal-room ore crates); the build puts them in when asked to pre-fill the base
  - the escape pod and the anchor lamp itself are left out
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
OUT = os.path.join(os.path.dirname(__file__), '..', 'src', 'Dashboard', 'Assets', 'main-base-tier%d.json' % TIER)
for i, a in enumerate(ARGS):
    if a == '--lamp':
        LAMP = [float(v) for v in ARGS[i + 1].split(',')]
    elif a == '--out':
        OUT = ARGS[i + 1]
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


def csv(s):
    return [x for x in str(s or '').split(',') if x]


template_objects, skipped = [], {}
planet = lamp.get('planet')
for o in objs.values():
    g = o['gId']
    if o['id'] == lamp['id'] or g in ('Beacon', 'EscapePod') or o.get('planet') != planet:
        continue
    x, y, z = pos(o)
    # only the base itself: the save may hold other things elsewhere
    if not (ox - 30 <= x <= ox + 200 and oz - 60 <= z <= oz + 200):
        continue
    if kinds.get(g) == 'item':
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
        held = {}
        for w in csv(inv['woIds']):
            r = allrec.get(int(w))
            if r is not None:
                held[r['gId']] = held.get(r['gId'], 0) + 1
        if held:
            if g.startswith('Container') and o.get('text'):
                spec['stock'] = held
            else:
                spec['held'] = held
        t['inv'] = spec
    if 'siIds' in o:
        t['sec'] = [invs[int(s)]['size'] for s in csv(o['siIds'])]
    template_objects.append(t)

template_objects.sort(key=lambda t: (t['g'], t['dx'], t['dz'], t['dy']))
counts = {}
for t in template_objects:
    counts[t['g']] = counts.get(t['g'], 0) + 1

doc = {
    'schema': 1,
    'name': 'Tier %d base' % TIER,
    'capturedFrom': '%s, %s, the outdoor lamp on Prime' % (os.path.basename(SRC), __import__('datetime').date.today().isoformat()),
    'beacon': {'text': 'Base', 'dirX': dir_x, 'dirZ': dir_z},
    'excluded': 'loose items on the ground (%d) and the escape pod' % sum(skipped.values()),
    'counts': counts,
    'objects': template_objects,
}
os.makedirs(os.path.dirname(os.path.abspath(OUT)), exist_ok=True)
with open(OUT, 'w', encoding='utf-8', newline='\n') as f:
    json.dump(doc, f, separators=(',', ':'))
print('objects in the template:', len(template_objects), '| left out as items:', skipped)
print('written', os.path.abspath(OUT), os.path.getsize(OUT), 'bytes')
print(dict(sorted(counts.items(), key=lambda kv: -kv[1])))
